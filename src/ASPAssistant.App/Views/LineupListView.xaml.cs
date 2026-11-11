using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Views;

public partial class LineupListView : UserControl
{
    public event Action? NewLineupRequested;
    public event Action? ImportLineupRequested;
    public event Action<Lineup>? EditLineupRequested;
    public event Action<Lineup>? ShareLineupRequested;
    public event Action<Lineup>? TrackLineupRequested;
    public event Action<Lineup>? DeleteLineupRequested;

    private LineupViewModel? _vm;

    public LineupListView()
    {
        InitializeComponent();
        DataContextChanged += OnViewDataContextChanged;
    }

    private void OnViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.Lineups.CollectionChanged -= OnLineupsChanged;

        _vm = e.NewValue as LineupViewModel;
        if (_vm is not null)
            _vm.Lineups.CollectionChanged += OnLineupsChanged;

        UpdateEmptyHint();
    }

    private void OnLineupsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => UpdateEmptyHint();

    private void UpdateEmptyHint()
    {
        EmptyHint.Visibility = (_vm?.Lineups.Count ?? 0) == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public void RefreshAllBadges()
    {
        // Force-rebuild templates: cheapest reliable way to refresh all the inline badges/icons
        // when a single lineup mutates (e.g. after editor returns).
        var src = LineupItems.ItemsSource;
        LineupItems.ItemsSource = null;
        LineupItems.ItemsSource = src;
        UpdateEmptyHint();
    }

    private void OnNewLineup(object sender, RoutedEventArgs e) => NewLineupRequested?.Invoke();
    private void OnImportLineup(object sender, RoutedEventArgs e) => ImportLineupRequested?.Invoke();

    private Lineup? FindLineup(object sender)
    {
        if (sender is FrameworkElement { DataContext: Lineup lineup }) return lineup;
        if (sender is FrameworkElement fe && fe.Tag is string id && _vm is not null)
            return _vm.Lineups.FirstOrDefault(l => l.Id == id);
        return null;
    }

    /// <summary>Activated covenants of a card, core first, shown as people ("乌萨斯 6").</summary>
    private void OnCovenantPillsLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ItemsControl list || list.DataContext is not Lineup lineup || _vm is null) return;
        list.ItemsSource = _vm.ComputeStats(lineup).Where(c => c.Activated)
            .OrderByDescending(c => c.BondType == "SEASON").ThenByDescending(c => c.Count)
            .Select(c => new LineupCovenantPill(c.Name, c.Count, c.BondType == "SEASON")).ToList();
    }

    private void OnEditLineup(object sender, RoutedEventArgs e)
    {
        if (FindLineup(sender) is { } l) EditLineupRequested?.Invoke(l);
    }

    private void OnShareLineup(object sender, RoutedEventArgs e)
    {
        if (FindLineup(sender) is { } l) ShareLineupRequested?.Invoke(l);
    }

    private void OnTrackLineup(object sender, RoutedEventArgs e)
    {
        if (FindLineup(sender) is { } l) TrackLineupRequested?.Invoke(l);
    }

    private void OnDeleteLineup(object sender, RoutedEventArgs e)
    {
        if (FindLineup(sender) is { } l) DeleteLineupRequested?.Invoke(l);
    }

    private void OnCovenantBadgeLoaded(object sender, RoutedEventArgs e)
    {
        // Compute activated count for this lineup card.
        if (sender is not TextBlock tb || tb.Tag is not string id || _vm is null) return;
        var lineup = _vm.Lineups.FirstOrDefault(l => l.Id == id);
        if (lineup is null) return;

        int activated = _vm.CountActivated(lineup);
        tb.Text = $"激活 {activated} 个盟约";
    }

    private void OnSlotIconLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Image img || img.Tag is not string opName) return;
        if (string.IsNullOrEmpty(opName))
        {
            img.Source = null;
            return;
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, "data", "icons", "operators", $"{opName}.png");
        if (!File.Exists(iconPath))
        {
            img.Source = null;
            return;
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(iconPath, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            img.Source = bmp;
        }
        catch
        {
            img.Source = null;
        }
    }
}

public sealed record LineupCovenantPill(string Name, int Count, bool IsCore);
