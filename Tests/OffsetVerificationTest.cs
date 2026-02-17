using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using BaselineMode.WPF.Core.Models.Baseline;
using BaselineMode.WPF.Infrastructure.Services;
using BaselineMode.WPF.Infrastructure.Services.Baseline;

namespace BaselineMode.WPF.Tests
{
    public class OffsetVerificationTest
    {
        [Fact]
        public async Task ProcessFileStream_ShouldUseCorrectOffsets()
        {
            // Arrange
            // Create a fake hex string where we know exactly what values should be at the "correct" offsets vs "incorrect" offsets.
            // 
            // Correct Offsets (char index):
            // L1/L2 Offset: 36 + 64 * 0 * 2 = 36
            // L6/L7 Offset: 1956 + 64 * 0 * 2 = 1956
            //
            // Incorrect Offsets (char index):
            // L1/L2 Offset: 18 + 0 = 18
            // L6/L7 Offset: 978 + 0 = 978

            var sb = new StringBuilder();

            // Header
            sb.Append("E225"); // 4 chars
            // Fill with '0' until index 18
            sb.Append('0', 18 - 4);

            // At index 18 (Incorrect Offset location), put a specific pattern "AAAA" (value 43690)
            sb.Append("AAAA"); // 4 chars, ends at 22

            // Fill with '0' until index 36
            sb.Append('0', 36 - 22);

            // At index 36 (Correct Offset location), put a specific pattern "BBBB" (value 48059)
            sb.Append("BBBB"); // 4 chars, ends at 40

            // Fill the rest until we reach another important point or end of chunk
            // We need to fill up to CHUNK_SIZE (4128 chars)
            // But we also need to handle the L6/L7 check if we want to be thorough, but checking L1/L2 is enough to prove the offset logic changed.

            int currentLength = 40;
            int targetLength = 4128;

            sb.Append('0', targetLength - currentLength);

            string fileContent = sb.ToString();
            string tempFilePath = Path.GetTempFileName();
            File.WriteAllText(tempFilePath, fileContent);

            var service = new BaselineFileService(new LoggerService());

            try
            {
                // Act
                var res = await service.ProcessFileStreamAsync(tempFilePath, null);
                List<BaselineData> result = res.IsSuccess ? res.Value : new List<BaselineData>();

                // Assert
                Assert.True(result.Count > 0, "Should have processed at least one segment");
                var firstSample = result[0];

                // If it read from index 18 ("AAAA"), value would be 0xAAAA = 43690
                // If it read from index 36 ("BBBB"), value would be 0xBBBB = 48059

                int expectedValue = 0xBBBB;
                double actualValue = firstSample.L1[0]; // The first channel of L1 is at the initial offset

                Assert.Equal((double)expectedValue, actualValue);
                Assert.NotEqual(0xAAAA, actualValue); // Explicitly ensure it didn't read the wrong one
            }
            finally
            {
                if (File.Exists(tempFilePath))
                    File.Delete(tempFilePath);
            }
        }
    }
}
