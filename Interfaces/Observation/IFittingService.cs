namespace BaselineMode.WPF.Interfaces.Observation
{
    using BaselineMode.WPF.Models.Observation;

    public interface IObservationFittingService
    {
        ObservationFittingResult GaussianFit(double[] xData, double[] yData);
        // LorentzianFit can be added here later
    }
}
