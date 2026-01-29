using Xunit;
using System.IO;
using BaselineMode.WPF.Services;

namespace BaselineMode.WPF.Tests
{
    public class HeaderValidatorTests
    {
        private readonly string _tempDir;

        public HeaderValidatorTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "BaselineModeTests");
            if (!Directory.Exists(_tempDir))
                Directory.CreateDirectory(_tempDir);
        }

        #region File Not Found Tests

        [Fact]
        public void ValidateFile_FileNotFound_ReturnsInvalid()
        {
            // Arrange
            string nonExistentFile = Path.Combine(_tempDir, "non_existent_file.txt");

            // Act
            var result = HeaderValidator.ValidateFile(nonExistentFile);

            // Assert
            Assert.False(result.IsValid);
            Assert.Equal("File not found.", result.ErrorMessage);
        }

        #endregion

        #region Empty File Tests

        [Fact]
        public void ValidateFile_EmptyFile_ReturnsInvalid()
        {
            // Arrange
            string emptyFile = Path.Combine(_tempDir, "empty_file.txt");
            File.WriteAllText(emptyFile, "");

            // Act
            var result = HeaderValidator.ValidateFile(emptyFile);

            // Assert
            Assert.False(result.IsValid);
            Assert.Equal("File is empty.", result.ErrorMessage);

            // Cleanup
            File.Delete(emptyFile);
        }

        [Fact]
        public void ValidateFile_OnlyWhitespaceLines_ReturnsInvalid()
        {
            // Arrange (whitespace-only lines are NOT skipped - they fail validation)
            // HeaderValidator skips empty lines but "   " is not empty
            string whitespaceFile = Path.Combine(_tempDir, "whitespace_file.txt");
            File.WriteAllText(whitespaceFile, "\n\n   \n\t\n");

            // Act
            var result = HeaderValidator.ValidateFile(whitespaceFile);

            // Assert - Line 3 has "   " which doesn't start with E225
            Assert.False(result.IsValid);
            Assert.Contains("line", result.ErrorMessage);

            // Cleanup
            File.Delete(whitespaceFile);
        }

        #endregion

        #region Valid Header Tests

        [Fact]
        public void ValidateFile_SingleValidLine_ReturnsValid()
        {
            // Arrange
            string validFile = Path.Combine(_tempDir, "valid_single.txt");
            string validHex = "E22508B0D7D10807025738E800BF00AE17D2" + new string('0', 4128 - 38);
            File.WriteAllText(validFile, validHex);

            // Act
            var result = HeaderValidator.ValidateFile(validFile);

            // Assert
            Assert.True(result.IsValid);
            Assert.NotNull(result.FirstHeaderContent);
            Assert.StartsWith("E225", result.FirstHeaderContent);

            // Cleanup
            File.Delete(validFile);
        }

        [Fact]
        public void ValidateFile_MultipleValidLines_ReturnsValid()
        {
            // Arrange
            string validFile = Path.Combine(_tempDir, "valid_multiple.txt");
            string line1 = "E22508B0D7D10807025738E800BF00AE17D2" + new string('A', 100);
            string line2 = "E22508B0D7D20807025738E8019400AE17D3" + new string('B', 100);
            string line3 = "E22508B0D7D30807025738E8027400AE17D4" + new string('C', 100);
            File.WriteAllLines(validFile, new[] { line1, line2, line3 });

            // Act
            var result = HeaderValidator.ValidateFile(validFile);

            // Assert
            Assert.True(result.IsValid);
            Assert.Equal(line1, result.FirstHeaderContent);

            // Cleanup
            File.Delete(validFile);
        }

        [Fact]
        public void ValidateFile_ValidLinesWithEmptyLinesBetween_ReturnsValid()
        {
            // Arrange (empty lines should be skipped)
            string validFile = Path.Combine(_tempDir, "valid_with_empty.txt");
            string content = "E225AAAA\n\nE225BBBB\n\nE225CCCC";
            File.WriteAllText(validFile, content);

            // Act
            var result = HeaderValidator.ValidateFile(validFile);

            // Assert
            Assert.True(result.IsValid);

            // Cleanup
            File.Delete(validFile);
        }

        #endregion

        #region Invalid Header Tests

        [Fact]
        public void ValidateFile_FirstLineInvalid_ReturnsInvalidAtLine1()
        {
            // Arrange
            string invalidFile = Path.Combine(_tempDir, "invalid_first.txt");
            File.WriteAllText(invalidFile, "XXXX0000\nE2250000");

            // Act
            var result = HeaderValidator.ValidateFile(invalidFile);

            // Assert
            Assert.False(result.IsValid);
            Assert.Equal(1, result.ErrorLine);
            Assert.Contains("line 1", result.ErrorMessage);
            Assert.Equal("XXXX0000", result.ErrorContent);
        }

        [Fact]
        public void ValidateFile_SecondLineInvalid_ReturnsInvalidAtLine2()
        {
            // Arrange
            string invalidFile = Path.Combine(_tempDir, "invalid_second.txt");
            File.WriteAllText(invalidFile, "E2250000\nINVALID!");

            // Act
            var result = HeaderValidator.ValidateFile(invalidFile);

            // Assert
            Assert.False(result.IsValid);
            Assert.Equal(2, result.ErrorLine);
            Assert.Contains("line 2", result.ErrorMessage);
            Assert.Equal("INVALID!", result.ErrorContent);
        }

        [Fact]
        public void ValidateFile_MiddleLineInvalid_ReturnsCorrectLineNumber()
        {
            // Arrange
            string invalidFile = Path.Combine(_tempDir, "invalid_middle.txt");
            string content = "E225AAAA\nE225BBBB\nE225CCCC\nBADLINE!\nE225DDDD";
            File.WriteAllText(invalidFile, content);

            // Act
            var result = HeaderValidator.ValidateFile(invalidFile);

            // Assert
            Assert.False(result.IsValid);
            Assert.Equal(4, result.ErrorLine);
            Assert.Equal("BADLINE!", result.ErrorContent);
        }

        [Fact]
        public void ValidateFile_LineStartsWithE22_NotE225_ReturnsInvalid()
        {
            // Arrange (must be exactly E225, not E22 or E224)
            string invalidFile = Path.Combine(_tempDir, "invalid_partial.txt");
            File.WriteAllText(invalidFile, "E224AAAA");

            // Act
            var result = HeaderValidator.ValidateFile(invalidFile);

            // Assert
            Assert.False(result.IsValid);
            Assert.Equal("E224AAAA", result.ErrorContent);
        }

        [Fact]
        public void ValidateFile_LowercaseE225_ReturnsInvalid()
        {
            // Arrange (StartsWith is case-sensitive by default)
            string invalidFile = Path.Combine(_tempDir, "invalid_lowercase.txt");
            File.WriteAllText(invalidFile, "e225AAAA");

            // Act
            var result = HeaderValidator.ValidateFile(invalidFile);

            // Assert
            Assert.False(result.IsValid);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void ValidateFile_LineWithLeadingSpace_ReturnsInvalid()
        {
            // Arrange (no trim, so space at start means doesn't start with E225)
            string invalidFile = Path.Combine(_tempDir, "invalid_space.txt");
            File.WriteAllText(invalidFile, " E225AAAA");

            // Act
            var result = HeaderValidator.ValidateFile(invalidFile);

            // Assert
            Assert.False(result.IsValid);
            Assert.Equal(" E225AAAA", result.ErrorContent);
        }

        [Fact]
        public void ValidateFile_VeryLongValidLine_ReturnsValid()
        {
            // Arrange
            string validFile = Path.Combine(_tempDir, "valid_long.txt");
            string longLine = "E225" + new string('F', 10000);
            File.WriteAllText(validFile, longLine);

            // Act
            var result = HeaderValidator.ValidateFile(validFile);

            // Assert
            Assert.True(result.IsValid);
            Assert.Equal(longLine, result.FirstHeaderContent);

            // Cleanup
            File.Delete(validFile);
        }

        #endregion
    }
}
