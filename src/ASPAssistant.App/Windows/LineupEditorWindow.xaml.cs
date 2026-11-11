// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App.Windows;

public partial class LineupEditorWindow : Window
{
    private readonly LineupEditorViewModel _vm;
    private readonly IReadOnlyList<Strategy> _strategies;
    private bool _editingStrategies;

    /// <summary>Set to the saved Lineup if user clicked Save, else null.</summary>
    public Lineup? Result { get; private set; }

    public LineupEditorWindow(LineupEditorViewModel vm, IReadOnlyList<Strategy> strategies)
    {
        _vm = vm;
        _strategies = strategies;
        InitializeComponent();
        foreach (var phase in Enum.GetValues<LineupPhase>())
        {
            var button = new Button { Content = phase.ToString(), Style = (Style)FindResource("ChipButton"), Padding = new Thickness(16, 4, 16, 4) };
            button.Click += (_, _) =>
            {
                _editingStrategies = false;
                _vm.SelectPhase(phase);
                RefreshEditor();
            };
            PhaseTabs.Children.Add(button);
        }
        var strategyTab = new Button { Content = "策略", Style = (Style)FindResource("ChipButton"), Padding = new Thickness(16, 4, 16, 4) };
        strategyTab.Click += (_, _) =>
        {
            _editingStrategies = true;
            RefreshEditor();
        };
        PhaseTabs.Children.Add(strategyTab);
        RefreshEditor();
    }

