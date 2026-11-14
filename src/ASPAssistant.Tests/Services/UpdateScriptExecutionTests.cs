using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ASPAssistant.Core.Services;
using FluentAssertions;
using Xunit.Abstractions;

namespace ASPAssistant.Tests.Services;

[Collection("Update environment")]
public class UpdateScriptExecutionTests(ITestOutputHelper output) : IAsyncLifetime
{
    private readonly string _root = Directory.CreateTempSubdirectory("ASPAssistant-UpdateTests-").FullName;
    private Process? _launcher;
    private bool _launcherStarted;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // xUnit reports disposal failures separately, preserving the original assertion/timeout.
        if (_launcher is not null)
        {
            try
            {
                if (!_launcher.HasExited) _launcher.Kill(entireProcessTree: true);
                await _launcher.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally { _launcher.Dispose(); }
        }
        else if (_launcherStarted)
        {
            // No PID was received: a slow-starting helper may still use this directory.
            output.WriteLine($"Launcher did not report its PID; retained diagnostics at {_root}");
            return;
        }
        DeleteTestDirectory(_root);
    }

    private static readonly string PowerShell = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    [Fact]
    public async Task GeneratedScript_ParsesInWindowsPowerShell()
    {
        var root = _root;
        var script = Path.Combine(root, "update.ps1");
        await File.WriteAllTextAsync(script, UpdateService.BuildUpdateScript(
            int.MaxValue, Path.Combine(root, "source"), Path.Combine(root, "app"),
            Path.Combine(root, "app", "ASPAssistant.App.exe"), Path.Combine(root, "staging"),
            Path.Combine(root, "backup"), Path.Combine(root, "update.log")), new UTF8Encoding(true));
        var parser = Path.Combine(root, "parse.ps1");
        await File.WriteAllTextAsync(parser, """
            $tokens = $null
            $errors = $null
            [System.Management.Automation.Language.Parser]::ParseFile($args[0], [ref]$tokens, [ref]$errors) | Out-Null
            if ($errors.Count -gt 0) { $errors | ForEach-Object { [Console]::Error.WriteLine($_.Message) }; exit 1 }
            """);
        var result = await RunPowerShellAsync(parser, root, script);
        result.ExitCode.Should().Be(0, result.Output);
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("中文 空格 & [目录] O'Neil $测试")]
    public async Task Update_ReleasesInheritedWorkingDirectoryAndStartsNewVersion(string folder)
    {
        var root = _root;
        var (app, source) = await PrepareFilesAsync(root, folder);
        var result = await RunUpdateAsync(root, app, source);
        result.ExitCode.Should().Be(0, result.Output);
        (await File.ReadAllTextAsync(Path.Combine(app, "version.txt"))).Should().Be("new");
        File.Exists(Path.Combine(app, "new-only.txt")).Should().BeTrue();
        File.Exists(Path.Combine(app, "old-only.txt")).Should().BeFalse();
        await AssertStartedAsync(app, "new");
        (await File.ReadAllTextAsync(Path.Combine(root, "update.log"))).Should().Contain("activated new version");
        Directory.Exists(Path.Combine(root, "backup")).Should().BeFalse();
    }

