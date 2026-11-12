using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

/// <summary>
/// 地图识别与格子数据：样例帧是休整期截图缩到 400×225 的 BMP（真实截图在 data/samples，未入库）。
/// </summary>
public class BoardRecognizerTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation");
    private static readonly string FrameDir = Path.Combine(AppContext.BaseDirectory, "TestData", "Board");

    private static async Task<GarrisonBoardData> LoadAsync() =>
        (await GarrisonBoardStore.LoadAsync(DataDir)) ?? throw new InvalidOperationException("缺少 garrison_maps.json");

    private static byte[] Frame(string name) => File.ReadAllBytes(Path.Combine(FrameDir, name + ".bmp"));

    [Theory]
    [InlineData("snow_collapsed", "snow", BoardCamera.Collapsed)]
    [InlineData("desert_camo_expanded", "desert_camo", BoardCamera.Expanded)]
    [InlineData("palace_arrows_expanded", "palace_arrows", BoardCamera.Expanded)]
    public async Task Recognize_FindsMapAndCamera(string frame, string mapId, BoardCamera camera)
    {
        var recognizer = new BoardRecognizer(await LoadAsync());
        var match = recognizer.Recognize(Frame(frame));
        match.Should().NotBeNull();
        match!.Map.Id.Should().Be(mapId);
        match.Camera.Should().Be(camera);
        match.Margin.Should().BeGreaterThan(8);
    }

    /// <summary>A full lineup stands on 8–9 cells; the map must still be recognised (the overlay depends on it).</summary>
    [Theory]
    [InlineData("snow_collapsed", "snow", BoardCamera.Collapsed)]
    [InlineData("desert_camo_expanded", "desert_camo", BoardCamera.Expanded)]
    [InlineData("palace_arrows_expanded", "palace_arrows", BoardCamera.Expanded)]
    public async Task Recognize_WithOperatorsStandingOnTheBoard(string frame, string mapId, BoardCamera camera)
    {
        var data = await LoadAsync();
        var recognizer = new BoardRecognizer(data);
        var bytes = Frame(frame);
        PaintCells(bytes, data.Cameras[camera],
            [(5, 3), (4, 3), (3, 3), (2, 3), (5, 0), (4, 0), (3, 1), (2, 2), (1, 3)]);
        var match = recognizer.Recognize(bytes);
        match.Should().NotBeNull();
        match!.Map.Id.Should().Be(mapId);
    }

    /// <summary>Paints whole cells (and a bit above, where a sprite's head goes) in flat colours.</summary>
    private static void PaintCells(byte[] bmp, BoardProjection projection, (int Col, int Row)[] cells)
    {
        int width = BitConverter.ToInt32(bmp, 18), rawHeight = BitConverter.ToInt32(bmp, 22), height = Math.Abs(rawHeight);
        int offset = BitConverter.ToInt32(bmp, 10), bpp = BitConverter.ToInt16(bmp, 28) / 8, stride = (width * bpp + 3) & ~3;
        var rnd = new Random(7);
        foreach (var (c, r) in cells)
        {
            byte cr = (byte)rnd.Next(40, 230), cg = (byte)rnd.Next(40, 230), cb = (byte)rnd.Next(40, 230);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    var (gc, gr) = projection.Unproject((x + 0.5) / width, (y + 0.5) / height);
                    if (gc < c + 0.05 || gc > c + 0.95 || gr < r - 0.4 || gr > r + 0.95) continue;
                    int i = offset + (rawHeight > 0 ? height - 1 - y : y) * stride + x * bpp;
                    bmp[i] = cb; bmp[i + 1] = cg; bmp[i + 2] = cr;
                }
        }
    }

    [Fact]
    public async Task Recognize_RejectsTheMapIntroCamera()
    {
        var recognizer = new BoardRecognizer(await LoadAsync());
        recognizer.Recognize(Frame("map_intro")).Should().BeNull();
    }

    [Fact]
    public async Task Maps_AreCompleteAndRoutesWalkable()
    {
        var data = await LoadAsync();
        data.Maps.Should().HaveCount(10);
        data.Cameras.Should().ContainKeys(BoardCamera.Collapsed, BoardCamera.Expanded);
        data.Fingerprints.Keys.Should().BeEquivalentTo(data.Maps.Select(m => m.Id));
        foreach (var map in data.Maps)
        {
            map.ColumnCount.Should().Be(12, map.Id);
            map.RowCount.Should().Be(5, map.Id);
            map.Routes.Should().HaveCount(2, map.Id);
            foreach (var route in map.Routes)
            {
                map.At(route[^1]).Should().Be(BoardCellType.Defense, map.Id);
                for (int i = 1; i < route.Count; i++)
                    (Math.Abs(route[i].Col - route[i - 1].Col) + Math.Abs(route[i].Row - route[i - 1].Row))
                        .Should().Be(1, $"{map.Id} 路线应逐格相邻");
                // 敌人只走可部署的地面，以及出怪口一侧的花纹格（第 6 列，不可部署但能走）
                route.Where(c => c.Col != 6).Should().OnlyContain(
                    c => map.At(c) == BoardCellType.Ground || map.At(c) == BoardCellType.Special
                         || map.At(c) == BoardCellType.Spawn || map.At(c) == BoardCellType.Defense, map.Id);
            }
        }
    }

    [Fact]
    public async Task Projection_RoundTripsCells()
    {
        var data = await LoadAsync();
        foreach (var projection in data.Cameras.Values)
        {
            var (x, y) = projection.Project(3.5, 2.5);
            projection.CellAt(x, y).Should().Be(new BoardCell(3, 2));
            var (c, r) = projection.Unproject(x, y);
            c.Should().BeApproximately(3.5, 1e-6);
            r.Should().BeApproximately(2.5, 1e-6);
        }
    }

    [Fact]
    public void CanDeploy_FollowsHighGroundRule()
    {
        GarrisonMap.CanDeploy(BoardCellType.High, melee: true).Should().BeFalse();
        GarrisonMap.CanDeploy(BoardCellType.High, melee: false).Should().BeTrue();
        GarrisonMap.CanDeploy(BoardCellType.Special, melee: true).Should().BeTrue();
        GarrisonMap.CanDeploy(BoardCellType.Blocked, melee: false).Should().BeFalse();
    }

    [Fact]
    public async Task Stabilizer_NeedsTwoFramesToSwitchAndSeveralToClear()
    {
        var data = await LoadAsync();
        var a = new BoardMatch(data.Maps[0], BoardCamera.Collapsed, 1, 10);
        var b = new BoardMatch(data.Maps[0], BoardCamera.Expanded, 1, 10);
        var s = new BoardMatchStabilizer { MissesBeforeClear = 3 };
        s.Observe(a).Should().BeFalse();
        s.Observe(a).Should().BeTrue();
        s.Current!.Camera.Should().Be(BoardCamera.Collapsed);
        s.Observe(b).Should().BeFalse();
        s.Observe(b).Should().BeTrue();
        s.Observe(null).Should().BeFalse();
        s.Observe(null).Should().BeFalse();
        s.Observe(null).Should().BeTrue();
        s.Current.Should().BeNull();
    }
}