    private void RefreshEditor()
    {
        LineupNameBox.Text = _vm.Name;
        foreach (Button button in PhaseTabs.Children)
            button.Tag = (string)button.Content == (_editingStrategies ? "策略" : _vm.CurrentPhase.ToString()) ? "active" : null;
        Grid.SetColumnSpan(SlotsPanel, _editingStrategies ? 2 : 1);
        CovenantPanel.Visibility = _editingStrategies ? Visibility.Collapsed : Visibility.Visible;
        BuildSlotCards();
        RefreshCovenantPanel();
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnLineupNameChanged(object sender, TextChangedEventArgs e)
    {
        _vm.Name = LineupNameBox.Text;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_vm.ValidationError.Length > 0)
        {
            MessageBox.Show(this, _vm.ValidationError, "请调整阵容", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Result = _vm.ToLineup();
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "确定恢复到本次打开编辑器时的状态？本次未保存的修改将被撤销。",
                "确认恢复", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        _vm.Reset();
        _editingStrategies = false;
        RefreshEditor();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        var contents = _editingStrategies ? "策略”页的策略和标签" : $"{_vm.CurrentPhase}”页的干员、装备和标签";
        if (MessageBox.Show(this, $"确定清空“{contents}？阵容名称和其他页将保留，保存前不会改动已保存阵容。",
                "确认清空", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        if (_editingStrategies)
            _vm.ClearStrategies();
        else
            _vm.Clear();
        RefreshEditor();
    }

    // ── Slot card construction ──────────────────────────────────────────────

    private readonly List<Border> _slotCards = [];

    private void BuildSlotCards()
    {
        _slotCards.Clear();
        SlotsHost.Items.Clear();
        SpecialSlotsHost.Items.Clear();
        for (int i = 0; i < (_editingStrategies ? _vm.StrategySlots.Count : _vm.Slots.Count); i++)
        {
            var card = CreateSlotCard(i);
            _slotCards.Add(card);
            if (!_editingStrategies && _vm.Slots[i].OperatorName == LineupEquipmentService.MechName)
                SpecialSlotsHost.Items.Add(card);
            else SlotsHost.Items.Add(card);
        }
    }

    private Border CreateSlotCard(int slotIndex)
    {
        var border = new Border
        {
            Margin = new Thickness(4),
            Padding = new Thickness(8),
            CornerRadius = (CornerRadius)FindResource("CardCornerRadius"),
            Background = (Brush)FindResource("CardBackgroundBrush"),
            BorderBrush = (Brush)FindResource("LineBrush"),
            BorderThickness = new Thickness(1),
            Tag = slotIndex,
        };
        RefreshSlotCard(border, slotIndex);
        return border;
    }

    private void RefreshSlotCard(Border border, int slotIndex)
    {
        if (_editingStrategies)
        {
            RefreshStrategyCard(border, slotIndex);
            return;
        }
        var slot = _vm.Slots[slotIndex];
        var stack = new StackPanel();

        // Header
        var headerDock = new DockPanel { LastChildFill = true };

        if (string.IsNullOrEmpty(slot.OperatorName))
        {
            var pickBtn = new Button
            {
                Content = "+ 选择干员",
                Style = (Style)FindResource("EditorSecondaryButton"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8, 14, 8, 14),
                FontSize = 12,
            };
            pickBtn.Click += (_, _) => PickOperator(slotIndex);
            stack.Children.Add(pickBtn);
            border.Child = stack;
            return;
        }

        // Filled: portrait + name + remove button
        var iconBorder = new Controls.Avatar
        {
            Width = 44,
            Height = 44,
            Text = slot.OperatorName,
            Source = $"icons/operators/{slot.OperatorName}.png",
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = Cursors.Hand,
            ToolTip = "点击更换干员",
        };
        iconBorder.MouseLeftButtonUp += (_, _) => PickOperator(slotIndex);
        DockPanel.SetDock(iconBorder, Dock.Left);
        headerDock.Children.Add(iconBorder);

        var rightStack = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };

        var nameRow = new DockPanel();
        var removeBtn = new Button
        {
            Tag = FindResource("IconClose"),
            Style = (Style)FindResource("RowIconButton"),
            Width = 22, Height = 22,
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = "清空该槽位",
        };
        removeBtn.Click += (_, _) =>
        {
            _vm.SetSlotOperator(slotIndex, "");
            RefreshEditor();
        };
        DockPanel.SetDock(removeBtn, Dock.Right);
        nameRow.Children.Add(removeBtn);

        var nameText = new TextBlock
        {
            Text = slot.OperatorName,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        nameRow.Children.Add(nameText);
        rightStack.Children.Add(nameRow);

        headerDock.Children.Add(rightStack);
        stack.Children.Add(headerDock);

        if (slot.OperatorName == LineupEquipmentService.MechName)
        {
            var elite = new CheckBox
            {
                Content = "精锐", IsChecked = slot.IsElite,
                FontSize = 10, Foreground = (Brush)FindResource("TextPrimaryBrush"), Margin = new Thickness(0, 6, 0, 2)
            };
            elite.Checked += (_, _) => { _vm.SetSlotElite(slotIndex, true); RefreshSlotCard(border, slotIndex); RefreshCovenantPanel(); };
            elite.Unchecked += (_, _) => { _vm.SetSlotElite(slotIndex, false); RefreshSlotCard(border, slotIndex); RefreshCovenantPanel(); };
            stack.Children.Add(elite);
            stack.Children.Add(new TextBlock
            {
                Text = !_vm.HasMechStrategy ? "需添加森蚺的“暴躁铁皮”策略" : slot.IsElite ? "精锐：6 装备 / 5 转职" : "普通：4 装备 / 3 转职",
                FontSize = 10, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource(_vm.HasMechStrategy ? "TextSecondaryBrush" : "DangerBrush")
            });
        }

        // Equipment chips (2 slots)
        var equipPanel = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        for (int slotI = 0; slotI < LineupEquipmentService.VisibleEquipmentSlots(slot); slotI++)
        {
            string? eqName = slotI < slot.Equipments.Count ? slot.Equipments[slotI] : null;
            var chip = new Button
            {
                Content = eqName ?? "+ 装备",
                Style = (Style)FindResource("ChipButton"),
                ToolTip = slotI >= LineupEquipmentService.Capacity(slot) ? "需要精锐状态；已有装备可点击移除" : eqName ?? "添加装备",
                IsEnabled = slotI < LineupEquipmentService.Capacity(slot) || eqName is not null,
            };
            System.Windows.Automation.AutomationProperties.SetName(chip, $"装备槽{slotI + 1}");
            ToolTipService.SetShowOnDisabled(chip, true);
            int captured = slotI;
            chip.Click += (_, _) => PickEquipment(slotIndex, captured);
            if (eqName is not null)
            {
                var isCore = slot.CoreEquipments.Contains(eqName);
                chip.Tag = isCore ? "core" : "active";
                if (isCore)
                {
                    chip.Content = $"★ {eqName}";
                    chip.ToolTip = $"核心装备：{eqName}";
                }
            }
            equipPanel.Children.Add(chip);
        }
        stack.Children.Add(equipPanel);

        var transformations = _vm.EquipmentRules.Transformations(slot);
        var transformPanel = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        for (var i = 0; i < LineupEquipmentService.VisibleTransformationSlots(slot); i++)
        {
            var index = i;
            var current = i < transformations.Count ? transformations[i] : null;
            var transform = new Button
            {
                Content = current is null ? "+ 转职" : $"转职：{current.Covenant}",
                Style = (Style)FindResource("ChipButton"), Tag = current is null ? null : "active",
                HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = i >= LineupEquipmentService.Capacity(slot) - 1 ? "需精锐后启用" : current?.EquipmentName ?? "选择转职盟约对应的装备",
                IsEnabled = i < LineupEquipmentService.Capacity(slot) - 1 || current is not null,
            };
            System.Windows.Automation.AutomationProperties.SetName(transform, $"转职槽{i + 1}");
            ToolTipService.SetShowOnDisabled(transform, true);
            transform.Click += (_, _) => PickTransformation(slotIndex, index);
            transformPanel.Children.Add(transform);
            if (current is not null)
            {
                var clear = new Button
                {
                    Content = "✕", Style = (Style)FindResource("ChipButton"), ToolTip = "取消本次转职",
                    Tag = current.EquipmentName, Padding = new Thickness(4, 2, 4, 2)
                };
                clear.Click += (_, _) => { _vm.ClearSlotTransformation(slotIndex, index); RefreshSlotCard(border, slotIndex); RefreshCovenantPanel(); };
                transformPanel.Children.Add(clear);
            }
        }
        stack.Children.Add(transformPanel);
        if (!string.IsNullOrEmpty(slot.TransformedCovenant))
        {
            var legacy = new Button { Content = $"清除旧转职：{slot.TransformedCovenant}", Style = (Style)FindResource("ChipButton"), ToolTip = "该旧转职没有对应装备，请重新选择" };
            legacy.Click += (_, _) =>
            {
                _vm.ClearLegacyTransformation(slotIndex);
                RefreshSlotCard(border, slotIndex);
                RefreshCovenantPanel();
            };
            stack.Children.Add(legacy);
        }

        // Tags
        var tagsLabel = new TextBlock
        {
            Text = "标签",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 8, 0, 2),
        };
        stack.Children.Add(tagsLabel);
        var tagPanel = new WrapPanel();
        foreach (LineupTag tag in Enum.GetValues<LineupTag>().OrderBy(tag => tag != LineupTag.阵容核心))
        {
            var chip = new Button
            {
                Content = tag.ToString(),
                Style = (Style)FindResource("ChipButton"),
            };
            if (slot.Tags.Contains(tag)) chip.Tag = tag == LineupTag.阵容核心 ? "core" : "active";
            chip.Click += (_, _) =>
            {
                _vm.ToggleSlotTag(slotIndex, tag);
                RefreshSlotCard(border, slotIndex);
                RefreshCovenantPanel();
            };
            tagPanel.Children.Add(chip);
        }
        stack.Children.Add(tagPanel);

        border.Child = stack;
    }

    private void RefreshStrategyCard(Border border, int slotIndex)
    {
        var slot = _vm.StrategySlots[slotIndex];
        var stack = new StackPanel();
        border.ToolTip = null;
        if (slot.StrategyId.Length == 0)
        {
            var pick = new Button
            {
                Content = "+ 选择策略", Style = (Style)FindResource("EditorSecondaryButton"),
                Padding = new Thickness(8, 14, 8, 14), FontSize = 12,
            };
            pick.Click += (_, _) => PickStrategy(slotIndex);
            stack.Children.Add(pick);
            border.Child = stack;
            return;
        }

        // 外部分享可能引用尚未收录的策略，保留 ID 和删除入口。
        var strategy = _strategies.FirstOrDefault(s => s.Id == slot.StrategyId);
        var header = new DockPanel();
        var portrait = new Controls.Avatar
        {
            Width = 44, Height = 44, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top,
            Text = strategy?.Name ?? slot.StrategyId, Source = strategy?.IconPath,
        };
        if (strategy is not null)
        {
            border.ToolTip = new TextBlock
            {
                Text = $"{strategy.Name} · {strategy.Initiator}\n初始生命值：{strategy.InitialHp}\n{strategy.Description}",
                TextWrapping = TextWrapping.Wrap, MaxWidth = 360,
            };
        }
        portrait.MouseLeftButtonUp += (_, _) => PickStrategy(slotIndex);
        DockPanel.SetDock(portrait, Dock.Left);
        header.Children.Add(portrait);

        var details = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        var nameRow = new DockPanel();
        var remove = new Button
        {
            Tag = FindResource("IconClose"), Style = (Style)FindResource("RowIconButton"),
            Width = 22, Height = 22, ToolTip = "清空该策略",
            VerticalAlignment = VerticalAlignment.Top,
        };
        remove.Click += (_, _) =>
        {
            _vm.SetStrategy(slotIndex, "");
            RefreshStrategyCard(border, slotIndex);
            RefreshCovenantPanel();
        };
        DockPanel.SetDock(remove, Dock.Right);
        nameRow.Children.Add(remove);
        nameRow.Children.Add(new TextBlock
        {
            Text = strategy?.Name ?? slot.StrategyId, FontSize = 13, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("TextPrimaryBrush"),
        });
        details.Children.Add(nameRow);
        details.Children.Add(new TextBlock
        {
            Text = strategy?.Initiator ?? "资料未收录", FontSize = 11, TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
        });
        if (strategy is not null)
            details.Children.Add(new TextBlock
            {
                Text = strategy.Description, TextWrapping = TextWrapping.Wrap,
                FontSize = 11, Margin = new Thickness(0, 6, 0, 0),
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
            });
        header.Children.Add(details);
        stack.Children.Add(header);
        stack.Children.Add(new TextBlock
        {
            Text = "标签", FontSize = 10, Margin = new Thickness(0, 8, 0, 2),
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
        });
        var tags = new WrapPanel();
        foreach (var tag in Enum.GetValues<StrategyTag>())
        {
            var chip = new Button { Content = tag.ToString(), Style = (Style)FindResource("ChipButton") };
            if (slot.Tags.Contains(tag)) chip.Tag = tag == StrategyTag.阵容核心 ? "core" : "active";
            chip.Click += (_, _) =>
            {
                _vm.ToggleStrategyTag(slotIndex, tag);
                RefreshStrategyCard(border, slotIndex);
            };
            tags.Children.Add(chip);
        }
        stack.Children.Add(tags);
        border.Child = stack;
    }

    private void PickStrategy(int slotIndex)
    {
        var dialog = new SimplePickerDialog("选择策略", _strategies.Select(s => new SimplePickerDialog.PickerItem(
            s.Id, $"{s.Name} · {s.Initiator}", s.IconPath, null,
            $"{s.Name} · {s.Initiator}\n初始生命值：{s.InitialHp}\n{s.Description}")),
            _vm.StrategySlots[slotIndex].StrategyId, "搜索策略名或发起人 · 单击选择") { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _vm.SetStrategy(slotIndex, dialog.Result!);
        RefreshStrategyCard(_slotCards[slotIndex], slotIndex);
        RefreshCovenantPanel();
    }

    private void PickOperator(int slotIndex)
    {
        var current = _vm.Slots[slotIndex].OperatorName;
        var dlg = new OperatorPickerWindow(_vm.AllOperators, current, _vm.StrategySlots.Select(s => s.StrategyId).ToHashSet())
        {
            Owner = this,
        };
        if (dlg.ShowDialog() == true)
        {
            _vm.SetSlotOperator(slotIndex, dlg.Result ?? "");
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

        RefreshSlotCard(_slotCards[slotIndex], slotIndex);
        RefreshCovenantPanel();
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
        RefreshSlotCard(_slotCards[slotIndex], slotIndex);
        RefreshCovenantPanel();
    }

    private void TryLoadOperatorIcon(string opName, Image image)
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "data", "icons", "operators", $"{opName}.png");
        if (!File.Exists(iconPath)) return;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(iconPath, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            image.Source = bmp;
        }
        catch
        {
            // ignore – just leave the placeholder background visible
        }
    }

    // ── Covenant panel ──────────────────────────────────────────────────────

    private void RefreshCovenantPanel()
    {
        ActivatedSummaryText.Text = _editingStrategies
            ? $"已选 {_vm.StrategySlots.Count(s => s.StrategyId.Length > 0)}/9 策略"
            : $"激活 {_vm.ActivatedCount} 个盟约 · 已选 {_vm.FilledSlotCount}/9 干员";
        // The ItemsControl re-binds against ActiveStats automatically.
        CovenantStatsList.ItemsSource = null;
        CovenantStatsList.ItemsSource = _vm.ActiveStats;
        EditorHint.Text = _vm.ValidationError.Length > 0 ? _vm.ValidationError :
            $"正在编辑：{(_editingStrategies ? "策略" : _vm.CurrentPhase.ToString())} · 列表预览与追踪使用后期阵容";
        EditorHint.ToolTip = _vm.ValidationError.Length > 0 ? _vm.ValidationError : null;
        EditorHint.SetResourceReference(TextBlock.ForegroundProperty, _vm.ValidationError.Length > 0 ? "DangerBrush" : "TextSecondaryBrush");
    }

    private void OnCovenantRowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border b || b.Tag is not bool activated) return;
        if (activated)
        {
            b.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
            b.SetResourceReference(Border.BorderBrushProperty, "AccentBorderBrush");
            b.BorderThickness = new Thickness(1);
        }
        else
        {
            b.SetResourceReference(Border.BackgroundProperty, "NeutralTintBrush");
            b.BorderThickness = new Thickness(0);
        }
    }
}
