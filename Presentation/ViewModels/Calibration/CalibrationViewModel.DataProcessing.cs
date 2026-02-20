using System;
using System.Collections.Generic;
using Avalonia.Threading;

namespace BaselineMode.WPF.Presentation.ViewModels.Calibration
{
    /// <summary>
    /// Hex parsing, calibration data accumulation, and list reset for Calibration mode.
    /// </summary>
    public partial class CalibrationViewModel
    {
        private void ResetDataLists(int? customCapacity = null)
        {
            int capacity = customCapacity ?? InitialCapacity;

            for (int i = 0; i < 16; i++)
            {
                _l1Columns[i]?.Clear();
                _l2Columns[i]?.Clear();
                _l6Columns[i]?.Clear();
                _l7Columns[i]?.Clear();
                _l1VoltColumns[i]?.Clear();
                _l2VoltColumns[i]?.Clear();
                _l6VoltColumns[i]?.Clear();
                _l7VoltColumns[i]?.Clear();

                _l1Columns[i] = new List<double>(capacity);
                _l2Columns[i] = new List<double>(capacity);
                _l6Columns[i] = new List<double>(capacity);
                _l7Columns[i] = new List<double>(capacity);
                _l1VoltColumns[i] = new List<double>(capacity);
                _l2VoltColumns[i] = new List<double>(capacity);
                _l6VoltColumns[i] = new List<double>(capacity);
                _l7VoltColumns[i] = new List<double>(capacity);
            }

            Dispatcher.UIThread.InvokeAsync(() =>
                StatusMessage = $"Lists initialized with capacity: {capacity:N0} per channel");
        }

        private void ProcessCalibration(string[] hexData)
        {
            if (hexData.Length < 18) return;
            if (_cts?.Token.IsCancellationRequested ?? false) return;

            const double voltageScale = (5.0 / 16383.0) * 1000.0;

            for (int i = 0; i < 11; i++)
            {
                int offsetL1L2 = 18 + 64 * i;
                int offsetL6L7 = 722 + 64 * i;

                if (offsetL1L2 + 64 > hexData.Length || offsetL6L7 + 64 > hexData.Length)
                    continue;
                if (i % 3 == 0 && (_cts?.Token.IsCancellationRequested ?? false))
                    return;

                for (int j = 0; j < 16; j++)
                {
                    int l1Idx = offsetL1L2 + (j * 2);
                    int l1Val = ParseHexPair(hexData, l1Idx);
                    _l1Columns[j].Add(l1Val);
                    _l1VoltColumns[j].Add(l1Val * voltageScale);

                    int l2Idx = offsetL1L2 + 32 + (j * 2);
                    int l2Val = ParseHexPair(hexData, l2Idx);
                    _l2Columns[j].Add(l2Val);
                    _l2VoltColumns[j].Add(l2Val * voltageScale);

                    int l6Idx = offsetL6L7 + (j * 2);
                    int l6Val = ParseHexPair(hexData, l6Idx);
                    _l6Columns[j].Add(l6Val);
                    _l6VoltColumns[j].Add(l6Val * voltageScale);

                    int l7Idx = offsetL6L7 + 32 + (j * 2);
                    int l7Val = ParseHexPair(hexData, l7Idx);
                    _l7Columns[j].Add(l7Val);
                    _l7VoltColumns[j].Add(l7Val * voltageScale);
                }
            }
        }

        private static int ParseHexPair(string[] hexData, int startIndex)
        {
            try
            {
                if (startIndex + 1 >= hexData.Length) return 0;
                int high = Convert.ToInt32(hexData[startIndex], 16);
                int low = Convert.ToInt32(hexData[startIndex + 1], 16);
                return (high << 8) + low;
            }
            catch
            {
                return 0;
            }
        }

        private List<double>[] GetCalibrationColumns(int layerIndex) => layerIndex switch
        {
            0 => _l1Columns,
            1 => _l2Columns,
            2 => _l6Columns,
            3 => _l7Columns,
            _ => _l1Columns
        };

        private List<double>[] GetVoltageColumns(int layerIndex) => layerIndex switch
        {
            0 => _l1VoltColumns,
            1 => _l2VoltColumns,
            2 => _l6VoltColumns,
            3 => _l7VoltColumns,
            _ => _l1VoltColumns
        };
    }
}
