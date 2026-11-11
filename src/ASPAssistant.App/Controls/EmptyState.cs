// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ASPAssistant.App.Controls;

/// <summary>Shared empty state: line icon, a title and one hint line, centred (tracking, operators, equipment, lineups, enemies).</summary>
public sealed class EmptyState : StackPanel
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(EmptyState), new PropertyMetadata(null, (d, e) => ((EmptyState)d)._path.Data = (Geometry?)e.NewValue));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(EmptyState), new PropertyMetadata("", (d, e) => ((EmptyState)d)._title.Text = (string)e.NewValue));

    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint), typeof(string), typeof(EmptyState), new PropertyMetadata("", (d, e) =>
        {
            var self = (EmptyState)d;
            self._hint.Text = (string)e.NewValue;
            self._hint.Visibility = string.IsNullOrEmpty((string)e.NewValue) ? Visibility.Collapsed : Visibility.Visible;
        }));

    public Geometry? Icon { get => (Geometry?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }

    private readonly Path _path = new()
    {
        StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
    };
    private readonly TextBlock _title = new()
    {
        FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center,
        TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 4)
    };
    private readonly TextBlock _hint = new()
    {
        FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap, LineHeight = 17, Visibility = Visibility.Collapsed
    };

    public EmptyState()
    {
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Margin = new Thickness(28, 0, 28, 0);
        MaxWidth = 240;
        IsHitTestVisible = false;

        // Icon on a soft rounded tile
        var tile = new Border { Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Center };
        tile.SetResourceReference(Border.BackgroundProperty, "SectionBackgroundBrush");
        tile.SetResourceReference(Border.CornerRadiusProperty, "CardCornerRadius");
        _path.SetResourceReference(Shape.StrokeProperty, "TextSecondaryBrush");
        tile.Child = new Viewbox { Width = 24, Height = 24, Child = new Canvas { Width = 24, Height = 24, Children = { _path } } };
        _title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Children.Add(tile);
        Children.Add(_title);
        Children.Add(_hint);
    }
}
