using CommunityToolkit.Mvvm.ComponentModel;

namespace BaselineMode.WPF.Presentation.ViewModels
{
    public abstract partial class SharedViewModelBase : ObservableObject
    {
        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private double _progressValue;

        [ObservableProperty]
        private string[]? _inputFileList;

        public virtual void Reset()
        {
            IsBusy = false;
            StatusMessage = "Ready";
            ProgressValue = 0;
            InputFileList = null;
        }
    }
}
