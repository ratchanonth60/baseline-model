using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BaselineMode.WPF.Models.Observation;
using BaselineMode.WPF.Interfaces.Observation;

namespace BaselineMode.WPF.Services.Observation
{
    public class ObservationDataProcessor : IObservationDataProcessor
    {
        // Global list to store processed data (raw particle objects)
        public List<object> StorageDataList { get; private set; } = new List<object>();

        // Global list to store results for each particle
        public List<Dictionary<string, object>> AllResults { get; private set; } = new List<Dictionary<string, object>>();

        // DSSD Data Storage
        public Dictionary<DetectorLayer, LayerData> DSSDData { get; private set; } = new Dictionary<DetectorLayer, LayerData>();

        // BGO Data Storage
        public Dictionary<BGOLayer, BGOData> BGOData { get; private set; } = new Dictionary<BGOLayer, BGOData>();

        // Kalman Filters (Using Dictionaries for better management if needed, but keeping simple fields for now)
        private KalmanFilter kalmanL3BGOH = new KalmanFilter(1, 1, 1, 10, 1, 0);
        private KalmanFilter kalmanL3BGOL = new KalmanFilter(1, 1, 1, 10, 1, 0);
        private KalmanFilter kalmanL4BGOH = new KalmanFilter(1, 1, 1, 10, 1, 0);
        private KalmanFilter kalmanL4BGOL = new KalmanFilter(1, 1, 1, 10, 1, 0);
        private KalmanFilter kalmanL5BGOH = new KalmanFilter(1, 1, 1, 10, 1, 0);
        private KalmanFilter kalmanL5BGOL = new KalmanFilter(1, 1, 1, 10, 1, 0);

        public KalmanFilter KalmanBGOLowGain { get; private set; } = new KalmanFilter(1, 1, 1, 1, 1, 0);
        public KalmanFilter KalmanBGOHighGain { get; private set; } = new KalmanFilter(1, 1, 1, 1, 1, 0);

        public ObservationDataProcessor()
        {
            InitializeDataStructures();
        }

        private void InitializeDataStructures()
        {
            // Initialize DSSD Layers
            foreach (DetectorLayer layer in Enum.GetValues(typeof(DetectorLayer)))
            {
                DSSDData[layer] = new LayerData();
            }

            // Initialize BGO Layers
            foreach (BGOLayer layer in Enum.GetValues(typeof(BGOLayer)))
            {
                BGOData[layer] = new BGOData();
            }
        }

        public void ClearData()
        {
            StorageDataList.Clear();
            AllResults.Clear();

            // Clear DSSD Data
            foreach (var layer in DSSDData.Values)
            {
                layer.PulseHeightX.Clear();
                layer.PulseHeightY.Clear();
                foreach (var stripList in layer.StripX.Values) stripList.Clear();
                foreach (var stripList in layer.StripY.Values) stripList.Clear();
            }

            // Clear BGO Data
            foreach (var layer in BGOData.Values)
            {
                layer.HighGain.Clear();
                layer.LowGain.Clear();
            }

            // Reset Kalman filters if necessary? (Logic preserved from original: none visible reset)
        }

        /// <summary>
        /// Async method to process multiple files
        /// </summary>
        public async Task<Dictionary<string, int[]>> ProcessFilesAsync(string[] filePaths)
        {
            ClearData();
            
            foreach (var filePath in filePaths)
            {
                await Task.Run(() => ProcessFile(filePath));
            }
            
            // Return processed data as histogram arrays
            return GetHistogramData();
        }
        
        /// <summary>
        /// Process a single file
        /// </summary>
        private void ProcessFile(string filePath)
        {
            var lines = File.ReadAllLines(filePath);
            foreach (var line in lines.Skip(1)) // Skip header
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                var hexData = SplitHexData(line.Trim());
                if (hexData.Length >= ObservationConstants.HeaderOffset + ObservationConstants.ParticleDataLength)
                {
                    ProcessParticles(hexData);
                }
            }
        }
        
