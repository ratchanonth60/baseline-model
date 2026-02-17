using CommunityToolkit.Mvvm.ComponentModel;

namespace BaselineMode.WPF.Presentation.ViewModels.Shared
{
    public abstract partial class SharedViewModelBase : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsProgressIndeterminate))]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsProgressIndeterminate))]
        private double _progressValue;

        /// <summary>True when busy and progress not yet reported (show indeterminate bar).</summary>
        public bool IsProgressIndeterminate => IsBusy && ProgressValue <= 0;

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
