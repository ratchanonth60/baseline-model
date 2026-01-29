using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using BaselineMode.WPF.Models;
using BaselineMode.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScottPlot;

namespace BaselineMode.WPF.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly FileService _fileService;
        private readonly MathService _mathService;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private double _progressValue;

        [ObservableProperty]
        private string _inputFilesInfo = "No files selected";

        [ObservableProperty]
        private string _outputDirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BaselineModeOutputs");

        // --- Added Properties ---
        [ObservableProperty]
        private int _selectedLayerIndex = 0; // 0=L1, 1=L2, 2=L6, 3=L7

        [ObservableProperty]
        private int _selectedDirectionIndex = 0; // 0=X, 1=Z

        [ObservableProperty]
        private bool _useKalmanFilter = false;

        [ObservableProperty]
        private bool _useThresholding = false;

        [ObservableProperty]
        private double _kFactor = 2.0;

        [ObservableProperty]
        private int _selectedFitMethod = 0; // 0=Gaussian, 1=Hyper-EMG

        [ObservableProperty]
        private bool _useGaussianFit = false;

        [ObservableProperty]
        private int _selectedXAxisIndex = 0; // 0=ADC, 1=Voltage

        [ObservableProperty]
        private int _selectedBaselineMode = 0; // 0=Auto, 1=File

        private List<string> _selectedFiles = new List<string>();
        // We will store result as list of objects
        [ObservableProperty]
        private List<BaselineData> _processedData = new List<BaselineData>();

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
        private ObservableCollection<ChannelViewModel> _channels = new ObservableCollection<ChannelViewModel>();

        [ObservableProperty]
        private ObservableCollection<ChannelViewModel> _channelsX = new ObservableCollection<ChannelViewModel>();

        [ObservableProperty]
        private ObservableCollection<ChannelViewModel> _channelsZ = new ObservableCollection<ChannelViewModel>();

        public event EventHandler<PlotUpdateEventArgs> RequestPlotUpdate;

        public MainViewModel()
        {
            _fileService = new FileService();
            _mathService = new MathService();
            // Initialize 16 channels
            InitializeChannels();

            // Initialize Timer for Clock
            _currentDateTime = DateTime.Now;
            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
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
        private System.Data.DataTable _displayDataTable = new System.Data.DataTable();

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

        private CancellationTokenSource _cts;

    }

    public class PlotUpdateEventArgs : EventArgs
    {
        public List<BaselineData> Data { get; }
        public PlotUpdateEventArgs(List<BaselineData> data) { Data = data; }

    }
}
