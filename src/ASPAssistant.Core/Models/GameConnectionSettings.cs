// Modified for ASP Assistant Arknights on 2026-11-11; see NOTICE.
using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<GameCaptureSource>))]
public enum GameCaptureSource { Pc, Adb }

public sealed record GameConnectionSettings
{
    public GameCaptureSource Source { get; init; }
    public string AdbPath { get; init; } = "";
    public string AdbSerial { get; init; } = "";
    public string WindowTitle { get; init; } = "MuMu安卓设备";
}
