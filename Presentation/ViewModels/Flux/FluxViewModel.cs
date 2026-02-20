using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using Avalonia.Media;
using BaselineMode.WPF.Core.Interfaces;
using BaselineMode.WPF.Core.Interfaces.Shared;
using BaselineMode.WPF.Core.Interfaces.Observation;
using BaselineMode.WPF.Core.Models.Flux;
using BaselineMode.WPF.Core.Models.Shared;
using BaselineMode.WPF.Presentation.ViewModels.Shared;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BaselineMode.WPF.Presentation.ViewModels.Flux
{
    /// <summary>
    /// ViewModel for Flux mode: file selection, raw data processing, and flux density plotting per layer.
    /// Split into partials: FileOperations, Commands, DataProcessing.
    /// </summary>
    public partial class FluxViewModel : SharedViewModelBase
    {
        private readonly IFileHelper _fileHelper;
        private readonly IObservationDataProcessor _dataProcessor;
        private readonly ILoggerService _logger;
        private readonly IDialogService _dialogService;

        /// <summary>Number of detector layers (L1–L7).</summary>
        private const int LayerCount = 7;

        /// <summary>Detector area in m² (32 mm × 32 mm).</summary>
        private const double DetectorAreaM2 = 32 * 32 * 1e-6;

        // ── Commands ─────────────────────────────────────────────────

        public IAsyncRelayCommand SelectFilesCommand { get; }
        public IAsyncRelayCommand ProcessDataCommand { get; }
        public IAsyncRelayCommand ReadDataCommand { get; }
        public IAsyncRelayCommand HeaderCheckCommand { get; }
        public IRelayCommand StopCommand { get; }
        public IRelayCommand ResetCommand { get; }

        // ── Constructor ─────────────────────────────────────────────

        public FluxViewModel(IFileHelper fileHelper, IObservationDataProcessor dataProcessor, ILoggerService logger, IDialogService dialogService)
        {
            _fileHelper = fileHelper;
            _dataProcessor = dataProcessor;
            _logger = logger;
            _dialogService = dialogService;

            SelectFilesCommand = new AsyncRelayCommand(SelectFiles);
            ProcessDataCommand = new AsyncRelayCommand(ProcessData);
            ReadDataCommand = new AsyncRelayCommand(ReadData);
            HeaderCheckCommand = new AsyncRelayCommand(HeaderCheck);
            StopCommand = new RelayCommand(Stop);
            ResetCommand = new RelayCommand(Reset);

            Layers = [];
            for (int i = 0; i < LayerCount; i++)
            {
                Layers.Add(new FluxLayerViewModel
                {
                    LayerName = $"L{i + 1}",
                    LayerIndex = i
                });
            }
        }

        // ── Observable Properties ────────────────────────────────────

        [ObservableProperty]
        private string _inputFilesInfo = "No files selected";

        [ObservableProperty]
        private string _outputFileName = "FluxResult";

        [ObservableProperty]
        private string _headerCheckStatus = "";

        [ObservableProperty]
        private string _startTimeText = "-";

        [ObservableProperty]
        private string _stopTimeText = "-";

        [ObservableProperty]
        private string _durationText = "-";

        [ObservableProperty]
        private int _dataCount;

        [ObservableProperty]
        private int _delayTime = 50;

        [ObservableProperty]
        private int _threshold = 50;

        [ObservableProperty]
        private double _timeRangeMin = 0;

        [ObservableProperty]
        private double _timeRangeMax = 1000;

        [ObservableProperty]
        private bool _isLogScale;

        [ObservableProperty]
        private string _headerInfo = "";

        [ObservableProperty]
        private Color _graphFigureColor = Color.FromRgb(30, 30, 30);

        [ObservableProperty]
        private Color _graphDataColor = Color.FromRgb(37, 37, 38);

        [ObservableProperty]
        private Color _graphSeriesColor = Colors.Cyan;

        [ObservableProperty]
        private Color _graphTextColor = Colors.White;

        [ObservableProperty]
        private double _barWidthMultiplier = 1.0;

        partial void OnGraphFigureColorChanged(Color value) => UpdateAllPlots();
        partial void OnGraphDataColorChanged(Color value) => UpdateAllPlots();
        partial void OnGraphSeriesColorChanged(Color value) => UpdateAllPlots();
        partial void OnGraphTextColorChanged(Color value) => UpdateAllPlots();
        partial void OnIsLogScaleChanged(bool value) => UpdateAllPlots();
        partial void OnTimeRangeMinChanged(double value) => UpdateAllPlots();
        partial void OnTimeRangeMaxChanged(double value) => UpdateAllPlots();
        partial void OnBarWidthMultiplierChanged(double value) => UpdateAllPlots();

        // ── Data & State ────────────────────────────────────────────

        private List<string> _selectedFiles = [];
        private readonly List<double> _secondsPartList = [];
        private readonly List<double>[] _particleCountingLists = Enumerable.Range(0, LayerCount).Select(_ => new List<double>()).ToArray();
        private readonly List<double[]> _particleLayerList = [];
        private readonly List<double[]> _particleOffsetTimeList = [];
        private readonly List<FluxDataResult> _allResults = [];
        private CancellationTokenSource? _cts;
        private TimeSpan _duration = TimeSpan.Zero;

        public ObservableCollection<FluxLayerViewModel> Layers { get; }

        // ── Reset (shared with base) ─────────────────────────────────

        public override void Reset()
        {
            base.Reset();
            InputFilesInfo = "No files selected";
            HeaderCheckStatus = "";
            StartTimeText = "-";
            StopTimeText = "-";
            DurationText = "-";
            DataCount = 0;
            HeaderInfo = "";
            _duration = TimeSpan.Zero;
            ResetDataLists();
            ClearLayerPlots();
            ProgressValue = 0;
        }

        private void Stop()
        {
            _cts?.Cancel();
            StatusMessage = "Stopped.";
        }

        private void ClearLayerPlots()
        {
            var defaultFig = System.Drawing.Color.FromArgb(30, 30, 30);
            var defaultData = System.Drawing.Color.FromArgb(37, 37, 38);
            var defaultFg = System.Drawing.Color.White;
            var defaultSeries = System.Drawing.Color.Gray;
            foreach (var layer in Layers)
            {
                layer.XData = null;
                layer.YData = null;
                layer.StatsText = "No Data";
                layer.RenderPlot(defaultFig, defaultData, defaultFg, defaultSeries);
            }
        }
    }
}
