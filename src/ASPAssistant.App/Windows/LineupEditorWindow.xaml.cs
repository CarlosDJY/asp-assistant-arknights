// Modified for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App.Windows;

public partial class LineupEditorWindow : Window
{
    private readonly LineupEditorViewModel _vm;
    private readonly IReadOnlyList<Strategy> _strategies;
    private readonly Dictionary<string, Operator> _operatorByName;
    private static readonly string[] Roman = ["", "Ⅰ", "Ⅱ", "Ⅲ", "Ⅳ", "Ⅴ", "Ⅵ"];

    /// <summary>Set to the saved Lineup if user clicked Save, else null.</summary>
    public Lineup? Result { get; private set; }

    public LineupEditorWindow(LineupEditorViewModel vm, IReadOnlyList<Strategy> strategies)
    {
        _vm = vm;
        _strategies = strategies;
        _operatorByName = vm.AllOperators.GroupBy(o => o.Name).ToDictionary(g => g.Key, g => g.First());
        InitializeComponent();
        foreach (var phase in Enum.GetValues<LineupPhase>())
        {
            var button = new Button { Style = (Style)FindResource("PhaseSegment"), DataContext = phase };
            button.Click += (_, _) =>
            {
                _vm.SelectPhase(phase);
                RefreshEditor();
            };
            PhaseTabs.Children.Add(button);
        }
        LineupNameBox.Text = _vm.Name;
        RefreshEditor();
    }

    private IEnumerable<LineupStrategySlot> ChosenStrategies => _vm.StrategySlots.Where(s => s.StrategyId.Length > 0);

    private void RefreshEditor()
    {
        if (LineupNameBox.Text != _vm.Name) LineupNameBox.Text = _vm.Name;
        foreach (Button button in PhaseTabs.Children)
        {
            var phase = (LineupPhase)button.DataContext;
            var count = _vm.FilledCount(phase);
            button.Content = new TextBlock
            {
                Inlines =
                {
                    new System.Windows.Documents.Run(phase.ToString()),
                    new System.Windows.Documents.Run(count > 0 ? $" {count}" : "")
                    {
                        FontFamily = (FontFamily)FindResource("NumberFontFamily"), FontSize = 11
                    }
                }
            };
            button.Tag = _vm.CurrentPhase == phase ? "active" : null;
        }
        var chosen = ChosenStrategies.ToList();
        StrategyTabText.Text = chosen.Count > 0 ? chosen.Count.ToString() : "";
        StrategyTabIcons.Children.Clear();
        foreach (var slot in chosen.Take(3))
        {
            var strategy = _strategies.FirstOrDefault(s => s.Id == slot.StrategyId);
            StrategyTabIcons.Children.Add(new Controls.Avatar
            {
                Width = 22, Height = 22, Small = true, Margin = new Thickness(0, 0, -3, 0),
                Text = strategy?.Name ?? slot.StrategyId, Source = strategy?.IconPath
            });
        }
        StrategyTabIcons.Visibility = chosen.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TitleMeta.Text = _vm.Name;
        BuildCards();
        RefreshTargets();
        RefreshSidePanel();
    }

