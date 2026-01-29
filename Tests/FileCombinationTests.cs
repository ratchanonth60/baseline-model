using Xunit;
using System.IO;
using System;

namespace BaselineMode.WPF.Tests
{
    /// <summary>
    /// Tests for file combination logic (matching original Form1.cs behavior)
    /// </summary>
    public class FileCombinationTests
    {
        private readonly string _tempDir;

        public FileCombinationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "BaselineModeCombineTests");
            if (!Directory.Exists(_tempDir))
                Directory.CreateDirectory(_tempDir);
        }

        #region string.Concat Behavior Tests

        [Fact]
        public void Concat_TwoFiles_NoSeparator()
        {
            // Arrange
            string file1Content = "E225AAAA";
            string file2Content = "E225BBBB";

            // Act - This is how the app now combines files
            string combined = string.Concat(file1Content, file2Content);

            // Assert
            Assert.Equal("E225AAAAE225BBBB", combined);
            Assert.DoesNotContain("\n", combined);
        }

        [Fact]
        public void Concat_MultipleFiles_ContinuousStream()
        {
            // Arrange
            var contents = new[] { "E225AAAA", "E225BBBB", "E225CCCC" };

            // Act
            string combined = string.Concat(contents);

            // Assert
            Assert.Equal("E225AAAAE225BBBBE225CCCC", combined);
        }

        [Fact]
        public void Concat_FileWithTrailingNewline_PreservesNewline()
        {
            // Arrange - File 1 ends with newline
            string file1Content = "E225AAAA\n";
            string file2Content = "E225BBBB";

            // Act
            string combined = string.Concat(file1Content, file2Content);

            // Assert
            Assert.Equal("E225AAAA\nE225BBBB", combined);
        }

        [Fact]
        public void Concat_EmptyFile_SkippedGracefully()
        {
            // Arrange
            var contents = new[] { "E225AAAA", "", "E225CCCC" };

            // Act
            string combined = string.Concat(contents);

            // Assert
            Assert.Equal("E225AAAAE225CCCC", combined);
        }

        #endregion

        #region vs string.Join Comparison

        [Fact]
        public void Join_vs_Concat_Difference()
        {
            // Arrange
            var contents = new[] { "E225AAAA", "E225BBBB" };

            // Act
            string withJoin = string.Join("\n", contents);
            string withConcat = string.Concat(contents);

            // Assert - Join adds separator, Concat doesn't
            Assert.Equal("E225AAAA\nE225BBBB", withJoin);
            Assert.Equal("E225AAAAE225BBBB", withConcat);
            Assert.NotEqual(withJoin, withConcat);
        }

        #endregion

        #region Real File Operations

        [Fact]
        public void CombineFiles_WriteAndRead_MatchesExpected()
        {
            // Arrange
            string file1 = Path.Combine(_tempDir, "part1.txt");
            string file2 = Path.Combine(_tempDir, "part2.txt");
            string combined = Path.Combine(_tempDir, "combined.txt");

            File.WriteAllText(file1, "E225" + new string('A', 100));
            File.WriteAllText(file2, "E225" + new string('B', 100));

            // Act - Simulate the app's file combination
            var allContents = new System.Collections.Generic.List<string>();
            allContents.Add(File.ReadAllText(file1));
            allContents.Add(File.ReadAllText(file2));
            File.WriteAllText(combined, string.Concat(allContents));

            // Assert
            string result = File.ReadAllText(combined);
            Assert.StartsWith("E225", result);
            Assert.Contains("E225" + new string('B', 100), result);
            Assert.Equal(208, result.Length); // 104 + 104

            // Cleanup
            File.Delete(file1);
            File.Delete(file2);
            File.Delete(combined);
        }

        [Fact]
        public void CombineFiles_LargeFiles_WorksCorrectly()
        {
            // Arrange
            string file1 = Path.Combine(_tempDir, "large1.txt");
            string file2 = Path.Combine(_tempDir, "large2.txt");
            string combined = Path.Combine(_tempDir, "largeCombined.txt");

            // Create files with multiple complete segments (4128 chars each)
            string segment1 = "E225" + new string('1', 4124);
            string segment2 = "E225" + new string('2', 4124);
            File.WriteAllText(file1, segment1 + segment1); // 2 segments
            File.WriteAllText(file2, segment2 + segment2); // 2 segments

            // Act
            var allContents = new System.Collections.Generic.List<string>();
            allContents.Add(File.ReadAllText(file1));
            allContents.Add(File.ReadAllText(file2));
            File.WriteAllText(combined, string.Concat(allContents));

            // Assert
            string result = File.ReadAllText(combined);
            Assert.Equal(4128 * 4, result.Length); // 4 complete segments

            // Count E225 headers
            int headerCount = 0;
            int index = 0;
            while ((index = result.IndexOf("E225", index, StringComparison.OrdinalIgnoreCase)) != -1)
            {
                headerCount++;
                index += 4;
            }
            Assert.Equal(4, headerCount);

            // Cleanup
            File.Delete(file1);
            File.Delete(file2);
            File.Delete(combined);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void CombineFiles_UnicodeContent_PreservedCorrectly()
        {
            // Arrange - Although hex data is ASCII, test encoding handling
            string file1 = Path.Combine(_tempDir, "uni1.txt");
            string file2 = Path.Combine(_tempDir, "uni2.txt");

            File.WriteAllText(file1, "E225CAFE");
            File.WriteAllText(file2, "E225BABE");

            // Act
            var allContents = new System.Collections.Generic.List<string>();
            allContents.Add(File.ReadAllText(file1));
            allContents.Add(File.ReadAllText(file2));
            string combined = string.Concat(allContents);

            // Assert
            Assert.Equal("E225CAFEE225BABE", combined);

            // Cleanup
            File.Delete(file1);
            File.Delete(file2);
        }

        [Fact]
        public void CombineFiles_FileEndsWithPartialHeader_HandledCorrectly()
        {
            // Arrange - File 1 ends with "E22" (partial header)
            string file1 = Path.Combine(_tempDir, "partial1.txt");
            string file2 = Path.Combine(_tempDir, "partial2.txt");

            File.WriteAllText(file1, "E225AAAAE22"); // Ends with partial "E22"
            File.WriteAllText(file2, "5BBBB"); // Starts with "5BBBB"

            // Act
            var allContents = new System.Collections.Generic.List<string>();
            allContents.Add(File.ReadAllText(file1));
            allContents.Add(File.ReadAllText(file2));
            string combined = string.Concat(allContents);

            // Assert - The partial "E22" + "5" should form "E225"
            Assert.Equal("E225AAAAE225BBBB", combined);

            // This combined file now has 2 valid E225 headers
            int headerCount = 0;
            int index = 0;
            while ((index = combined.IndexOf("E225", index, StringComparison.OrdinalIgnoreCase)) != -1)
            {
                headerCount++;
                index += 4;
            }
            Assert.Equal(2, headerCount);

            // Cleanup
            File.Delete(file1);
            File.Delete(file2);
        }

        #endregion
    }
}
