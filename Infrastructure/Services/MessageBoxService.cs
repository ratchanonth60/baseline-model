using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using BaselineMode.WPF.Core.Models.Shared;
using BaselineMode.WPF.Views.Shared;

namespace BaselineMode.WPF.Infrastructure.Services
{
    public static class MessageBoxService
    {
        public static async Task<MsgBoxResult> ShowAsync(string message, string title = "Notification", MsgBoxButton button = MsgBoxButton.OK, MsgBoxImage image = MsgBoxImage.Information)
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dlg = new ModernMessageBox(message, title, button, image);

                // Set owner if a main window is available
                if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    && desktop.MainWindow is { IsVisible: true } mainWindow)
                {
                    await dlg.ShowDialog(mainWindow);
                }
                else
                {
                    dlg.Show();
                    // Wait for dialog to close
                    var tcs = new TaskCompletionSource();
                    dlg.Closed += (_, _) => tcs.TrySetResult();
                    await tcs.Task;
                }

                return dlg.Result;
            });
        }

        /// <summary>
        /// Synchronous wrapper for backward compatibility (fire-and-forget or blocking on UI thread).
        /// For new code, prefer ShowAsync.
        /// </summary>
        public static MsgBoxResult Show(string message, string title = "Notification", MsgBoxButton button = MsgBoxButton.OK, MsgBoxImage image = MsgBoxImage.Information)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                // Already on UI thread - run synchronously via nested dispatch
                var dlg = new ModernMessageBox(message, title, button, image);

                if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    && desktop.MainWindow is { IsVisible: true } mainWindow)
                {
                    // ShowDialog returns a task; we need to block
                    dlg.ShowDialog(mainWindow).GetAwaiter().GetResult();
                }
                else
                {
                    dlg.Show();
                }

                return dlg.Result;
            }
            else
            {
                return Dispatcher.UIThread.InvokeAsync(() => Show(message, title, button, image)).GetAwaiter().GetResult();
            }
        }
    }
}
