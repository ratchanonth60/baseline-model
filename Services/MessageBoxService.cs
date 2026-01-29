using System;
using System.Windows;
using BaselineMode.WPF.Views;

namespace BaselineMode.WPF.Services
{
    public static class MessageBoxService
    {
        public static MessageBoxResult Show(string message, string title = "Notification", MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.Information)
        {
            // Execute on UI Thread
            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                return Application.Current.Dispatcher.Invoke(() => Show(message, title, button, image));
            }

            var dlg = new ModernMessageBox(message, title, button, image);

            // Set owner if any window is active to center properly
            if (Application.Current?.MainWindow != null && Application.Current.MainWindow.IsVisible)
            {
                dlg.Owner = Application.Current.MainWindow;
                dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }

            dlg.ShowDialog();
            return dlg.Result;
        }
    }
}