    private void OnStrategyTabClick(object sender, RoutedEventArgs e) => PickStrategies();

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnMinimize(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnLineupNameChanged(object sender, TextChangedEventArgs e)
    {
        _vm.Name = LineupNameBox.Text;
        TitleMeta.Text = _vm.Name;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_vm.ValidationError.Length > 0)
        {
            // The footer already says what is wrong; make it stand out instead of a system dialog.
            RefreshSidePanel();
            EditorHint.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            WarningIcon.SetResourceReference(Shape.StrokeProperty, "DangerBrush");
            return;
        }
        Result = _vm.ToLineup();
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDialog.Ask(this, "撤销修改", "恢复到本次打开编辑器时的状态？本次未保存的修改将被撤销。", "撤销"))
            return;
        _vm.Reset();
        LineupNameBox.Text = _vm.Name;
        RefreshEditor();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDialog.Ask(this, "清空本阶段", $"清空{_vm.CurrentPhase}的干员、装备和标签？阵容名称和其他阶段保留，保存前不会改动已保存的阵容。", "清空", danger: true))
            return;
        _vm.Clear();
        RefreshEditor();
    }

    // ── Cards ───────────────────────────────────────────────────────────────

    private void BuildCards()
    {
        SlotsHost.Items.Clear();
        MechHost.Content = null;
        MechHost.Visibility = Visibility.Collapsed;
        if (_vm.MechSlotIndex is var mech and >= 0)
        {
            MechHost.Content = MechCard(mech);
            MechHost.Visibility = Visibility.Visible;
        }
        foreach (var i in _vm.RegularSlotIndices) SlotsHost.Items.Add(SlotCard(i));
    }

    private Border Card(bool highlight = false, bool empty = false)
    {
        var card = new Border
        {
            Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(10), MinHeight = 122,
            BorderThickness = new Thickness(1), Background = empty ? Brushes.Transparent : null
        };
        card.SetResourceReference(Border.CornerRadiusProperty, "CardCornerRadius");
        if (!empty) card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
        card.SetResourceReference(Border.BorderBrushProperty, highlight ? "EliteBrush" : "LineBrush");
        return card;
    }

    /// <summary>Dashed “＋ 添加干员” card.</summary>
    private UIElement EmptyCard(string text, Action onClick)
    {
        var frame = new Rectangle { StrokeThickness = 1, StrokeDashArray = [4, 3] };
        frame.SetResourceReference(Shape.StrokeProperty, "BorderBrush");
        frame.RadiusY = frame.RadiusX = (double)FindResource("RowRadiusValue") + 2;
        var plus = new Path { Data = Geometry.Parse("M 12 5 L 12 19 M 5 12 L 19 12"), StrokeThickness = 1.8, Width = 16, Height = 16, Stretch = Stretch.Uniform,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, HorizontalAlignment = HorizontalAlignment.Center };
        plus.SetResourceReference(Shape.StrokeProperty, "TextSecondaryBrush");
        var label = new TextBlock { Text = text, FontSize = 12, Margin = new Thickness(0, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { plus, label } };
        var hover = new Border { Opacity = 0 };
        hover.SetResourceReference(Border.BackgroundProperty, "NeutralHoverBrush");
        hover.SetResourceReference(Border.CornerRadiusProperty, "CardCornerRadius");
        var grid = new Grid { MinHeight = 122, Margin = new Thickness(0, 0, 8, 8), Background = Brushes.Transparent, Cursor = Cursors.Hand, Children = { hover, frame, content } };
        grid.MouseEnter += (_, _) => hover.Opacity = 1;
        grid.MouseLeave += (_, _) => hover.Opacity = 0;
        grid.MouseLeftButtonUp += (_, _) => onClick();
        return grid;
    }

    private static TextBlock Text(string text, double size, string brush, bool bold = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    /// <summary>Small pill: tone "core" / "goal" / "goalCore" / "tag" / "extra".</summary>
    private static Border Pill(string text, string tone)
    {
        var (bg, fg, border) = tone switch
        {
            "core" => ("AccentSoftBrush", "AccentBrush", null),
            "goalCore" => ("AccentSoftBrush", "AccentBrush", "AccentBorderBrush"),
            "goal" => ("SuccessTintBrush", "SuccessTextBrush", "SuccessBorderBrush"),
            "tag" => (null, "TextSecondaryBrush", "BorderBrush"),
            _ => ("SectionBackgroundBrush", "TextSecondaryBrush", null),
        };
        var pill = new Border { Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 0, 3, 3), BorderThickness = new Thickness(border is null ? 0 : 1), MinHeight = 17 };
        pill.SetResourceReference(Border.CornerRadiusProperty, "PillCornerRadius");
        if (bg is not null) pill.SetResourceReference(Border.BackgroundProperty, bg);
        if (border is not null) pill.SetResourceReference(Border.BorderBrushProperty, border);
        pill.Child = Text(text, 10, fg, bold: tone is "core" or "goal" or "goalCore");
        return pill;
    }

    private UIElement SlotCard(int slotIndex)
    {
        var slot = _vm.Slots[slotIndex];
        if (string.IsNullOrEmpty(slot.OperatorName)) return EmptyCard("添加干员", () => PickOperator(slotIndex));

        var isCore = slot.Tags.Contains(LineupTag.阵容核心);
        var card = Card(highlight: isCore);
        var stack = new StackPanel();
        stack.Children.Add(Header(slotIndex, slot, isCore));

        // 2 equipment tiles + the transformation tile
        var transformations = _vm.EquipmentRules.Transformations(slot);
        var row = new Grid { Margin = new Thickness(0, 9, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
        for (var e = 0; e < 2; e++)
        {
            var tile = EquipmentTile(slotIndex, slot, e);
            Grid.SetColumn(tile, e);
            row.Children.Add(tile);
        }
        var tr = TransformTile(slotIndex, slot, 0, transformations, compact: true);
        tr.Margin = new Thickness(0);
        Grid.SetColumn(tr, 2);
        row.Children.Add(tr);
        stack.Children.Add(row);
        if (!string.IsNullOrEmpty(slot.TransformedCovenant)) stack.Children.Add(LegacyButton(slotIndex, slot));
        stack.Children.Add(CandidateRow(slotIndex, slot, labelWidth: 0));
        card.Child = stack;
        card.ContextMenu = SlotMenu(slotIndex, slot);
        return card;
    }

    /// <summary>Avatar with the tier in its corner, name, 核心, ⋯; covenants (targets stand out) and the chosen tags.</summary>
    private Grid Header(int slotIndex, LineupSlot slot, bool isCore)
    {
        var op = _operatorByName.GetValueOrDefault(slot.OperatorName);
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());

        var avatarBox = new Grid { Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Top, Cursor = Cursors.Hand, ToolTip = "点击更换干员" };
        var avatar = new Controls.Avatar { Width = 46, Height = 46, Text = slot.OperatorName, Source = op?.IconPath ?? $"icons/operators/{slot.OperatorName}.png" };
        if (isCore) avatar.SetResourceReference(Controls.Avatar.RingProperty, "EliteBrush");
        avatarBox.Children.Add(avatar);
        if (op is not null && op.Tier is > 0 and < 7)
        {
            var tier = new Border { Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(-3, 0, 0, -3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
            tier.SetResourceReference(Border.BackgroundProperty, "TitleBackgroundBrush");
            tier.SetResourceReference(Border.CornerRadiusProperty, "DotCornerRadius");
            var tierText = Text(Roman[op.Tier], 10, "TitleTextBrush", bold: true);
            tierText.SetResourceReference(TextBlock.FontFamilyProperty, "NumberFontFamily");
            tier.Child = tierText;
            avatarBox.Children.Add(tier);
        }
        avatarBox.MouseLeftButtonUp += (_, _) => PickOperator(slotIndex);
        header.Children.Add(avatarBox);

        var info = new StackPanel();
        Grid.SetColumn(info, 1);
        var nameRow = new DockPanel();
        var more = new Button { Tag = FindResource("IconMore"), Style = (Style)FindResource("RowIconButton"), Width = 22, Height = 22, ToolTip = "更多（标签、精锐、复制、移出）" };
        System.Windows.Automation.AutomationProperties.SetName(more, "更多");
        more.Click += (_, _) =>
        {
            var menu = SlotMenu(slotIndex, slot);
            menu.PlacementTarget = more;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        };
        DockPanel.SetDock(more, Dock.Right);
        nameRow.Children.Add(more);
        var name = Text(slot.OperatorName, 13, "TextPrimaryBrush", bold: true);
        if (isCore)
        {
            var badge = Pill("核心", "core");
            badge.Margin = new Thickness(5, 0, 0, 0);
            badge.VerticalAlignment = VerticalAlignment.Center;
            badge.SetResourceReference(Border.BackgroundProperty, "EliteTintBrush");
            ((TextBlock)badge.Child).SetResourceReference(TextBlock.ForegroundProperty, "EliteBrush");
            DockPanel.SetDock(badge, Dock.Right);
            nameRow.Children.Add(badge);
        }
        nameRow.Children.Add(name);
        info.Children.Add(nameRow);

        var pills = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var targets = _vm.TargetCovenants.Select(t => t.Name).ToHashSet();
        if (op is not null)
            foreach (var c in op.CoreCovenants.Concat(op.AdditionalCovenants).Distinct())
            {
                var core = op.CoreCovenants.Contains(c);
                pills.Children.Add(Pill(c, targets.Contains(c) ? (core ? "goalCore" : "goal") : core ? "core" : "extra"));
            }
        foreach (var tag in slot.Tags.Where(t => t != LineupTag.阵容核心))
            pills.Children.Add(Pill(tag.ToString(), "tag"));
        info.Children.Add(pills);
        header.Children.Add(info);
        return header;
    }

    /// <summary>⋯ / right-click: tags, 精锐, replace, copy to another phase, remove.</summary>
    private ContextMenu SlotMenu(int slotIndex, LineupSlot slot)
    {
        var menu = new ContextMenu();
        foreach (var tag in Enum.GetValues<LineupTag>())
        {
            var item = new MenuItem { Header = tag.ToString(), IsCheckable = true, IsChecked = slot.Tags.Contains(tag), StaysOpenOnClick = false };
            item.Click += (_, _) => { _vm.ToggleSlotTag(slotIndex, tag); RefreshEditor(); };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var elite = new MenuItem { Header = "精锐", IsCheckable = true, IsChecked = slot.IsElite };
        elite.Click += (_, _) => { _vm.SetSlotElite(slotIndex, !slot.IsElite); RefreshEditor(); };
        menu.Items.Add(elite);
        var replace = new MenuItem { Header = "换一位干员" };
        replace.Click += (_, _) => PickOperator(slotIndex);
        menu.Items.Add(replace);
        var copy = new MenuItem { Header = "复制到" };
        foreach (var phase in Enum.GetValues<LineupPhase>().Where(p => p != _vm.CurrentPhase))
        {
            var target = new MenuItem { Header = phase.ToString() };
            target.Click += (_, _) =>
            {
                if (!_vm.CopySlotToPhase(slotIndex, phase))
                    EditorHint.Text = $"{phase}已满，没有复制";
                RefreshEditor();
            };
            copy.Items.Add(target);
        }
        menu.Items.Add(copy);
        menu.Items.Add(new Separator());
        var remove = new MenuItem { Header = "移出阵容" };
        remove.SetResourceReference(ForegroundProperty, "DangerBrush");
        remove.Click += (_, _) => { _vm.SetSlotOperator(slotIndex, ""); RefreshEditor(); };
        menu.Items.Add(remove);
        return menu;
    }

    private Button EquipmentTile(int slotIndex, LineupSlot slot, int index)
    {
        var name = index < slot.Equipments.Count ? slot.Equipments[index] : null;
        var capacity = LineupEquipmentService.Capacity(slot);
        object content;
        if (name is null) content = new TextBlock { Text = index < capacity ? "＋ 装备" : "需精锐" };
        else
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Controls.Avatar { Width = 18, Height = 18, Small = true, Margin = new Thickness(-3, 0, 4, 0), Text = name,
                Source = _vm.AllEquipment.FirstOrDefault(e => e.Name == name)?.IconPath });
            row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            content = row;
        }
        var tile = new Button
        {
            Style = (Style)FindResource("EditorTile"), Height = 26, HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = content,
            Tag = name is null ? "add" : name == LineupEquipmentService.Shapeshifter ? "shape" : slot.CoreEquipments.Contains(name) ? "core" : null,
            ToolTip = index >= capacity && name is null ? "需要精锐状态" : name is null ? "添加装备" : slot.CoreEquipments.Contains(name) ? $"核心装备：{name}" : name,
            IsEnabled = index < capacity || name is not null,
        };
        System.Windows.Automation.AutomationProperties.SetName(tile, $"装备槽{index + 1}");
        ToolTipService.SetShowOnDisabled(tile, true);
        tile.Click += (_, _) => PickEquipment(slotIndex, index);
        return tile;
    }

    private Button TransformTile(int slotIndex, LineupSlot slot, int index, IReadOnlyList<EquipmentTransformation> transformations, bool compact = false)
    {
        var current = index < transformations.Count ? transformations[index] : null;
        // One place goes to 变形同构体 itself
        var enabled = index < LineupEquipmentService.Capacity(slot) - 1 || current is not null;
        var tile = new Button
        {
            Style = (Style)FindResource("EditorTile"), Height = 26,
            Content = new TextBlock { Text = current is null ? (enabled ? "＋ 转职" : "需精锐") : compact ? $"→ {current.Covenant}" : $"转职 → {current.Covenant}" },
            Tag = current is null ? "add" : "tr",
            ToolTip = current is null ? enabled ? "选择转职装备（会一起配上变形同构体）" : "需精锐后启用" : $"转职 → {current.Covenant}：{current.EquipmentName}（点击更换，右键取消）",
            IsEnabled = enabled,
        };
        System.Windows.Automation.AutomationProperties.SetName(tile, $"转职槽{index + 1}");
        ToolTipService.SetShowOnDisabled(tile, true);
        tile.Click += (_, _) => PickTransformation(slotIndex, index);
        if (current is not null)
            tile.MouseRightButtonUp += (_, e) => { _vm.ClearSlotTransformation(slotIndex, index); RefreshEditor(); e.Handled = true; };
        return tile;
    }

    private Button LegacyButton(int slotIndex, LineupSlot slot)
    {
        var legacy = new Button
        {
            Style = (Style)FindResource("EditorTile"), Tag = "add", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0),
            Content = new TextBlock { Text = $"清除旧转职：{slot.TransformedCovenant}" }, ToolTip = "该旧转职没有对应装备，请重新选择"
        };
        legacy.Click += (_, _) => { _vm.ClearLegacyTransformation(slotIndex); RefreshEditor(); };
        return legacy;
    }

    /// <summary>暴躁铁皮: wide card with 精锐, 4 / 6 equipment places and 3 / 5 transformations.</summary>
    private UIElement MechCard(int slotIndex)
    {
        var slot = _vm.Slots[slotIndex];
        var card = Card(highlight: true);
        card.MinHeight = 0;
        card.Padding = new Thickness(12, 10, 12, 10);
        card.Margin = new Thickness(0);
        var dock = new DockPanel();
        var avatar = new Controls.Avatar
        {
            Width = 52, Height = 52, Text = slot.OperatorName, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top,
            Source = _vm.AllOperators.FirstOrDefault(o => o.Name == slot.OperatorName)?.IconPath
        };
        avatar.SetResourceReference(Controls.Avatar.RingProperty, "EliteBrush");
        DockPanel.SetDock(avatar, Dock.Left);
        dock.Children.Add(avatar);

        var stack = new StackPanel();
        var top = new DockPanel();
        var elite = new CheckBox
        {
            Style = (Style)FindResource("SwitchToggle"), IsChecked = slot.IsElite, VerticalAlignment = VerticalAlignment.Center
        };
        elite.Checked += (_, _) => { _vm.SetSlotElite(slotIndex, true); RefreshEditor(); };
        elite.Unchecked += (_, _) => { _vm.SetSlotElite(slotIndex, false); RefreshEditor(); };
        // The switch template has no label: put “精锐” next to it
        var eliteLabel = new TextBlock { Text = "精锐", FontSize = 11, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        eliteLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var eliteRow = new StackPanel { Orientation = Orientation.Horizontal, ToolTip = "精锐：6 件装备、5 个转职", Children = { eliteLabel, elite } };
        DockPanel.SetDock(eliteRow, Dock.Right);
        top.Children.Add(eliteRow);
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        var name = new TextBlock { Text = slot.OperatorName, FontSize = 13, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        title.Children.Add(name);
        var badge = new Border { Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        badge.SetResourceReference(Border.BackgroundProperty, "EliteTintBrush");
        badge.SetResourceReference(Border.CornerRadiusProperty, "PillCornerRadius");
        var badgeText = new TextBlock { Text = "森蚺策略 · 占 1 人口", FontSize = 10, FontWeight = FontWeights.Bold };
        badgeText.SetResourceReference(TextBlock.ForegroundProperty, "EliteBrush");
        badge.Child = badgeText;
        title.Children.Add(badge);
        if (!_vm.HasMechStrategy)
        {
            var warn = new TextBlock { Text = "需添加森蚺的“暴躁铁皮”策略", FontSize = 11, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            warn.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            title.Children.Add(warn);
        }
        top.Children.Add(title);
        stack.Children.Add(top);

        // One row each: the tiles share the width instead of wrapping onto a second line
        var capacity = LineupEquipmentService.Capacity(slot);
        var eqTiles = new UniformGrid { Rows = 1 };
        for (var e = 0; e < LineupEquipmentService.VisibleEquipmentSlots(slot); e++)
            eqTiles.Children.Add(EquipmentTile(slotIndex, slot, e));
        stack.Children.Add(TileRow($"装备 {slot.Equipments.Count}/{capacity}", eqTiles, top: 8));

        var transformations = _vm.EquipmentRules.Transformations(slot);
        var trTiles = new UniformGrid { Rows = 1, Columns = LineupEquipmentService.VisibleEquipmentSlots(slot) };
        for (var t = 0; t < LineupEquipmentService.VisibleTransformationSlots(slot); t++)
            trTiles.Children.Add(TransformTile(slotIndex, slot, t, transformations));
        stack.Children.Add(TileRow($"转职 {transformations.Count}/{capacity - 1}", trTiles, top: 6));
        stack.Children.Add(CandidateRow(slotIndex, slot, labelWidth: 64));
        dock.Children.Add(stack);
        card.Child = dock;
        return card;
    }

    /// <summary>
    /// 候选 row: candidate equipment as small icons (gold ★ = 优先). Click one or ＋ to open the picker;
    /// right-click an icon for 设为优先 / 移到装备格 / 移除.
    /// </summary>
    private UIElement CandidateRow(int slotIndex, LineupSlot slot, double labelWidth)
    {
        var row = new DockPanel { Margin = new Thickness(0, 7, 0, 0) };
        var label = Text("候选", 10, "TextSecondaryBrush");
        label.Margin = new Thickness(0, 0, 6, 0);
        label.ToolTip = "候选装备：有了也行，不参与转职和盟约计算；追踪阵容时一起追踪";
        label.Width = labelWidth > 0 ? labelWidth : 26;
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);
        var chips = new WrapPanel();
        foreach (var candidate in slot.CandidateEquipments)
        {
            var name = candidate.Name;
            var box = new Grid { Width = 26, Height = 24, Margin = new Thickness(0, 0, 4, 0) };
            var frame = new Border { BorderThickness = new Thickness(1) };
            frame.SetResourceReference(Border.CornerRadiusProperty, "DotCornerRadius");
            frame.SetResourceReference(Border.BorderBrushProperty, candidate.IsPriority ? "EliteBrush" : "BorderBrush");
            if (candidate.IsPriority) frame.SetResourceReference(Border.BackgroundProperty, "EliteTintBrush");
            box.Children.Add(frame);
            box.Children.Add(new Controls.Avatar { Width = 18, Height = 18, Small = true, Text = name,
                Source = _vm.AllEquipment.FirstOrDefault(e => e.Name == name)?.IconPath });
            if (candidate.IsPriority)
            {
                var star = Text("★", 9, "EliteBrush");
                star.HorizontalAlignment = HorizontalAlignment.Right;
                star.VerticalAlignment = VerticalAlignment.Top;
                star.Margin = new Thickness(0, -5, -4, 0);
                box.Children.Add(star);
            }
            var chip = new Button { Content = box, Style = (Style)FindResource("ClickableButtonBase"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
                ToolTip = candidate.IsPriority ? $"优先：{name}（右键更多）" : $"候选：{name}（右键更多）" };
            chip.Template = BareButton();
            chip.Click += (_, _) => PickCandidates(slotIndex);
            var menu = new ContextMenu();
            var priority = new MenuItem { Header = candidate.IsPriority ? "取消优先" : "设为优先" };
            priority.Click += (_, _) => { _vm.ToggleCandidatePriority(slotIndex, name); RefreshEditor(); };
            var move = new MenuItem { Header = "移到装备格", IsEnabled = slot.Equipments.Count < LineupEquipmentService.Capacity(slot) };
            move.Click += (_, _) => { _vm.MoveCandidateToSlot(slotIndex, name); RefreshEditor(); };
            var remove = new MenuItem { Header = "移除" };
            remove.Click += (_, _) => { _vm.RemoveCandidate(slotIndex, name); RefreshEditor(); };
            menu.Items.Add(priority);
            menu.Items.Add(move);
            menu.Items.Add(remove);
            chip.ContextMenu = menu;
            chips.Children.Add(chip);
        }
        var add = new Button
        {
            Content = slot.CandidateEquipments.Count == 0 ? "＋ 候选装备" : "＋", Style = (Style)FindResource("CandidateChip"), Tag = "add",
            MinWidth = 26, Height = 24, Margin = new Thickness(0), ToolTip = "选择候选装备，可以一次选多件"
        };
        System.Windows.Automation.AutomationProperties.SetName(add, "添加候选装备");
        add.Click += (_, _) => PickCandidates(slotIndex);
        chips.Children.Add(add);
        row.Children.Add(chips);
        return row;
    }

    private static ControlTemplate BareButton()
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        return new ControlTemplate(typeof(Button)) { VisualTree = presenter };
    }

    private void PickCandidates(int slotIndex)
    {
        var slot = _vm.Slots[slotIndex];
        if (string.IsNullOrEmpty(slot.OperatorName)) return;
        var dlg = new EquipmentPickerWindow(_vm.AllEquipment, _vm.EquipmentRules, slot.OperatorName, slot.CandidateEquipments, slot.Equipments) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Candidates is null) return;
        _vm.SetSlotCandidates(slotIndex, dlg.Candidates);
        RefreshEditor();
    }

    private DockPanel TileRow(string label, UIElement tiles, double top)
    {
        var row = new DockPanel { Margin = new Thickness(0, top, 0, 0) };
        var text = RowLabel(label);
        DockPanel.SetDock(text, Dock.Left);
        row.Children.Add(text);
        row.Children.Add(tiles);
        return row;
    }

    private TextBlock RowLabel(string text)
    {
        var label = new TextBlock { Text = text, Width = 64, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return label;
    }

    // ── Side panel ──────────────────────────────────────────────────────────

    private TextBlock SideTitle(string text, double top)
    {
        var t = Text(text, 11, "TextSecondaryBrush", bold: true);
        t.Margin = new Thickness(0, top, 0, 8);
        return t;
    }

    /// <summary>目标 (with how far each is) first, then the other covenants of the phase, then the strategies.</summary>
    private void RefreshSidePanel()
    {
        CovenantList.Children.Clear();
        var targets = _vm.TargetCovenants.Select(t => t.Name).ToHashSet();
        var phaseStats = _vm.ActiveStats.ToDictionary(s => s.Name);
        if (_vm.TargetStates.Count > 0)
        {
            CovenantList.Children.Add(SideTitle("目标", 0));
            foreach (var state in _vm.TargetStates)
                CovenantList.Children.Add(TargetRow(state, phaseStats.GetValueOrDefault(state.Name)?.Count ?? 0));
        }
        var others = _vm.ActiveStats.Where(s => !targets.Contains(s.Name)).ToList();
        if (others.Count > 0)
        {
            CovenantList.Children.Add(SideTitle(targets.Count > 0 ? "其他盟约" : "盟约", CovenantList.Children.Count > 0 ? 8 : 0));
            foreach (var stat in others) CovenantList.Children.Add(CovenantRow(stat));
        }
        CovenantEmpty.Visibility = CovenantList.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        StrategyList.Children.Clear();
        foreach (var slot in ChosenStrategies)
        {
            var strategy = _strategies.FirstOrDefault(s => s.Id == slot.StrategyId);
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            row.MouseLeftButtonUp += (_, _) => PickStrategies();
            var icon = new Controls.Avatar { Width = 30, Height = 30, Small = true, Text = strategy?.Name ?? slot.StrategyId, Source = strategy?.IconPath, Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            if (StrategyTagText(slot) is { } tag)
            {
                var pill = Pill(tag, tag == "核心" ? "core" : "extra");
                pill.VerticalAlignment = VerticalAlignment.Center;
                if (tag == "核心")
                {
                    pill.SetResourceReference(Border.BackgroundProperty, "EliteTintBrush");
                    ((TextBlock)pill.Child).SetResourceReference(TextBlock.ForegroundProperty, "EliteBrush");
                }
                else if (tag == "优选")
                {
                    pill.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
                    ((TextBlock)pill.Child).SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                }
                DockPanel.SetDock(pill, Dock.Right);
                row.Children.Add(pill);
            }
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(Text(strategy?.Name ?? slot.StrategyId, 12, "TextPrimaryBrush", bold: true));
            info.Children.Add(Text(strategy?.Initiator ?? "", 10, "TextSecondaryBrush"));
            row.Children.Add(info);
            StrategyList.Children.Add(row);
        }
        StrategyEmpty.Visibility = StrategyList.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var error = _vm.ValidationError;
        WarningIcon.Visibility = error.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        WarningIcon.SetResourceReference(Shape.StrokeProperty, "TransitionTextBrush");
        var empty = LineupEditorViewModel.MaxSlots - _vm.FilledSlotCount;
        EditorHint.Text = error.Length > 0 ? error
            : empty > 0 && _vm.FilledSlotCount > 0 ? $"{_vm.CurrentPhase}还有 {empty} 个空位" : $"正在编辑{_vm.CurrentPhase} · 列表预览与追踪使用后期阵容";
        EditorHint.ToolTip = error.Length > 0 ? error : null;
        EditorHint.SetResourceReference(TextBlock.ForegroundProperty, error.Length > 0 ? "TransitionTextBrush" : "TextSecondaryBrush");
    }

    private static string? StrategyTagText(LineupStrategySlot slot)
        => slot.Tags.Contains(StrategyTag.阵容核心) ? "核心" : slot.Tags.Contains(StrategyTag.优选) ? "优选" : slot.Tags.Contains(StrategyTag.次选) ? "次选" : null;

    private static Border LevelBadge(string text, bool core, bool reached = true)
    {
        var badge = new Border { Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        badge.SetResourceReference(Border.BackgroundProperty, !reached ? "SectionBackgroundBrush" : core ? "AccentSoftBrush" : "SuccessTintBrush");
        badge.SetResourceReference(Border.CornerRadiusProperty, "DotCornerRadius");
        var t = Text(text, 11, !reached ? "TextSecondaryBrush" : core ? "AccentBrush" : "SuccessTextBrush", bold: true);
        t.SetResourceReference(TextBlock.FontFamilyProperty, "NumberFontFamily");
        badge.Child = t;
        return badge;
    }

    /// <summary>A target: icon, name, late-game people / needed, the target tier, and a bar towards it.</summary>
    private UIElement TargetRow(TargetState state, int phaseCount)
    {
        var box = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var row = new DockPanel();
        var icon = new Controls.CovenantIcon { Covenant = state.Name, Width = 16, Height = 16, Margin = new Thickness(0, 0, 7, 0), Opacity = 0.85 };
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        var count = Text(state.Count.ToString(), 15, state.Reached ? "TextPrimaryBrush" : "WarningTextBrush", bold: true);
        count.SetResourceReference(TextBlock.FontFamilyProperty, "NumberFontFamily");
        right.Children.Add(count);
        var need = Text($" / {state.Needed}", 11, "TextSecondaryBrush");
        need.SetResourceReference(TextBlock.FontFamilyProperty, "NumberFontFamily");
        right.Children.Add(need);
        right.Children.Add(LevelBadge(Roman[Math.Clamp(state.Tier, 1, 2)], state.IsCore, state.Reached));
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);
        row.Children.Add(Text(state.Name, 12, "TextPrimaryBrush", bold: true));
        box.Children.Add(row);
        var track = new Border { Height = 4, Margin = new Thickness(0, 6, 0, 0), CornerRadius = new CornerRadius(2), ClipToBounds = true };
        track.SetResourceReference(Border.BackgroundProperty, "SectionBackgroundBrush");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Min(state.Count, state.Needed), GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, state.Needed - state.Count), GridUnitType.Star) });
        var fill = new Border { CornerRadius = new CornerRadius(2) };
        fill.SetResourceReference(Border.BackgroundProperty, !state.Reached ? "WarningBrush" : state.IsCore ? "AccentSolidBrush" : "FinalBrush");
        grid.Children.Add(fill);
        track.Child = grid;
        box.Children.Add(track);
        box.ToolTip = _vm.CurrentPhase == LineupPhase.后期
            ? $"{state.Label}：需要 {state.Needed} 人，后期 {state.Count} 人"
            : $"{state.Label}：需要 {state.Needed} 人，后期 {state.Count} 人，{_vm.CurrentPhase} {phaseCount} 人";
        return box;
    }

    /// <summary>A non-target covenant of the phase: icon, people, level (or how many are missing).</summary>
    private UIElement CovenantRow(CovenantStat stat)
    {
        var isCore = stat.BondType == "SEASON";
        var activate = stat.ActivateCount ?? (isCore ? 3 : 2);
        var level = !stat.Activated ? 0
            : isCore ? (stat.Count >= 9 ? 3 : stat.Count >= 6 ? 2 : 1)
            : LineupPlanner.PeopleSecond.Contains(stat.Name) && stat.Count >= 3 ? 2 : 1;
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var icon = new Controls.CovenantIcon { Covenant = stat.Name, Width = 16, Height = 16, Margin = new Thickness(0, 0, 7, 0), Opacity = stat.Activated ? 0.85 : 0.35 };
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var count = Text(stat.Count.ToString(), 13, stat.Activated ? "TextPrimaryBrush" : "TextSecondaryBrush", bold: true);
        count.SetResourceReference(TextBlock.FontFamilyProperty, "NumberFontFamily");
        right.Children.Add(count);
        if (level > 0) right.Children.Add(LevelBadge(Roman[level], isCore));
        else
        {
            var missing = Text($"差 {Math.Max(1, activate - stat.Count)}", 10, "TextSecondaryBrush");
            missing.Margin = new Thickness(6, 0, 0, 0);
            right.Children.Add(missing);
        }
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);
        row.Children.Add(Text(stat.Name, 12, stat.Activated ? "TextPrimaryBrush" : "TextSecondaryBrush", bold: stat.Activated));
        return row;
    }

    /// <summary>目标盟约 chips: icon, name, Ⅰ / Ⅱ, and how the late-game slots stand against it.</summary>
    private void RefreshTargets()
    {
        TargetChips.Children.Clear();
        foreach (var state in _vm.TargetStates)
            TargetChips.Children.Add(TargetChip(state));
        var add = new Button
        {
            Content = _vm.TargetCovenants.Count == 0 ? "＋ 添加目标盟约" : "＋ 目标", Style = (Style)FindResource("CandidateChip"),
            Tag = "add", Height = 28, Margin = new Thickness(0, 0, 4, 4), ToolTip = "这套阵容要凑的盟约；阵容页的筛选和本局推荐都按它判断"
        };
        add.Click += (_, _) => PickTarget();
        TargetChips.Children.Add(add);
    }

    private UIElement TargetChip(TargetState state)
    {
        var chip = new Border { Padding = new Thickness(8, 0, 3, 0), Margin = new Thickness(0, 0, 5, 4), BorderThickness = new Thickness(1), Height = 28 };
        chip.SetResourceReference(Border.CornerRadiusProperty, "RowCornerRadius");
        chip.SetResourceReference(Border.BackgroundProperty, state.Reached ? "SuccessTintBrush" : "EliteTintBrush");
        chip.SetResourceReference(Border.BorderBrushProperty, state.Reached ? "SuccessBorderBrush" : "EliteBrush");
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new Controls.CovenantIcon { Covenant = state.Name, Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0), Opacity = 0.85 });
        content.Children.Add(Text(state.Name, 12, "TextPrimaryBrush", bold: true));

        if (_vm.HasSecondTier(state.Name))
        {
            var seg = new Border { Padding = new Thickness(1), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            seg.SetResourceReference(Border.BackgroundProperty, "SectionBackgroundBrush");
            seg.SetResourceReference(Border.CornerRadiusProperty, "DotCornerRadius");
            var tiers = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var tier in new[] { 1, 2 })
            {
                var active = state.Tier == tier;
                var b = new Button { Content = Roman[tier], Width = 20, Height = 18, FontSize = 11, Padding = new Thickness(0), BorderThickness = new Thickness(0),
                    FontWeight = active ? FontWeights.Bold : FontWeights.Normal, ToolTip = tier == 2 ? "目标 Ⅱ 档" : "目标 Ⅰ 档" };
                b.Style = (Style)FindResource("ClickableButtonBase");
                b.Template = TierButtonTemplate();
                b.SetResourceReference(FontFamilyProperty, "NumberFontFamily");
                if (active) b.SetResourceReference(BackgroundProperty, "AccentSolidBrush");
                else b.Background = Brushes.Transparent;
                b.SetResourceReference(ForegroundProperty, active ? "OnAccentBrush" : "TextSecondaryBrush");
                var name_ = state.Name;
                b.Click += (_, _) => { _vm.SetTargetTier(name_, tier); RefreshEditor(); };
                tiers.Children.Add(b);
            }
            seg.Child = tiers;
            content.Children.Add(seg);
        }
        else
        {
            var tier = Text("Ⅰ", 11, "TextSecondaryBrush", bold: true);
            tier.Margin = new Thickness(6, 0, 0, 0);
            content.Children.Add(tier);
        }

        var status = Text(state.Reached ? $"{state.Count}/{state.Needed}" : $"差 {state.Missing}", 12, state.Reached ? "SuccessTextBrush" : "WarningTextBrush", bold: true);
        status.Margin = new Thickness(7, 0, 0, 0);
        status.SetResourceReference(TextBlock.FontFamilyProperty, "NumberFontFamily");
        content.Children.Add(status);

        var remove = new Button { Width = 18, Height = 18, Margin = new Thickness(3, 0, 0, 0), ToolTip = "移除这个目标", VerticalAlignment = VerticalAlignment.Center };
        remove.Style = (Style)FindResource("RowIconButton");
        remove.Tag = FindResource("IconClose");
        var target = state.Name;
        remove.Click += (_, _) => { _vm.RemoveTarget(target); RefreshEditor(); };
        content.Children.Add(remove);
        chip.Child = content;
        chip.ToolTip = $"{state.Label}：需要 {state.Needed} 人，后期现在 {state.Count} 人";
        return chip;
    }

    private static ControlTemplate TierButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private void PickTarget()
    {
        var dialog = new TargetPickerWindow(_vm.AllCovenants, _vm.ComputeLate(), _vm.TargetCovenants, _vm.HasSecondTier) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        foreach (var (name, tier) in dialog.Picked)
        {
            _vm.AddTarget(name);
            _vm.SetTargetTier(name, tier);
        }
        RefreshEditor();
    }

    private void OnFillTargets(object sender, RoutedEventArgs e)
    {
        _vm.FillTargetsFromLate();
        RefreshEditor();
    }

    private void PickStrategies()
    {
        var current = ChosenStrategies.Select(s => (s.StrategyId, Tag: s.Tags.Count > 0 ? (StrategyTag?)s.Tags[0] : null)).ToList();
        var dialog = new StrategyPickerWindow(_strategies, current) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _vm.SetStrategies(dialog.Picked);
        RefreshEditor();
    }

    private async void PickOperator(int slotIndex)
    {
        var current = _vm.Slots[slotIndex].OperatorName;
        var context = new OperatorPickerContext(
            $"{_vm.CurrentPhase} · 第 {slotIndex + 1} 位", current,
            _vm.Slots.Where((s, i) => i != slotIndex && s.OperatorName.Length > 0).Select(s => s.OperatorName).ToHashSet(),
            _vm.CountsWith(slotIndex, ""), name => _vm.CountsWith(slotIndex, name), _vm.TargetStates, _vm.AllCovenants);
        // 暴躁铁皮 has its own card (added with the strategy), so it is not offered for the 9 places
        var dlg = new OperatorPickerWindow(_vm.AllOperators.Where(o => o.Name != LineupEquipmentService.MechName), current,
            _vm.StrategySlots.Select(s => s.StrategyId).ToHashSet(), context)
        {
            Owner = this,
        };
        await MinimizableDialog.ShowAsync(dlg, EditorContent);
        if (dlg.Result is not null)
        {
            _vm.SetSlotOperator(slotIndex, dlg.Result);
            RefreshEditor();
        }
    }

    private void PickEquipment(int slotIndex, int equipIndex)
    {
        var slot = _vm.Slots[slotIndex];
        if (string.IsNullOrEmpty(slot.OperatorName)) return;

        var current = equipIndex < slot.Equipments.Count ? slot.Equipments[equipIndex] : null;
        var dlg = new EquipmentPickerWindow(_vm.AllEquipment, _vm.EquipmentRules, $"{slot.OperatorName} · 装备格 {equipIndex + 1}", current,
            current is not null && slot.CoreEquipments.Contains(current))
        {
            Owner = this,
        };
        if (dlg.ShowDialog() != true) return;

        var picked = dlg.Result;
        if (picked is null) return;

        _vm.SetSlotEquipment(slotIndex, equipIndex, picked, dlg.IsCoreEquipment);

        RefreshEditor();
    }

    private void PickTransformation(int slotIndex, int transformationIndex)
    {
        var choices = _vm.AllEquipment.Where(e => _vm.EquipmentRules.EquipmentToCovenant.ContainsKey(e.Name))
            .OrderBy(e => e.Tier).ThenBy(e => e.Name)
            .Select(e => new SimplePickerDialog.PickerItem(e.Name,
                $"{_vm.EquipmentRules.EquipmentToCovenant[e.Name]} · {e.Name}", e.IconPath, null, e.Normal.EffectDescription));
        var current = _vm.EquipmentRules.Transformations(_vm.Slots[slotIndex]);
        var picker = new SimplePickerDialog("选择转职装备", choices,
            transformationIndex < current.Count ? current[transformationIndex].EquipmentName : null,
            "会同步配置变形同构体及所选装备") { Owner = this };
        if (picker.ShowDialog() != true || picker.Result is null) return;
        if (picker.Result.Length == 0)
        {
            _vm.ClearSlotTransformation(slotIndex, transformationIndex);
        }
        else
        {
            var proposed = _vm.PreviewTransformation(slotIndex, transformationIndex, picker.Result);
            var replaced = new List<string>();
            while (proposed.Equipments.Count > LineupEquipmentService.Capacity(proposed))
            {
                var replace = new SimplePickerDialog("装备位不足，请选择要替换的装备",
                    proposed.Equipments.Where(e => e != LineupEquipmentService.Shapeshifter && e != picker.Result)
                        .Select(e => new SimplePickerDialog.PickerItem(e, e, _vm.AllEquipment.FirstOrDefault(x => x.Name == e)?.IconPath)),
                    null, "原装备会在确认转职后替换；取消则保留原配置") { Owner = this };
                if (replace.ShowDialog() != true || string.IsNullOrEmpty(replace.Result)) return;
                replaced.Add(replace.Result);
                proposed.Equipments.Remove(replace.Result);
            }
            _vm.SetSlotTransformation(slotIndex, transformationIndex, picker.Result, replaced);
        }
        RefreshEditor();
    }

}
