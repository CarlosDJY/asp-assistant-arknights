using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ASPAssistant.App.Windows;

public static class MinimizableDialog
{
    [DllImport("user32.dll")]
    private static extern bool EnableWindow(IntPtr window, bool enabled);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    public static async Task ShowAsync(Window dialog, UIElement ownerContent)
    {
        var owner = dialog.Owner;
        var ownerHandle = new WindowInteropHelper(owner).Handle;
        var ownerWasEnabled = owner.IsEnabled;
        var contentWasEnabled = ownerContent.IsEnabled;
        var closed = new TaskCompletionSource();
        void SetOwnerEnabled(bool enabled)
        {
            owner.IsEnabled = enabled;
            // Closing the owner also closes its child; its HWND is then already gone.
            if (IsWindow(ownerHandle)) EnableWindow(ownerHandle, enabled);
        }
        void UpdateOwner(object? sender, EventArgs e)
            => SetOwnerEnabled(ownerWasEnabled && dialog.WindowState == WindowState.Minimized);
        void OnClosing(object? sender, CancelEventArgs e) => SetOwnerEnabled(ownerWasEnabled);
        void OnClosed(object? sender, EventArgs e) => closed.SetResult();

        dialog.StateChanged += UpdateOwner;
        dialog.Closing += OnClosing;
        dialog.Closed += OnClosed;
        try
        {
            ownerContent.IsEnabled = false;
            dialog.Show();
            UpdateOwner(dialog, EventArgs.Empty);
            await closed.Task;
        }
        finally
        {
            dialog.StateChanged -= UpdateOwner;
            dialog.Closing -= OnClosing;
            dialog.Closed -= OnClosed;
            SetOwnerEnabled(ownerWasEnabled);
            ownerContent.IsEnabled = contentWasEnabled;
        }
    }
}
