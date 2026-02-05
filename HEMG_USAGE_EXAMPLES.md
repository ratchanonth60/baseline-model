// HEMG FITTING USAGE EXAMPLE
// This file shows how to use the HEMG fitting functionality
// Do NOT compile this directly - use as reference for integration

/*
BASIC USAGE EXAMPLE
===================

using BaselineMode.WPF.Services;
using BaselineMode.WPF.Models;

// 1. Create math service
var mathService = new MathService();

// 2. Prepare your energy spectrum data (e.g., from detector)
double[] energyData = GetEnergyDataFromDetector();

// 3. Perform double-sided HEMG fitting
FittingResult result = mathService.HyperEMGDoubleSidedFit(energyData, energyData);

// 4. Access the fitted parameters
Console.WriteLine($"Amplitude (A):        {result.A:F6}");
Console.WriteLine($"Mean (μ):             {result.Mu:F6}");
Console.WriteLine($"Sigma (σ):            {result.Sigma:F6}");
Console.WriteLine($"Tau Left 1 (τL1):     {result.TauL1:F6}");
Console.WriteLine($"Tau Right 1 (τR1):    {result.TauR1:F6}");
Console.WriteLine($"Eta Left 1 (ηL1):     {result.EtaL1:F6}");
Console.WriteLine($"Eta Right 1 (ηR1):    {result.EtaR1:F6}");

// 5. Use the fitted curve for plotting
double[] fitCurve = result.FitCurve;
// Plot fitCurve in your visualization

// 6. Clean up
mathService.Dispose();


WPFMVVM INTEGRATION EXAMPLE
============================

In MainViewModel.cs:

public class MainViewModel : ObservableObject
{
    private IMathService _mathService;
    
    public MainViewModel(IMathService mathService)
    {
        _mathService = mathService;
    }
    
    public void FitEnergySpectrum()
    {
        try
        {
            // Get spectrum data
            double[] spectrumData = _detectorService.GetCurrentSpectrum();
            
            // Perform HEMG fit
            FittingResult result = _mathService.HyperEMGDoubleSidedFit(
                spectrumData, spectrumData);
            
            // Update UI properties
            FitAmplitude = result.A;
            FitMean = result.Mu;
            FitSigma = result.Sigma;
            FitTauLeft = result.TauL1;
            FitTauRight = result.TauR1;
            FitEtaLeft = result.EtaL1;
            FitEtaRight = result.EtaR1;
            
            // Update plot
            RequestPlotUpdate?.Invoke(this, new PlotUpdateEventArgs(result.FitCurve));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fitting error: {ex.Message}");
        }
    }
}

In MainWindow.xaml.cs or View:

<StackPanel>
    <TextBlock Text="HEMG Fitting Results:" FontWeight="Bold" />
    <TextBlock Text="{Binding FitAmplitude, StringFormat='Amplitude: {0:F6}'}" />
    <TextBlock Text="{Binding FitMean, StringFormat='Mean: {0:F6}'}" />
    <TextBlock Text="{Binding FitSigma, StringFormat='Sigma: {0:F6}'}" />
    <TextBlock Text="{Binding FitTauLeft, StringFormat='Tau Left: {0:F6}'}" />
    <TextBlock Text="{Binding FitTauRight, StringFormat='Tau Right: {0:F6}'}" />
    <TextBlock Text="{Binding FitEtaLeft, StringFormat='Eta Left: {0:F6}'}" />
    <TextBlock Text="{Binding FitEtaRight, StringFormat='Eta Right: {0:F6}'}" />
    <Button Content="Fit Spectrum" Command="{Binding FitSpectrumCommand}" />
</StackPanel>


PARAMETER INTERPRETATION
========================

A (Amplitude)     : The peak height of the fitted distribution
                    Higher values indicate stronger signal

Mu (μ, Mean)      : The center position of the peak
                    In energy calibration: the energy corresponding to peak center
                    Range: 0-16384 (matching ADC channels)

Sigma (σ)         : Width/standard deviation of the Gaussian component
                    Larger values = broader peak
                    Smaller values = narrower peak

Tau Left (τL1)    : Left exponential tail decay constant
                    Controls the falloff on the low-energy side
                    Larger τ = slower decay (more tail)

Tau Right (τR1)   : Right exponential tail decay constant
                    Controls the falloff on the high-energy side
                    Larger τ = slower decay (more tail)

Eta Left (ηL1)    : Left tail weight/amplitude
                    Range [0,1]: 0 = no left tail, 1 = strong left tail

Eta Right (ηR1)   : Right tail weight/amplitude
                    Range [0,1]: 0 = no right tail, 1 = strong right tail


TYPICAL PARAMETER RANGES FOR ENERGY SPECTROSCOPY
==================================================

Alpha spectroscopy (5-6 MeV, 16-bit ADC):
  A:        100-100,000  (depends on detector efficiency)
  μ:        5,000-12,000 (energy calibration)
  σ:        50-500       (energy resolution)
  τL1:      0.1-2.0      (asymmetric tail)
  τR1:      0.5-3.0      (broader right tail)
  ηL1:      0.1-0.9      (significant left tail)
  ηR1:      0.1-0.8      (moderate right tail)

Beta spectroscopy (typical):
  A:        1,000-1,000,000
  μ:        7,000-15,000
  σ:        100-1000
  τL1:      0.2-1.0
  τR1:      1.0-5.0
  ηL1:      0.3-0.9
  ηR1:      0.2-0.8


TROUBLESHOOTING
===============

1. Poor fit quality?
   - Check that data is properly thresholded/preprocessed
   - Ensure histogram has sufficient counts in peak region
   - Try adjusting initial parameter estimates

2. Convergence issues?
   - May indicate complex/asymmetric peak shape
   - Try fitting subset of data
   - Check for multiple peaks - HEMG models single peak only

3. Unphysical parameters?
   - Eta values > 1: clipped to [0,1] automatically
   - Tau < 0: invalid physics
   - Check raw data for anomalies

4. Performance issues?
   - Typical fitting: 100-500ms
   - Large spectra (16K+ bins) will be slower
   - Consider downsampling if needed


COMPARING WITH MATLAB
=====================

The C# implementation aims to be compatible with the original MATLAB code:

MATLAB lsqcurvefit:  Uses Levenberg-Marquardt algorithm
C# Implementation:   Uses gradient descent with numerical differentiation

Results should be very similar, but may differ slightly due to:
- Different optimization algorithms
- Numerical precision differences
- Learning rate tuning

For critical applications, compare results on known reference spectra.


INTEGRATION WITH CALIBRATION WORKFLOW
======================================

Typical workflow:
1. Acquire energy spectrum from detector
2. Apply threshold (background subtraction)
3. Create histogram (0-16384 bins)
4. Call HyperEMGDoubleSidedFit()
5. Extract peak position (μ) and width (σ)
6. Correlate with known energy reference
7. Generate energy calibration curve

Example:
double[] spectrum = detector.GetSpectrum();
FittingResult[] results = new FittingResult[knownEnergies.Length];

for (int i = 0; i < knownEnergies.Length; i++)
{
    results[i] = mathService.HyperEMGDoubleSidedFit(
        spectrum, spectrum);  // or subset of spectrum for each peak
}

// Extract calibration points
double[] channels = results.Select(r => r.Mu).ToArray();
double[] energies = knownEnergies;

// Fit calibration curve (linear or polynomial)
CalibrationCurve curve = FitCalibrationCurve(channels, energies);
*/