    [Fact]
    public async Task LockedDirectory_DoesNotPartiallyOverwriteOldVersion()
    {
        var root = _root;
        var (app, source) = await PrepareFilesAsync(root, "locked");
        var start = new ProcessStartInfo(Path.Combine(app, "ASPAssistant.App.exe"))
        {
            UseShellExecute = false, WorkingDirectory = app,
        };
        start.ArgumentList.Add("hold");
        using var holder = Process.Start(start)!;
        try
        {
            await WaitForFileAsync(Path.Combine(app, "held.ready"));
            var result = await RunUpdateAsync(root, app, source);
            result.ExitCode.Should().Be(1, result.Output);
            (await File.ReadAllTextAsync(Path.Combine(app, "version.txt"))).Should().Be("old");
            File.Exists(Path.Combine(app, "old-only.txt")).Should().BeTrue();
            File.Exists(Path.Combine(app, "new-only.txt")).Should().BeFalse();
            await AssertStartedAsync(app, "old");
            (await File.ReadAllTextAsync(Path.Combine(root, "update.log"))).Should().Contain("rename attempt 10 failed");
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(app, "release.hold"), "release");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await holder.WaitForExitAsync(timeout.Token);
        }
    }

    [Fact]
    public async Task NewVersionLaunchFails_RestoresCompleteOldDirectory()
    {
        var root = _root;
        var (app, source) = await PrepareFilesAsync(root, "rollback");
        const string failNewLaunch = """
            function Start-Version([string]$path) {
                if ((Get-Content -LiteralPath (Join-Path $appDir 'version.txt') -Raw) -eq 'new') {
                    throw 'simulated new-version launch failure'
                }
                $start = New-Object System.Diagnostics.ProcessStartInfo
                $start.FileName = $path
                $start.WorkingDirectory = $appDir
                $start.UseShellExecute = $false
                $process = [System.Diagnostics.Process]::Start($start)
                return $process
            }

            """;
        var result = await RunUpdateAsync(root, app, source, prefix: failNewLaunch);
        result.ExitCode.Should().Be(1, result.Output);
        (await File.ReadAllTextAsync(Path.Combine(app, "version.txt"))).Should().Be("old");
        File.Exists(Path.Combine(app, "old-only.txt")).Should().BeTrue();
        File.Exists(Path.Combine(app, "new-only.txt")).Should().BeFalse();
        await AssertStartedAsync(app, "old");
        (await File.ReadAllTextAsync(Path.Combine(root, "update.log"))).Should().Contain("rolled back to previous version");
    }

    [Fact]
    public async Task OldProcessStillRunning_DoesNotReplaceFilesOrStartAnotherInstance()
    {
        var root = _root;
        var (app, source) = await PrepareFilesAsync(root, "waiting");
        var result = await RunUpdateAsync(root, app, source, pid: Environment.ProcessId, shortWait: true);
        result.ExitCode.Should().Be(1, result.Output);
        (await File.ReadAllTextAsync(Path.Combine(app, "version.txt"))).Should().Be("old");
        File.Exists(Path.Combine(app, "new-only.txt")).Should().BeFalse();
        File.Exists(Path.Combine(app, "started.log")).Should().BeFalse();
        (await File.ReadAllTextAsync(Path.Combine(root, "update.log"))).Should().Contain("请完全退出辅助");
    }

    [Theory]
    [InlineData("exit", "未确认就绪就已退出")]
    [InlineData("failed", "新版启动失败")]
    public async Task NewVersionFailsBeforeReady_RestoresOldVersion(string mode, string expectedError)
    {
        var root = _root;
        var (app, source) = await PrepareFilesAsync(root, "early-failure");
        await File.WriteAllTextAsync(Path.Combine(source, "startup-mode.txt"), mode);
        var result = await RunUpdateAsync(root, app, source);
        result.ExitCode.Should().Be(1, result.Output);
        (await File.ReadAllTextAsync(Path.Combine(app, "version.txt"))).Should().Be("old");
        File.Exists(Path.Combine(app, "old-only.txt")).Should().BeTrue();
        File.Exists(Path.Combine(app, "new-only.txt")).Should().BeFalse();
        await AssertStartedAsync(app, "old");
        (await File.ReadAllTextAsync(Path.Combine(root, "update.log"))).Should()
            .Contain(expectedError).And.Contain("rolled back to previous version");
    }

    [Fact]
    public async Task NewVersionRunningWithoutReady_KeepsBackupWithoutStoppingIt()
    {
        var root = _root;
        var (app, source) = await PrepareFilesAsync(root, "unconfirmed");
        await File.WriteAllTextAsync(Path.Combine(source, "startup-mode.txt"), "no-result");
        var result = await RunUpdateAsync(root, app, source, shortWait: true);
        result.ExitCode.Should().Be(1, result.Output);
        (await File.ReadAllTextAsync(Path.Combine(app, "version.txt"))).Should().Be("new");
        (await File.ReadAllTextAsync(Path.Combine(root, "backup", "version.txt"))).Should().Be("old");
        (await File.ReadAllTextAsync(Path.Combine(root, "update.log"))).Should()
            .Contain("等待新版启动就绪超时").And.Contain("未执行回滚").And.NotContain("removed backup");
    }

    [Fact]
    public async Task RestartLauncher_UsesScriptDirectoryInsteadOfInheritingInstallDirectory()
    {
        var root = _root;
        var script = Path.Combine(root, "launcher.ps1");
        await File.WriteAllTextAsync(script, """
            $ErrorActionPreference = 'Stop'
            $result = @{ cwd = [Environment]::CurrentDirectory; pid = $PID } | ConvertTo-Json
            $marker = Join-Path $PSScriptRoot 'launched.json'
            [IO.File]::WriteAllText($marker + '.tmp', $result)
            [IO.File]::Move($marker + '.tmp', $marker)
            $deadline = [Diagnostics.Stopwatch]::StartNew()
            while (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'release.launcher')) -and $deadline.Elapsed.TotalSeconds -lt 30) {
                Start-Sleep -Milliseconds 25
            }
            """);
        var service = new UpdateService();
        typeof(UpdateService).GetProperty(nameof(UpdateService.PendingScriptPath))!.SetValue(service, script);
        _launcherStarted = service.LaunchPendingUpdateScript();
        _launcherStarted.Should().BeTrue();
        var marker = Path.Combine(root, "launched.json");
        await WaitForFileAsync(marker);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(marker));
        // The helper waits for release, keeping its PID alive until we acquire the handle.
        _launcher = Process.GetProcessById(json.RootElement.GetProperty("pid").GetInt32());
        json.RootElement.GetProperty("cwd").GetString().Should().Be(root);
        await File.WriteAllTextAsync(Path.Combine(root, "release.launcher"), "release");
        await _launcher.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        _launcher.ExitCode.Should().Be(0, "the test launcher must exit normally after release");
    }

    private static async Task<(string App, string Source)> PrepareFilesAsync(string root, string folder)
    {
        var compiler = Path.Combine(root, "compile.ps1");
        await File.WriteAllTextAsync(compiler, """
            $ErrorActionPreference = 'Stop'
            Add-Type -OutputType WindowsApplication -OutputAssembly (Join-Path $PSScriptRoot 'test-host.exe') -TypeDefinition @'
            using System;
            using System.IO;
            public static class TestUpdateHost {
                public static void Main(string[] args) {
                    var root = AppDomain.CurrentDomain.BaseDirectory;
                    if (args.Length > 0) {
                        File.WriteAllText(Path.Combine(root, "held.ready"), "ready");
                        var wait = System.Diagnostics.Stopwatch.StartNew();
                        while (!File.Exists(Path.Combine(root, "release.hold")) && wait.ElapsedMilliseconds < 30000)
                            System.Threading.Thread.Sleep(25);
                        return;
                    }
                    File.WriteAllText(Path.Combine(root, "started.log"),
                        File.ReadAllText(Path.Combine(root, "version.txt")) + "|" + Environment.CurrentDirectory);
                    if (File.ReadAllText(Path.Combine(root, "version.txt")) == "new") {
                        var modePath = Path.Combine(root, "startup-mode.txt");
                        var mode = File.Exists(modePath) ? File.ReadAllText(modePath) : "ready";
                        if (mode == "exit") Environment.Exit(17);
                        var resultPath = Environment.GetEnvironmentVariable("ASPA_UPDATE_STARTUP_RESULT");
                        if (mode == "failed") {
                            File.WriteAllText(resultPath + ".tmp", "failed\nmock startup failure", System.Text.Encoding.UTF8);
                            File.Move(resultPath + ".tmp", resultPath);
                            Environment.Exit(18);
                        }
                        File.WriteAllText(Path.Combine(root, "new.running"), "running");
                        if (mode == "ready") {
                            File.WriteAllText(resultPath + ".tmp", "ready", System.Text.Encoding.UTF8);
                            File.Move(resultPath + ".tmp", resultPath);
                        }
                        var wait = System.Diagnostics.Stopwatch.StartNew();
                        while (!File.Exists(Path.Combine(root, "release.new")) && wait.ElapsedMilliseconds < 30000)
                            System.Threading.Thread.Sleep(25);
                        File.WriteAllText(Path.Combine(root, "new.exited"), "exited");
                    }
                }
            }
            '@
            """);
        var result = await RunPowerShellAsync(compiler, root);
        result.ExitCode.Should().Be(0, result.Output);
        var app = Directory.CreateDirectory(Path.Combine(root, folder)).FullName;
        var source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
        foreach (var directory in new[] { app, source })
            File.Copy(Path.Combine(root, "test-host.exe"), Path.Combine(directory, "ASPAssistant.App.exe"));
        await File.WriteAllTextAsync(Path.Combine(app, "version.txt"), "old");
        await File.WriteAllTextAsync(Path.Combine(app, "old-only.txt"), "old");
        await File.WriteAllTextAsync(Path.Combine(source, "version.txt"), "new");
        await File.WriteAllTextAsync(Path.Combine(source, "new-only.txt"), "new");
        return (app, source);
    }

    private static async Task<(int ExitCode, string Output)> RunUpdateAsync(
        string root, string app, string source, int pid = int.MaxValue, bool shortWait = false, string prefix = "")
    {
        var script = UpdateService.BuildUpdateScript(pid, source, app, Path.Combine(app, "ASPAssistant.App.exe"),
            Path.Combine(root, "staging"), Path.Combine(root, "backup"), Path.Combine(root, "update.log"));
        if (shortWait) script = script.Replace("$timeout = 300", "$timeout = 1")
            .Replace("$startupTimeout = 60", "$startupTimeout = 1");
        var path = Path.Combine(root, "update.ps1");
        script = script.Replace("Write-Log \"update started (pid=$appPid)\"", prefix + "\nWrite-Log \"update started (pid=$appPid)\"");
        await File.WriteAllTextAsync(path, script, new UTF8Encoding(true));
        var result = await RunPowerShellAsync(path, app);
        if (File.Exists(Path.Combine(app, "new.running")))
        {
            await File.WriteAllTextAsync(Path.Combine(app, "release.new"), "release");
            await WaitForFileAsync(Path.Combine(app, "new.exited"));
        }
        return result;
    }

    private static async Task<(int ExitCode, string Output)> RunPowerShellAsync(string script, string workingDirectory, params string[] args)
    {
        var start = new ProcessStartInfo(PowerShell)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }.Concat(args))
            start.ArgumentList.Add(argument);
        start.Environment["ASPA_UPDATE_SILENT"] = "1";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true); // Only this test's own helper process.
            await process.WaitForExitAsync();
            throw new TimeoutException("The isolated update test did not finish.");
        }
        return (process.ExitCode, await output + await error);
    }

    private static async Task AssertStartedAsync(string app, string version)
    {
        var path = Path.Combine(app, "started.log");
        await WaitForFileAsync(path);
        (await File.ReadAllTextAsync(path)).Should().Be(version + "|" + app);
    }

    private static async Task WaitForFileAsync(string path)
    {
        var timeout = Stopwatch.StartNew();
        while (!File.Exists(path) && timeout.Elapsed < TimeSpan.FromSeconds(30))
            await Task.Delay(25);
        File.Exists(path).Should().BeTrue("the test-only helper must publish {0} within 30 seconds", path);
    }

    private static void DeleteTestDirectory(string root)
    {
        var path = Path.GetFullPath(root);
        if (!path.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(path).StartsWith("ASPAssistant-UpdateTests-", StringComparison.Ordinal))
            throw new InvalidOperationException("Update test cleanup escaped its temporary directory.");
        for (int attempt = 0; ; attempt++)
        {
            try { Directory.Delete(path, true); return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 20) { Thread.Sleep(100); }
        }
    }
}
