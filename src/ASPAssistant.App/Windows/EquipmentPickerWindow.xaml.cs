// Added for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App.Windows;

/// <summary>One equipment row of <see cref="EquipmentPickerWindow"/>.</summary>
public sealed class EquipmentPickRow(Equipment equipment, string? jobCovenant, bool candidateMode) : INotifyPropertyChanged
{
    public Equipment Equipment { get; } = equipment;
    public string Name => Equipment.Name;
    public string IconPath => Equipment.IconPath;
    public string TierText => $"T{Equipment.Tier}";
    public bool HasJob => jobCovenant is not null;
    public string JobText => $"转职 → {jobCovenant}";
    public string Effect => Equipment.Normal.EffectDescription.Replace('\n', ' ');
    public string Summary => $"{Name} · T{Equipment.Tier}" + (Equipment.Categories.Count > 0 ? $" · {string.Join(" / ", Equipment.Categories)}" : "")
        + (HasJob ? $"\n{JobText}" : "") + $"\n普通：{Equipment.Normal.EffectDescription}"
        + (Equipment.Elite.EffectDescription.Length > 0 ? $"\n精锐：{Equipment.Elite.EffectDescription}" : "");
    public bool CandidateMode { get; } = candidateMode;

    private bool _isChecked, _isPriority, _isSelected;
    public bool IsChecked { get => _isChecked; set { _isChecked = value; if (!value) _isPriority = false; Notify(); Notify(nameof(IsPriority)); Notify(nameof(IsActive)); Notify(nameof(PickedText)); } }
    public bool IsPriority { get => _isPriority; set { _isPriority = value; Notify(); Notify(nameof(PickedText)); } }
    /// <summary>Single mode: the picked one.</summary>
    public bool IsSelected { get => _isSelected; set { _isSelected = value; Notify(); Notify(nameof(IsActive)); } }
    public bool IsActive => CandidateMode ? IsChecked : IsSelected;
    public string PickedText => IsPriority ? $"★ {Name}" : Name;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Equipment picker in two columns. Single mode (an equipment place): pick one, 普通 / 核心装备. Candidate mode:
/// tick any number as 候选 and ★ the 优先 ones; pieces already in the operator's places are not listed.
/// </summary>
public partial class EquipmentPickerWindow : Window
{
    private static readonly string[] Categories = ["攻击", "防御", "生存", "技力", "经济", "盟约", "其他"];
    private const string JobCategory = "转职";

    private readonly List<EquipmentPickRow> _rows;
    private readonly ObservableCollection<EquipmentPickRow> _shown = [];
    private readonly ObservableCollection<EquipmentPickRow> _picked = [];
    private readonly HashSet<string> _categories = [];
    private readonly bool _candidateMode;

    /// <summary>Single mode: null = cancelled, "" = cleared, else the picked name.</summary>
    public string? Result { get; private set; }
    public bool IsCoreEquipment => CoreMode.SelectedIndex == 1;

    /// <summary>Candidate mode result (null = cancelled), in the order picked.</summary>
    public IReadOnlyList<CandidateEquipment>? Candidates { get; private set; }

    private EquipmentPickerWindow(IEnumerable<Equipment> equipment, LineupEquipmentService rules, bool candidateMode, IEnumerable<string> hidden)
    {
        _candidateMode = candidateMode;
        InitializeComponent();
        var hide = hidden.ToHashSet(StringComparer.Ordinal);
        _rows = [.. equipment.Where(e => !hide.Contains(e.Name)).OrderBy(e => e.Tier).ThenBy(e => e.Name, StringComparer.CurrentCulture)
            .Select(e => new EquipmentPickRow(e, rules.EquipmentToCovenant.TryGetValue(e.Name, out var c) ? c : e.Type == EquipmentType.JobChange && e.JobChangeCovenant.Length > 0 ? e.JobChangeCovenant : null, candidateMode))];
        ItemsList.ItemsSource = _shown;
        PickedList.ItemsSource = _picked;
        foreach (var t in new[] { "全部" }.Concat(Enumerable.Range(1, 6).Select(i => $"T{i}")))
            TierList.Items.Add(new ListBoxItem { Content = t });
        TierList.SelectedIndex = 0;
        foreach (var c in new[] { JobCategory }.Concat(Categories))
        {
            var chip = new ToggleButton { Content = c, Style = (Style)FindResource("CatChip") };
            if (c == JobCategory) chip.SetResourceReference(ForegroundProperty, "EliteBrush");
            chip.Checked += (_, _) => { _categories.Add(c); ApplyFilter(); };
            chip.Unchecked += (_, _) => { _categories.Remove(c); ApplyFilter(); };
            CategoryChips.Children.Add(chip);
        }
        Loaded += (_, _) => { SearchBox.Focus(); Keyboard.Focus(SearchBox); };
    }

