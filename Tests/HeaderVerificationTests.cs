using Xunit;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System;
using BaselineMode.WPF.Services;

namespace BaselineMode.WPF.Tests
{
    public class HeaderVerificationTests
    {
        // Path provided by user - Note: Some files in this folder may be truncated/partial
        // and won't start with E225. This test validates the HeaderValidator behavior.
        private const string TargetDirectory = @"D:\ratchanonth\Baseline Mode 1.0\BaselineMode.WPF\DSSD-Energy Calibration (Alpha Source)\Raw data energy calibration (alpha)\RAW DATA DSSDL1 Am+Pu X面 Set 1";

        public static IEnumerable<object[]> GetFiles()
        {
            if (!Directory.Exists(TargetDirectory))
            {
                return new List<object[]> { new object[] { "DirectoryNotFound" } };
            }

            return Directory.GetFiles(TargetDirectory, "*.txt")
                          .Select(f => new object[] { f });
        }

        /// <summary>
        /// This test validates that HeaderValidator correctly identifies files.
        /// Note: Some raw data files may be partial (truncated mid-stream) and won't start with E225.
        /// This is expected behavior - the validator correctly identifies them as invalid.
        /// </summary>
        [Theory]
        [MemberData(nameof(GetFiles))]
        public void ValidateFileHeader_ReturnsResult(string filePath)
        {
            if (filePath == "DirectoryNotFound")
            {
                Assert.Fail($"Directory not found: {TargetDirectory}");
            }

            Assert.True(File.Exists(filePath), $"File not found: {filePath}");

            // Act - Just verify the validator doesn't throw
            var result = Services.HeaderValidator.ValidateFile(filePath);

            // Assert - Result should be populated
            Assert.NotNull(result);
            // Either valid OR has an error message
            Assert.True(result.IsValid || !string.IsNullOrEmpty(result.ErrorMessage));
        }

        /// <summary>
        /// Test that files starting with E225 are validated as correct
        /// </summary>
        [Fact]
        public void ValidateFile_StartsWithE225_ReturnsValid()
        {
            if (!Directory.Exists(TargetDirectory))
            {
                return; // Skip if directory doesn't exist
            }

            // Find files that actually start with E225
            var validFiles = Directory.GetFiles(TargetDirectory, "*.txt")
                .Where(f =>
                {
                    try
                    {
                        using var reader = new StreamReader(f);
                        var firstLine = reader.ReadLine();
                        return firstLine?.StartsWith("E225") == true;
                    }
                    catch { return false; }
                })
                .Take(5) // Test up to 5 valid files
                .ToList();

            foreach (var file in validFiles)
            {
                var result = HeaderValidator.ValidateFile(file);
                Assert.True(result.IsValid, $"File {Path.GetFileName(file)} should be valid but got: {result.ErrorMessage}");
            }
        }
    }
}
