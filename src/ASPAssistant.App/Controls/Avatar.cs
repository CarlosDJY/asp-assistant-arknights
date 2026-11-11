// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ASPAssistant.App.Converters;

namespace ASPAssistant.App.Controls;

/// <summary>
/// Operator / enemy portrait as a rounded square (radius from the theme's <c>AvatarCornerRadius</c>), with the
/// first character as a placeholder and an optional 2px ring drawn just outside (solid or dashed).
/// </summary>
public sealed class Avatar : Grid
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(string), typeof(Avatar), new PropertyMetadata(null, (d, _) => ((Avatar)d).UpdateImage()));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(Avatar), new PropertyMetadata("", (d, _) => ((Avatar)d).UpdateText()));

    public static readonly DependencyProperty RingProperty = DependencyProperty.Register(
        nameof(Ring), typeof(Brush), typeof(Avatar), new PropertyMetadata(null, (d, _) => ((Avatar)d).UpdateRing()));

    public static readonly DependencyProperty RingDashedProperty = DependencyProperty.Register(
        nameof(RingDashed), typeof(bool), typeof(Avatar), new PropertyMetadata(false, (d, _) => ((Avatar)d).UpdateRing()));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(Avatar), new PropertyMetadata(new CornerRadius(7), (d, _) => ((Avatar)d).UpdateShape()));

    public static readonly DependencyProperty SmallProperty = DependencyProperty.Register(
        nameof(Small), typeof(bool), typeof(Avatar), new PropertyMetadata(false, (d, _) => ((Avatar)d).BindRadius()));

    /// <summary>Local icon path (same format as the operator / enemy data).</summary>
    public string? Source { get => (string?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    /// <summary>Name; its first character is shown until (or instead of) the icon.</summary>
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public Brush? Ring { get => (Brush?)GetValue(RingProperty); set => SetValue(RingProperty, value); }
    public bool RingDashed { get => (bool)GetValue(RingDashedProperty); set => SetValue(RingDashedProperty, value); }
    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

    /// <summary>Uses the smaller radius (for avatars under ~24px).</summary>
    public bool Small { get => (bool)GetValue(SmallProperty); set => SetValue(SmallProperty, value); }

    private readonly Border _plate = new();
    private readonly TextBlock _initial = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold
    };
    private readonly Border _photo = new();
    private readonly Rectangle _ring = new() { StrokeThickness = 2, Margin = new Thickness(-2), IsHitTestVisible = false, Visibility = Visibility.Collapsed };

    public Avatar()
    {
        _plate.SetResourceReference(Border.BackgroundProperty, "SectionBackgroundBrush");
        _initial.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        Children.Add(_plate);
        Children.Add(_initial);
        Children.Add(_photo);
        Children.Add(_ring);
        BindRadius();
        SizeChanged += (_, _) => _initial.FontSize = Math.Max(9, Math.Round(ActualWidth * 0.4));
    }

    private void BindRadius() => SetResourceReference(CornerRadiusProperty, Small ? "AvatarSmallCornerRadius" : "AvatarCornerRadius");

    private void UpdateShape()
    {
        _plate.CornerRadius = CornerRadius;
        _photo.CornerRadius = CornerRadius;
        _ring.RadiusX = _ring.RadiusY = CornerRadius.TopLeft + 2;
    }

    private void UpdateText() => _initial.Text = string.IsNullOrEmpty(Text) ? "" : Text[..1];

    private void UpdateImage()
    {
        var image = LocalPathToImageConverter.LoadCached(Source);
        _photo.Background = image is null ? null : new ImageBrush(image) { Stretch = Stretch.UniformToFill };
    }

    private void UpdateRing()
    {
        _ring.Stroke = Ring;
        _ring.StrokeDashArray = RingDashed ? new DoubleCollection { 3, 2 } : null;
        _ring.Visibility = Ring is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
