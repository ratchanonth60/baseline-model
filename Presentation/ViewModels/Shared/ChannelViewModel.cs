using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using ScottPlot;

namespace BaselineMode.WPF.Presentation.ViewModels.Shared
{
    public partial class ChannelViewModel : ObservableObject
    {
        public class FitData
        {
            public double[]? Curve { get; set; }
            public System.Drawing.Color Color { get; set; }
            public string Label { get; set; } = "Fit";
        }

        [ObservableProperty]
        private string _channelName = "Channel";

        [ObservableProperty]
        private string _title = "Channel";

        [ObservableProperty]
        private string _statsText = "No Data";

        public double[]? BinCenters { get; set; }
        public double[]? Counts { get; set; }
        public double[]? RawCounts { get; set; }

        [ObservableProperty]
        private bool _isFitting = false;

        // Multi-Fit Support: Dictionary of FitName -> FitData
        public Dictionary<string, FitData> ActiveFits { get; set; } = [];

        // Cache: FitType -> FitData
        private readonly Dictionary<string, FitData> _fitCache = [];

        public void CacheFit(string fitType, FitData data)
        {
            if (!_fitCache.ContainsKey(fitType))
                _fitCache[fitType] = data;
        }

        public FitData? GetCachedFit(string fitType)
        {
            return _fitCache.TryGetValue(fitType, out FitData? value) ? value : null;
        }

        [ObservableProperty]
        private bool _isLogScale = false;

        // Statistics (Primary Fit)
        public double Mu { get; set; }
        public double Sigma { get; set; }
        public double Peak { get; set; }
        public double FWHM { get; set; }
        public double Resolution { get; set; }

        /// <summary>Multiplier for bar width (1.0 = auto from bin spacing).</summary>
        public double BarWidthMultiplier { get; set; } = 1.0;

        public WpfPlot? PlotControl { get; set; }
        public WpfPlot? ResidualPlotControl { get; set; }

        public void RenderPlot(System.Drawing.Color figBg, System.Drawing.Color dataBg, System.Drawing.Color foreColor, System.Drawing.Color seriesColor, double? xMin = null, double? xMax = null, string? xLabel = null)
        {
            if (PlotControl != null)
                RenderTo(PlotControl, figBg, dataBg, foreColor, seriesColor, xMin, xMax, xLabel);
        }

        public void RenderTo(WpfPlot targetPlot, System.Drawing.Color figBg, System.Drawing.Color dataBg, System.Drawing.Color foreColor, System.Drawing.Color seriesColor, double? xMin = null, double? xMax = null, string? xLabel = null)
        {
            if (targetPlot == null) return;
            targetPlot.Plot.Clear();

            // Apply Configured Style
            targetPlot.Plot.Style(ScottPlot.Style.Gray1);

            targetPlot.Plot.Style(figureBackground: figBg, dataBackground: dataBg);
            targetPlot.Plot.XAxis.Label(color: foreColor);
            targetPlot.Plot.YAxis.Label(color: foreColor);
            targetPlot.Plot.XAxis.TickLabelStyle(color: foreColor);
            targetPlot.Plot.YAxis.TickLabelStyle(color: foreColor);
            targetPlot.Plot.Title(Title, color: foreColor);

            if (Counts != null && Counts.Length > 0 && BinCenters != null)
            {
                bool isLogScale = IsLogScale;

                if (isLogScale)
                {
                    var scatter = targetPlot.Plot.AddScatter(BinCenters, Counts);
                    scatter.LineWidth = 2;
                    scatter.Color = foreColor;
                    scatter.MarkerSize = 5;
                    scatter.MarkerShape = ScottPlot.MarkerShape.filledCircle;
                    scatter.MarkerLineWidth = 0;
                    scatter.MarkerColor = seriesColor;
                    scatter.Label = "Data";

                    targetPlot.Plot.YAxis.TickLabelFormat(value => $"10^{value:F0}");
                    targetPlot.Plot.SetAxisLimitsY(0.1, double.NaN);
                }
                else
                {
                    var bar = targetPlot.Plot.AddBar(values: Counts, positions: BinCenters);
                    bar.FillColor = seriesColor;
                    double autoWidth = BinCenters.Length > 1 ? BinCenters[1] - BinCenters[0] : 1;
                    bar.BarWidth = autoWidth * BarWidthMultiplier;
                    bar.BorderLineWidth = 0;
                    bar.Label = "Data";

                    targetPlot.Plot.SetAxisLimitsY(0, double.NaN);
                }

                // Plot Active Fits
                if (ActiveFits != null && ActiveFits.Count > 0)
                {
                    foreach (var fit in ActiveFits.Values)
                    {
                        if (fit.Curve != null && fit.Curve.Length > 0)
                        {
                            var fitScatter = targetPlot.Plot.AddScatter(BinCenters, fit.Curve);
                            fitScatter.LineWidth = 2;
                            fitScatter.Color = fit.Color;
                            fitScatter.MarkerSize = 0;
                            fitScatter.Label = fit.Label;

                            // Ghost Trace Logic: If this is "Manual" and we have a Ghost Trace, plot it
                            if (fit.Label == "Manual Fit" && ActiveFits.ContainsKey("Ghost"))
                            {
                                var ghost = ActiveFits["Ghost"];
                                var ghostScatter = targetPlot.Plot.AddScatter(BinCenters, ghost.Curve);
                                ghostScatter.LineWidth = 1;
                                ghostScatter.Color = System.Drawing.Color.Gray;
                                ghostScatter.LineStyle = ScottPlot.LineStyle.Dash;
                                ghostScatter.MarkerSize = 0;
                                ghostScatter.Label = "Original Fit";
                            }
                        }
                    }
                }

                // ... Stats Annotation ...
                if (Mu > 0 && !isLogScale)
                {
                    string statsLabel = $"μ = {Mu:F2}\nσ = {Sigma:F2}\nFWHM = {FWHM:F2}\nRes = {Resolution:F2}%";
                    var annotation = targetPlot.Plot.AddText(statsLabel, 0.02, 0.98);
                    annotation.Font.Size = 10;
                    annotation.Font.Color = System.Drawing.Color.Cyan;
                    annotation.BackgroundColor = System.Drawing.Color.FromArgb(200, dataBg.R, dataBg.G, dataBg.B);
                    annotation.BorderColor = System.Drawing.Color.Cyan;
                    annotation.Alignment = ScottPlot.Alignment.UpperLeft;
                }

                targetPlot.Plot.XLabel(xLabel ?? "ADC Channel");
                targetPlot.Plot.YLabel(isLogScale ? "log scale Count" : "Count");
                targetPlot.Plot.Legend(true, ScottPlot.Alignment.UpperRight);

                if (xMin.HasValue && xMax.HasValue)
                {
                    targetPlot.Plot.SetAxisLimits(xMin: xMin.Value, xMax: xMax.Value);
                    targetPlot.Plot.AxisAutoY();
                }
                else
                {
                    targetPlot.Plot.AxisAuto();
                }

                if (!isLogScale)
                {
                    var limits = targetPlot.Plot.GetAxisLimits();
                    targetPlot.Plot.SetAxisLimits(yMin: 0, yMax: limits.YMax * 1.05);
                }
            }
            else
            {
                targetPlot.Plot.AddText("No Data", 0, 0, size: 24, color: System.Drawing.Color.Gray);
                targetPlot.Plot.SetAxisLimits(-1, 1, -1, 1);
            }

            targetPlot.Refresh();
        }

