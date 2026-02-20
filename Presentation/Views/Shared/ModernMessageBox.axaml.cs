using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using BaselineMode.WPF.Core.Models.Shared;

namespace BaselineMode.WPF.Views.Shared
{
    public partial class ModernMessageBox : Window
    {
        public MsgBoxResult Result { get; private set; } = MsgBoxResult.None;

        public ModernMessageBox(string message, string title, MsgBoxButton button, MsgBoxImage image)
        {
            InitializeComponent();
            TxtMessage.Text = message;
            TxtTitle.Text = title;

            SetupButtons(button);
            SetupImage(image);
        }

        // Parameterless constructor required by Avalonia XAML loader
        public ModernMessageBox()
        {
            InitializeComponent();
        }

        private void SetupButtons(MsgBoxButton button)
        {
            switch (button)
            {
                case MsgBoxButton.OK:
                    BtnOk.IsVisible = true;
                    BtnCancel.IsVisible = false;
                    BtnYes.IsVisible = false;
                    BtnNo.IsVisible = false;
                    break;
                case MsgBoxButton.OKCancel:
                    BtnOk.IsVisible = true;
                    BtnCancel.IsVisible = true;
                    BtnYes.IsVisible = false;
                    BtnNo.IsVisible = false;
                    break;
                case MsgBoxButton.YesNo:
                    BtnOk.IsVisible = false;
                    BtnCancel.IsVisible = false;
                    BtnYes.IsVisible = true;
                    BtnNo.IsVisible = true;
                    break;
                case MsgBoxButton.YesNoCancel:
                    BtnOk.IsVisible = false;
                    BtnCancel.IsVisible = true;
                    BtnYes.IsVisible = true;
                    BtnNo.IsVisible = true;
                    break;
            }
        }

        private void SetupImage(MsgBoxImage image)
        {
            var primaryColor = Avalonia.Application.Current?.Resources.TryGetResource("PrimaryColor", null, out var pc) == true
                ? (ISolidColorBrush)pc! : new SolidColorBrush(Avalonia.Media.Color.Parse("#2563EB"));
            var dangerColor = Avalonia.Application.Current?.Resources.TryGetResource("DangerColor", null, out var dc) == true
                ? (ISolidColorBrush)dc! : new SolidColorBrush(Avalonia.Media.Color.Parse("#EF4444"));
            var warningColor = Avalonia.Application.Current?.Resources.TryGetResource("WarningColor", null, out var wc) == true
                ? (ISolidColorBrush)wc! : new SolidColorBrush(Avalonia.Media.Color.Parse("#F59E0B"));

            switch (image)
            {
                case MsgBoxImage.Error:
                    IconPath.Data = Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z");
                    IconPath.Fill = dangerColor;
                    break;
                case MsgBoxImage.Question:
                    IconPath.Data = Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 17h-2v-2h2v2zm2.07-7.75l-.9.92C13.45 12.9 13 13.5 13 15h-2v-.5c0-1.1.45-2.1 1.17-2.83l1.24-1.26c.37-.36.59-.86.59-1.41 0-1.1-.9-2-2-2s-2 .9-2 2H8c0-2.21 1.79-4 4-4s4 1.79 4 4c0 .88-.36 1.68-.93 2.25z");
                    IconPath.Fill = primaryColor;
                    break;
                case MsgBoxImage.Warning:
                    IconPath.Data = Geometry.Parse("M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z");
                    IconPath.Fill = warningColor;
                    break;
                case MsgBoxImage.Information:
                default:
                    IconPath.Data = Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z");
                    IconPath.Fill = primaryColor;
                    break;
            }
        }

        private void BtnOk_Click(object? sender, RoutedEventArgs e)
        {
            Result = MsgBoxResult.OK;
            Close();
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e)
        {
            Result = MsgBoxResult.Cancel;
            Close();
        }

        private void BtnYes_Click(object? sender, RoutedEventArgs e)
        {
            Result = MsgBoxResult.Yes;
            Close();
        }

        private void BtnNo_Click(object? sender, RoutedEventArgs e)
        {
            Result = MsgBoxResult.No;
            Close();
        }
    }
}
