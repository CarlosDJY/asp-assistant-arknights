// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
using System.Text;

namespace ASPAssistant.Core.Services;

/// <summary>仅更新器启动的实例向本次临时任务回报启动结果，普通启动不写文件。</summary>
public sealed class UpdateStartupNotification
{
    public const string ResultVariable = "ASPA_UPDATE_STARTUP_RESULT";
    private string? _resultPath;

    public UpdateStartupNotification(string? resultPath)
    {
        if (string.IsNullOrEmpty(resultPath)) return;
        var fullPath = Path.GetFullPath(resultPath);
        var directory = Path.GetDirectoryName(fullPath)!;
        var name = Path.GetFileNameWithoutExtension(fullPath);
        if (!Path.IsPathFullyQualified(resultPath)
            || !string.Equals(Path.GetDirectoryName(directory)?.TrimEnd('\\', '/'), Path.GetTempPath().TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(directory).StartsWith("ASPAssistant-Update-", StringComparison.Ordinal)
            || Path.GetExtension(fullPath) != ".status" || !name.StartsWith("startup-", StringComparison.Ordinal)
            || !Guid.TryParseExact(name["startup-".Length..], "N", out _))
            throw new InvalidDataException("更新启动确认路径无效");
        _resultPath = fullPath;
    }

    public bool IsPending => _resultPath is not null;

    public void ReportReady() => Report("ready");

    public void ReportFailure(string message) => Report("failed\n" + message);

    private void Report(string status)
    {
        var path = Interlocked.Exchange(ref _resultPath, null);
        if (path is null) return;
        try
        {
            File.WriteAllText(path + ".tmp", status, new UTF8Encoding(true));
            File.Move(path + ".tmp", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时目录被清理或拦截时，更新器会保留备份；不因此关闭已经启动的程序。
            AppLogger.Warn("Update", $"无法回报更新启动状态：{ex.Message}");
        }
    }
}
