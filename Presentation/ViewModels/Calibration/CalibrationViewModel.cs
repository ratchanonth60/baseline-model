using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using BaselineMode.WPF.Core.Interfaces;
using BaselineMode.WPF.Core.Interfaces.Observation;
using BaselineMode.WPF.Presentation.ViewModels.Shared;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BaselineMode.WPF.Core.Interfaces.Shared;

namespace BaselineMode.WPF.Presentation.ViewModels.Calibration
{
    /// <summary>
    /// ViewModel for Calibration mode: file selection, raw/Excel processing, channel histograms.
    /// Split into partials: Commands, DataProcessing, Plotting.
    /// </summary>
    public partial class CalibrationViewModel : SharedViewModelBase
    {
        private readonly IMathService _mathService;
        private readonly IFileHelper _fileHelper;
        private readonly IObservationDataProcessor _dataProcessor;
        private readonly ILoggerService _logger;
        private readonly IDialogService _dialogService;

        private const int EstimatedRows = 10000;
        private const int DataPointsPerRow = 11;
        private const int InitialCapacity = EstimatedRows * DataPointsPerRow;

        public CalibrationViewModel(IMathService mathService, IFileHelper fileHelper, IObservationDataProcessor dataProcessor, ILoggerService logger, IDialogService dialogService)
        {
            _mathService = mathService;
            _fileHelper = fileHelper;
            _dataProcessor = dataProcessor;
            _logger = logger;
            _dialogService = dialogService;

            Channels = [];
            for (int i = 0; i < 16; i++)
            {
                string name = i < 8 ? $"X{i + 1}" : $"Z{i - 7}";
                Channels.Add(new ChannelViewModel { ChannelName = name, Title = name, ChannelIndex = i });

                _l1Columns[i] = new List<double>(InitialCapacity);
                _l2Columns[i] = new List<double>(InitialCapacity);
                _l6Columns[i] = new List<double>(InitialCapacity);
                _l7Columns[i] = new List<double>(InitialCapacity);
                _l1VoltColumns[i] = new List<double>(InitialCapacity);
                _l2VoltColumns[i] = new List<double>(InitialCapacity);
                _l6VoltColumns[i] = new List<double>(InitialCapacity);
                _l7VoltColumns[i] = new List<double>(InitialCapacity);
            }
        }

        [ObservableProperty]
        private string _inputFilesInfo = "No files selected";

        [ObservableProperty]
        private string _outputFileName = "CalibrationResult";

        [ObservableProperty]
        private int _selectedLayerIndex = 0;

        [ObservableProperty]
        private int _selectedXAxisIndex = 0;

        [ObservableProperty]
        private double _xAxisMin = 0;

        [ObservableProperty]
        private double _xAxisMax = 16384;

        [ObservableProperty]
        private int _delayTime = 50;

        [ObservableProperty]
        private int _threshold = 50;

        [ObservableProperty]
        private double _barWidthMultiplier = 1.0;

        partial void OnBarWidthMultiplierChanged(double value)
        {
            foreach (var ch in Channels)
                ch.BarWidthMultiplier = value;
            _ = UpdatePlotsAsync();
        }

        [ObservableProperty]
        private Avalonia.Media.Color _graphFigureColor = Avalonia.Media.Color.FromRgb(30, 30, 30);

        [ObservableProperty]
        private Avalonia.Media.Color _graphDataColor = Avalonia.Media.Color.FromRgb(37, 37, 38);

        [ObservableProperty]
        private Avalonia.Media.Color _graphSeriesColor = Avalonia.Media.Colors.Cyan;

        [ObservableProperty]
        private Avalonia.Media.Color _graphTextColor = Avalonia.Media.Colors.White;

        partial void OnGraphFigureColorChanged(Avalonia.Media.Color value) => _ = UpdatePlotsAsync();
        partial void OnGraphDataColorChanged(Avalonia.Media.Color value) => _ = UpdatePlotsAsync();
        partial void OnGraphSeriesColorChanged(Avalonia.Media.Color value) => _ = UpdatePlotsAsync();
        partial void OnGraphTextColorChanged(Avalonia.Media.Color value) => _ = UpdatePlotsAsync();

        [ObservableProperty]
        private string _headerCheckStatus = "";

        [ObservableProperty]
        private bool _readMultipleFiles = false;

        private CancellationTokenSource? _cts;

        private readonly List<double>[] _l1Columns = new List<double>[16];
        private readonly List<double>[] _l2Columns = new List<double>[16];
        private readonly List<double>[] _l6Columns = new List<double>[16];
        private readonly List<double>[] _l7Columns = new List<double>[16];
        private readonly List<double>[] _l1VoltColumns = new List<double>[16];
        private readonly List<double>[] _l2VoltColumns = new List<double>[16];
        private readonly List<double>[] _l6VoltColumns = new List<double>[16];
        private readonly List<double>[] _l7VoltColumns = new List<double>[16];

        public ObservableCollection<ChannelViewModel> Channels { get; }

        public IEnumerable<ChannelViewModel> XChannels => Channels.Take(8);
        public IEnumerable<ChannelViewModel> ZChannels => Channels.Skip(8).Take(8);

        [RelayCommand]
        private async Task SelectFiles()
        {
            var files = await _dialogService.OpenFilesAsync("Select files", true, "Text files (*.txt)|*.txt|All files (*.*)|*.*");
            if (files != null && files.Length > 0)
            {
                InputFileList = files;
                ReadMultipleFiles = false;
                InputFilesInfo = $"{InputFileList.Length} txt file(s) selected";
                StatusMessage = "Files selected.";
            }
        }

        [RelayCommand]
        private async Task SelectExcelFiles()
        {
            var files = await _dialogService.OpenFilesAsync("Select Excel files to read", true, "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*");
            if (files != null && files.Length > 0)
            {
                InputFileList = files;
                ReadMultipleFiles = true;
                InputFilesInfo = $"{InputFileList.Length} Excel file(s) selected for reading";
                StatusMessage = "Excel files selected.";
            }
        }

        [RelayCommand]
        private void Stop()
        {
            _cts?.Cancel();
            StatusMessage = "Stopped.";
        }

        [RelayCommand]
        public override void Reset()
        {
            base.Reset();
            InputFilesInfo = "No files selected";
            ReadMultipleFiles = false;
            ResetDataLists();
            ClearChannelPlots();
            StatusMessage = "Reset complete.";
            ProgressValue = 0;
            HeaderCheckStatus = "";
        }

        private void ClearChannelPlots()
        {
            var fig = Color.FromArgb(30, 30, 30);
            var data = Color.FromArgb(37, 37, 38);
            var fg = Color.White;
            var series = Color.Gray;
            foreach (var ch in Channels)
            {
                ch.Counts = null;
                ch.BinCenters = null;
                ch.StatsText = "";
                ch.RenderPlot(fig, data, fg, series);
            }
        }
    }
}
