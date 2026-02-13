using BaselineMode.WPF.Core.Models.Baseline;

namespace BaselineMode.WPF.Core.Models.Shared
{
    public class PlotUpdateEventArgs : EventArgs
    {
        public List<BaselineData> Data { get; }

        public PlotUpdateEventArgs(List<BaselineData> data)
        {
            Data = data;
        }
    }
}
