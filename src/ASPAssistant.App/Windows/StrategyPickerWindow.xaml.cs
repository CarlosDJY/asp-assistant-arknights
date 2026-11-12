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

public sealed class StrategyPickRow(Strategy strategy) : INotifyPropertyChanged
{
    public Strategy Strategy { get; } = strategy;
    public string Id => Strategy.Id;
    public string Name => Strategy.Name;
    public string Initiator => Strategy.Initiator;
    public string IconPath => Strategy.IconPath;
    public int InitialHp => Strategy.InitialHp;
    public string Description => Strategy.Description.Replace('\n', ' ');
    public bool IsActive => !Strategy.Description.StartsWith("被动", StringComparison.Ordinal);
    public string KindText => IsActive ? "主动" : "被动";
    public string Summary => $"{Name} · {Initiator}\n初始生命值：{InitialHp}\n{Strategy.Description}";

    private bool _chosen;
    private StrategyTag? _tag;
    public bool IsChosen { get => _chosen; set { _chosen = value; if (!value) _tag = null; NotifyAll(); } }
    public StrategyTag? Tag { get => _tag; set { _tag = value; NotifyAll(); } }
    public bool IsCore { get => _tag == StrategyTag.阵容核心; set => Tag = value ? StrategyTag.阵容核心 : null; }
    public bool IsPreferred { get => _tag == StrategyTag.优选; set => Tag = value ? StrategyTag.优选 : null; }
    public bool IsSecond { get => _tag == StrategyTag.次选; set => Tag = value ? StrategyTag.次选 : null; }
    public string TagText => _tag switch { StrategyTag.阵容核心 => "核心", StrategyTag.优选 => "优选", StrategyTag.次选 => "次选", _ => "" };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

/// <summary>The lineup's strategies at once: click to add / remove, then 核心 / 优选 / 次选 on the chosen ones.</summary>
public partial class StrategyPickerWindow : Window
{
    private readonly List<StrategyPickRow> _rows;
    private readonly ObservableCollection<StrategyPickRow> _shown = [];
    private readonly ObservableCollection<StrategyPickRow> _chosen = [];
    // Strategies from shared lineups that are not in the data: kept as they are.
    private readonly List<(string Id, StrategyTag? Tag)> _unknown = [];

    /// <summary>The picked strategies in order (set when 完成 is clicked).</summary>
    public IReadOnlyList<(string Id, StrategyTag? Tag)> Picked { get; private set; } = [];

    public StrategyPickerWindow(IEnumerable<Strategy> strategies, IEnumerable<(string Id, StrategyTag? Tag)> current)
    {
        InitializeComponent();
        _rows = [.. strategies.Select(s => new StrategyPickRow(s))];
        foreach (var (id, tag) in current)
        {
            if (_rows.FirstOrDefault(r => r.Id == id) is not { } row) { _unknown.Add((id, tag)); continue; }
            row.IsChosen = true;
            row.Tag = tag;
            _chosen.Add(row);
        }
        foreach (var row in _rows) row.PropertyChanged += (_, _) => RefreshChosen();
        ItemsList.ItemsSource = _shown;
        ChosenList.ItemsSource = _chosen;
        ApplyFilter();
        Loaded += (_, _) => { SearchBox.Focus(); Keyboard.Focus(SearchBox); };
    }

    private void RefreshChosen()
    {
        // re-render chips after a tag change
        var copy = _chosen.ToList();
        _chosen.Clear();
        foreach (var r in copy) _chosen.Add(r);
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        if (_rows is null) return;
        var query = SearchBox.Text;
        var kind = KindList.SelectedIndex;
        _shown.Clear();
        foreach (var row in _rows)
        {
            if (kind == 1 && row.IsActive || kind == 2 && !row.IsActive) continue;
            if (!SearchMatch.Matches(query, row.Name, row.Initiator) && !row.Strategy.Description.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            _shown.Add(row);
        }
        EmptyHint.Visibility = _shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnItemClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(ItemsList, e.OriginalSource as DependencyObject) is not ListBoxItem { Content: StrategyPickRow row }) return;
        for (var d = e.OriginalSource as DependencyObject; d is not null && d != ItemsList; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            if (d is ToggleButton) return;
        row.IsChosen = !row.IsChosen;
        if (row.IsChosen) _chosen.Add(row);
        else _chosen.Remove(row);
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        foreach (var r in _chosen.ToList()) r.IsChosen = false;
        _chosen.Clear();
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        Picked = [.. _chosen.Select(r => (r.Id, r.Tag)), .. _unknown];
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
