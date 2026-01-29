namespace BaselineMode.WPF.Models
{
    public class HeaderValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public int ErrorLine { get; set; }
        public string? ErrorContent { get; set; }
        public string? FirstHeaderContent { get; set; } // To capture the valid header for parsing
        public string? FilteredFilePath { get; set; }
    }
}
