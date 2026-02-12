namespace BaselineMode.WPF.Core.Models.Observation
{
    public struct ObservationProcessReport
    {
        public int CurrentStep { get; set; }
        public int TotalSteps { get; set; }
        public string Message { get; set; }
        public DateTime? CurrentTime { get; set; }
        public bool IsComplete { get; set; }
        public string[]? LastHexData { get; set; }
    }
}
