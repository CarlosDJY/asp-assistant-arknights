// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ASPAssistant.App.Windows;

/// <summary>Short fade + slide used when pages appear (tabs, settings). Kept under 0.25 s so it never feels slow.</summary>
internal static class PageTransition
{
    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    public static void SlideIn(FrameworkElement element, double fromX = 0, double fromY = 0)
    {
        var move = Transform(element);
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = Ease });
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = Ease });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = Ease });
    }

    public static void SlideOut(FrameworkElement element, double toX, Action completed)
    {
        var move = Transform(element);
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140)) { EasingFunction = Ease };
        fade.Completed += (_, _) =>
        {
            completed();
            // Release the animated values so the next SlideIn starts clean.
            element.BeginAnimation(UIElement.OpacityProperty, null);
            move.BeginAnimation(TranslateTransform.XProperty, null);
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, toX, TimeSpan.FromMilliseconds(140)) { EasingFunction = Ease });
    }

    private static TranslateTransform Transform(FrameworkElement element)
    {
        if (element.RenderTransform is TranslateTransform t && !t.IsFrozen) return t;
        var created = new TranslateTransform();
        element.RenderTransform = created;
        return created;
    }
}
