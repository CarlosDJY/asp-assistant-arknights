using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Encodings.Web;
using System.Text.Json;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

[Collection("Native OCR")]
public class Scale90RecognitionTests(EnemyRecognitionFixture fixture) : IClassFixture<EnemyRecognitionFixture>
{
    [Trait("Category", "LocalScreenshot")]
    [Theory]
    [InlineData("商店普通.png", 3840)]
    [InlineData("商店普通.png", 1920)]
    [InlineData("商店冻结.png", 3840)]
    [InlineData("商店冻结.png", 1920)]
    [InlineData("金币花完商店普通.png", 3840)]
    [InlineData("金币花完商店普通.png", 1920)]
    [InlineData("金币花完商店冻结.png", 3840)]
    [InlineData("金币花完商店冻结.png", 1920)]
    [InlineData("本局信息.png", 3840)]
    [InlineData("本局信息.png", 1920)]
    [InlineData("局内信息.png", 3840)]
    [InlineData("局内信息.png", 1920)]
    public async Task Real4kDesktop150Game90Samples(string file, int width)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "4k-scale90", file));
        using var input = new MemoryStream(bytes);
        using var original = new Bitmap(input);
        original.Size.Should().Be(new Size(3840, 2160));
        var height = width * 9 / 16;
        using var image = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.InterpolationMode = InterpolationMode.Bilinear; // ScreenCaptureService production path.
            graphics.DrawImage(original, 0, 0, width, height);
        }
        var frame = width == 3840 ? bytes : ImageBytes.Encode(image);
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var operators = await store.LoadOperatorsAsync();
        var (equipment, _) = await store.LoadEquipmentAsync();
        var names = operators.Where(o => o.RequiredStrategyId.Length == 0).Select(o => o.Name)
            .Concat(equipment.Select(e => e.Name)).Distinct().ToList();
        var theme = new GameShopThemeDetector(Path.Combine(AppContext.BaseDirectory, "data", "shop_theme_refs.json")).Detect(frame);
        var items = await new ShopReader(fixture.Engine, names) { Theme = GameShopTheme.Closure }.ReadAsync(frame, width, height, names);
        var level = await new TerminalLevelReader(fixture.Engine).ReadAsync(frame, width, height, GameShopTheme.Closure);
        var boardData = await GarrisonBoardStore.LoadAsync(Path.Combine(AppContext.BaseDirectory, "data"));
        var board = new BoardRecognizer(boardData!).Recognize(ImageBytes.Encode(image));
        var strategy = new GarrisonOcrStrategy();
        var detector = new InformationScreenDetector(fixture.Engine, strategy);
        var opening = await detector.IsOpeningScreenAsync(frame, width, height);
        var information = await detector.IsInformationScreenAsync(frame, width, height);
        var output = Path.Combine(AppContext.BaseDirectory, "TestResults", "4k-scale90");
        Directory.CreateDirectory(output);
        if (file.Contains("商店"))
        {
            var controls = await fixture.Engine.RecognizeRegionAsync(frame, (int)(width * .74), (int)(height * .61),
                (int)(width * .25), (int)(height * .15));
            File.WriteAllText(Path.Combine(output, $"{Path.GetFileNameWithoutExtension(file)}-{width}.json"),
                JsonSerializer.Serialize(new { file, width, height, theme, level, opening, information,
                    board = board is null ? null : new { board.Map.Id, camera = board.Camera.ToString(), board.Score, board.Margin },
                    controls = controls.Select(c => c.Text),
                    items = items.Select(i => new { i.Name, i.Id, i.OcrRegion.X, i.OcrRegion.Y, i.OcrRegion.Width, i.OcrRegion.Height }) },
                    new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            var expected = file.StartsWith("金币") ? new[] { "不退者之盾", "野鬃", "赫德雷", "安洁莉娜", "森蚺", "铸铁" }
                : new[] { "蜂鸣器", "锡人", "砾", "伊桑", "野鬃", "复奏" };
            items.Select(i => i.Name).Should().Equal(expected);
            items.Select(i => i.Id).Should().OnlyHaveUniqueItems();
            for (var i = 0; i < items.Count; i++)
                (items[i].OcrRegion.X + items[i].OcrRegion.Width / 2).Should().BeInRange(.18 + i * .113, .18 + (i + 1) * .113);
            level.Should().Be(4);
            theme.Should().Be(GameShopTheme.Closure);
            opening.Should().BeFalse();
            information.Should().BeFalse();
            board.Should().NotBeNull();
            board!.Map.Id.Should().Be("desert_cactus");
            controls.Should().Contain(c => c.Text.Contains(file.Contains("冻结") ? "解冻" : "冻结"));
            return;
        }
        var (covenants, _) = await store.LoadCovenantsAsync();
        var read = await BanCovenantReader.ReadAsync(fixture.Engine, strategy, frame, width, height,
            covenants.Where(c => c.BondType == "SEASON").Select(c => c.Name).ToList(),
            covenants.Where(c => c.BondType != "SEASON").Select(c => c.Name).ToList());
        var enemy = await new EnemyRecognizer(fixture.Engine, strategy, fixture.Bosses).RecognizeAsync(frame, width, height);
        File.WriteAllText(Path.Combine(output, $"{Path.GetFileNameWithoutExtension(file)}-{width}.json"),
            JsonSerializer.Serialize(new { file, width, height, theme, level, opening, information,
                board = board?.Map.Id, read, enemy },
                new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        items.Should().BeEmpty();
        level.Should().BeNull();
        opening.Should().Be(file == "本局信息.png");
        information.Should().BeTrue();
        board.Should().BeNull();
        read.Core.Should().HaveCount(10);
        read.Additional.Should().HaveCount(16);
        read.UnnamedCore.Should().BeEmpty();
        read.UnnamedAdditional.Should().BeEmpty();
        read.HasIncompleteRows.Should().BeFalse();
        read.NeedsScroll.Should().BeFalse();
        read.Core.Where(c => c.IsBanned).Select(c => c.Name).Should().BeEquivalentTo("炎", "阿戈尔", "乌萨斯", "伊比利亚", "莱塔尼亚");
        read.Additional.Where(c => c.IsBanned).Select(c => c.Name).Should().BeEquivalentTo("灵巧", "助力", "远见", "不屈", "奥术");
        enemy.Should().NotBeNull();
        enemy!.BossName.Should().Contain("萨米的意志");
        enemy.EnemyTypes.Should().BeEquivalentTo("特训敌人·持续", "特训敌人·复苏", "特训敌人·飞行");
    }
}
