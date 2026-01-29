using BaselineMode.WPF.Services;
using System.IO;
using Xunit;

namespace BaselineMode.WPF.Tests
{
    public class FileVerificationTests
    {
        [Fact]
        public void Verify_RawData_Processing()
        {
            // Acknowledge the user's specific path
            // Note: In a real CI/CD environment, this path wouldn't exist, but this is a local verification test requested by the user.
            string folderPath = @"D:\ratchanonth\Baseline Mode 1.0\BaselineMode.WPF\DSSD-Energy Calibration (Alpha Source)\Raw data energy calibration (alpha)\RAW DATA DSSDL1 Am+Pu X面 Set 1";

            // Pick the first file found (as I listed earlier)
            string fileName = "2025-12-25-10-10-34-371.txt";
            string fullPath = Path.Combine(folderPath, fileName);

            // Assert file exists
            Assert.True(File.Exists(fullPath), $"Test dictionary file not found at: {fullPath}");

            // Arrange
            var fileService = new FileService();

            // Act
            // We invoke the streaming processor
            // Since it might parse large chunks, we just want to ensure it DOES return data.
            var result = fileService.ProcessFileStream(fullPath, null);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);

            // Check basic structure of first item
            var firstItem = result[0];
            Assert.True(firstItem.SamplingPacketNo > 0, "Packet Number should be parsed");

            // Check if L1 channels have some non-zero data (usually expected)
            // Or just check that the array is populated
            Assert.NotNull(firstItem.L1);
            Assert.Equal(16, firstItem.L1.Length);

            // Output some info
            // (XUnit captures Console.WriteLine, but we can't easily see it unless we fail or use ITestOutputHelper)
        }
    }
}
