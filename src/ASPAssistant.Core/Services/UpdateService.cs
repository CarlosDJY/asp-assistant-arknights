// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ASPAssistant.Core.Services;

public record UpdateInfo(
    string TagName,
    string Version,
    string ReleaseNotes,
    string DownloadUrl,
    string AssetName,
    string? ChecksumsUrl);

/// <summary>Thrown when the downloaded package does not match the published SHA-256.</summary>
public class UpdateIntegrityException(string message) : Exception(message);

public partial class UpdateService
{
    /// <summary>
    /// 覆盖更新源的环境变量（仅用于本地验证更新流程，正式用户不设置就走 GitHub）。
    /// 值应为返回 GitHub Release JSON 结构的地址。
    /// </summary>
    public const string FeedOverrideVariable = "ASPA_UPDATE_FEED";

    public const string DefaultFeedUrl =
        "https://api.github.com/repos/bingyunhe/hongxue-stronghold-companion/releases/latest";

    private const string PackageSuffix = "-win-x64.zip";
    private const string ChecksumsAssetName = "SHA256SUMS.txt";
    private const string AppExecutableName = "ASPAssistant.App.exe";

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { { "User-Agent", "HongxueStrongholdCompanion-Updater/1.0" } }
    };

    public Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>
    /// 返回 true 表示当前是开发构建（版本号为 0.0.0），不进行更新检查。
    /// </summary>
    public bool IsDevBuild => CurrentVersion is { Major: 0, Minor: 0, Build: 0 };

    /// <summary>最近一次失败的人类可读原因；成功时清空。</summary>
    public string? LastError { get; private set; }

    /// <summary>已下载并暂存好的更新脚本；点了“立即重启”后才启动它。</summary>
    public string? PendingScriptPath { get; private set; }

    /// <summary>本次更新的暂存目录（唯一），便于失败时提示用户手动处理。</summary>
    public string? PendingStagingDir { get; private set; }

    public string FeedUrl => ResolveFeedUrl(Environment.GetEnvironmentVariable(FeedOverrideVariable));

    /// <summary>返回生效的更新源地址：环境变量优先，其次 GitHub。</summary>
    public static string ResolveFeedUrl(string? overrideUrl)
        => string.IsNullOrWhiteSpace(overrideUrl) ? DefaultFeedUrl : overrideUrl.Trim();

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        // Skip update check in dev / IDE builds where no real version is embedded
        if (IsDevBuild) return null;

        LastError = null;
        try
        {
            var release = await _http.GetFromJsonAsync<GitHubRelease>(FeedUrl, ct);
            if (release is null || string.IsNullOrEmpty(release.TagName))
            {
                LastError = "更新源返回的数据不完整";
                return null;
            }

            var latestVersion = TryParseVersion(release.TagName);
            if (latestVersion is null)
            {
                LastError = $"无法解析版本号 {release.TagName}";
                return null;
            }
            if (latestVersion <= CurrentVersion) return null;

            var asset = release.Assets.FirstOrDefault(a =>
                a.Name.EndsWith(PackageSuffix, StringComparison.OrdinalIgnoreCase));
            if (asset is null)
            {
                LastError = $"发布 {release.TagName} 中没有 {PackageSuffix} 包";
                return null;
            }

            var checksums = release.Assets.FirstOrDefault(a =>
                string.Equals(a.Name, ChecksumsAssetName, StringComparison.OrdinalIgnoreCase));

            return new UpdateInfo(
                release.TagName,
                latestVersion.ToString(),
                release.Body ?? string.Empty,
                asset.DownloadUrl,
                asset.Name,
                checksums?.DownloadUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            LastError = DescribeError(ex);
            return null;
        }
        catch (System.Text.Json.JsonException ex)
        {
            LastError = $"更新源返回的不是有效 JSON：{ex.Message}";
            return null;
        }
    }

    public async Task DownloadAndApplyUpdateAsync(
        UpdateInfo info,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        LastError = null;

        // 每次尝试用独立目录：上一次的 update.ps1 可能仍被 PowerShell 占用，
        // 复用固定目录会让 Directory.Delete 抛 IOException 而整条下载失败。
        CleanupStaleUpdateDirs();
        var tempDir = Directory.CreateTempSubdirectory("ASPAssistant-Update-").FullName;

        var zipPath = Path.Combine(tempDir, info.AssetName);

        // Download with progress reporting
        using (var response = await _http.GetAsync(
                   info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using var httpStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = File.Create(zipPath);
            var buffer = new byte[65536];
            long downloaded = 0;
            int bytesRead;
            while ((bytesRead = await httpStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                downloaded += bytesRead;
                if (totalBytes > 0)
                    progress?.Report((double)downloaded / totalBytes * 0.85);
            }
        }

        progress?.Report(0.87);

        // Integrity check against the published SHA256SUMS.txt (fail closed).
        var actualHash = await ComputeSha256Async(zipPath, ct);
        if (string.IsNullOrWhiteSpace(info.ChecksumsUrl))
            throw IntegrityFailure($"发布 {info.TagName} 缺少 {ChecksumsAssetName}，已停止更新");
        var checksumsText = await _http.GetStringAsync(info.ChecksumsUrl, ct);
        var expectedHash = ParseChecksum(checksumsText, info.AssetName)
            ?? throw IntegrityFailure($"校验文件中没有 {info.AssetName} 的记录，已停止更新");
        if (!ChecksumMatches(expectedHash, actualHash))
            throw IntegrityFailure(
                $"安装包校验不通过（期望 {expectedHash[..12]}…，实际 {actualHash[..12]}…），已停止更新");

        progress?.Report(0.92);

        // Extract zip
        var extractDir = Path.Combine(tempDir, "extracted");
        Directory.CreateDirectory(extractDir);
        ZipFile.ExtractToDirectory(zipPath, extractDir);

        progress?.Report(0.96);

        // Resolve the source directory inside the zip: release.yml zips publish/win-x64,
        // so the archive usually has a single root folder.
        var subDirs = Directory.GetDirectories(extractDir);
        var sourceDir = subDirs.Length == 1 ? subDirs[0] : extractDir;

        var appDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var parentDir = Path.GetDirectoryName(appDir) ?? appDir;
        var stamp = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var scriptPath = Path.Combine(tempDir, "update.ps1");

        var scriptContent = BuildUpdateScript(
            Environment.ProcessId,
            sourceDir,
            appDir,
            Path.Combine(appDir, AppExecutableName),
            Path.Combine(parentDir, $"ASPAssistant-Staging-{stamp}"),
            Path.Combine(parentDir, $"ASPAssistant-backup-{stamp}"),
            Path.Combine(tempDir, "update.log"));

        await File.WriteAllTextAsync(scriptPath, scriptContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ct);

        // 只记录脚本位置；真正的替换要等用户点“立即重启”后再启动，
        // 否则脚本会在程序仍运行时白等并误报“无法重命名/已回滚”。
        PendingScriptPath = scriptPath;
        PendingStagingDir = Path.Combine(parentDir, $"ASPAssistant-Staging-{stamp}");
        progress?.Report(1.0);
    }

    /// <summary>
    /// 启动已暂存的更新脚本（应在应用即将退出时调用）。
    /// 返回 false 表示没有可用的暂存脚本。
    /// </summary>
    public bool LaunchPendingUpdateScript()
    {
        if (string.IsNullOrWhiteSpace(PendingScriptPath) || !File.Exists(PendingScriptPath))
            return false;

        using var updater = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -NonInteractive -File \"{PendingScriptPath}\"",
            WorkingDirectory = Path.GetDirectoryName(PendingScriptPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        if (updater is null) return false;
        PendingScriptPath = null;
        return true;
    }

    /// <summary>尽力清理以前遗留的更新临时目录；失败不影响本次更新。</summary>
    private static void CleanupStaleUpdateDirs()
    {
        try
        {
            var root = Path.GetTempPath();
            foreach (var dir in Directory.GetDirectories(root, "ASPAssistant-Update-*"))
            {
                try
                {
                    // 保留最近一天的任务，避免重试下载时删除仍待重启的更新。
                    if (Directory.GetLastWriteTimeUtc(dir) >= DateTime.UtcNow.AddDays(-1)) continue;
                    Directory.Delete(dir, true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 上一次的 update.ps1 可能仍被 PowerShell 占用，留着即可。
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private UpdateIntegrityException IntegrityFailure(string message)
    {
        LastError = message;
        return new UpdateIntegrityException(message);
    }

    /// <summary>把异常翻译成用户和日志都能看懂的短句。</summary>
    public static string DescribeError(Exception ex) => ex switch
    {
        UpdateIntegrityException => ex.Message,
        TaskCanceledException => "连接更新源超时",
        HttpRequestException http when (int?)http.StatusCode == 403
            => "更新源拒绝了请求（可能是 GitHub 限流）",
        HttpRequestException http when http.StatusCode is not null
            => $"更新源返回 HTTP {(int)http.StatusCode}",
        HttpRequestException => "无法连接更新源（网络受限或代理拦截）",
        IOException io when io.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
            => "文件正被其他程序占用（可能上次更新残留），请关闭杀毒/资源管理器预览后重试",
        IOException => $"读写更新文件失败：{ex.Message}",
        _ => ex.Message
    };

    public static Version? TryParseVersion(string tag)
    {
        var text = (tag ?? "").Trim().TrimStart('v', 'V');
        return Version.TryParse(text, out var result) ? result : null;
    }

    /// <summary>从 SHA256SUMS 文本中取出指定文件的哈希（兼容 "hash  name" 与 "hash *name"）。</summary>
    public static string? ParseChecksum(string? checksumsText, string assetName)
    {
        if (string.IsNullOrWhiteSpace(checksumsText)) return null;

        var wanted = Path.GetFileName(assetName);
        foreach (var rawLine in checksumsText.Split('\n'))
        {
            var match = ChecksumLine().Match(rawLine.Trim().TrimEnd('\r'));
            if (!match.Success) continue;
            var fileName = Path.GetFileName(match.Groups["name"].Value.Trim());
            if (string.Equals(fileName, wanted, StringComparison.OrdinalIgnoreCase))
                return match.Groups["hash"].Value.ToLowerInvariant();
        }
        return null;
    }

    public static bool ChecksumMatches(string expected, string actual)
        => string.Equals(expected?.Trim(), actual?.Trim(), StringComparison.OrdinalIgnoreCase);

    public static async Task<string> ComputeSha256Async(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// 生成更新脚本：等旧进程退出 → 复制到 staging（与安装目录同盘）→ 旧目录改名备份 →
    /// staging 改名为正式目录 → 启动新版；任一步失败回滚并重新启动旧版。
    /// </summary>
    public static string BuildUpdateScript(
        int pid,
        string sourceDir,
        string appDir,
        string exePath,
        string stagingDir,
        string backupDir,
        string logPath)
    {
        static string Q(string value) => "'" + (value ?? "").Replace("'", "''") + "'";

        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine($"$appPid = {pid}");
        sb.AppendLine($"$sourceDir = {Q(sourceDir)}");
        sb.AppendLine($"$appDir = {Q(appDir)}");
        sb.AppendLine($"$stagingDir = {Q(stagingDir)}");
        sb.AppendLine($"$backupDir = {Q(backupDir)}");
        sb.AppendLine($"$exePath = {Q(exePath)}");
        sb.AppendLine($"$logPath = {Q(logPath)}");
        sb.AppendLine("$silent = ($env:ASPA_UPDATE_SILENT -eq '1')");
        sb.AppendLine();
        sb.AppendLine("function Write-Log([string]$message) {");
        sb.AppendLine("    try {");
        sb.AppendLine("        $line = \"$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $message\"");
        sb.AppendLine("        Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8");
        sb.AppendLine("    } catch { }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("function Show-Failure([string]$message) {");
        sb.AppendLine("    Write-Log \"FAILED: $message\"");
        sb.AppendLine("    if ($silent) { return }");
        sb.AppendLine("    try {");
        sb.AppendLine("        Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue");
        sb.AppendLine("        [System.Windows.Forms.MessageBox]::Show($message, '鸿雪·卫戍助手更新失败') | Out-Null");
        sb.AppendLine("    } catch { }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("function Start-Version([string]$path, [string]$resultPath) {");
        sb.AppendLine("    $start = New-Object System.Diagnostics.ProcessStartInfo");
        sb.AppendLine("    $start.FileName = $path");
        sb.AppendLine("    $start.WorkingDirectory = $appDir");
        sb.AppendLine("    $start.UseShellExecute = $false");
        sb.AppendLine("    $start.EnvironmentVariables.Remove('ASPA_UPDATE_STARTUP_RESULT')");
        sb.AppendLine("    if ($resultPath) { $start.EnvironmentVariables['ASPA_UPDATE_STARTUP_RESULT'] = $resultPath }");
        sb.AppendLine("    $process = [System.Diagnostics.Process]::Start($start)");
        sb.AppendLine("    if ($null -eq $process) { throw '无法启动辅助程序' }");
        sb.AppendLine("    return $process");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("function Start-OldVersion {");
        sb.AppendLine("    if (Get-Process -Id $appPid -ErrorAction SilentlyContinue) { return }");
        sb.AppendLine("    $oldExe = Join-Path -Path $appDir -ChildPath (Split-Path -Leaf $exePath)");
        sb.AppendLine("    if (Test-Path -LiteralPath $oldExe) {");
        sb.AppendLine("        Write-Log \"starting previous version: $oldExe\"");
        sb.AppendLine("        $oldProcess = Start-Version $oldExe ''");
        sb.AppendLine("        $oldProcess.Dispose()");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Write-Log \"update started (pid=$appPid)\"");
        sb.AppendLine("$renamedOld = $false");
        sb.AppendLine("$newProcess = $null");
        sb.AppendLine("try {");
        sb.AppendLine("    $appDir = [System.IO.Path]::GetFullPath($appDir).TrimEnd('\\')");
        sb.AppendLine("    $stagingDir = [System.IO.Path]::GetFullPath($stagingDir).TrimEnd('\\')");
        sb.AppendLine("    $backupDir = [System.IO.Path]::GetFullPath($backupDir).TrimEnd('\\')");
        sb.AppendLine("    $parentDir = Split-Path -Parent $appDir");
        sb.AppendLine("    if (-not $parentDir -or $appDir -eq $stagingDir -or $appDir -eq $backupDir -or $stagingDir -eq $backupDir -or");
        sb.AppendLine("        (Split-Path -Parent $stagingDir) -ne $parentDir -or (Split-Path -Parent $backupDir) -ne $parentDir) {");
        sb.AppendLine("        throw '更新目录路径不正确，已停止替换'");
        sb.AppendLine("    }");
        // 同时移动 PowerShell 位置和原生工作目录，释放继承的安装目录句柄。
        sb.AppendLine("    [Environment]::CurrentDirectory = Split-Path -Parent $logPath");
        sb.AppendLine("    Set-Location -LiteralPath ([Environment]::CurrentDirectory)");
        // 等待旧进程退出：用户可能点了“立即重启”却还在看托盘，给足 5 分钟。
        sb.AppendLine("    $timeout = 300");
        sb.AppendLine("    $elapsed = 0");
        sb.AppendLine("    while ((Get-Process -Id $appPid -ErrorAction SilentlyContinue) -and ($elapsed -lt $timeout)) {");
        sb.AppendLine("        Start-Sleep -Milliseconds 500");
        sb.AppendLine("        $elapsed += 0.5");
        sb.AppendLine("    }");
        sb.AppendLine("    Write-Log \"waited $elapsed s for pid $appPid to exit\"");
        sb.AppendLine("    if (Get-Process -Id $appPid -ErrorAction SilentlyContinue) {");
        sb.AppendLine("        throw '辅助仍在运行，请完全退出辅助（含托盘图标）后重新检查更新。'");
        sb.AppendLine("    }");
        sb.AppendLine("    Start-Sleep -Milliseconds 800");
        sb.AppendLine();
        sb.AppendLine("    if (-not (Test-Path -LiteralPath (Join-Path $sourceDir 'ASPAssistant.App.exe'))) {");
        sb.AppendLine("        throw '更新包不完整：缺少 ASPAssistant.App.exe'");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    if (Test-Path -LiteralPath $stagingDir) { Remove-Item -LiteralPath $stagingDir -Recurse -Force }");
        sb.AppendLine("    if (Test-Path -LiteralPath $backupDir) { Remove-Item -LiteralPath $backupDir -Recurse -Force }");
        sb.AppendLine("    Copy-Item -LiteralPath $sourceDir -Destination $stagingDir -Recurse -Force");
        sb.AppendLine("    Write-Log \"staged new files to $stagingDir\"");
        sb.AppendLine();
        sb.AppendLine("    $renamedOld = $false");
        sb.AppendLine("    for ($try = 1; $try -le 10; $try++) {");
        sb.AppendLine("        try {");
        sb.AppendLine("            Rename-Item -LiteralPath $appDir -NewName (Split-Path -Leaf $backupDir) -ErrorAction Stop");
        sb.AppendLine("            $renamedOld = $true");
        sb.AppendLine("            break");
        sb.AppendLine("        } catch {");
        sb.AppendLine("            Write-Log \"rename attempt $try failed: $($_.Exception.Message)\"");
        sb.AppendLine("            Start-Sleep -Milliseconds 500");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("    Write-Log \"renamed current install to ${backupDir}: $renamedOld\"");
        sb.AppendLine();
        sb.AppendLine("    if (-not $renamedOld) {");
        sb.AppendLine("        throw '安装目录仍被占用，未替换任何旧版文件。请完全退出辅助（含托盘图标），并关闭该目录的终端或预览后重试。'");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    try {");
        sb.AppendLine("        Rename-Item -LiteralPath $stagingDir -NewName (Split-Path -Leaf $appDir)");
        sb.AppendLine("        Write-Log \"activated new version\"");
        sb.AppendLine("    } catch {");
        sb.AppendLine("        Write-Log \"activation failed, rolling back: $($_.Exception.Message)\"");
        sb.AppendLine("        if ($renamedOld) { Rename-Item -LiteralPath $backupDir -NewName (Split-Path -Leaf $appDir) }");
        sb.AppendLine("        $renamedOld = $false");
        sb.AppendLine("        Start-OldVersion");
        sb.AppendLine("        Show-Failure \"无法切换新版本，已回滚到旧版本：$($_.Exception.Message)\"");
        sb.AppendLine("        exit 1");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    Start-Sleep -Milliseconds 400");
        sb.AppendLine("    $startupResult = Join-Path (Split-Path -Parent $logPath) ('startup-' + [Guid]::NewGuid().ToString('N') + '.status')");
        sb.AppendLine("    $newProcess = Start-Version $exePath $startupResult");
        sb.AppendLine("    $startupTimeout = 60");
        sb.AppendLine("    $deadline = [DateTime]::UtcNow.AddSeconds($startupTimeout)");
        sb.AppendLine("    while ($true) {");
        sb.AppendLine("        if (Test-Path -LiteralPath $startupResult) {");
        sb.AppendLine("            $status = Get-Content -LiteralPath $startupResult -Raw -Encoding UTF8");
        sb.AppendLine("            if ($status -eq 'ready') {");
        sb.AppendLine("                if ($newProcess.HasExited) { throw \"新版在确认就绪时已退出（代码 $($newProcess.ExitCode)）\" }");
        sb.AppendLine("                break");
        sb.AppendLine("            }");
        sb.AppendLine("            if ($status.StartsWith(\"failed`n\")) {");
        sb.AppendLine("                $newProcess.WaitForExit(5000) | Out-Null");
        sb.AppendLine("                throw ('新版启动失败：' + $status.Substring(7))");
        sb.AppendLine("            }");
        sb.AppendLine("            throw '新版启动确认内容无效'");
        sb.AppendLine("        }");
        sb.AppendLine("        if ($newProcess.HasExited) { throw \"新版未确认就绪就已退出（代码 $($newProcess.ExitCode)）\" }");
        sb.AppendLine("        if ([DateTime]::UtcNow -ge $deadline) { throw '等待新版启动就绪超时' }");
        sb.AppendLine("        Start-Sleep -Milliseconds 200");
        sb.AppendLine("    }");
        sb.AppendLine("    Write-Log 'new version confirmed ready'");
        sb.AppendLine();
        sb.AppendLine("    try {");
        sb.AppendLine("        Remove-Item -LiteralPath $backupDir -Recurse -Force");
        sb.AppendLine("        Write-Log \"removed backup $backupDir\"");
        sb.AppendLine("    } catch {");
        sb.AppendLine("        Write-Log \"backup kept at $backupDir ($($_.Exception.Message))\"");
        sb.AppendLine("    }");
        sb.AppendLine("} catch {");
        sb.AppendLine("    $message = $_.Exception.Message");
        sb.AppendLine("    if ($renamedOld -and $null -ne $newProcess -and -not $newProcess.HasExited) {");
        sb.AppendLine("        Show-Failure \"更新未确认完成：$message。新版进程仍在运行，未执行回滚；旧版备份保留在 $backupDir，请先检查辅助状态。\"");
        sb.AppendLine("        exit 1");
        sb.AppendLine("    }");
        sb.AppendLine("    if ($renamedOld) {");
        sb.AppendLine("        try {");
        sb.AppendLine("            if (Test-Path -LiteralPath $appDir) { Remove-Item -LiteralPath $appDir -Recurse -Force }");
        sb.AppendLine("            Rename-Item -LiteralPath $backupDir -NewName (Split-Path -Leaf $appDir)");
        sb.AppendLine("            $renamedOld = $false");
        sb.AppendLine("            Write-Log 'rolled back to previous version'");
        sb.AppendLine("        } catch {");
        sb.AppendLine("            Write-Log \"ROLLBACK FAILED: $($_.Exception.Message)\"");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("    if ($renamedOld) {");
        sb.AppendLine("        Show-Failure \"更新未完成且自动恢复失败：$message。请保留备份目录 $backupDir。\"");
        sb.AppendLine("    } else {");
        sb.AppendLine("        Start-OldVersion");
        sb.AppendLine("        Show-Failure \"更新未完成，旧版本文件已保留：$message\"");
        sb.AppendLine("    }");
        sb.AppendLine("    exit 1");
        sb.AppendLine("}");
        sb.AppendLine("finally {");
        sb.AppendLine("    if ($null -ne $newProcess) { $newProcess.Dispose() }");
        sb.AppendLine("}");
        sb.AppendLine("exit 0");
        return sb.ToString();
    }

    [GeneratedRegex(@"^(?<hash>[0-9a-fA-F]{64})\s+\*?(?<name>.+)$")]
    private static partial Regex ChecksumLine();

    private record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("assets")] List<GitHubAsset> Assets);

    private record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string DownloadUrl);
}
