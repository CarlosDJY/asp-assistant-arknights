// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
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
    private bool _editingStrategies;
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
                _editingStrategies = false;
                _vm.SelectPhase(phase);
                RefreshEditor();
            };
            PhaseTabs.Children.Add(button);
        }
        LineupNameBox.Text = _vm.Name;
        RefreshEditor();
    }

    private void OnStrategyTabClick(object sender, RoutedEventArgs e)
    {
        _editingStrategies = true;
        RefreshEditor();
    }

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
            button.Tag = !_editingStrategies && _vm.CurrentPhase == phase ? "active" : null;
        }
        var strategyCount = _vm.StrategySlots.Count(s => s.StrategyId.Length > 0);
        StrategyTabText.Text = strategyCount > 0 ? $"策略 · {strategyCount}" : "策略";
        StrategyTab.Tag = _editingStrategies ? "active" : null;
        StrategyTab.SetResourceReference(BorderBrushProperty, _editingStrategies ? "AccentBorderBrush" : "BorderBrush");
        StrategyTab.SetResourceReference(ForegroundProperty, _editingStrategies ? "AccentBrush" : "TextPrimaryBrush");
        TitleMeta.Text = string.Join(" · ", _vm.StrategySlots.Where(s => s.StrategyId.Length > 0)
            .Select(s => _strategies.FirstOrDefault(x => x.Id == s.StrategyId)?.Name ?? s.StrategyId).Take(2));
        BuildCards();
        RefreshSidePanel();
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnMinimize(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnLineupNameChanged(object sender, TextChangedEventArgs e) => _vm.Name = LineupNameBox.Text;

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
        _editingStrategies = false;
        LineupNameBox.Text = _vm.Name;
        RefreshEditor();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        var contents = _editingStrategies ? "策略页的策略和标签" : $"{_vm.CurrentPhase}的干员、装备和标签";
        if (!ConfirmDialog.Ask(this, "清空本阶段", $"清空{contents}？阵容名称和其他阶段保留，保存前不会改动已保存的阵容。", "清空", danger: true))
            return;
        if (_editingStrategies) _vm.ClearStrategies();
        else _vm.Clear();
        RefreshEditor();
    }

    // ── Cards ───────────────────────────────────────────────────────────────

    private void BuildCards()
    {
        SlotsHost.Items.Clear();
        MechHost.Content = null;
        MechHost.Visibility = Visibility.Collapsed;
        if (_editingStrategies)
        {
            for (var i = 0; i < _vm.StrategySlots.Count; i++) SlotsHost.Items.Add(StrategyCard(i));
            return;
        }
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
            Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(9), MinHeight = 118,
            BorderThickness = new Thickness(1), Background = empty ? Brushes.Transparent : null
        };
        card.SetResourceReference(Border.CornerRadiusProperty, "CardCornerRadius");
        if (!empty) card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
        card.SetResourceReference(Border.BorderBrushProperty, highlight ? "EliteBrush" : "LineBrush");
        return card;
    }

    /// <summary>Dashed “＋ 添加干员 / 策略” card.</summary>
    private UIElement EmptyCard(string text, Action onClick)
    {
        var frame = new Rectangle { StrokeThickness = 1, StrokeDashArray = [4, 3] };
        frame.SetResourceReference(Shape.StrokeProperty, "BorderBrush");
        frame.RadiusY = frame.RadiusX = (double)FindResource("RowRadiusValue") + 2;
        var label = new TextBlock { Text = text, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var hover = new Border { Opacity = 0 };
        hover.SetResourceReference(Border.BackgroundProperty, "NeutralHoverBrush");
        hover.SetResourceReference(Border.CornerRadiusProperty, "CardCornerRadius");
        var grid = new Grid { MinHeight = 118, Margin = new Thickness(0, 0, 8, 8), Background = Brushes.Transparent, Cursor = Cursors.Hand, Children = { hover, frame, label } };
        grid.MouseEnter += (_, _) => hover.Opacity = 1;
        grid.MouseLeave += (_, _) => hover.Opacity = 0;
        grid.MouseLeftButtonUp += (_, _) => onClick();
        return grid;
    }

    private UIElement SlotCard(int slotIndex)
    {
        var slot = _vm.Slots[slotIndex];
        if (string.IsNullOrEmpty(slot.OperatorName)) return EmptyCard("＋ 添加干员", () => PickOperator(slotIndex));

        var isCore = slot.Tags.Contains(LineupTag.阵容核心);
        var card = Card(highlight: isCore);
        var stack = new StackPanel();
        stack.Children.Add(Header(slotIndex, slot, isCore, avatarSize: 40));

        // 2 equipment tiles + the transformation tile
        var transformations = _vm.EquipmentRules.Transformations(slot);
        var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var e = 0; e < 2; e++)
        {
            var tile = EquipmentTile(slotIndex, slot, e);
            Grid.SetColumn(tile, e);
            row.Children.Add(tile);
        }
        var tr = TransformTile(slotIndex, slot, 0, transformations);
        tr.Margin = new Thickness(0);
        Grid.SetColumn(tr, 2);
        row.Children.Add(tr);
        stack.Children.Add(row);
        if (!string.IsNullOrEmpty(slot.TransformedCovenant)) stack.Children.Add(LegacyButton(slotIndex, slot));

        var tags = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var tag in Enum.GetValues<LineupTag>().Where(t => t != LineupTag.阵容核心))
        {
            var chip = new Button { Content = tag.ToString(), Style = (Style)FindResource("TagChip"), Tag = slot.Tags.Contains(tag) ? "active" : null };
            chip.Click += (_, _) => { _vm.ToggleSlotTag(slotIndex, tag); RefreshEditor(); };
            tags.Children.Add(chip);
        }
        stack.Children.Add(tags);
        card.Child = stack;
        return card;
    }

    /// <summary>Avatar, name, tier, ★ (阵容核心) and covenants; the ✕ clears the slot.</summary>
    private DockPanel Header(int slotIndex, LineupSlot slot, bool isCore, int avatarSize)
    {
        var op = _operatorByName.GetValueOrDefault(slot.OperatorName);
        var header = new DockPanel();
        var avatar = new Controls.Avatar
        {
            Width = avatarSize, Height = avatarSize, Text = slot.OperatorName, Source = op?.IconPath ?? $"icons/operators/{slot.OperatorName}.png",
            Cursor = Cursors.Hand, ToolTip = "点击更换干员", Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top
        };
        if (isCore) avatar.SetResourceReference(Controls.Avatar.RingProperty, "EliteBrush");
        avatar.MouseLeftButtonUp += (_, _) => PickOperator(slotIndex);
        DockPanel.SetDock(avatar, Dock.Left);
        header.Children.Add(avatar);

        var remove = new Button { Tag = FindResource("IconClose"), Style = (Style)FindResource("RowIconButton"), Width = 20, Height = 20, ToolTip = "清空该位置", VerticalAlignment = VerticalAlignment.Top };
        remove.Click += (_, _) => { _vm.SetSlotOperator(slotIndex, ""); RefreshEditor(); };
        DockPanel.SetDock(remove, Dock.Right);
        header.Children.Add(remove);

        var star = new Button
        {
            Tag = FindResource("IconStar"), Style = (Style)FindResource("RowIconButton"), Width = 20, Height = 20,
            ToolTip = isCore ? "取消阵容核心" : "设为阵容核心", VerticalAlignment = VerticalAlignment.Top
        };
        if (isCore) star.SetResourceReference(ForegroundProperty, "EliteBrush");
        star.Click += (_, _) => { _vm.ToggleSlotTag(slotIndex, LineupTag.阵容核心); RefreshEditor(); };
        DockPanel.SetDock(star, Dock.Right);
        header.Children.Add(star);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        var name = new TextBlock { Text = slot.OperatorName, FontSize = 12, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        nameRow.Children.Add(name);
        if (op is not null && op.Tier is > 0 and < 7)
        {
            var tier = new Border { Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            tier.SetResourceReference(Border.BackgroundProperty, "SectionBackgroundBrush");
            tier.SetResourceReference(Border.CornerRadiusProperty, "DotCornerRadius");
            var tierText = new TextBlock { Text = Roman[op.Tier], FontSize = 10, FontWeight = FontWeights.Bold };
            tierText.SetResourceReference(TextBlock.FontFamilyProperty, "NumberFontFamily");
            tierText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            tier.Child = tierText;
            nameRow.Children.Add(tier);
        }
        info.Children.Add(nameRow);
        var covenants = op is null ? "" : string.Join(" · ", op.CoreCovenants.Concat(op.AdditionalCovenants).Distinct());
        var cov = new TextBlock { Text = covenants, FontSize = 10, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = covenants };
        cov.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        info.Children.Add(cov);
        header.Children.Add(info);
        return header;
    }

    private Button EquipmentTile(int slotIndex, LineupSlot slot, int index)
    {
        var name = index < slot.Equipments.Count ? slot.Equipments[index] : null;
        var capacity = LineupEquipmentService.Capacity(slot);
        var tile = new Button
        {
            Style = (Style)FindResource("EditorTile"),
            Content = new TextBlock { Text = name ?? (index < capacity ? "＋ 装备" : "需精锐") },
            Tag = name is null ? "add" : name == LineupEquipmentService.Shapeshifter ? "shape" : slot.CoreEquipments.Contains(name) ? "core" : null,
            ToolTip = index >= capacity && name is null ? "需要精锐状态" : name is null ? "添加装备" : slot.CoreEquipments.Contains(name) ? $"核心装备：{name}" : name,
            IsEnabled = index < capacity || name is not null,
        };
        System.Windows.Automation.AutomationProperties.SetName(tile, $"装备槽{index + 1}");
        ToolTipService.SetShowOnDisabled(tile, true);
        tile.Click += (_, _) => PickEquipment(slotIndex, index);
        return tile;
    }

    private Button TransformTile(int slotIndex, LineupSlot slot, int index, IReadOnlyList<EquipmentTransformation> transformations)
    {
        var current = index < transformations.Count ? transformations[index] : null;
        // One place goes to 变形同构体 itself
        var enabled = index < LineupEquipmentService.Capacity(slot) - 1 || current is not null;
        var tile = new Button
        {
            Style = (Style)FindResource("EditorTile"),
            Content = new TextBlock { Text = current is null ? (enabled ? "＋ 转职" : "需精锐") : $"转职 → {current.Covenant}" },
            Tag = current is null ? "add" : "tr",
            ToolTip = current is null ? enabled ? "选择转职装备（会一起配上变形同构体）" : "需精锐后启用" : $"{current.EquipmentName}（点击更换，右键取消）",
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
        dock.Children.Add(stack);
        card.Child = dock;
        return card;
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

    private UIElement StrategyCard(int slotIndex)
    {
        var slot = _vm.StrategySlots[slotIndex];
        if (slot.StrategyId.Length == 0) return EmptyCard("＋ 添加策略", () => PickStrategy(slotIndex));

        // 外部分享可能引用尚未收录的策略，保留 ID 和删除入口。
        var strategy = _strategies.FirstOrDefault(s => s.Id == slot.StrategyId);
        var isCore = slot.Tags.Contains(StrategyTag.阵容核心);
        var card = Card(highlight: isCore);
        if (strategy is not null)
            card.ToolTip = new TextBlock
            {
                Text = $"{strategy.Name} · {strategy.Initiator}\n初始生命值：{strategy.InitialHp}\n{strategy.Description}",
                TextWrapping = TextWrapping.Wrap, MaxWidth = 360,
            };
        var stack = new StackPanel();
        var header = new DockPanel();
        var portrait = new Controls.Avatar
        {
            Width = 40, Height = 40, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 8, 0),
            Text = strategy?.Name ?? slot.StrategyId, Source = strategy?.IconPath, ToolTip = "点击更换策略"
        };
        if (isCore) portrait.SetResourceReference(Controls.Avatar.RingProperty, "EliteBrush");
        portrait.MouseLeftButtonUp += (_, _) => PickStrategy(slotIndex);
        DockPanel.SetDock(portrait, Dock.Left);
        header.Children.Add(portrait);
        var remove = new Button { Tag = FindResource("IconClose"), Style = (Style)FindResource("RowIconButton"), Width = 20, Height = 20, ToolTip = "移除该策略", VerticalAlignment = VerticalAlignment.Top };
        remove.Click += (_, _) => { _vm.SetStrategy(slotIndex, ""); RefreshEditor(); };
        DockPanel.SetDock(remove, Dock.Right);
        header.Children.Add(remove);
        var info = new StackPanel();
        var name = new TextBlock { Text = strategy?.Name ?? slot.StrategyId, FontSize = 12, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        info.Children.Add(name);
        var by = new TextBlock { Text = strategy?.Initiator ?? "资料未收录", FontSize = 10, Margin = new Thickness(0, 2, 0, 0) };
        by.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        info.Children.Add(by);
        header.Children.Add(info);
        stack.Children.Add(header);
        if (strategy is not null)
        {
            var description = new TextBlock { Text = strategy.Description, FontSize = 10, Margin = new Thickness(0, 7, 0, 0), TextWrapping = TextWrapping.Wrap, MaxHeight = 42, TextTrimming = TextTrimming.CharacterEllipsis };
            description.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            stack.Children.Add(description);
        }
        var tags = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var tag in Enum.GetValues<StrategyTag>())
        {
            var chip = new Button { Content = tag == StrategyTag.阵容核心 ? "核心" : tag.ToString(), Style = (Style)FindResource("TagChip"), Tag = slot.Tags.Contains(tag) ? "active" : null };
            chip.Click += (_, _) => { _vm.ToggleStrategyTag(slotIndex, tag); RefreshEditor(); };
            tags.Children.Add(chip);
        }
        stack.Children.Add(tags);
        card.Child = stack;
        return card;
    }

    // ── Side panel ──────────────────────────────────────────────────────────

    /// <summary>Covenants as people (“伊比利亚 7” + level), inactive ones with how many are missing; then the strategies.</summary>
    private void RefreshSidePanel()
    {
        CovenantList.Children.Clear();
        foreach (var stat in _vm.ActiveStats)
            CovenantList.Children.Add(CovenantRow(stat));
        CovenantEmpty.Visibility = _vm.ActiveStats.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        StrategyList.Children.Clear();
        foreach (var slot in _vm.StrategySlots.Where(s => s.StrategyId.Length > 0))
        {
            var strategy = _strategies.FirstOrDefault(s => s.Id == slot.StrategyId);
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var icon = new Controls.Avatar { Width = 28, Height = 28, Small = true, Text = strategy?.Name ?? slot.StrategyId, Source = strategy?.IconPath, Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            var tag = slot.Tags.Contains(StrategyTag.阵容核心) ? "核心" : slot.Tags.Contains(StrategyTag.优选) ? "优选" : slot.Tags.Contains(StrategyTag.次选) ? "次选" : null;
            if (tag is not null)
            {
                var pill = new Border { Padding = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                pill.SetResourceReference(Border.BackgroundProperty, tag == "核心" ? "EliteTintBrush" : "AccentSoftBrush");
                pill.SetResourceReference(Border.CornerRadiusProperty, "PillCornerRadius");
                var text = new TextBlock { Text = tag, FontSize = 10, FontWeight = FontWeights.Bold };
                text.SetResourceReference(TextBlock.ForegroundProperty, tag == "核心" ? "EliteBrush" : "AccentBrush");
                pill.Child = text;
                DockPanel.SetDock(pill, Dock.Right);
                row.Children.Add(pill);
            }
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock { Text = strategy?.Name ?? slot.StrategyId, FontSize = 12, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            info.Children.Add(name);
            var by = new TextBlock { Text = strategy?.Initiator ?? "", FontSize = 10 };
            by.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            info.Children.Add(by);
            row.Children.Add(info);
            StrategyList.Children.Add(row);
        }
        StrategyEmpty.Visibility = StrategyList.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var error = _vm.ValidationError;
        WarningIcon.Visibility = error.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        WarningIcon.SetResourceReference(Shape.StrokeProperty, "TransitionTextBrush");
        var empty = _editingStrategies ? 0 : LineupEditorViewModel.MaxSlots - _vm.FilledSlotCount;
        EditorHint.Text = error.Length > 0 ? error
            : _editingStrategies ? "一局只会带其中一个策略；标记“核心”的会写进规划"
            : empty > 0 && _vm.FilledSlotCount > 0 ? $"{_vm.CurrentPhase}还有 {empty} 个空位" : $"正在编辑{_vm.CurrentPhase} · 列表预览与追踪使用后期阵容";
        EditorHint.ToolTip = error.Length > 0 ? error : null;
        EditorHint.SetResourceReference(TextBlock.ForegroundProperty, error.Length > 0 ? "TransitionTextBrush" : "TextSecondaryBrush");
    }

    private UIElement CovenantRow(CovenantStat stat)
    {
        var isCore = stat.BondType == "SEASON";
        var activate = stat.ActivateCount ?? (isCore ? 3 : 2);
        var level = !stat.Activated ? 0
            : isCore ? (stat.Count >= 9 ? 3 : stat.Count >= 6 ? 2 : 1)
            : LineupPlanner.PeopleSecond.Contains(stat.Name) && stat.Count >= 3 ? 2 : 1;
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 9), Opacity = stat.Activated ? 1 : 0.75 };
        var dot = new Border { Width = 8, Height = 8, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = stat.Activated ? 1 : 0.35 };
        dot.SetResourceReference(Border.BackgroundProperty, isCore ? "AccentSolidBrush" : "FinalBrush");
        dot.SetResourceReference(Border.CornerRadiusProperty, "DotCornerRadius");
        DockPanel.SetDock(dot, Dock.Left);
        row.Children.Add(dot);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var count = new TextBlock { Text = stat.Count.ToString(), FontSize = 14, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        count.SetResourceReference(TextBlock.FontFamilyProperty, "NumberFontFamily");
        count.SetResourceReference(TextBlock.ForegroundProperty, stat.Activated ? "TextPrimaryBrush" : "TextSecondaryBrush");
        right.Children.Add(count);
        if (level > 0)
        {
            var badge = new Border { Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            badge.SetResourceReference(Border.BackgroundProperty, isCore ? "AccentSoftBrush" : "SuccessTintBrush");
            badge.SetResourceReference(Border.CornerRadiusProperty, "DotCornerRadius");
            var text = new TextBlock { Text = Roman[level], FontSize = 11, FontWeight = FontWeights.Bold };
            text.SetResourceReference(TextBlock.FontFamilyProperty, "NumberFontFamily");
            text.SetResourceReference(TextBlock.ForegroundProperty, isCore ? "AccentBrush" : "SuccessTextBrush");
            badge.Child = text;
            right.Children.Add(badge);
        }
        else
        {
            var missing = new TextBlock { Text = $"差 {Math.Max(1, activate - stat.Count)}", FontSize = 11, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            missing.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            right.Children.Add(missing);
        }
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);
        var name = new TextBlock { Text = stat.Name, FontSize = 12, FontWeight = stat.Activated ? FontWeights.Bold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        row.Children.Add(name);
        return row;
    }

    private void PickStrategy(int slotIndex)
    {
        var dialog = new SimplePickerDialog("选择策略", _strategies.Select(s => new SimplePickerDialog.PickerItem(
            s.Id, $"{s.Name} · {s.Initiator}", s.IconPath, null,
            $"{s.Name} · {s.Initiator}\n初始生命值：{s.InitialHp}\n{s.Description}")),
            _vm.StrategySlots[slotIndex].StrategyId, "搜索策略名或发起人 · 单击选择") { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _vm.SetStrategy(slotIndex, dialog.Result!);
        RefreshEditor();
    }

    private async void PickOperator(int slotIndex)
    {
        var current = _vm.Slots[slotIndex].OperatorName;
        // 暴躁铁皮 has its own card (added with the strategy), so it is not offered for the 9 places
        var dlg = new OperatorPickerWindow(_vm.AllOperators.Where(o => o.Name != LineupEquipmentService.MechName), current, _vm.StrategySlots.Select(s => s.StrategyId).ToHashSet())
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

        var dlg = new SimplePickerDialog(_vm.AllEquipment, current,
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
