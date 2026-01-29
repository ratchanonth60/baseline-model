using System;
using System.IO;
using BaselineMode.WPF.Models;

namespace BaselineMode.WPF.Services
{
    // HeaderValidationResult moved to Models namespace

    public static class HeaderValidator
    {
        public static HeaderValidationResult ValidateFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return new HeaderValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "File not found."
                };
            }

            try
            {
                using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    string? line;
                    int lineNumber = 0;
                    string? firstHeader = null;

                    while ((line = reader.ReadLine()) != null)
                    {
                        lineNumber++;
                        if (string.IsNullOrEmpty(line)) continue;

                        // User requested strict check: "No trim, just check if it starts with E225 for every row"
                        if (!line.StartsWith("E225"))
                        {
                            return new HeaderValidationResult
                            {
                                IsValid = false,
                                ErrorLine = lineNumber,
                                ErrorContent = line,
                                FilteredFilePath = filePath,
                                ErrorMessage = $"Header INCORRECT at line {lineNumber}"
                            };
                        }

                        if (firstHeader == null) firstHeader = line;
                    }

                    if (lineNumber == 0)
                    {
                        return new HeaderValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = "File is empty."
                        };
                    }

                    return new HeaderValidationResult
                    {
                        IsValid = true,
                        FirstHeaderContent = firstHeader
                    };
                }
            }
            catch (Exception ex)
            {
                return new HeaderValidationResult
                {
                    IsValid = false,
                    FilteredFilePath = filePath,
                    ErrorMessage = $"Error reading file: {ex.Message}"
                };
            }
        }
    }
}
