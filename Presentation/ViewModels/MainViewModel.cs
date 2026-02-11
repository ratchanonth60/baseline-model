using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using BaselineMode.WPF.Core.Models;
using BaselineMode.WPF.Infrastructure.Services;
using BaselineMode.WPF.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScottPlot;
using BaselineMode.WPF.Core.Interfaces.Observation;

namespace BaselineMode.WPF.Presentation.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly IFileService _fileService;
        private readonly IMathService _mathService;
        private bool _disposed = false;

        [ObservableProperty]
        private CalibrationViewModel _calibrationVM;

        [ObservableProperty]
        private FluxViewModel _fluxVM;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private System.Windows.Media.Brush _statusColor = System.Windows.Media.Brushes.Gray;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private double _progressValue;

        [ObservableProperty]
        private string _inputFilesInfo = "No files selected";

        [ObservableProperty]
        private string _outputDirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BaselineModeOutputs");

        // --- Graph Color Configuration ---
        [ObservableProperty]
        private System.Windows.Media.Color _graphFigureColor = System.Windows.Media.Color.FromRgb(37, 37, 38);

        [ObservableProperty]
        private System.Windows.Media.Color _graphDataColor = System.Windows.Media.Color.FromRgb(40, 40, 40);

        [ObservableProperty]
        private System.Windows.Media.Color _graphSeriesColor = System.Windows.Media.Colors.DodgerBlue;

        [ObservableProperty]
        private System.Windows.Media.Color _graphTextColor = System.Windows.Media.Colors.White;

        partial void OnGraphFigureColorChanged(System.Windows.Media.Color value) => RequestPlotUpdate?.Invoke(this, new PlotUpdateEventArgs(ProcessedData));
        partial void OnGraphDataColorChanged(System.Windows.Media.Color value) => RequestPlotUpdate?.Invoke(this, new PlotUpdateEventArgs(ProcessedData));
        partial void OnGraphSeriesColorChanged(System.Windows.Media.Color value) => RequestPlotUpdate?.Invoke(this, new PlotUpdateEventArgs(ProcessedData));
        partial void OnGraphTextColorChanged(System.Windows.Media.Color value) => RequestPlotUpdate?.Invoke(this, new PlotUpdateEventArgs(ProcessedData));

        // --- Added Properties ---
        [ObservableProperty]
        private int _selectedLayerIndex = 0; // 0=L1, 1=L2, 2=L6, 3=L7

        [ObservableProperty]
        private int _selectedDirectionIndex = 0; // 0=X, 1=Z

        [ObservableProperty]
        private int _selectedMode = 0; // 0=Cut off baseline, 1=Baseline setting

        [ObservableProperty]
        private bool _useKalmanFilter = false;

        [ObservableProperty]
        private bool _useThresholding = false;

        [ObservableProperty]
        private double _kFactor = 2.0;

        // --- Multi-Fit Flags ---
        [ObservableProperty]
        private bool _showGaussianFit = true;

        [ObservableProperty]
        private bool _showHemgSingleFit = false;

        [ObservableProperty]
        private bool _showHemgDoubleFit = false;

        [ObservableProperty]
        private bool _showLorentzianFit = false;

        // Deprecated: private int _selectedFitMethod = 0; 

        partial void OnShowGaussianFitChanged(bool value) => RefreshIfHasData();
        partial void OnShowHemgSingleFitChanged(bool value) => RefreshIfHasData();
        partial void OnShowHemgDoubleFitChanged(bool value) => RefreshIfHasData();
        partial void OnShowLorentzianFitChanged(bool value) => RefreshIfHasData();

        [ObservableProperty]
        private int _selectedXAxisIndex = 0; // 0=ADC, 1=Voltage

        [ObservableProperty]
        private int _selectedBaselineMode = 0; // 0=Before, 1=After, 2=Before log, 3=After log

        [ObservableProperty]
        private double _energyCalibrationSlope = 0.000427; // Placeholder: 7 MeV / 16384 channels

        [ObservableProperty]
        private double _energyCalibrationIntercept = 0.0;

        private List<string> _selectedFiles = [];
        // We will store result as list of objects
        [ObservableProperty]
        private List<BaselineData> _processedData = [];

        // Statistics
        [ObservableProperty]
        private string _statsText = "Peak: -, Mean: -, Sigma: -";

        [ObservableProperty]
        private bool _canSaveMean = false;

        // Plot Control (We will bind a method to pass the plot control or use a wrapper)
        // For simplicity in this step, we will expose data collections that the View can observe, 
        // or we handle plotting in the View's CodeBehind triggered by an event/message. 
        // A common pattern with ScottPlot 4 is to pass the WpfPlot to the VM or use a service.
        // --- Collections ---
        [ObservableProperty]
        private ObservableCollection<ChannelViewModel> _channels = [];

        [ObservableProperty]
        private ObservableCollection<ChannelViewModel> _channelsX = [];

        [ObservableProperty]
        private ObservableCollection<ChannelViewModel> _channelsZ = [];

        public event EventHandler<PlotUpdateEventArgs>? RequestPlotUpdate;

        public MainViewModel(IFileService fileService, IMathService mathService, IFileHelper fileHelper, IObservationDataProcessor dataProcessor)
        {
            _fileService = fileService;
            _mathService = mathService;

            // Initialize CalibrationVM with dependencies
            _calibrationVM = new CalibrationViewModel(mathService, fileHelper, dataProcessor);

            // Initialize 16 channels
            InitializeChannels();

            // Initialize Timer for Clock
            _currentDateTime = DateTime.Now;
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timer.Tick += (s, e) => CurrentDateTime = DateTime.Now;
            timer.Start();
        }

        [ObservableProperty]
        private DateTime _currentDateTime;

        [ObservableProperty]
        private double _thresholdValue = 0;

        [ObservableProperty]
        private string _headerInfoText = string.Empty;


        [ObservableProperty]
        private System.Data.DataTable _displayDataTable = new();

        [ObservableProperty]
        private int _currentPage = 1;

        [ObservableProperty]
        private int _pageSize = 100;

        [ObservableProperty]
        private int _totalPages = 1;

        [ObservableProperty]
        private string _pageInfoText = "Page 0 of 0";

        [ObservableProperty]
        private int _delayTimeMs = 0;

        [ObservableProperty]
        private string _outputFileName = "output.txt";

        [ObservableProperty]
        private string _startTimeStr = "-";

        [ObservableProperty]
        private string _stopTimeStr = "-";

        [ObservableProperty]
        private string _durationStr = "-";

        [ObservableProperty]
        private string _dataCountsStr = "-";

        private CancellationTokenSource? _cts;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources
                    _cts?.Cancel();
                    _cts?.Dispose();
                    _fileService?.Dispose();
                    _mathService?.Dispose();

                    // Clear large data structures
                    ProcessedData?.Clear();
                    Channels?.Clear();
                    ChannelsX?.Clear();
                    ChannelsZ?.Clear();
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    // PlotUpdateEventArgs moved to Models namespace
}
