// Added for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.Windows.Input;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App;

/// <summary>Shows and records <see cref="Hotkey"/>s: “Ctrl + Shift + H”, “未设置”.</summary>
public static class HotkeyText
{
    public static string Describe(Hotkey key)
    {
        if (key.IsNone) return "未设置";
        var name = KeyName(KeyInterop.KeyFromVirtualKey((int)key.Key));
        return key.Modifiers == 0 ? name : $"{DescribeModifiers(key.Modifiers)} + {name}";
    }

    public static string DescribeModifiers(uint modifiers)
    {
        var parts = new List<string>();
        if ((modifiers & Hotkey.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & Hotkey.Alt) != 0) parts.Add("Alt");
        if ((modifiers & Hotkey.Shift) != 0) parts.Add("Shift");
        if ((modifiers & Hotkey.Win) != 0) parts.Add("Win");
        return string.Join(" + ", parts);
    }

    public static uint Modifiers(ModifierKeys keys)
        => ((keys & ModifierKeys.Control) != 0 ? Hotkey.Control : 0)
         | ((keys & ModifierKeys.Alt) != 0 ? Hotkey.Alt : 0)
         | ((keys & ModifierKeys.Shift) != 0 ? Hotkey.Shift : 0)
         | ((keys & ModifierKeys.Windows) != 0 ? Hotkey.Win : 0);

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None;

    public static bool IsFunctionKey(Key key) => key is >= Key.F1 and <= Key.F24;

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => $"Num {key - Key.NumPad0}",
        Key.Oem2 => "/",
        Key.OemPeriod => ".",
        Key.OemComma => ",",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.Oem1 => ";",
        Key.Oem7 => "'",
        Key.OemOpenBrackets => "[",
        Key.Oem6 => "]",
        Key.Oem5 => "\\",
        Key.Oem3 => "`",
        Key.Space => "Space",
        Key.Return => "Enter",
        Key.Prior => "PageUp",
        Key.Next => "PageDown",
        _ => key.ToString(),
    };
}
