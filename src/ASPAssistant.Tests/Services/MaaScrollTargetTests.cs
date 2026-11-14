using System.Text.Json;
using ASPAssistant.Core.Services;
using FluentAssertions;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Custom;
using Moq;

namespace ASPAssistant.Tests.Services;

[Collection("Native OCR")]
public class MaaScrollTargetTests
{
    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1600, 900)]
    [InlineData(3840, 2160)]
    public void ExplicitScrollTargetSurvivesTheNativeControllerWithoutClicking(int width, int height)
    {
        var moves = new List<(int X, int Y)>();
        var input = new Mock<IMaaCustomController>();
        input.SetupProperty(c => c.Name, "ScrollTargetProbe");
        input.Setup(c => c.Connect()).Returns(true);
        input.Setup(c => c.Connected()).Returns(true);
        input.Setup(c => c.RequestUuid(It.IsAny<IMaaStringBuffer>())).Returns(true);
        input.Setup(c => c.GetInfo(It.IsAny<IMaaStringBuffer>())).Returns(true);
        input.Setup(c => c.GetFeatures()).Returns(ControllerFeatures.None);
        using var frame = new System.Drawing.Bitmap(width, height);
        var bytes = ImageBytes.Encode(frame);
        input.Setup(c => c.Screencap(It.IsAny<IMaaImageBuffer>()))
            .Returns((IMaaImageBuffer buffer) => buffer.TrySetEncodedData(bytes));
        input.Setup(c => c.TouchMove(0, It.IsAny<int>(), It.IsAny<int>(), 0))
            .Callback<int, int, int, int>((_, x, y, _) => moves.Add((x, y))).Returns(true);
        input.Setup(c => c.Scroll(0, -120)).Returns(true);
        using var controller = new MaaCustomController(input.Object);
        controller.LinkStart().Wait().Should().Be(MaaJobStatus.Succeeded);
        controller.SetOption_ScreenshotUseRawSize(true).Should().BeTrue();
        controller.Screencap().Wait().Should().Be(MaaJobStatus.Succeeded);

        // The coordinate-free API discards the preceding hover target.
        controller.TouchMove(0, width / 2, height / 2, 0).Wait().Should().Be(MaaJobStatus.Succeeded);
        moves.Last().Should().Be((width / 2, height / 2));
        controller.Scroll(0, -120).Wait().Should().Be(MaaJobStatus.Succeeded);
        moves.Last().Should().Be((0, 0));
        moves.Clear();

        using var resource = new MaaResource();
        resource.AppendBundle(Path.Combine(AppContext.BaseDirectory, "data", "maa_resource"))
            .Wait().Should().Be(MaaJobStatus.Succeeded);
        using var tasker = new MaaTasker(controller, resource, DisposeOptions.None);
        ((IMaaTasker)tasker).AppendAction("Scroll", JsonSerializer.Serialize(new
            { target = new[] { width / 2, height / 2 }, dx = 0, dy = -120 }))
            .Wait().Should().Be(MaaJobStatus.Succeeded);
        moves.Should().Equal((width / 2, height / 2));
        input.Verify(c => c.TouchDown(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        input.Verify(c => c.Click(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }
}
