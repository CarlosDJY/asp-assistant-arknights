// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ASPAssistant.App.Views;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Controls;

public partial class OperatorCard : UserControl
{
    public static readonly RoutedEvent TrackingToggledEvent =
        EventManager.RegisterRoutedEvent(
            "TrackingToggled", RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(OperatorCard));

    public event RoutedEventHandler TrackingToggled
    {
        add => AddHandler(TrackingToggledEvent, value);
        remove => RemoveHandler(TrackingToggledEvent, value);
    }

    public static readonly RoutedEvent BanToggledEvent =
        EventManager.RegisterRoutedEvent(
            "BanToggled", RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(OperatorCard));

    public event RoutedEventHandler BanToggled
    {
        add => AddHandler(BanToggledEvent, value);
        remove => RemoveHandler(BanToggledEvent, value);
    }

    public Func<string, bool>? IsTrackedCheck { get; set; }
    public bool IsTracked { get; set; }

    public Func<string, bool>? IsBannedCheck { get; set; }
    public bool IsBanned { get; set; }

    private bool _isTrackingList;
    public bool IsTrackingList
    {
        get => _isTrackingList;
        set
        {
            _isTrackingList = value;
            RefreshOperatorState();
        }
    }

    public OperatorCard()
    {
        InitializeComponent();
        HoverToolTipBehavior.Attach(CardBorder, DetailsToolTip);
        DataContextChanged += (_, _) => RefreshOperatorState();
        Loaded += (_, _) => RefreshOperatorState();
    }

    private void RefreshOperatorState()
    {
        DetailsToolTip.IsOpen = false;
        if (DataContext is not Operator op) return;

        CovenantChips.ItemsSource = op.CoreCovenants.Select(c => new CovenantChipItem(c, true))
            .Concat(op.AdditionalCovenants.Select(c => new CovenantChipItem(c, false)))
            .ToList();

        // Inherit IsTrackedCheck from parent view if not explicitly set
        if (IsTrackedCheck == null)
            IsTrackedCheck = FindAncestor<OperatorBrowseView>()?.IsTrackedCheck;
        IsTracked = IsTrackingList || (IsTrackedCheck != null && IsTrackedCheck(op.Name));

        // Inherit IsBannedCheck from parent view if not explicitly set
        if (IsBannedCheck == null)
            IsBannedCheck = FindAncestor<OperatorBrowseView>()?.IsBannedCheck;
        IsBanned = IsBannedCheck != null && IsBannedCheck(op.Name);

        TrackingPortrait.Visibility = IsTrackingList ? Visibility.Visible : Visibility.Collapsed;
        PortraitBackground.Visibility = IsTrackingList ? Visibility.Collapsed : Visibility.Visible;
        BanButton.Visibility = IsTrackingList ? Visibility.Collapsed : Visibility.Visible;

        UpdateTrackButton();
        UpdateBanButton();
        UpdateBannedDisplay();
    }

    private T? FindAncestor<T>() where T : DependencyObject
    {
        DependencyObject? current = VisualTreeHelper.GetParent(this);
        while (current != null)
        {
            if (current is T t) return t;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        DetailsToolTip.IsOpen = false;
    }

    private void OnTrackClick(object sender, RoutedEventArgs e)
    {
        IsTracked = !IsTracked;
        UpdateTrackButton();
        RaiseEvent(new RoutedEventArgs(TrackingToggledEvent, this));
    }

    public void RefreshTrackedState(bool isTracked)
    {
        IsTracked = isTracked;
        UpdateTrackButton();
    }

    public void RefreshBannedState(bool isBanned)
    {
        IsBanned = isBanned;
        UpdateBanButton();
        UpdateBannedDisplay();
    }

    private void OnBanClick(object sender, RoutedEventArgs e)
    {
        IsBanned = !IsBanned;
        UpdateBanButton();
        UpdateBannedDisplay();
        RaiseEvent(new RoutedEventArgs(BanToggledEvent, this));
    }

    private void UpdateTrackButton()
    {
        TrackButton.Content = IsTrackingList ? "✕" : IsTracked ? "★" : "☆";
        TrackButton.ToolTip = IsTracked ? "取消追踪" : "追踪干员";
        TrackButton.Tag = IsTracked ? "tracked" : "";
        CardBorder.SetResourceReference(Border.BorderBrushProperty, IsTracked ? "TrackingBrush" : "BorderBrush");
    }

    private void UpdateBanButton()
    {
        BanButton.Content = IsBanned ? "✕" : "⊘";
        BanButton.ToolTip = IsBanned ? "取消禁用" : "标记为本局禁用";
        BanButton.Tag = IsBanned ? "banned" : "";
    }

    private void UpdateBannedDisplay()
    {
        var banVis = IsBanned ? Visibility.Visible : Visibility.Collapsed;
        BanDimOverlay.Visibility = banVis;
        BanBadge.Visibility = banVis;
        // Slightly reduce overall card opacity to visually deprioritise banned operators
        Opacity = IsBanned ? 0.65 : 1.0;
    }
}

/// <summary>One covenant label on an operator row.</summary>
public sealed record CovenantChipItem(string Name, bool IsCore);
