// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
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

    public static readonly RoutedEvent AcquiredEvent = EventManager.RegisterRoutedEvent(
        "Acquired", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(OperatorCard));

    public event RoutedEventHandler Acquired
    {
        add => AddHandler(AcquiredEvent, value);
        remove => RemoveHandler(AcquiredEvent, value);
    }

    public static readonly DependencyProperty IsAcquiredProperty = DependencyProperty.Register(
        nameof(IsAcquired), typeof(bool), typeof(OperatorCard),
        new PropertyMetadata(false, (d, _) => ((OperatorCard)d).UpdateTrackButton()));

    public bool IsAcquired
    {
        get => (bool)GetValue(IsAcquiredProperty);
        set => SetValue(IsAcquiredProperty, value);
    }

    private void OnAcquiredClick(object sender, RoutedEventArgs e)
        => RaiseEvent(new RoutedEventArgs(AcquiredEvent, this));

    public OperatorCard()
    {
        InitializeComponent();
        HoverToolTipBehavior.Attach(CardBorder, DetailsToolTip);
        // Details are built on demand and dropped when the pop-up closes.
        CardBorder.ToolTipOpening += (_, _) => DetailsHost.Content = DataContext;
        DetailsToolTip.Closed += (_, _) => DetailsHost.Content = null;
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
        IsTracked = IsTrackingList ? !IsAcquired : IsTrackedCheck != null && IsTrackedCheck(op.Name);

        // Inherit IsBannedCheck from parent view if not explicitly set
        if (IsBannedCheck == null)
            IsBannedCheck = FindAncestor<OperatorBrowseView>()?.IsBannedCheck;
        IsBanned = IsBannedCheck != null && IsBannedCheck(op.Name);

        TierRail.SetResourceReference(Border.BackgroundProperty,
            op.Tier >= 5 ? "EliteBrush" : op.Tier == 4 ? "AccentSolidBrush" : "BorderBrush");


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
        if (IsTrackingList) IsTracked = !IsAcquired;
        AcquiredButton.Tag = IsAcquired ? "acquired" : "";
        var tracked = IsTracked && !IsTrackingList;
        TrackButton.Tag = TryFindResource(IsTrackingList ? "IconClose" : "IconStar");
        TrackButton.ToolTip = IsTrackingList || IsTracked ? "取消追踪" : "追踪干员";
        if (tracked)
        {
            TrackButton.SetResourceReference(ForegroundProperty, "FinalBrush");
            TrackButton.SetResourceReference(BackgroundProperty, "FinalTintBrush");
        }
        else
        {
            TrackButton.ClearValue(ForegroundProperty);
            TrackButton.ClearValue(BackgroundProperty);
        }
        if (tracked)
        {
            TrackingPortrait.SetResourceReference(Avatar.RingProperty, "FinalBrush");
            CardBorder.SetResourceReference(Border.BorderBrushProperty, "FinalBrush");
        }
        else
        {
            TrackingPortrait.ClearValue(Avatar.RingProperty);
            CardBorder.ClearValue(Border.BorderBrushProperty);
        }
        UpdateButtonVisibility();
    }

    /// <summary>Single-column rows have room for ☆ and ⊘ all the time; the tracking list only removes (✕).</summary>
    private void UpdateButtonVisibility()
    {
        TrackButton.Visibility = Visibility.Visible;
        BanButton.Visibility = IsTrackingList ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateBanButton()
    {
        BanButton.ToolTip = IsBanned ? "取消禁用" : "标记为本局禁用";
        UpdateButtonVisibility();
        if (IsBanned)
        {
            BanButton.SetResourceReference(ForegroundProperty, "DangerBrush");
            BanButton.SetResourceReference(BackgroundProperty, "DangerTintBrush");
        }
        else
        {
            BanButton.ClearValue(ForegroundProperty);
            BanButton.ClearValue(BackgroundProperty);
        }
    }

    private void UpdateBannedDisplay()
    {
        BanBadge.Visibility = IsBanned ? Visibility.Visible : Visibility.Collapsed;
        NameText.TextDecorations = IsBanned ? TextDecorations.Strikethrough : null;
        // Banned operators stay listed but fade back
        CardBorder.Opacity = IsBanned ? 0.5 : 1.0;
    }
}

/// <summary>One covenant label on an operator row.</summary>
public sealed record CovenantChipItem(string Name, bool IsCore);
