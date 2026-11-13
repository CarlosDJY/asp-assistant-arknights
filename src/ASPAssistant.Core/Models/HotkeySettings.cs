// Added for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

/// <summary>A global hotkey: RegisterHotKey modifier flags (MOD_ALT 1, MOD_CONTROL 2, MOD_SHIFT 4, MOD_WIN 8) and a virtual-key code; key 0 = not used.</summary>
public sealed record Hotkey(
    [property: JsonPropertyName("modifiers")] uint Modifiers,
    [property: JsonPropertyName("key")] uint Key)
{
    public const uint Alt = 0x1, Control = 0x2, Shift = 0x4, Win = 0x8;
    public static Hotkey None { get; } = new(0, 0);
    [JsonIgnore] public bool IsNone => Key == 0;
}

/// <summary>The side panel's global hotkeys, changeable in 设置.</summary>
public sealed record HotkeySettings
{
    /// <summary>Ctrl+Shift+/ (VK_OEM_2).</summary>
    public static Hotkey DefaultManualScan { get; } = new(Hotkey.Control | Hotkey.Shift, 0xBF);
    /// <summary>Ctrl+Shift+H.</summary>
    public static Hotkey DefaultTogglePanel { get; } = new(Hotkey.Control | Hotkey.Shift, 0x48);

    /// <summary>截屏录入 the run info page.</summary>
    [JsonPropertyName("manualScan")]
    public Hotkey ManualScan { get; init; } = DefaultManualScan;

    /// <summary>收起 / 展开 the side panel.</summary>
    [JsonPropertyName("togglePanel")]
    public Hotkey TogglePanel { get; init; } = DefaultTogglePanel;
}
