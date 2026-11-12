// Modified for ASP Assistant Arknights on 2026-11-11; see NOTICE.
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public sealed class GameConnectionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"asp_connection_{Guid.NewGuid():N}");

    [Fact]
    public async Task MissingSettingsPreservePcCaptureDefault()
    {
        var settings = await new SettingsManager(_directory).LoadGameConnectionAsync();
        using var connection = new GameConnectionService(settings);
        connection.IsAdb.Should().BeFalse();
        connection.Settings.Source.Should().Be(GameCaptureSource.Pc);
    }

    [Fact]
    public async Task AdbSettingsRoundTripWithoutPersistingWindowHandles()
    {
        var manager = new SettingsManager(_directory);
        var expected = new GameConnectionSettings
        {
            Source = GameCaptureSource.Adb,
            AdbPath = @"C:\模拟器\shell\adb.exe",
            AdbSerial = "127.0.0.1:16384",
            WindowTitle = "MuMu 自定义窗口",
        };
        await manager.SaveGameConnectionAsync(expected);
        (await manager.LoadGameConnectionAsync()).Should().Be(expected);
        File.Exists(Path.Combine(_directory, "game-connection.json.tmp")).Should().BeFalse();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{broken")]
    [InlineData("{\"Source\":\"Unknown\"}")]
    [InlineData("{\"Source\":99}")]
    [InlineData("{\"AdbPath\":null}")]
    public async Task InvalidSettingsFallBackToPc(string json)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "game-connection.json"), json);
        (await new SettingsManager(_directory).LoadGameConnectionAsync()).Source.Should().Be(GameCaptureSource.Pc);
    }

    [Fact]
    public async Task ConcurrentSavesLeaveOneCompleteConfiguration()
    {
        var manager = new SettingsManager(_directory);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(i => manager.SaveGameConnectionAsync(
            new() { Source = GameCaptureSource.Adb, AdbSerial = $"device-{i}" })));
        (await manager.LoadGameConnectionAsync()).AdbSerial.Should().StartWith("device-");
    }

    [Fact]
    public void InvalidAdbPathReturnsNoFrameAndRetriesWithoutThrowing()
    {
        using var connection = new GameConnectionService(new() { Source = GameCaptureSource.Adb,
            AdbPath = Path.Combine(_directory, "missing-adb.exe"), AdbSerial = "offline" });
        var capture = new ScreenCaptureService(connection);
        capture.CaptureScreen().Should().BeNull();
        connection.Status.Should().Contain("adb.exe");
        capture.CaptureScreen().Should().BeNull();
    }

    [Fact]
    public void ReplacingConnectionDisposesPreviousCaptureSource()
    {
        using var previous = new GameConnectionService(new() { Source = GameCaptureSource.Adb });
        var capture = new ScreenCaptureService(previous);
        using var next = new GameConnectionService();
        capture.SetConnection(next);
        capture.Connection.Should().BeSameAs(next);
        previous.CaptureAdbScreen(1920).Should().BeNull();
    }

    [Theory]
    [InlineData(1920, 1080, true)]
    [InlineData(1280, 720, true)]
    [InlineData(2560, 1440, true)]
    [InlineData(1080, 1920, false)]
    [InlineData(2560, 1080, false)]
    [InlineData(1920, 1200, false)]
    [InlineData(0, 0, false)]
    public void AdbAspectRatioIsValidatedBeforeOcr(int width, int height, bool supported)
        => WindowGeometry.IsSupportedGameAspectRatio(width, height).Should().Be(supported);

    [Theory]
    [InlineData(0, 0, 1600, 1000, 0, 50, 1600, 950)]
    [InlineData(-1920, 100, 0, 1180, -1920, 100, 0, 1180)]
    [InlineData(120, 180, 1540, 980, 120, 180, 1540, 979)]
    public void ViewportKeepsPhysicalPixelCoordinatesAndLetterboxing(
        int left, int top, int right, int bottom, int expectedLeft, int expectedTop, int expectedRight, int expectedBottom)
    {
        var viewport = WindowGeometry.FitAspectRatio(new RECT { Left = left, Top = top, Right = right, Bottom = bottom }, 16, 9);
        viewport.Should().BeEquivalentTo(new RECT { Left = expectedLeft, Top = expectedTop, Right = expectedRight, Bottom = expectedBottom });
        var marker = WindowGeometry.ShopMarkerPosition((.5, .9, .1, .03), viewport.Width / 1.5, viewport.Height / 1.5, 27);
        marker.X.Should().BeApproximately(viewport.Width / 1.5 * .6 - 27, .001);
        marker.Y.Should().BeApproximately(viewport.Height / 1.5 * .9 - 29, .001);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
