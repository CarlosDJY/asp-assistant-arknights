// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ASPAssistant.App.Views;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Controls;

public partial class EquipmentCard : UserControl
{
    public static readonly RoutedEvent TrackingToggledEvent =
        EventManager.RegisterRoutedEvent(
            "TrackingToggled", RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(EquipmentCard));

    public event RoutedEventHandler TrackingToggled
    {
        add => AddHandler(TrackingToggledEvent, value);
        remove => RemoveHandler(TrackingToggledEvent, value);
    }

    public Func<string, bool>? IsTrackedCheck { get; set; }
    public bool IsTracked { get; set; }
    public bool ShowTrackingButton
    {
        get => TrackButton.Visibility == Visibility.Visible;
        set => TrackButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }
    private static readonly Regex EffectNumber = new(@"\d+(?:\.\d+)?%?");

    public EquipmentCard()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Inherit IsTrackedCheck from parent view if not explicitly set
        if (IsTrackedCheck == null)
            IsTrackedCheck = FindAncestor<EquipmentBrowseView>()?.IsTrackedCheck;
        if (DataContext is Equipment eq && IsTrackedCheck != null)
            IsTracked = IsTrackedCheck(eq.Name);
        if (DataContext is Equipment eq2)
        {
            LoadIcon(eq2.IconPath);
        }
        UpdateEffectDisplay();
        UpdateTrackButton();
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

    private void UpdateEffectDisplay()
    {
        if (DataContext is not Equipment eq) return;
        var normal = eq.Normal.EffectDescription;
        var elite = eq.Elite.EffectDescription;
        NoEliteText.Visibility = string.IsNullOrEmpty(elite) ? Visibility.Visible : Visibility.Collapsed;
        JobChangeText.Text = $"转职：{eq.JobChangeCovenant}";
        JobChangeText.Visibility = string.IsNullOrEmpty(eq.JobChangeCovenant)
            ? Visibility.Collapsed : Visibility.Visible;
        EffectText.Inlines.Clear();
        if (string.IsNullOrEmpty(elite) || normal == elite)
        {
            EffectText.Inlines.Add(new Run(normal));
            return;
        }
        if (EffectNumber.Replace(normal, "#") != EffectNumber.Replace(elite, "#"))
        {
            EffectText.Inlines.Add(new Run($"普通：{normal}\n精锐：{elite}"));
            return;
        }

        var normalNumbers = EffectNumber.Matches(normal);
        var eliteNumbers = EffectNumber.Matches(elite);
        var offset = 0;
        for (var i = 0; i < normalNumbers.Count; i++)
        {
            var number = normalNumbers[i];
            EffectText.Inlines.Add(new Run(normal[offset..number.Index]));
            if (number.Value == eliteNumbers[i].Value)
                EffectText.Inlines.Add(new Run(number.Value));
            else
            {
                var highlighted = new Run(number.Value.TrimEnd('%')) { FontWeight = FontWeights.Bold };
                highlighted.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
                EffectText.Inlines.Add(highlighted);
                EffectText.Inlines.Add(new Run($"({eliteNumbers[i].Value})"));
                if (number.Value.EndsWith('%')) EffectText.Inlines.Add(new Run("%"));
            }
            offset = number.Index + number.Length;
        }
        EffectText.Inlines.Add(new Run(normal[offset..]));
    }

    private void OnTrackClick(object sender, RoutedEventArgs e)
    {
        IsTracked = !IsTracked;
        UpdateTrackButton();
        RaiseEvent(new RoutedEventArgs(TrackingToggledEvent, this));
    }

    private void LoadIcon(string iconPath)
    {
        if (string.IsNullOrEmpty(iconPath))
            return;
        var fullPath = Path.Combine(AppContext.BaseDirectory, "data", iconPath);
        if (File.Exists(fullPath))
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            IconImage.Source = bitmap;
        }
    }

    public void RefreshTrackedState(bool isTracked)
    {
        IsTracked = isTracked;
        UpdateTrackButton();
    }

    private void UpdateTrackButton()
    {
        TrackButton.Content = IsTracked ? "★ 追踪中" : "☆ 追踪";
        TrackButton.Tag = IsTracked ? "tracked" : "";
    }
}
