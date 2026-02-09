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

namespace BaselineMode.WPF.Views.models
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly IFileService _fileService;
        private readonly IMathService _mathService;
        private bool _disposed = false;

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
        private int _selectedMode = 0; // 0=Cut off baseline, 1=Baseline setting

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
        private int _selectedBaselineMode = 0; // 0=Before, 1=After, 2=Before log, 3=After log

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

        public event EventHandler<PlotUpdateEventArgs>? RequestPlotUpdate;

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

        private CancellationTokenSource? _cts;

        // ── Shared helpers to eliminate duplication across partial files ──

        /// <summary>
        /// Returns the layer accessor matching the current <see cref="SelectedLayerIndex"/>.
        /// Used by Plotting, Processing, UpdateDisplayTable, and CoincidenceMatrix.
        /// </summary>
        private Func<BaselineData, double[]> GetLayerSelector() => SelectedLayerIndex switch
        {
            1 => d => d.L2,
            2 => d => d.L6,
            3 => d => d.L7,
            _ => d => d.L1
        };

        /// <summary>
        /// Extracts raw channel data from <see cref="ProcessedData"/> for a given channel index.
        /// </summary>
        private double[] ExtractChannelData(Func<BaselineData, double[]> layerSelector, int channelIndex)
        {
            int count = ProcessedData.Count;
            double[] data = new double[count];
            for (int i = 0; i < count; i++)
                data[i] = layerSelector(ProcessedData[i])[channelIndex];
            return data;
        }

        /// <summary>
        /// Applies baseline subtraction to raw channel data if the current mode requires it.
        /// Returns <c>true</c> when subtraction was applied.
        /// </summary>
        private bool ApplyBaselineSubtraction(double[] rawData, int channelIndex, out double meanUsed)
        {
            bool shouldSubtract = SelectedBaselineMode == 1 || SelectedBaselineMode == 3;
            meanUsed = 0;

            if (!shouldSubtract)
                return false;

            if (SelectedMode == 0)
            {
                meanUsed = LoadMeanFromFile(channelIndex);
                if (meanUsed == 0)
                    meanUsed = CalculateMean(rawData);
            }
            else
            {
                meanUsed = CalculateMean(rawData);
            }

            for (int i = 0; i < rawData.Length; i++)
                rawData[i] -= meanUsed;

            return true;
        }

        /// <summary>
        /// Builds a histogram from filtered channel data with correct range/binCount.
        /// </summary>
        private (double[] counts, double[] binCenters) BuildHistogram(double[] filteredData, bool baselineSubtracted, int binCount = 16384)
        {
            double minVal, maxVal;

            if (baselineSubtracted)
            {
                minVal = filteredData.Min();
                maxVal = filteredData.Max();
                double range = maxVal - minVal;
                minVal -= range * 0.05;
                maxVal += range * 0.05;
            }
            else
            {
                minVal = 0;
                maxVal = 16383;
            }

            var (counts, binEdges) = ScottPlot.Statistics.Common.Histogram(
                filteredData, min: minVal, max: maxVal, binCount: binCount);

            double[] binCenters = new double[binEdges.Length - 1];
            for (int i = 0; i < binCenters.Length; i++)
                binCenters[i] = (binEdges[i] + binEdges[i + 1]) / 2.0;

            // Voltage conversion
            if (SelectedXAxisIndex == 1 && !baselineSubtracted)
            {
                for (int i = 0; i < binCenters.Length; i++)
                    binCenters[i] = (binCenters[i] / 16383.0) * 5000.0;
            }

            return (counts, binCenters);
        }

        private static double CalculateMean(double[] data)
        {
            double sum = 0;
            for (int i = 0; i < data.Length; i++)
                sum += data[i];
            return data.Length > 0 ? sum / data.Length : 0;
        }

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
