using System;
using System.IO;
using BaselineMode.WPF.Core.Interfaces;
using BaselineMode.WPF.Core.Interfaces.Observation;
using BaselineMode.WPF.Core.Models.Observation;
using BaselineMode.WPF.Core.Interfaces.Shared;
using BaselineMode.WPF.Presentation.ViewModels.Shared;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BaselineMode.WPF.Presentation.ViewModels.Observation
{
    /// <summary>
    /// ViewModel for Observation mode: file selection, segment filtering, Excel export, and analysis.
    /// Split into partials: FileOperations, Commands, ExcelProcessing.
    /// </summary>
    public partial class ObservationViewModel(
        IObservationDataProcessor dataProcessor,
        IObservationExcelHelper excelHelper,
        IFittingService fittingService,
        IMathService mathService,
        IFileService fileService,
        IFileHelper fileHelper,
        ILoggerService logger,
        IDialogService dialogService) : SharedViewModelBase
    {
        private readonly IObservationDataProcessor _dataProcessor = dataProcessor;
        private readonly IObservationExcelHelper _excelHelper = excelHelper;
        private readonly IFittingService _fittingService = fittingService;
        private readonly IMathService _mathService = mathService;
        private readonly IFileService _fileService = fileService;
        private readonly IFileHelper _fileHelper = fileHelper;
        private readonly ILoggerService _logger = logger;
        private readonly IDialogService _dialogService = dialogService;

        public IObservationDataProcessor DataProcessor => _dataProcessor;
        public IObservationExcelHelper ExcelHelper => _excelHelper;
        public IFittingService FittingService => _fittingService;
        public IMathService MathProvider => _mathService;
        public IFileHelper FileHelper => _fileHelper;

        public string CombinedOutputFileName { get; set; } = "CombinedData.xlsx";

        [ObservableProperty]
        private string? _outputFileName;

        [ObservableProperty]
        private string _dataCountStr = "-";

        [ObservableProperty]
        private string _particleCountStr = "-";

        [ObservableProperty]
        private string? _lastSavedFilePath;

        [ObservableProperty]
        private string _outputDirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BaselineModeOutputs");

        [ObservableProperty]
        private string _stopTimeStr = "-";

        // Graph Settings
        [ObservableProperty]
        private Avalonia.Media.Color _selectedGraphBackground = Avalonia.Media.Colors.Gray;

        [ObservableProperty]
        private Avalonia.Media.Color _selectedDSSDColor = Avalonia.Media.Colors.Orange;

        [ObservableProperty]
        private Avalonia.Media.Color _selectedBGOColor = Avalonia.Media.Colors.Cyan;

        partial void OnSelectedGraphBackgroundChanged(Avalonia.Media.Color value) => RequestPlotUpdate?.Invoke(this, EventArgs.Empty);
        partial void OnSelectedDSSDColorChanged(Avalonia.Media.Color value) => RequestPlotUpdate?.Invoke(this, EventArgs.Empty);
        partial void OnSelectedBGOColorChanged(Avalonia.Media.Color value) => RequestPlotUpdate?.Invoke(this, EventArgs.Empty);

        [ObservableProperty]
        private double _barWidthMultiplier = 1.0;
        partial void OnBarWidthMultiplierChanged(double value) => RequestPlotUpdate?.Invoke(this, EventArgs.Empty);

        // Fit Selection Flags (DSSD)
        [ObservableProperty]
        private bool _showGaussianFitDSSD = true;

        [ObservableProperty]
        private bool _showLorentzianFitDSSD = false;

        [ObservableProperty]
        private bool _showHemgFitDSSD = false;

        // Fit Selection Flags (BGO)
        [ObservableProperty]
        private bool _showGaussianFitBGO = true;

        [ObservableProperty]
        private bool _showLorentzianFitBGO = false;

        [ObservableProperty]
        private bool _showHemgFitBGO = false;

        partial void OnShowGaussianFitDSSDChanged(bool value) => RequestPlotUpdate?.Invoke(this, EventArgs.Empty);
        partial void OnShowLorentzianFitDSSDChanged(bool value) => RequestPlotUpdate?.Invoke(this, EventArgs.Empty);
        partial void OnShowHemgFitDSSDChanged(bool value) => RequestPlotUpdate?.Invoke(this, EventArgs.Empty);
        partial void OnShowGaussianFitBGOChanged(bool value) => RequestPlotUpdate?.Invoke(this, EventArgs.Empty);
        partial void OnShowLorentzianFitBGOChanged(bool value) => RequestPlotUpdate?.Invoke(this, EventArgs.Empty);
        partial void OnShowHemgFitBGOChanged(bool value) => RequestPlotUpdate?.Invoke(this, EventArgs.Empty);

        // --- X-Axis & Calibration Settings ---
        [ObservableProperty]
        private int _selectedXAxisIndex = 0; // 0=ADC, 1=Voltage, 2=Energy

        [ObservableProperty]
        private double _energyCalibrationSlope = 0.000427; // Placeholder

        [ObservableProperty]
        private double _energyCalibrationIntercept = 0.0;

        partial void OnSelectedXAxisIndexChanged(int value) => RequestPlotUpdate?.Invoke(this, EventArgs.Empty);

        public Dictionary<string, int[]>? HistogramData { get; private set; }

        public event EventHandler? RequestPlotUpdate;

        [RelayCommand]
        private async Task BrowseOutputDirectoryAsync()
        {
            var folderPath = await _dialogService.OpenFolderAsync("Select Output Root Folder");
            if (folderPath != null)
                OutputDirectoryPath = folderPath;
        }

        [RelayCommand]
        private async Task SelectFiles()
        {
            var files = await _dialogService.OpenFilesAsync("Select files", true, "Text files (*.txt)|*.txt|All files (*.*)|*.*");
            if (files != null && files.Length > 0)
                LoadFiles(files);
        }

        public LayerData? GetDSSDLayerData(DetectorLayer layer) =>
            _dataProcessor.DSSDData.TryGetValue(layer, out var value) ? value : null;

        public BGOData? GetBGOLayerData(BGOLayer layer) =>
            _dataProcessor.BGOData.TryGetValue(layer, out var value) ? value : null;

        [RelayCommand]
        public override void Reset()
        {
            base.Reset();
            _dataProcessor.ClearData();
            OutputFileName = string.Empty;
        }
    }
}
