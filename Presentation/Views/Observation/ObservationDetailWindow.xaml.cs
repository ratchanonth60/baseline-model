using System.Linq;
using System.Windows;
using ScottPlot;
using BaselineMode.WPF.Core.Interfaces;

namespace BaselineMode.WPF.Views.Observation
{
    public partial class ObservationDetailWindow : Window
    {
        private readonly IFittingService? _fittingService;

        public ObservationDetailWindow()
        {
            InitializeComponent();
        }

        public ObservationDetailWindow(IFittingService fittingService) : this()
        {
            _fittingService = fittingService;
        }

        public void ShowHistogram(double[] data, string title, bool showFit = true)
        {
            TitleText.Text = title;
            base.Title = $"Detail View - {title}";

            DetailPlot.Plot.Clear();

            if (data == null || data.Length == 0)
            {
                DetailPlot.Plot.AddText("No data", 0, 0, size: 14, color: System.Drawing.Color.Gray);
                DetailPlot.Refresh();
                return;
            }

            // Filter positive values
            var filteredData = data.Where(v => v > 0).ToArray();
            TxtCounts.Text = filteredData.Length.ToString("N0");

            if (filteredData.Length == 0)
            {
                DetailPlot.Plot.AddText("No positive data", 0, 0, size: 14, color: System.Drawing.Color.Gray);
                DetailPlot.Refresh();
                return;
            }

            // Create histogram
            var (hist, binEdges) = ScottPlot.Statistics.Common.Histogram(filteredData, binCount: 4096);
            double[] binMidpoints = new double[hist.Length];
            for (int i = 0; i < hist.Length; i++)
                binMidpoints[i] = (binEdges[i] + binEdges[i + 1]) / 2.0;

            var bar = DetailPlot.Plot.AddBar(hist, binMidpoints);
            bar.FillColor = System.Drawing.Color.FromArgb(0, 150, 136);

            // Try Gaussian fit
            if (showFit && _fittingService != null)
            {
                try
                {
                    var fitResult = _fittingService.GaussianFit(binMidpoints, hist);
                    if (fitResult?.FitCurve != null)
                    {
                        DetailPlot.Plot.AddScatter(binMidpoints, fitResult.FitCurve,
                            System.Drawing.Color.FromArgb(255, 82, 82), lineWidth: 2);
                        DetailPlot.Plot.AddPoint(fitResult.Mu, fitResult.Peak,
                            color: System.Drawing.Color.Yellow, size: 10);

                        TxtPeak.Text = $"{fitResult.Peak:F0}";
                        TxtMean.Text = $"{fitResult.Mu:F2}";
                        TxtRMS.Text = $"{fitResult.Sigma:F2}";
                        TxtFWHM.Text = $"{fitResult.FWHM:F2}";
                        TxtResolution.Text = $"{fitResult.Resolution:F2}%";
                    }
                }
                catch { /* Fitting failed */ }
            }

            DetailPlot.Plot.Style(ScottPlot.Style.Gray1);
            DetailPlot.Plot.Style(figureBackground: System.Drawing.Color.FromArgb(37, 37, 38));
            DetailPlot.Plot.Style(dataBackground: System.Drawing.Color.FromArgb(40, 40, 40));
            DetailPlot.Plot.YAxis.Label("Count");
            DetailPlot.Plot.XAxis.Label("ADC Channel");
            DetailPlot.Plot.SetAxisLimits(yMin: 0);
            DetailPlot.Refresh();
        }
    }
}
