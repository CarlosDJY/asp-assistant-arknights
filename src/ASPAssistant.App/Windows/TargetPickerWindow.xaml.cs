// Added for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ASPAssistant.App.Controls;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App.Windows;

/// <summary>
/// Pick target covenants: every covenant as a cell with its icon and late-game people. A click picks Ⅰ, the next
/// one Ⅱ (where the covenant has one), the third takes it back. Existing targets are shown but not clickable.
/// </summary>
public partial class TargetPickerWindow : Window
{
    private static readonly string[] Roman = ["", "Ⅰ", "Ⅱ"];
    private readonly IReadOnlyDictionary<string, int> _late;
    private readonly Func<string, bool> _hasSecond;
    private readonly Dictionary<string, CovenantInfo> _infos;
    private readonly Dictionary<string, int> _picked = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];
    private readonly Dictionary<string, Button> _cells = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _existing;

    /// <summary>Picked covenants with their tier, in the order clicked.</summary>
    public IReadOnlyList<(string Name, int Tier)> Picked => [.. _order.Select(n => (n, _picked[n]))];

    public TargetPickerWindow(IReadOnlyList<CovenantInfo> covenants, IReadOnlyDictionary<string, int> late,
        IReadOnlyList<TargetCovenant> existing, Func<string, bool> hasSecond)
    {
        InitializeComponent();
        _late = late;
        _hasSecond = hasSecond;
        _infos = covenants.ToDictionary(c => c.Name, StringComparer.Ordinal);
        _existing = existing.ToDictionary(t => t.Name, t => t.Tier, StringComparer.Ordinal);
        foreach (var c in covenants.Where(c => c.BondType == "SEASON")) CoreGrid.Children.Add(Cell(c.Name));
        foreach (var c in covenants.Where(c => c.BondType != "SEASON").OrderByDescending(c => late.GetValueOrDefault(c.Name)))
            ExtraGrid.Children.Add(Cell(c.Name));
    }

    private Button Cell(string name)
    {
        var button = new Button { Style = (Style)FindResource("ClickableButtonBase"), Margin = new Thickness(0, 0, 5, 5), Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Tag = name };
        button.Template = CellTemplate();
        button.Click += (_, _) => Cycle(name);
        _cells[name] = button;
        Render(name);
        return button;
    }

    private static ControlTemplate CellTemplate()
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        return new ControlTemplate(typeof(Button)) { VisualTree = presenter };
    }

    private int Needed(string name, int tier)
        => _infos.TryGetValue(name, out var info) ? LineupPlanner.CreateGoal(info, tier).Count : 0;

    private void Render(string name)
    {
        var button = _cells[name];
        var count = _late.GetValueOrDefault(name);
        var isTarget = _existing.TryGetValue(name, out var existingTier);
        var tier = _picked.GetValueOrDefault(name);
        var none = count == 0 && !isTarget;

        var frame = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(8, 7, 8, 7), Cursor = isTarget ? Cursors.Arrow : Cursors.Hand };
        frame.SetResourceReference(Border.CornerRadiusProperty, "RowCornerRadius");
        frame.SetResourceReference(Border.BackgroundProperty, tier > 0 ? "AccentSoftBrush" : isTarget ? "SectionBackgroundBrush" : "CardBackgroundBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, tier > 0 ? "AccentBorderBrush" : "LineBrush");
        if (isTarget) frame.Opacity = 0.6;

        var dock = new DockPanel();
        var icon = new CovenantIcon { Covenant = name, Width = 30, Height = 30, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center,
            Opacity = none ? 0.35 : 0.85 };
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var top = new DockPanel();
        if (isTarget || tier > 0)
        {
            var badge = new Border { Padding = new Thickness(5, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, MinWidth = 22 };
            badge.SetResourceReference(Border.CornerRadiusProperty, "DotCornerRadius");
            badge.SetResourceReference(Border.BackgroundProperty, tier > 0 ? "AccentSolidBrush" : "CardBackgroundBrush");
            var b = new TextBlock { Text = tier > 0 ? Roman[tier] : $"目标 {Roman[Math.Clamp(existingTier, 1, 2)]}", FontSize = tier > 0 ? 12 : 9, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center };
            b.SetResourceReference(TextBlock.ForegroundProperty, tier > 0 ? "OnAccentBrush" : "TextSecondaryBrush");
            if (tier > 0) b.SetResourceReference(TextBlock.FontFamilyProperty, "NumberFontFamily");
            badge.Child = b;
            DockPanel.SetDock(badge, Dock.Right);
            top.Children.Add(badge);
        }
        var label = new TextBlock { Text = name, FontSize = 12, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
        label.SetResourceReference(TextBlock.ForegroundProperty, none ? "TextSecondaryBrush" : "TextPrimaryBrush");
        top.Children.Add(label);
        text.Children.Add(top);
        var sub = new TextBlock
        {
            Text = tier > 0 ? $"后期 {count} 人 · 需 {Needed(name, tier)} 人" : count > 0 ? $"后期 {count} 人" : "后期没有",
            FontSize = 10, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis
        };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        text.Children.Add(sub);
        dock.Children.Add(text);
        frame.Child = dock;
        button.Content = frame;
        button.IsEnabled = !isTarget;
        button.ToolTip = isTarget ? "已经是目标" : null;
        ToolTipService.SetShowOnDisabled(button, true);
    }

    private void Cycle(string name)
    {
        var tier = _picked.GetValueOrDefault(name);
        var next = tier == 0 ? 1 : tier == 1 && _hasSecond(name) ? 2 : 0;
        if (next == 0) { _picked.Remove(name); _order.Remove(name); }
        else { if (tier == 0) _order.Add(name); _picked[name] = next; }
        Render(name);
        ConfirmButton.IsEnabled = _picked.Count > 0;
        ConfirmButton.Content = _picked.Count > 0 ? $"添加 {_picked.Count} 个" : "添加";
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
