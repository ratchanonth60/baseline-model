using System;
using System.Collections.Generic;

namespace BaselineMode.WPF.Models
{
    public class PlotUpdateEventArgs : EventArgs
    {
        public List<BaselineData> Data { get; }
        public PlotUpdateEventArgs(List<BaselineData> data) { Data = data; }
    }
}
