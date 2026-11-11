// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

/// <summary>
/// Side panel look: one of the three mode themes (终端 / 乌萨斯 / 可露希尔), each with a day and a night palette.
/// By default the look follows the 模式主题 used for shop recognition.
/// </summary>
public sealed class SidebarAppearance
{
    /// <summary>Use the 模式主题 (shop recognition theme) as the side panel theme.</summary>
    [JsonPropertyName("followModeTheme")]
    public bool FollowModeTheme { get; set; } = true;

    /// <summary>Side panel theme when <see cref="FollowModeTheme"/> is off.</summary>
    [JsonPropertyName("theme")]
    public GameShopTheme Theme { get; set; } = GameShopTheme.Terminal;

    /// <summary>Night palette per theme; missing entries use <see cref="DefaultNight"/>.</summary>
    [JsonPropertyName("night")]
    public Dictionary<GameShopTheme, bool> Night { get; set; } = [];

    /// <summary>终端 defaults to night, 乌萨斯 and 可露希尔 to day.</summary>
    public static bool DefaultNight(GameShopTheme theme) => theme == GameShopTheme.Terminal;

    public GameShopTheme ThemeFor(GameShopTheme modeTheme) => FollowModeTheme ? modeTheme : Theme;

    public bool IsNight(GameShopTheme theme) => Night.TryGetValue(theme, out var night) ? night : DefaultNight(theme);
}