        [ObservableProperty]
        private bool _isManualMode = false;
        partial void OnIsManualModeChanged(bool value)
        {
            if (value && MathService != null && BinCenters != null)
            {
                if (ManualA <= 1) ManualA = Peak > 0 ? Peak : 100;
                if (Math.Abs(ManualMu) < 1e-6) ManualMu = Mu;
                if (ManualSigma <= 1e-6) ManualSigma = Sigma > 0 ? Sigma : 10;
                if (ManualTauL <= 1e-6) ManualTauL = Sigma;
                if (ManualTauR <= 1e-6) ManualTauR = Sigma;
                if (ManualEtaL <= 1e-6) ManualEtaL = 0.5;
                if (ManualEtaR <= 1e-6) ManualEtaR = 0.5;

                // Capture Ghost Trace (current auto-fit)
                if (ActiveFits.ContainsKey("HEMG-D"))
                {
                    var original = ActiveFits["HEMG-D"];
                    if (original.Curve != null)
                    {
                        ActiveFits["Ghost"] = new FitData
                        {
                            Curve = (double[])original.Curve.Clone(),
                            Color = System.Drawing.Color.Gray,
                            Label = "Original"
                        };
                    }
                }

                UpdateManualCurve();
            }
            else
            {
                // Clear Ghost? Maybe keep it.
                if (ActiveFits.ContainsKey("Ghost")) ActiveFits.Remove("Ghost");
                if (ActiveFits.ContainsKey("Manual")) ActiveFits.Remove("Manual");
                if (PlotControl != null)
                    RenderTo(PlotControl, _lastFigBg, _lastDataBg, _lastForeColor, _lastSeriesColor);
            }
        }

        [ObservableProperty] private double _manualA = 100;
        partial void OnManualAChanged(double value) => UpdateManualCurve();

        [ObservableProperty] private double _manualMu = 0;
        partial void OnManualMuChanged(double value) => UpdateManualCurve();

        [ObservableProperty] private double _manualSigma = 10;
        partial void OnManualSigmaChanged(double value) => UpdateManualCurve();

        [ObservableProperty] private double _manualTauL = 10;
        partial void OnManualTauLChanged(double value) => UpdateManualCurve();

        [ObservableProperty] private double _manualTauR = 10;
        partial void OnManualTauRChanged(double value) => UpdateManualCurve();

        [ObservableProperty] private double _manualEtaL = 0.5;
        partial void OnManualEtaLChanged(double value) => UpdateManualCurve();

        [ObservableProperty] private double _manualEtaR = 0.5;
        partial void OnManualEtaRChanged(double value) => UpdateManualCurve();

