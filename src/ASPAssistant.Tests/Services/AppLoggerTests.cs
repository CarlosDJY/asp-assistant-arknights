using System.Reflection;
using ASPAssistant.Core;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

[CollectionDefinition("Application logging", DisableParallelization = true)]
public class ApplicationLoggingCollection;

[Collection("Application logging")]
public class AppLoggerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aspa_logging_{Guid.NewGuid():N}");
    private readonly FieldInfo _pathField = typeof(AppLogger).GetField("_logFilePath", BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly string? _previousPath;
    private readonly Func<bool> _previousPresenceCheck;
    private string LogPath => Directory.GetFiles(_directory, "aspa-*.log").Single();

    public AppLoggerTests()
    {
        _previousPath = (string?)_pathField.GetValue(null);
        _previousPresenceCheck = (Func<bool>)typeof(AppLogger).GetField("_isGamePresent", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        AppLogger.SetGamePresenceCheck(static () => true);
        AppLogger.Initialize(_directory);
    }

    [Fact]
    public void StartupInformationRemainsBeforeGamePresenceCheckIsInstalled()
    {
        AppLogger.Info("App", "Startup complete");
        AppLogger.SetGamePresenceCheck(static () => false);
        AppLogger.Info("ScreenCapture", "Arknights window not found");

        var text = File.ReadAllText(LogPath);
        text.Should().Contain("===== ASPAssistant").And.Contain("Startup complete");
        text.Should().NotContain("Arknights window not found");
    }

    [Fact]
    public void MissingGameStopsRoutineFilesAndGameStartResumesThem()
    {
        var present = false;
        AppLogger.SetGamePresenceCheck(() => present);
        var before = File.ReadAllText(LogPath);

        Parallel.For(0, 100, _ =>
        {
            AppLogger.Info("Overlay", "idle update");
            AppLogger.Warn("ScreenCapture", "Arknights window not found");
        });
        AppLogger.SaveArtifact("absent", [1, 2, 3], ".png").Should().BeNull();
        File.ReadAllText(LogPath).Should().Be(before);
        Directory.GetFiles(_directory).Should().ContainSingle();

        present = true;
        AppLogger.Info("ShopScan", "game started");
        AppLogger.Warn("ScreenCapture", "foreground mismatch");
        var artifact = AppLogger.SaveArtifact("present", [1, 2, 3], ".png");
        File.ReadAllBytes(artifact!).Should().Equal(1, 2, 3);
        var activeText = File.ReadAllText(LogPath);
        activeText.Should().Contain("game started").And.Contain("foreground mismatch");

        present = false;
        AppLogger.Info("Overlay", "game exited");
        AppLogger.Warn("ScreenCapture", "Arknights window not found");
        AppLogger.SaveArtifact("exited", [4, 5], ".png").Should().BeNull();
        File.ReadAllText(LogPath).Should().Be(activeText);
        Directory.GetFiles(_directory).Should().HaveCount(2);

        present = true;
        AppLogger.Info("ShopScan", "game restarted");
        File.ReadAllText(LogPath).Should().Contain("game restarted");
    }

    [Fact]
    public void MissingGameStillRecordsErrorsAndExceptionDetails()
    {
        AppLogger.SetGamePresenceCheck(static () => false);

        AppLogger.Error("App", "critical failure");
        AppLogger.Error("OcrScanner", "scan failed", new InvalidOperationException("test exception details"));

        var text = File.ReadAllText(LogPath);
        text.Should().Contain("[ERROR] [App] critical failure");
        text.Should().Contain("[ERROR] [OcrScanner] scan failed: System.InvalidOperationException: test exception details");
    }

    public void Dispose()
    {
        AppLogger.SetGamePresenceCheck(_previousPresenceCheck);
        _pathField.SetValue(null, _previousPath);
        Path.GetDirectoryName(Path.GetFullPath(_directory)).Should().Be(Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\', '/'));
        Directory.Delete(_directory, true);
    }
}
