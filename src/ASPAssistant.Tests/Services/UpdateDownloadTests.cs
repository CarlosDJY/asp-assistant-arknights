// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

[Collection("Update environment")]
public class UpdateDownloadTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task LocalFeed_ChecksDownloadsVerifiesAndPreparesWithoutLaunching(bool wrapped, bool wrongHash)
    {
        using var zipStream = new MemoryStream();
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var prefix = wrapped ? "win-x64/" : "";
            foreach (var name in new[] { "ASPAssistant.App.exe", "data/test.json", "runtimes/test.txt" })
            {
                await using var writer = zip.CreateEntry(prefix + name).Open();
                await writer.WriteAsync(Encoding.UTF8.GetBytes("isolated download fixture; never executed"));
            }
        }
        var zipBytes = zipStream.ToArray();
        const string asset = "hongxue-stronghold-companion-v99.0.0-win-x64.zip";
        var hash = wrongHash ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(zipBytes)).ToLowerInvariant();
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var url = $"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/";
        var release = Encoding.UTF8.GetBytes($$"""
            {"tag_name":"v99.0.0","body":"isolated test","assets":[
              {"name":"{{asset}}","browser_download_url":"{{url}}package"},
              {"name":"SHA256SUMS.txt","browser_download_url":"{{url}}checksums"}]}
            """);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var serving = Task.Run(async () =>
        {
            for (var i = 0; i < 3; i++)
            {
                using var client = await server.AcceptTcpClientAsync(timeout.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var request = (await reader.ReadLineAsync(timeout.Token))!.Split(' ')[1];
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token))) { }
                var body = request switch
                {
                    "/" => release,
                    "/package" => zipBytes,
                    "/checksums" => Encoding.ASCII.GetBytes($"{hash}  {asset}\n"),
                    _ => throw new InvalidOperationException(request),
                };
                var contentType = request == "/" ? "application/json" : "application/octet-stream";
                // 不发送包的 Content-Length，同时覆盖未知下载长度的进度路径。
                var length = request == "/package" ? "" : $"Content-Length: {body.Length}\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\n{length}Connection: close\r\n\r\n"), timeout.Token);
                await stream.WriteAsync(body, timeout.Token);
            }
        });
        var previousFeed = Environment.GetEnvironmentVariable(UpdateService.FeedOverrideVariable);
        string? job = null;
        var beforeJobs = Directory.GetDirectories(Path.GetTempPath(), "ASPAssistant-Update-*").ToHashSet();
        try
        {
            Environment.SetEnvironmentVariable(UpdateService.FeedOverrideVariable, url);
            var service = new UpdateService();
            var info = await service.CheckForUpdateAsync(timeout.Token);
            info.Should().NotBeNull();
            if (wrongHash)
            {
                Func<Task> download = () => service.DownloadAndApplyUpdateAsync(info!, ct: timeout.Token);
                await download.Should().ThrowAsync<UpdateIntegrityException>();
                service.PendingScriptPath.Should().BeNull();
                service.LastError.Should().Contain("校验不通过");
            }
            else
            {
                await service.DownloadAndApplyUpdateAsync(info!, ct: timeout.Token);
                job = Path.GetDirectoryName(service.PendingScriptPath)!;
                File.Exists(service.PendingScriptPath).Should().BeTrue();
                Directory.Exists(service.PendingStagingDir).Should().BeFalse("preparation must not replace the running installation");
                var script = await File.ReadAllTextAsync(service.PendingScriptPath!);
                script.Should().Contain("$startupTimeout = 60").And.Contain("ASPA_UPDATE_STARTUP_RESULT");
                var source = wrapped ? Path.Combine(job, "extracted", "win-x64") : Path.Combine(job, "extracted");
                File.Exists(Path.Combine(source, "ASPAssistant.App.exe")).Should().BeTrue();
                File.ReadAllBytes(service.PendingScriptPath!).Take(3).Should().Equal(new byte[] { 0xef, 0xbb, 0xbf });
            }
            await serving;
        }
        finally
        {
            Environment.SetEnvironmentVariable(UpdateService.FeedOverrideVariable, previousFeed);
            timeout.Cancel();
            server.Stop();
            foreach (var directory in Directory.GetDirectories(Path.GetTempPath(), "ASPAssistant-Update-*").Except(beforeJobs))
            {
                Path.GetDirectoryName(Path.GetFullPath(directory))!.TrimEnd('\\')
                    .Should().Be(Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\'));
                Directory.Delete(directory, true);
            }
        }
    }
}