        /// <summary>
        /// Read header information from a file
        /// </summary>
        public string ReadHeader(string filePath)
        {
            var lines = File.ReadAllLines(filePath);
            if (lines.Length == 0) return "Empty file";
            
            var headerLine = lines[0];
            var sb = new System.Text.StringBuilder();
            
            sb.AppendLine($"File: {Path.GetFileName(filePath)}");
            sb.AppendLine($"Header Length: {headerLine.Length} characters");
            sb.AppendLine($"Total Lines: {lines.Length}");
            
            // Try to parse timestamp from header
            if (headerLine.Length >= 28)
            {
                try
                {
                    var hexData = SplitHexData(headerLine);
                    var dateTime = GetDateTimeFromHexData(hexData);
                    sb.AppendLine($"Start Time: {dateTime:yyyy-MM-dd HH:mm:ss}");
                }
                catch
                {
                    sb.AppendLine("Start Time: Unable to parse");
                }
            }
            
            sb.AppendLine($"\nRaw Header (first 100 chars):");
            sb.AppendLine(headerLine.Length > 100 ? headerLine.Substring(0, 100) + "..." : headerLine);
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Get histogram data for all DSSD layers
        /// </summary>
        private Dictionary<string, int[]> GetHistogramData()
        {
            var result = new Dictionary<string, int[]>();
            int histogramSize = 16384;
            
            foreach (DetectorLayer layer in Enum.GetValues(typeof(DetectorLayer)))
            {
                // Create histograms for X and Y
                var histX = new int[histogramSize];
                var histY = new int[histogramSize];
                
                foreach (var value in DSSDData[layer].PulseHeightX)
                {
                    int intValue = (int)value;
                    if (intValue >= 0 && intValue < histogramSize)
                        histX[intValue]++;
                }
                
                foreach (var value in DSSDData[layer].PulseHeightY)
                {
                    int intValue = (int)value;
                    if (intValue >= 0 && intValue < histogramSize)
                        histY[intValue]++;
                }
                
                result[$"DSSD{layer}_X"] = histX;
                result[$"DSSD{layer}_Y"] = histY;
            }
            
            return result;
        }

        public void ProcessParticles(string[] hexData)
        {
            for (int i = 0; i < ObservationConstants.ParticlesPerLine; i++)
            {
                var particleData = hexData.Skip(ObservationConstants.HeaderOffset + ObservationConstants.ParticleDataLength * i)
                                          .Take(ObservationConstants.ParticleDataLength).ToArray();

                // Robustness fix using ObservationConstants
                if (particleData.Length < ObservationConstants.ParticleDataLength) break;

                var processedData = ProcessParticleData(particleData, i);
                StorageDataList.Add(processedData);
            }
        }

        public object ProcessParticleData(string[] particleData, int i)
        {
            // Convert hex data to decimal
            var particleDataDec = particleData.Select(hex => Convert.ToInt32(hex, 16)).ToArray();

            // Initialize variables to hold the processed data
            var result = new Dictionary<string, object>();

            if (particleDataDec.Length < 7)
            {
                throw new ArgumentException("Insufficient particle data");
            }

            // Particle time
            var timecodeDec = new int[] { particleDataDec[0], particleDataDec[1] };
            int milliseconds = (timecodeDec[0] << 8) + timecodeDec[1];
            milliseconds /= 1000;

            // --- Process DSSD Layers ---
            // Mapping Logic:
            // L1: Pos Index 2, X-Ph Index 3-4, Y-Ph Index 5-6
            // L2: Pos Index 7, X-Ph Index 8-9, Y-Ph Index 10-11
            // L6: Pos Index 24, X-Ph Index 25-26, Y-Ph Index 27-28
            // L7: Pos Index 29, X-Ph Index 30-31, Y-Ph Index 32-33

            ProcessDSSDLayer(particleDataDec, DetectorLayer.L1, 2, 3, 5);
            ProcessDSSDLayer(particleDataDec, DetectorLayer.L2, 7, 8, 10);
            ProcessDSSDLayer(particleDataDec, DetectorLayer.L6, 24, 25, 27);
            ProcessDSSDLayer(particleDataDec, DetectorLayer.L7, 29, 30, 32);

            // --- Process BGO Layers ---
            // Mapping Logic:
            // L3: H Index 12-13, L Index 14-15
            // L4: H Index 16-17, L Index 18-19
            // L5: H Index 20-21, L Index 22-23

            ProcessBGOLayer(particleDataDec, BGOLayer.L3, 12, 14, result);
            ProcessBGOLayer(particleDataDec, BGOLayer.L4, 16, 18, result);
            ProcessBGOLayer(particleDataDec, BGOLayer.L5, 20, 22, result);

            // Store metadata (Legacy support for result dict keys if needed for UI bind, though we prefer typed access primarily)
            result["ParticleData"] = particleData;
            result["ParticleNumber"] = i + 1;
            result["Milliseconds"] = milliseconds;

            AllResults.Add(result);
            return result;
        }

        private void ProcessDSSDLayer(int[] data, DetectorLayer layer, int posIdx, int xPhIdx, int yPhIdx)
        {
            // Detect Position
            int detectX = (data[posIdx] & 240) >> 4;  // Upper 4 bits
            int detectY = data[posIdx] & 15;          // Lower 4 bits

            // Pulse Heights
            int phX = (data[xPhIdx] << 8) + data[xPhIdx + 1];
            int phY = (data[yPhIdx] << 8) + data[yPhIdx + 1];

            // Add to main lists
            DSSDData[layer].PulseHeightX.Add(phX);
            DSSDData[layer].PulseHeightY.Add(phY);

            // Add to strip lists
            // Note: Strip Index is 0-8. Original code had cases 0-8.
            // If detectX is 0-8, we map it directly.
            // Original code: Case 0 -> Strip1List (Wait, Strip1List is usually index 0?)
            // Let's check logic: case 0: PulseHeightL1X_Strip1List.Add...
            // Wait, List names were Strip1List..Strip8List AND Strip0List.
            // Case 0 -> Strip1 (Index 1?)
            // Case 8 -> Strip0 (Index 0?)
            // I need to replicate this mapping carefully!
            // Looking at original Code:
            // case 0: ...Strip1List
            // case 1: ...Strip2List
            // ...
            // case 7: ...Strip8List
            // case 8: ...Strip0List

            // Refactored Mapping:
            // Strip Dictionary Key: 0-8.
            // If I map key 0 to Strip0List (from case 8), and key 1 to Strip1List (from case 0)...
            // Let's standardize: Key 1..8 match Strip1..8. Key 0 matches Strip0.

            int targetStripX = (detectX == 8) ? 0 : (detectX + 1);
            int targetStripY = (detectY == 8) ? 0 : (detectY + 1);

            // Original: case 0 -> Strip1 | case 1 -> Strip2 ... case 7 -> Strip8 | case 8 -> Strip0
            // My calculation:
            // detectX=0 -> target=1 (Strip1) Correct.
            // detectX=7 -> target=8 (Strip8) Correct.
            // detectX=8 -> target=0 (Strip0) Correct.

            // Add X Strip
            if (DSSDData[layer].StripX.ContainsKey(targetStripX))
                DSSDData[layer].StripX[targetStripX].Add(phX);

            // Add Y Strip
            if (DSSDData[layer].StripY.ContainsKey(targetStripY))
                DSSDData[layer].StripY[targetStripY].Add(phY);
        }

        private void ProcessBGOLayer(int[] data, BGOLayer layer, int hIdx, int lIdx, Dictionary<string, object> result)
        {
            int phH = (data[hIdx] << 8) + data[hIdx + 1];
            int phL = (data[lIdx] << 8) + data[lIdx + 1];

            if (phH < 0 || phL < 0) throw new ArgumentException("Pulse heights must be non-negative.");

            BGOData[layer].HighGain.Add(phH);
            BGOData[layer].LowGain.Add(phL);

            // Populate result dictionary for legacy checks/compatibility
            result[$"PulseHeight BGO {layer} H"] = phH;
            result[$"PulseHeight BGO {layer} L"] = phL;
        }

        public DateTime GetDateTimeFromHexData(string[] hexData)
        {
            var timecodeHex = hexData.Skip(8).Take(6).ToArray();
            var timecodeDec = timecodeHex.Select(h => Convert.ToByte(h, 16)).ToArray();
            var secondsPart = BitConverter.ToUInt32(timecodeDec.Take(4).Reverse().ToArray(), 0);
            var millisecondsPart = BitConverter.ToUInt16(timecodeDec.Skip(4).Take(2).Reverse().ToArray(), 0);

            double totalSeconds = secondsPart + millisecondsPart / 1000.0;
            return DateTimeOffset.FromUnixTimeSeconds((long)totalSeconds).DateTime;
        }

        public string[] SplitHexData(string hexString)
        {
            int n = 2; // Length of each substring
            return Enumerable.Range(0, hexString.Length / n)
                             .Select(i => hexString.Substring(i * n, n))
                             .ToArray();
        }
    }
}
