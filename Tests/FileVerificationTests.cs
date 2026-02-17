using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using BaselineMode.WPF.Infrastructure.Services;
using BaselineMode.WPF.Infrastructure.Services.Baseline;

namespace BaselineMode.WPF.Tests
{
    public class FileVerificationTests
    {
        [Fact]
        public async Task Verify_RawData_Processing()
        {
            // รองรับทั้ง path เดิม (local) และ TestData ใน repo (relative to test output)
            string folderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "TestData");
            string fallbackPath = @"D:\ratchanonth\Baseline Mode 1.0\BaselineMode.WPF\DSSD-Energy Calibration (Alpha Source)\Raw data energy calibration (alpha)\RAW DATA DSSDL1 Am+Pu X面 Set 1";
            string fileName = "2025-12-25-10-10-34-371.txt";

            string fullPath = Path.Combine(folderPath, fileName);
            if (!File.Exists(fullPath))
                fullPath = Path.Combine(fallbackPath, fileName);

            // ข้ามเทสเมื่อไม่มีไฟล์ test data (เช่นใน CI)
            if (!File.Exists(fullPath))
                return;

            // Arrange
            var logger = new LoggerService();
            var fileService = new BaselineFileService(logger);

            // Act
            var result = await fileService.ProcessFileStreamAsync(fullPath, null);

            // Assert
            Assert.True(result.IsSuccess, result.Error ?? "ProcessFileStreamAsync failed");
            var list = result.Value;
            Assert.NotNull(list);
            Assert.NotEmpty(list);

            var firstItem = list[0];
            Assert.True(firstItem.SamplingPacketNo > 0, "Packet Number should be parsed");
            Assert.NotNull(firstItem.L1);
            Assert.Equal(16, firstItem.L1.Length);
        }
    }
}
