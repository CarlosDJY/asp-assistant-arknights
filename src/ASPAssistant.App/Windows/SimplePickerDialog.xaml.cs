// Modified for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App.Windows;

/// <summary>
/// Simple modal picker dialog: takes a list of (key, display, iconPath) items and
/// returns the chosen key (or null if cleared / cancelled).
/// </summary>
public partial class SimplePickerDialog : Window
{
    /// <summary>
    /// One picker entry. <see cref="RightLabel"/> is shown right-aligned on the row
    /// (e.g. covenant tags for operator picker); leave null to hide.
    /// </summary>
    public sealed record PickerItem(string Key, string Display, string? IconPath, string? RightLabel = null, string? Summary = null);

    private readonly List<PickerItem> _allItems;
    private readonly ObservableCollection<PickerItem> _filtered = [];
    private bool _suppressAutoCommit = true;
    private bool _isEquipmentPicker;
    public bool IsCoreEquipment => CoreEquipmentToggle.IsChecked == true;

    /// <summary>
    /// "selected" semantics: null means "no change / cancelled"; empty string means "cleared";
    /// non-empty means user picked that key.
    /// </summary>
    public string? Result { get; private set; }

    public SimplePickerDialog(IEnumerable<Equipment> equipment, string? currentKey, bool isCore)
        : this("选择装备", EquipmentItems(equipment), currentKey, "搜索装备名 / 双击或确定")
    {
        _isEquipmentPicker = true;
        CoreEquipmentToggle.Visibility = Visibility.Visible;
        CoreEquipmentToggle.IsChecked = isCore;
        ConfirmButton.Visibility = Visibility.Visible;
    }

    private static IEnumerable<PickerItem> EquipmentItems(IEnumerable<Equipment> equipment)
        => equipment.OrderBy(eq => eq.Tier).ThenBy(eq => eq.Name, StringComparer.CurrentCulture)
            .Select(eq => new PickerItem(eq.Name, $"[T{eq.Tier}] {eq.Name}", eq.IconPath, null,
                $"{eq.Name} · T{eq.Tier}\n{string.Join(" · ", eq.Categories)}\n" +
                (eq.Type == EquipmentType.JobChange ? $"转职：{eq.JobChangeCovenant}\n" : "普通装备\n") +
                $"普通：{SummarizeEffect(eq.Normal.EffectDescription)}\n" +
                (eq.Elite.EffectDescription.Length == 0 ? "无精锐装备" : $"精锐：{SummarizeEffect(eq.Elite.EffectDescription)}")));

    // ── Candidate mode: many pieces at once, each 候选 → 优先 → none on click ──
    private readonly Dictionary<string, bool> _candidates = new(StringComparer.Ordinal);   // name → IsPriority, in pick order
    private readonly List<string> _candidateOrder = [];
    private readonly HashSet<string> _inSlots = new(StringComparer.Ordinal);
    private bool _isCandidatePicker;

    /// <summary>Candidate mode result (null = cancelled).</summary>
    public IReadOnlyList<CandidateEquipment>? Candidates { get; private set; }

    public SimplePickerDialog(IEnumerable<Equipment> equipment, string operatorName,
        IEnumerable<CandidateEquipment> current, IEnumerable<string> inSlots)
        : this($"候选装备 · {operatorName}", EquipmentItems(equipment), null, "点一下加入候选，再点切换优先，第三下取消")
    {
        _isCandidatePicker = true;
        _inSlots.UnionWith(inSlots);
        foreach (var c in current.Where(c => !_inSlots.Contains(c.Name)))
        {
            _candidates[c.Name] = c.IsPriority;
            _candidateOrder.Add(c.Name);
        }
        for (var i = 0; i < _allItems.Count; i++) _allItems[i] = _allItems[i] with { RightLabel = CandidateLabel(_allItems[i].Key) };
        ApplyFilter("");
        ConfirmButton.Visibility = Visibility.Visible;
        UpdateCandidateButton();
    }

    private string? CandidateLabel(string key) => _inSlots.Contains(key) ? "装备格"
        : _candidates.TryGetValue(key, out var priority) ? priority ? "★ 优先" : "候选" : null;

    private void CycleCandidate(PickerItem item)
    {
        if (_inSlots.Contains(item.Key)) return;
        if (!_candidates.TryGetValue(item.Key, out var priority))
        {
            _candidates[item.Key] = false;
            _candidateOrder.Add(item.Key);
        }
        else if (!priority) _candidates[item.Key] = true;
        else
        {
            _candidates.Remove(item.Key);
            _candidateOrder.Remove(item.Key);
        }
        var updated = item with { RightLabel = CandidateLabel(item.Key) };
        _allItems[_allItems.FindIndex(i => i.Key == item.Key)] = updated;
        var shown = _filtered.IndexOf(item);
        _suppressAutoCommit = true;
        if (shown >= 0) _filtered[shown] = updated;
        ItemsList.SelectedItem = null;
        _suppressAutoCommit = false;
        UpdateCandidateButton();
    }

    private void UpdateCandidateButton() => ConfirmButton.Content = _candidates.Count == 0 ? "完成" : $"完成 · {_candidates.Count} 件";

    private static string SummarizeEffect(string effect)
        => effect.Length <= 90 ? effect : effect[..90] + "…";

    public SimplePickerDialog(string title, IEnumerable<PickerItem> items, string? currentKey, string? hint = null)
    {
        InitializeComponent();
        TitleText.Text = title;
        Title = title;
        if (!string.IsNullOrWhiteSpace(hint))
            HintText.Text = hint;
        _allItems = [.. items];
        ItemsList.ItemsSource = _filtered;
        ApplyFilter("");

        if (!string.IsNullOrEmpty(currentKey))
        {
            var match = _filtered.FirstOrDefault(i => i.Key == currentKey);
            if (match is not null)
                ItemsList.SelectedItem = match;
        }

        Loaded += (_, _) =>
        {
            // Allow the pre-selection above to settle (it would otherwise trip the
            // SelectionChanged auto-commit and close the dialog instantly).
            _suppressAutoCommit = false;
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        };
    }

    private void ApplyFilter(string text)
    {
        _filtered.Clear();
        if (string.IsNullOrWhiteSpace(text))
        {
            foreach (var item in _allItems)
                _filtered.Add(item);
        }
        else
        {
            foreach (var item in _allItems)
            {
                if (SearchMatch.Matches(text, item.Display, item.Key))
                    _filtered.Add(item);
            }
        }
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        => ApplyFilter(SearchBox.Text);

    private void OnItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_isCandidatePicker)
        {
            if (!_suppressAutoCommit && ItemsList.SelectedItem is PickerItem picked) CycleCandidate(picked);
            return;
        }
        if (_suppressAutoCommit || _isEquipmentPicker) return;
        if (ItemsList.SelectedItem is not PickerItem p) return;
        Result = p.Key;
        DialogResult = true;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (_isCandidatePicker)
        {
            Candidates = [.. _candidateOrder.Select(n => new CandidateEquipment { Name = n, IsPriority = _candidates[n] })];
            DialogResult = true;
            return;
        }
        if (ItemsList.SelectedItem is not PickerItem item) return;
        Result = item.Key;
        DialogResult = true;
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_isCandidatePicker) return;
        if (_isEquipmentPicker && ItemsList.SelectedItem is PickerItem
            && ItemsControl.ContainerFromElement(ItemsList, (DependencyObject)e.OriginalSource) is ListBoxItem)
            OnConfirmClick(sender, e);
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        Candidates = [];
        Result = "";
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
}
