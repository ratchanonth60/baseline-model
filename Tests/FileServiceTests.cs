using Xunit;
using BaselineMode.WPF.Services;
using System.IO;
using System;

namespace BaselineMode.WPF.Tests
{
    public class FileServiceTests
    {
        private readonly FileService _fileService;
        private readonly string _tempDir;

        // Each 4128-char segment produces 15 BaselineData items (SAMPLES_PER_SEGMENT)
        private const int SAMPLES_PER_SEGMENT = 15;
        private const int CHUNK_SIZE = 4128;

        public FileServiceTests()
        {
            _fileService = new FileService();
            _tempDir = Path.Combine(Path.GetTempPath(), "BaselineModeFileTests");
            if (!Directory.Exists(_tempDir))
                Directory.CreateDirectory(_tempDir);
        }

        #region Helper Methods

        /// <summary>
        /// Creates a valid hex segment starting with E225.
        /// Total length = 4128 characters (CHUNK_SIZE)
        /// </summary>
        private string CreateValidSegment(int packetNo = 1)
        {
            // E225 = Sync code (4 chars)
            // 08B0 = Package ID (4 chars)
            // XXXX = Packet sequence (4 chars based on packetNo)
            // Rest = padding to reach 4128 total
            string header = "E22508B0";
            string sequence = packetNo.ToString("X4"); // 4 hex chars
            int remainingLength = CHUNK_SIZE - header.Length - sequence.Length;
            string padding = new string('0', remainingLength);
            return header + sequence + padding;
        }

        #endregion

        #region ProcessFileStream Tests

        [Fact]
        public void ProcessFileStream_EmptyFile_ReturnsEmptyList()
        {
            // Arrange
            string emptyFile = Path.Combine(_tempDir, "empty.txt");
            File.WriteAllText(emptyFile, "");

            // Act
            var result = _fileService.ProcessFileStream(emptyFile);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);

            // Cleanup
            File.Delete(emptyFile);
        }

        [Fact]
        public void ProcessFileStream_NoE225Header_ReturnsEmptyList()
        {
            // Arrange
            string noHeaderFile = Path.Combine(_tempDir, "no_header.txt");
            File.WriteAllText(noHeaderFile, new string('A', 10000));

            // Act
            var result = _fileService.ProcessFileStream(noHeaderFile);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);

