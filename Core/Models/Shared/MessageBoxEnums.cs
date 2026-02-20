namespace BaselineMode.WPF.Core.Models.Shared
{
    /// <summary>
    /// Cross-platform message box enums to replace WPF's System.Windows types.
    /// These are used by ModernMessageBox and MessageBoxService.
    /// </summary>

    public enum MsgBoxButton
    {
        OK,
        OKCancel,
        YesNo,
        YesNoCancel
    }

    public enum MsgBoxImage
    {
        Information,
        Warning,
        Error,
        Question
    }

    public enum MsgBoxResult
    {
        None,
        OK,
        Cancel,
        Yes,
        No
    }
}
