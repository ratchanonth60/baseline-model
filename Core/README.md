# Core Layer

เลเยอร์ Core เก็บ **interface**, **model** และ **helper** ที่ไม่ผูกกับ UI หรือการอ่าน/เขียนไฟล์จริง (implementation อยู่ที่ Infrastructure)

## โครงสร้างโฟลเดอร์

```
Core/
├── Interfaces/           # สัญญา (contract) สำหรับบริการต่าง ๆ
│   ├── IFileHelper.cs
│   ├── IFileService.cs
│   ├── ILoggerService.cs
│   ├── IMathService.cs
│   ├── IFittingService.cs
│   ├── IHemgFittingService.cs
│   └── Observation/
│       ├── IObservationDataProcessor.cs
│       └── IObservationExcelHelper.cs
├── Models/              # DTO / โครงข้อมูล
│   ├── Shared/          # ใช้ร่วมหลายโหมด
│   │   ├── AppConstants.cs
│   │   ├── Result.cs, Result<T>.cs
│   │   ├── RegexPatterns.cs
│   │   ├── PlotUpdateEventArgs.cs
│   │   └── HeaderValidationResult.cs
│   ├── Baseline/
│   │   ├── BaselineData.cs
│   │   ├── FittingResult.cs
│   │   └── FittingAlgorithm.cs
│   ├── Observation/
│   │   ├── BGOData.cs, LayerData.cs
│   │   ├── Enums.cs (DetectorLayer, BGOLayer)
│   │   └── ObservationProcessReport.cs
│   └── Flux/
│       └── FluxDataResult.cs
└── Helpers/
    └── ColorHelper.cs   # แปลงสี WPF ↔ System.Drawing
```

## แนวทาง

- **Interfaces:** ประกาศเฉพาะใน Core; โปรเจกต์อื่นอ้างอิง Core ไม่อ้างอิง Infrastructure โดยตรง (ถ้าเป็นไปได้)
- **Models:** แยกตาม feature (Baseline / Observation / Flux); ของร่วมใช้อยู่ใต้ `Models/Shared`
- **Constants:** ค่าคงที่ร่วม (เช่น `AppConstants`, `RegexPatterns`) อยู่ที่ `Models/Shared`