        public Core.Interfaces.IMathService? MathService { get; set; }

        private System.Drawing.Color _lastFigBg = System.Drawing.Color.White;
        private System.Drawing.Color _lastDataBg = System.Drawing.Color.White;
        private System.Drawing.Color _lastForeColor = System.Drawing.Color.Black;
        private System.Drawing.Color _lastSeriesColor = System.Drawing.Color.Blue;

        private void UpdateManualCurve()
        {
            var mathService = MathService;
            if (!IsManualMode || mathService == null || BinCenters == null) return;

            var p = new double[] { ManualA, ManualMu, ManualSigma, ManualTauL, ManualTauR, ManualEtaL, ManualEtaR };
            var curve = mathService.GenerateHemgCurve(BinCenters, p);

            var fitData = new FitData
            {
                Curve = curve,
                Color = System.Drawing.Color.Orange,
                Label = "Manual Fit"
            };
            ActiveFits["Manual"] = fitData;

            // Calculate Residuals (Counts - Curve)
            if (Counts != null && Counts.Length == curve.Length)
            {
                double[] residuals = new double[Counts.Length];
                for (int i = 0; i < Counts.Length; i++) residuals[i] = Counts[i] - curve[i];
                UpdateResidualPlot(residuals);
            }

            if (PlotControl != null)
            {
                RenderTo(PlotControl, _lastFigBg, _lastDataBg, _lastForeColor, _lastSeriesColor);
            }
        }

        private void UpdateResidualPlot(double[] residuals)
        {
            if (ResidualPlotControl == null || BinCenters == null) return;

            ResidualPlotControl.Plot.Clear();
            ResidualPlotControl.Plot.Style(ScottPlot.Style.Gray1);
            ResidualPlotControl.Plot.Style(figureBackground: _lastFigBg, dataBackground: _lastDataBg);
            ResidualPlotControl.Plot.XAxis.Label(color: _lastForeColor);
            ResidualPlotControl.Plot.YAxis.Label(color: _lastForeColor);
            ResidualPlotControl.Plot.XAxis.TickLabelStyle(color: _lastForeColor);
            ResidualPlotControl.Plot.YAxis.TickLabelStyle(color: _lastForeColor);

            var scatter = ResidualPlotControl.Plot.AddScatter(BinCenters, residuals);
            scatter.LineWidth = 1;
            scatter.Color = System.Drawing.Color.Cyan;
            scatter.MarkerSize = 2;

            // Add zero line
            ResidualPlotControl.Plot.AddHorizontalLine(0, System.Drawing.Color.Gray, style: ScottPlot.LineStyle.Dash);

            ResidualPlotControl.Plot.Title("Residuals");
            ResidualPlotControl.Refresh();
        }

        // Add Command to apply current manual params as fix/refit?
        // For now just visualization.

        [ObservableProperty] private bool _isLockedA = false;
        [ObservableProperty] private bool _isLockedMu = false;
        [ObservableProperty] private bool _isLockedSigma = false;
        [ObservableProperty] private bool _isLockedTauL = false;
        [ObservableProperty] private bool _isLockedTauR = false;
        [ObservableProperty] private bool _isLockedEtaL = false;
        [ObservableProperty] private bool _isLockedEtaR = false;

        [RelayCommand]
        private async Task OptimizeManual()
        {
            if (MathService == null || BinCenters == null || Counts == null) return;
            var guess = new double[] { ManualA, ManualMu, ManualSigma, ManualTauL, ManualTauR, ManualEtaL, ManualEtaR };
            var locks = new bool[] { IsLockedA, IsLockedMu, IsLockedSigma, IsLockedTauL, IsLockedTauR, IsLockedEtaL, IsLockedEtaR };

            var result = await Task.Run(() => MathService.HemgDoubleSidedFitManual(BinCenters, Counts, guess, locks));

            if (result.IsValid)
            {
                if (!IsLockedA) ManualA = result.A;
                if (!IsLockedMu) ManualMu = result.Mu;
                if (!IsLockedSigma) ManualSigma = result.Sigma;
                if (!IsLockedTauL) ManualTauL = result.TauL1;
                if (!IsLockedTauR) ManualTauR = result.TauR1;
                if (!IsLockedEtaL) ManualEtaL = result.EtaL1;
                if (!IsLockedEtaR) ManualEtaR = result.EtaR1;

                Mu = result.Mu;
                Sigma = result.Sigma;
                Peak = result.Peak;
                FWHM = result.FWHM;

                var fitData = new FitData
                {
                    Curve = result.FitCurve,
                    Color = System.Drawing.Color.Magenta,
                    Label = "HEMG-D (Refit)"
                };
                ActiveFits["HEMG-D"] = fitData;
                ActiveFits["Manual"] = fitData;

                if (PlotControl != null)
                    RenderTo(PlotControl, _lastFigBg, _lastDataBg, _lastForeColor, _lastSeriesColor);
            }
        }

        public int ChannelIndex { get; set; }
    }
}
