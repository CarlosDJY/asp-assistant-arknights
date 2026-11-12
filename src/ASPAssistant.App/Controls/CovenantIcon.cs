// Added for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ASPAssistant.App.Converters;

namespace ASPAssistant.App.Controls;

/// <summary>
/// The game's covenant glyph (data/icons/covenants/&lt;name&gt;.png, white on transparent from PRTS) drawn in
/// <see cref="Control.Foreground"/>-like <see cref="Brush"/>, so it follows the theme: dark on day themes, light at night.
/// Falls back to the first character when the icon is missing.
/// </summary>
public sealed class CovenantIcon : Grid
{
    public static readonly DependencyProperty CovenantProperty = DependencyProperty.Register(
        nameof(Covenant), typeof(string), typeof(CovenantIcon), new PropertyMetadata("", (d, _) => ((CovenantIcon)d).Update()));

    public static readonly DependencyProperty BrushProperty = DependencyProperty.Register(
        nameof(Brush), typeof(Brush), typeof(CovenantIcon), new PropertyMetadata(null, (d, e) => ((CovenantIcon)d)._glyph.Background = (Brush?)e.NewValue));

    public string Covenant { get => (string)GetValue(CovenantProperty); set => SetValue(CovenantProperty, value); }

    /// <summary>Fill colour; defaults to the theme's primary text colour.</summary>
    public Brush? Brush { get => (Brush?)GetValue(BrushProperty); set => SetValue(BrushProperty, value); }

    private readonly Border _glyph = new();
    private readonly TextBlock _fallback = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold };

    public CovenantIcon()
    {
        SetResourceReference(BrushProperty, "TextPrimaryBrush");
        _fallback.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Children.Add(_glyph);
        Children.Add(_fallback);
        SizeChanged += (_, _) => _fallback.FontSize = Math.Max(8, Math.Round(ActualHeight * 0.6));
    }

    private void Update()
    {
        var image = string.IsNullOrEmpty(Covenant) ? null : LocalPathToImageConverter.LoadCached($"icons/covenants/{Covenant}.png");
        _glyph.OpacityMask = image is null ? null : new ImageBrush(image) { Stretch = Stretch.Uniform };
        _glyph.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;
        _fallback.Text = image is null && Covenant.Length > 0 ? Covenant[..1] : "";
    }
}
