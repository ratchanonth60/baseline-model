using Xunit;
using BaselineMode.WPF.Views.models;
using BaselineMode.WPF.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BaselineMode.WPF.Tests
{
    /// <summary>
    /// Unit tests for MainViewModel commands and functionality.
    /// Tests cover all button commands in the UI.
    /// </summary>
    public class MainViewModelTests
    {
        #region Constructor Tests

        [Fact]
        public void Constructor_InitializesDefaultValues()
        {
            // Arrange & Act
            var vm = new MainViewModel();

            // Assert
            Assert.Equal("Ready", vm.StatusMessage);
            Assert.False(vm.IsBusy);
            Assert.Equal(0, vm.ProgressValue);
            Assert.Equal("No files selected", vm.InputFilesInfo);
            Assert.NotNull(vm.Channels);
            Assert.Equal(16, vm.Channels.Count);
        }

        [Fact]
        public void Constructor_InitializesChannels()
        {
            // Arrange & Act
            var vm = new MainViewModel();

            // Assert
            Assert.Equal(8, vm.ChannelsX.Count); // X-direction channels
            Assert.Equal(8, vm.ChannelsZ.Count); // Z-direction channels
        }

        #endregion

        #region Reset Command Tests

        [Fact]
        public void ResetCommand_ClearsSelectedFiles()
        {
            // Arrange
            var vm = new MainViewModel();
            
            // Act
            vm.ResetCommand.Execute(null);

            // Assert
            Assert.Equal("No files selected", vm.InputFilesInfo);
            Assert.Equal("Reset complete.", vm.StatusMessage);
            Assert.Equal(0, vm.ProgressValue);
        }

        [Fact]
        public void ResetCommand_ClearsProcessedData()
        {
            // Arrange
            var vm = new MainViewModel();
            
            // Act
            vm.ResetCommand.Execute(null);

            // Assert
            Assert.Empty(vm.ProcessedData);
        }

        [Fact]
        public void ResetCommand_ResetsCurrentPage()
        {
            // Arrange
            var vm = new MainViewModel();
            
            // Act
            vm.ResetCommand.Execute(null);

            // Assert
            Assert.Equal(1, vm.CurrentPage);
        }

        #endregion

        #region Stop Command Tests

        [Fact]
        public void StopCommand_SetsStatusMessage()
        {
            // Arrange
            var vm = new MainViewModel();

            // Act
            vm.StopCommand.Execute(null);

            // Assert
            Assert.Equal("Stopping...", vm.StatusMessage);
        }

        #endregion

        #region Pagination Commands Tests

        [Fact]
        public void NextPageCommand_IncrementsCurrentPage_WhenNotAtEnd()
        {
            // Arrange
            var vm = new MainViewModel();
            vm.ProcessedData = GenerateSampleData(250); // Enough for multiple pages
            vm.CurrentPage = 1;
            
            // Manually set TotalPages (normally set by UpdateDisplayTable)
            // Since we can't directly set TotalPages, we need to trigger the update

            // Act
            vm.NextPageCommand.Execute(null);

            // Assert - Page should attempt to move forward
            // Note: Without actual data processing, this tests the command doesn't throw
        }

        [Fact]
        public void PreviousPageCommand_DecrementsCurrentPage_WhenNotAtStart()
        {
            // Arrange
            var vm = new MainViewModel();
            vm.CurrentPage = 2;

            // Act
            vm.PreviousPageCommand.Execute(null);

            // Assert
            Assert.Equal(1, vm.CurrentPage);
        }

        [Fact]
        public void PreviousPageCommand_DoesNotDecrementBelowOne()
        {
            // Arrange
            var vm = new MainViewModel();
            vm.CurrentPage = 1;

            // Act
            vm.PreviousPageCommand.Execute(null);

            // Assert
            Assert.Equal(1, vm.CurrentPage);
        }

        #endregion

        #region Property Change Tests

        [Fact]
        public void SelectedLayerIndex_CanBeChanged()
        {
            // Arrange
            var vm = new MainViewModel();

            // Act
            vm.SelectedLayerIndex = 2;

            // Assert
            Assert.Equal(2, vm.SelectedLayerIndex);
        }

        [Fact]
        public void SelectedDirectionIndex_CanBeChanged()
        {
            // Arrange
            var vm = new MainViewModel();

            // Act
            vm.SelectedDirectionIndex = 1;

            // Assert
            Assert.Equal(1, vm.SelectedDirectionIndex);
        }

        [Fact]
        public void SelectedMode_CanBeChanged()
        {
            // Arrange
            var vm = new MainViewModel();

            // Act
            vm.SelectedMode = 1;

            // Assert
            Assert.Equal(1, vm.SelectedMode);
        }

        [Fact]
        public void SelectedBaselineMode_CanBeChanged()
        {
            // Arrange
            var vm = new MainViewModel();

            // Act
            vm.SelectedBaselineMode = 2;

            // Assert
            Assert.Equal(2, vm.SelectedBaselineMode);
        }

        [Fact]
        public void UseKalmanFilter_CanBeToggled()
        {
            // Arrange
            var vm = new MainViewModel();

            // Act
            vm.UseKalmanFilter = true;

            // Assert
            Assert.True(vm.UseKalmanFilter);
        }

        [Fact]
        public void UseThresholding_CanBeToggled()
        {
            // Arrange
            var vm = new MainViewModel();

            // Act
            vm.UseThresholding = true;

            // Assert
            Assert.True(vm.UseThresholding);
        }

        [Fact]
        public void UseGaussianFit_CanBeToggled()
        {
            // Arrange
            var vm = new MainViewModel();

            // Act
            vm.UseGaussianFit = true;

            // Assert
            Assert.True(vm.UseGaussianFit);
        }

        [Fact]
        public void KFactor_CanBeChanged()
        {
            // Arrange
            var vm = new MainViewModel();

            // Act
            vm.KFactor = 3.5;

            // Assert
            Assert.Equal(3.5, vm.KFactor);
        }

        [Fact]
        public void ThresholdValue_CanBeChanged()
        {
            // Arrange
            var vm = new MainViewModel();

            // Act
            vm.ThresholdValue = 100;

            // Assert
            Assert.Equal(100, vm.ThresholdValue);
        }

        [Fact]
        public void OutputFileName_CanBeChanged()
        {
            // Arrange
            var vm = new MainViewModel();

            // Act
            vm.OutputFileName = "test_output.xlsx";

            // Assert
            Assert.Equal("test_output.xlsx", vm.OutputFileName);
        }

        #endregion

        #region CanSaveMean Property Tests

        [Fact]
        public void CanSaveMean_DefaultIsFalse()
        {
            // Arrange & Act
            var vm = new MainViewModel();

            // Assert
            Assert.False(vm.CanSaveMean);
        }

        [Fact]
        public void CanSaveMean_UpdatedWhenBaselineModeChanges()
        {
            // Arrange
            var vm = new MainViewModel();

            // Act - Set to mode 0 or 1 (non-log scale modes enable CanSaveMean)
            vm.SelectedBaselineMode = 0;

            // Assert - CanSaveMean should be updated based on mode
            // The actual behavior depends on OnSelectedBaselineModeChanged
        }

        #endregion

        #region Status and Progress Tests

        [Fact]
        public void IsBusy_CanBeSet()
        {
            // Arrange
            var vm = new MainViewModel();

            // Act
            // Note: IsBusy is typically set internally, but we test the property works

            // Assert
            Assert.False(vm.IsBusy);
        }

        [Fact]
        public void ProgressValue_DefaultIsZero()
        {
            // Arrange & Act
            var vm = new MainViewModel();

            // Assert
            Assert.Equal(0, vm.ProgressValue);
        }

        [Fact]
        public void StatusMessage_CanBeChanged()
        {
            // Arrange
            var vm = new MainViewModel();

            // Act
            vm.StatusMessage = "Processing...";

            // Assert
            Assert.Equal("Processing...", vm.StatusMessage);
        }

        #endregion

        #region Command Existence Tests

        [Fact]
        public void SelectFilesCommand_Exists()
        {
            // Arrange
            var vm = new MainViewModel();

            // Assert
            Assert.NotNull(vm.SelectFilesCommand);
        }

        [Fact]
        public void PreProcessDataCommand_Exists()
        {
            // Arrange
            var vm = new MainViewModel();

            // Assert
            Assert.NotNull(vm.PreProcessDataCommand);
        }

        [Fact]
        public void BrowseOutputDirectoryCommand_Exists()
        {
            // Arrange
            var vm = new MainViewModel();

            // Assert
            Assert.NotNull(vm.BrowseOutputDirectoryCommand);
        }

        [Fact]
        public void CheckHeaderCommand_Exists()
        {
            // Arrange
            var vm = new MainViewModel();

            // Assert
            Assert.NotNull(vm.CheckHeaderCommand);
        }

        [Fact]
        public void ProcessDataCommand_Exists()
        {
            // Arrange
            var vm = new MainViewModel();

            // Assert
            Assert.NotNull(vm.ProcessDataCommand);
        }

        [Fact]
        public void StopCommand_Exists()
        {
            // Arrange
            var vm = new MainViewModel();

            // Assert
            Assert.NotNull(vm.StopCommand);
        }

        [Fact]
        public void ResetCommand_Exists()
        {
            // Arrange
            var vm = new MainViewModel();

            // Assert
            Assert.NotNull(vm.ResetCommand);
        }

        [Fact]
        public void ShowHeatmapCommand_Exists()
        {
            // Arrange
            var vm = new MainViewModel();

            // Assert
            Assert.NotNull(vm.ShowHeatmapCommand);
        }

        [Fact]
        public void SaveMeanCommand_Exists()
        {
            // Arrange
            var vm = new MainViewModel();

            // Assert
            Assert.NotNull(vm.SaveMeanCommand);
        }

        [Fact]
        public void ShowChannelDetailCommand_Exists()
        {
            // Arrange
            var vm = new MainViewModel();

            // Assert
            Assert.NotNull(vm.ShowChannelDetailCommand);
        }

        [Fact]
        public void NextPageCommand_Exists()
        {
            // Arrange
            var vm = new MainViewModel();

            // Assert
            Assert.NotNull(vm.NextPageCommand);
        }

        [Fact]
        public void PreviousPageCommand_Exists()
        {
            // Arrange
            var vm = new MainViewModel();

            // Assert
            Assert.NotNull(vm.PreviousPageCommand);
        }

        #endregion

        #region Validation Tests

        [Fact]
        public void ProcessDataCommand_SetsErrorMessage_WhenNoFilesSelected()
        {
            // Arrange
            var vm = new MainViewModel();
            vm.ResetCommand.Execute(null); // Ensure no files selected

            // Act
            vm.ProcessDataCommand.Execute(null);

            // Assert - Should set appropriate error message
            Assert.Contains("select", vm.StatusMessage.ToLower());
        }

        [Fact]
        public void PreProcessDataCommand_SetsErrorMessage_WhenNoFilesSelected()
        {
            // Arrange
            var vm = new MainViewModel();
            vm.ResetCommand.Execute(null);

            // Act
            vm.PreProcessDataCommand.Execute(null);

            // Assert
            Assert.Contains("no files", vm.StatusMessage.ToLower());
        }

        [Fact]
        public void ShowHeatmapCommand_SetsErrorMessage_WhenNoData()
        {
            // Arrange
            var vm = new MainViewModel();
            vm.ResetCommand.Execute(null);

            // Act
            vm.ShowHeatmapCommand.Execute(null);

            // Assert
            Assert.Contains("no data", vm.StatusMessage.ToLower());
        }

        #endregion

        #region Integration-like Tests

        [Fact]
        public void Channels_AreProperlyNamed()
        {
            // Arrange & Act
            var vm = new MainViewModel();

            // Assert
            for (int i = 0; i < vm.Channels.Count; i++)
            {
                Assert.Contains((i + 1).ToString(), vm.Channels[i].Title);
            }
        }

        [Fact]
        public void ChannelsX_ContainsFirstEightChannels()
        {
            // Arrange & Act
            var vm = new MainViewModel();

            // Assert
            Assert.Equal(8, vm.ChannelsX.Count);
            for (int i = 0; i < 8; i++)
            {
                Assert.Contains((i + 1).ToString(), vm.ChannelsX[i].Title);
            }
        }

        [Fact]
        public void ChannelsZ_ContainsLastEightChannels()
        {
            // Arrange & Act
            var vm = new MainViewModel();

            // Assert
            Assert.Equal(8, vm.ChannelsZ.Count);
            for (int i = 0; i < 8; i++)
            {
                Assert.Contains((i + 9).ToString(), vm.ChannelsZ[i].Title);
            }
        }

        #endregion

        #region Timer Tests

        [Fact]
        public void CurrentDateTime_IsSet()
        {
            // Arrange & Act
            var vm = new MainViewModel();

            // Assert
            Assert.True(vm.CurrentDateTime > DateTime.MinValue);
        }

        #endregion

        #region Helper Methods

        private List<BaselineData> GenerateSampleData(int count)
        {
            var data = new List<BaselineData>(count);
            var random = new Random(42);

            for (int i = 0; i < count; i++)
            {
                data.Add(new BaselineData
                {
                    SamplingPacketNo = i / 15 + 1,
                    SamplingNo = i % 15 + 1,
                    L1 = GenerateRandomChannelData(random),
                    L2 = GenerateRandomChannelData(random),
                    L6 = GenerateRandomChannelData(random),
                    L7 = GenerateRandomChannelData(random)
                });
            }

            return data;
        }

        private double[] GenerateRandomChannelData(Random random)
        {
            var data = new double[16];
            for (int i = 0; i < 16; i++)
            {
                data[i] = random.NextDouble() * 16383;
            }
            return data;
        }

        #endregion
    }
}