            // Cleanup
            File.Delete(noHeaderFile);
        }

        [Fact]
        public void ProcessFileStream_SingleValidSegment_Returns15Items()
        {
            // Arrange - Each segment produces 15 samples
            string singleFile = Path.Combine(_tempDir, "single_segment.txt");
            string segment = CreateValidSegment(1);
            File.WriteAllText(singleFile, segment);

            // Act
            var result = _fileService.ProcessFileStream(singleFile);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(SAMPLES_PER_SEGMENT, result.Count); // 15 items per segment

            // Cleanup
            File.Delete(singleFile);
        }

        [Fact]
        public void ProcessFileStream_MultipleSegments_ReturnsCorrectCount()
        {
            // Arrange - 3 segments = 3 * 15 = 45 samples
            string multiFile = Path.Combine(_tempDir, "multi_segment.txt");
            string content = CreateValidSegment(1) + CreateValidSegment(2) + CreateValidSegment(3);
            File.WriteAllText(multiFile, content);

            // Act
            var result = _fileService.ProcessFileStream(multiFile);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(3 * SAMPLES_PER_SEGMENT, result.Count); // 45 items

            // Cleanup
            File.Delete(multiFile);
        }

        [Fact]
        public void ProcessFileStream_SegmentsWithNewlines_ParsesCorrectly()
        {
            // Arrange - Newlines are ignored, still 3 segments
            string nlFile = Path.Combine(_tempDir, "newlines.txt");
            string content = CreateValidSegment(1) + "\n" + CreateValidSegment(2) + "\r\n" + CreateValidSegment(3);
            File.WriteAllText(nlFile, content);

            // Act
            var result = _fileService.ProcessFileStream(nlFile);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(3 * SAMPLES_PER_SEGMENT, result.Count);

            // Cleanup
            File.Delete(nlFile);
        }

        [Fact]
        public void ProcessFileStream_SegmentsWithSpaces_ParsesCorrectly()
        {
            // Arrange - Spaces are filtered out, hex chars only
            string spaceFile = Path.Combine(_tempDir, "spaces.txt");
            string seg1 = CreateValidSegment(1);
            string seg2 = CreateValidSegment(2);
            // Insert spaces - they get filtered, so we still have 2 full segments
            string content = seg1.Substring(0, 100) + " " + seg1.Substring(100) + "  " + seg2;
            File.WriteAllText(spaceFile, content);

            // Act
            var result = _fileService.ProcessFileStream(spaceFile);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2 * SAMPLES_PER_SEGMENT, result.Count);

            // Cleanup
            File.Delete(spaceFile);
        }

        [Fact]
        public void ProcessFileStream_IncompleteSegmentAtEnd_IgnoresIncomplete()
        {
            // Arrange
            string incompleteFile = Path.Combine(_tempDir, "incomplete.txt");
            string completeSegment = CreateValidSegment(1);
            string incompleteSegment = "E225" + new string('0', 100); // Only 104 chars, not 4128
            File.WriteAllText(incompleteFile, completeSegment + incompleteSegment);

            // Act
            var result = _fileService.ProcessFileStream(incompleteFile);

            // Assert - Only the complete segment (15 samples)
            Assert.NotNull(result);
            Assert.Equal(SAMPLES_PER_SEGMENT, result.Count);

            // Cleanup
            File.Delete(incompleteFile);
        }

        [Fact]
        public void ProcessFileStream_GarbageBeforeHeader_FindsHeader()
        {
            // Arrange
            string garbageFile = Path.Combine(_tempDir, "garbage_before.txt");
            string garbage = "GARBAGE123456789"; // Non-hex chars filtered out
            string segment = CreateValidSegment(1);
            File.WriteAllText(garbageFile, garbage + segment);

            // Act
            var result = _fileService.ProcessFileStream(garbageFile);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(SAMPLES_PER_SEGMENT, result.Count);

            // Cleanup
            File.Delete(garbageFile);
        }

        [Fact]
        public void ProcessFileStream_CaseInsensitiveHeader_Finds_e225()
        {
            // Arrange - lowercase e225
            string lowerFile = Path.Combine(_tempDir, "lowercase.txt");
            string segment = "e225" + new string('0', CHUNK_SIZE - 4); // lowercase e225
            File.WriteAllText(lowerFile, segment);

            // Act
            var result = _fileService.ProcessFileStream(lowerFile);

            // Assert - Should find it (IndexOf is case-insensitive)
            Assert.NotNull(result);
            Assert.Equal(SAMPLES_PER_SEGMENT, result.Count);

            // Cleanup
            File.Delete(lowerFile);
        }

        #endregion

        #region BaselineData Structure Tests

        [Fact]
        public void ProcessFileStream_ValidSegment_PopulatesBaselineData()
        {
            // Arrange
            string dataFile = Path.Combine(_tempDir, "data_check.txt");
            string segment = CreateValidSegment(42);
            File.WriteAllText(dataFile, segment);

            // Act
            var result = _fileService.ProcessFileStream(dataFile);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(SAMPLES_PER_SEGMENT, result.Count);

            var data = result[0];
            Assert.NotNull(data.L1);
            Assert.NotNull(data.L2);
            Assert.NotNull(data.L6);
            Assert.NotNull(data.L7);
            Assert.Equal(16, data.L1.Length);
            Assert.Equal(16, data.L2.Length);
            Assert.Equal(16, data.L6.Length);
            Assert.Equal(16, data.L7.Length);

            // Cleanup
            File.Delete(dataFile);
        }

        #endregion

        #region Progress Reporting Tests

        [Fact]
        public void ProcessFileStream_WithProgress_ReportsProgress()
        {
            // Arrange
            string progressFile = Path.Combine(_tempDir, "progress.txt");
            // Create 100 segments = 1500 samples
            string content = "";
            for (int i = 0; i < 100; i++)
            {
                content += CreateValidSegment(i);
            }
            File.WriteAllText(progressFile, content);

            double lastProgress = 0;
            var progress = new Progress<double>(p => lastProgress = p);

            // Act
            var result = _fileService.ProcessFileStream(progressFile, progress);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(100 * SAMPLES_PER_SEGMENT, result.Count); // 1500 samples

            // Cleanup
            File.Delete(progressFile);
        }

        #endregion

        #region Large File Handling

        [Fact]
        public void ProcessFileStream_LargeFile_ProcessesWithoutMemoryIssue()
        {
            // Arrange - Create a moderately large file with 500 segments
            string largeFile = Path.Combine(_tempDir, "large.txt");
            using (var writer = new StreamWriter(largeFile))
            {
                for (int i = 0; i < 500; i++)
                {
                    writer.Write(CreateValidSegment(i));
                }
            }

            // Act
            var result = _fileService.ProcessFileStream(largeFile);

            // Assert - 500 segments * 15 samples = 7500
            Assert.NotNull(result);
            Assert.Equal(500 * SAMPLES_PER_SEGMENT, result.Count);

            // Cleanup
            File.Delete(largeFile);
        }

        #endregion
    }
}
