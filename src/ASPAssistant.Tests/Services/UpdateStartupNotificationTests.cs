// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class UpdateStartupNotificationTests
{
    [Fact]
    public void NormalStartup_DoesNotRequireAnUpdateTask()
    {
        var notification = new UpdateStartupNotification(null);
        notification.IsPending.Should().BeFalse();
        notification.ReportReady();
        notification.ReportFailure("ignored");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StartupResult_IsWrittenOnceWithUtf8Bom(bool ready)
    {
        var root = Directory.CreateTempSubdirectory("ASPAssistant-Update-").FullName;
        try
        {
            var path = Path.Combine(root, $"startup-{Guid.NewGuid():N}.status");
            var notification = new UpdateStartupNotification(path);
            notification.IsPending.Should().BeTrue();
            if (ready) notification.ReportReady();
            else notification.ReportFailure("缺少运行库");
            notification.IsPending.Should().BeFalse();
            var expected = ready ? "ready" : "failed\n缺少运行库";
            File.ReadAllText(path).Should().Be(expected);
            File.ReadAllBytes(path).Take(3).Should().Equal(new byte[] { 0xef, 0xbb, 0xbf });
            notification.ReportFailure("后续异常不改变启动结果");
            File.ReadAllText(path).Should().Be(expected);
            File.Exists(path + ".tmp").Should().BeFalse();
        }
        finally
        {
            Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                .Should().BeTrue();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ResultOutsideUpdateTask_IsRejectedBeforeWriting()
    {
        Action construct = () => new UpdateStartupNotification(Path.Combine(Path.GetTempPath(), "startup-invalid.status"));
        construct.Should().Throw<InvalidDataException>();
    }
}
