// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

/// <summary>
/// Side panel look: one of the three mode themes (终端 / 乌萨斯 / 可露希尔), each with a day and a night palette.
/// Defaults: 终端 日间, chosen on its own (not following the 模式主题 used for shop recognition).
/// </summary>
public sealed class SidebarAppearance
{
    /// <summary>Use the 模式主题 (shop recognition theme) as the side panel theme.</summary>
    [JsonPropertyName("followModeTheme")]
    public bool FollowModeTheme { get; set; }

    /// <summary>Side panel theme when <see cref="FollowModeTheme"/> is off.</summary>
    [JsonPropertyName("theme")]
    public GameShopTheme Theme { get; set; } = GameShopTheme.Terminal;

    /// <summary>Night palette per theme; missing entries use <see cref="DefaultNight"/>.</summary>
    [JsonPropertyName("night")]
    public Dictionary<GameShopTheme, bool> Night { get; set; } = [];

    /// <summary>Every theme starts in its day palette.</summary>
    public static bool DefaultNight(GameShopTheme theme) => false;

    public GameShopTheme ThemeFor(GameShopTheme modeTheme) => FollowModeTheme ? modeTheme : Theme;

    public bool IsNight(GameShopTheme theme) => Night.TryGetValue(theme, out var night) ? night : DefaultNight(theme);
}
