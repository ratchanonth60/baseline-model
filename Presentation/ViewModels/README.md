# ViewModels โครงสร้าง

โฟลเดอร์นี้ใช้ **partial class** แยกตามหน้าที่ (ไฟล์ operations / commands / data processing / plotting) ให้หาส่วนที่เกี่ยวข้องและดูแลโค้ดง่าย

## โครงสร้างตามโหมด

### Baseline — `MainViewModel`
| ไฟล์ | หน้าที่ |
|------|--------|
| `MainViewModel.cs` | ตัวหลัก: dependencies, properties, channels, events, constructor |
| `MainViewModel.FileOperations.cs` | เลือกไฟล์, รวมไฟล์, โฟลเดอร์ output |
| `MainViewModel.Processing.cs` | Reset, Stop, PreProcessData, ประมวลผล baseline |
| `MainViewModel.Plotting.cs` | InitializeChannels, อัปเดตกราฟ, เลือก layer/direction |

### Observation — `ObservationViewModel`
| ไฟล์ | หน้าที่ |
|------|--------|
| `ObservationViewModel.cs` | ตัวหลัก: primary constructor, properties, SelectFiles, Reset, GetDSSD/BGOLayerData |
| `ObservationViewModel.FileOperations.cs` | LoadFiles, ConvertFilesToExcel, ExportToPathAsync, FilterSegmentsAsync |
| `ObservationViewModel.Commands.cs` | AnalyzeFiles |
| `ObservationViewModel.ExcelProcessing.cs` | ProcessExcelDataAsync, CheckHeaderAsync |

### Calibration — `CalibrationViewModel`
| ไฟล์ | หน้าที่ |
|------|--------|
| `CalibrationViewModel.cs` | ตัวหลัก: constructor, properties, SelectFiles, SelectExcelFiles, Stop, Reset |
| `CalibrationViewModel.Commands.cs` | ProcessData, ReadData |
| `CalibrationViewModel.DataProcessing.cs` | ProcessCalibration, ParseHexPair, ResetDataLists, GetCalibration/VoltageColumns |
| `CalibrationViewModel.Plotting.cs` | UpdatePlotsAsync, UpdatePlots, OpenZoomWindow, partial On*Changed (axis/layer) |

### Flux — `FluxViewModel`
| ไฟล์ | หน้าที่ |
|------|--------|
| `FluxViewModel.cs` | ตัวหลัก: dependencies, constants, properties, Layers, Reset, Stop |
| `FluxViewModel.FileOperations.cs` | SelectFiles, CombineFilesAsync |
| `FluxViewModel.Commands.cs` | ProcessData, ReadData, HeaderCheck |
| `FluxViewModel.DataProcessing.cs` | ProcessFluxObservation, GetDateTimeFromHexData, ProcessHeader, CalculateAndPlotFlux, UpdateAllPlots, ResetDataLists |

## Shared
- **SharedViewModelBase** — Base สำหรับ ViewModels: IsBusy, ProgressValue, StatusMessage, InputFileList, Reset
- **ChannelViewModel** — ใช้ใน Baseline/Calibration สำหรับ channel + histogram
- **FluxLayerViewModel** — ใช้ใน Flux สำหรับแต่ละ layer (L1–L7)

## แนวทาง
- แต่ละ partial เก็บเฉพาะ using ที่ใช้
- ชื่อ constant ใช้ PascalCase (เช่น `LayerCount`, `InitialCapacity`)
- Logic ที่ใช้ร่วมกัน (เช่น ClearChannelPlots, FilterSegmentsAsync) แยกไว้ใน partial ที่เกี่ยวข้อง
