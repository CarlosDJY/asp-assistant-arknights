// Modified for ASP Assistant Regulation on 2026-11-10; see MODIFICATIONS.md.
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ASPAssistant.App.Controls;

internal static class HoverToolTipBehavior
{
    public static void Attach(Border row, ToolTip details)
    {
        var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        closeTimer.Tick += (_, _) =>
        {
            closeTimer.Stop();
            if (!row.IsMouseOver && !details.IsMouseOver)
                details.IsOpen = false;
        };
        row.MouseEnter += (_, _) => closeTimer.Stop();
        details.MouseEnter += (_, _) => closeTimer.Stop();
        row.MouseLeave += OnMouseLeave;
        details.MouseLeave += OnMouseLeave;
        details.Closed += (_, _) => closeTimer.Stop();
        row.Unloaded += (_, _) =>
        {
            closeTimer.Stop();
            details.IsOpen = false;
        };

        void OnMouseLeave(object sender, MouseEventArgs e)
        {
            // Bridge the 8px gap while excluding unrelated rows from WPF's safe area.
            if (details.IsOpen)
                closeTimer.Start();
        }
    }
}