    /// <summary>One equipment place: <paramref name="context"/> is “冻河 · 装备格 2”.</summary>
    public EquipmentPickerWindow(IEnumerable<Equipment> equipment, LineupEquipmentService rules, string context, string? current, bool isCore)
        : this(equipment, rules, candidateMode: false, [])
    {
        TitleMeta.Text = context;
        ContextBar.Visibility = Visibility.Visible;
        ContextText.Text = context;
        CoreMode.SelectedIndex = isCore ? 1 : 0;
        HintText.Text = "双击直接放入";
        if (_rows.FirstOrDefault(r => r.Name == current) is { } row) row.IsSelected = true;
        ApplyFilter();
        UpdateConfirm();
    }

    /// <summary>Candidates of <paramref name="operatorName"/>; <paramref name="inSlots"/> (already in its places) are left out.</summary>
    public EquipmentPickerWindow(IEnumerable<Equipment> equipment, LineupEquipmentService rules, string operatorName,
        IEnumerable<CandidateEquipment> current, IEnumerable<string> inSlots)
        : this(equipment, rules, candidateMode: true, inSlots)
    {
        TitleText.Text = "候选装备";
        TitleMeta.Text = operatorName;
        PickedBar.Visibility = Visibility.Visible;
        ClearButton.Content = "全部取消";
        HintText.Text = "单击勾选为候选，★ 设为优先";
        foreach (var c in current)
            if (_rows.FirstOrDefault(r => r.Name == c.Name) is { } row)
            {
                row.IsChecked = true;
                row.IsPriority = c.IsPriority;
                _picked.Add(row);
            }
        ApplyFilter();
        UpdateConfirm();
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded || _rows is not null) ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_rows is null) return;
        var query = SearchBox.Text;
        var tier = TierList.SelectedIndex;
        _shown.Clear();
        foreach (var row in _rows)
        {
            if (tier > 0 && row.Equipment.Tier != tier) continue;
            if (_categories.Count > 0 && !_categories.Any(c => c == JobCategory ? row.HasJob : row.Equipment.Categories.Contains(c))) continue;
            if (!SearchMatch.Matches(query, row.Name) && !row.Equipment.Normal.EffectDescription.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            _shown.Add(row);
        }
        EmptyHint.Visibility = _shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private EquipmentPickRow? RowAt(MouseButtonEventArgs e)
        => ItemsControl.ContainerFromElement(ItemsList, e.OriginalSource as DependencyObject) is ListBoxItem { Content: EquipmentPickRow row } ? row : null;

    private void OnItemClick(object sender, MouseButtonEventArgs e)
    {
        if (RowAt(e) is not { } row || IsInside<ButtonBase>(e.OriginalSource as DependencyObject)) return;
        if (_candidateMode)
        {
            row.IsChecked = !row.IsChecked;
            if (row.IsChecked) _picked.Add(row);
            else _picked.Remove(row);
        }
        else
        {
            foreach (var r in _rows) r.IsSelected = r == row;
        }
        UpdateConfirm();
    }

    private static bool IsInside<T>(DependencyObject? d) where T : DependencyObject
    {
        for (; d is not null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            if (d is T) return true;
        return false;
    }

    private void OnStarClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EquipmentPickRow row } && row.IsChecked)
        {
            row.IsPriority = !row.IsPriority;
            // refresh the 已选 chip
            var i = _picked.IndexOf(row);
            if (i >= 0) { _picked.RemoveAt(i); _picked.Insert(i, row); }
        }
        e.Handled = true;
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_candidateMode || RowAt(e) is not { } row) return;
        Result = row.Name;
        DialogResult = true;
    }

    private void UpdateConfirm()
    {
        if (_candidateMode) ConfirmButton.Content = _picked.Count == 0 ? "完成" : $"完成 · {_picked.Count} 件";
        else ConfirmButton.IsEnabled = _rows.Any(r => r.IsSelected);
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (_candidateMode)
            Candidates = [.. _picked.Select(r => new CandidateEquipment { Name = r.Name, IsPriority = r.IsPriority })];
        else if (_rows.FirstOrDefault(r => r.IsSelected) is { } row)
            Result = row.Name;
        else return;
        DialogResult = true;
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (_candidateMode)
        {
            foreach (var r in _picked) r.IsChecked = false;
            _picked.Clear();
            UpdateConfirm();
            return;
        }
        Result = "";
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
