# Infrastructure Layer

เลเยอร์ Infrastructure เป็น **implementation จริง** ของ interface ใน Core เช่น ทำงานกับไฟล์, Excel, คำนวณ, fitting

## การจับคู่ Interface (Core) ↔ Implementation (Infrastructure)

| Core Interface | Infrastructure Implementation |
|----------------|-------------------------------|
| `IFileHelper` | `FileHelper` |
| `IFileService` | `BaselineFileService` (Baseline) |
| `ILoggerService` | `LoggerService` |
| `IMathService` | `MathService` |
| `IFittingService` | `MathService` (implement ทั้ง IMathService และ IFittingService) |
| `IHemgFittingService` | `HemgFittingService` |
| `IObservationDataProcessor` | `ObservationDataProcessor` (Observation) |
| `IObservationExcelHelper` | `ObservationExcelHelper` (Observation) |

## โครงสร้างโฟลเดอร์

```
Infrastructure/
├── Services/
│   ├── FileHelper.cs           # รวมไฟล์, บันทึก Excel (ใช้ร่วมหลายโหมด)
│   ├── LoggerService.cs
│   ├── MathService.cs         # คำนวณ + Fitting (Gaussian, HEMG, Lorentzian)
│   ├── HemgFittingService.cs  # HEMG double-sided fit
│   ├── MessageBoxService.cs   # แสดงกล่องข้อความ (static)
│   ├── HeaderValidator.cs
│   ├── KalmanFilter.cs        # ใช้โดย Observation (อาจย้ายไป Observation/)
│   ├── Baseline/
│   │   └── BaselineFileService.cs
│   └── Observation/
│       ├── DataProcessor.cs   # แยก hex, ProcessParticles, ProcessFilesAsync
│       └── ExcelHelper.cs     # ObservationExcelHelper
```

## แนวทาง

- แต่ละ service รับ dependency ผ่าน constructor (เช่น `ILoggerService`) เพื่อให้ทดสอบและสลับ implementation ได้
- โฟลเดอร์ย่อย `Baseline/` และ `Observation/` ใช้จัด service ที่เฉพาะโหมดนั้น
- `KalmanFilter` ใช้เฉพาะใน Observation ถ้าต้องการจัดให้ชัดขึ้นอาจย้ายไป `Services/Observation/KalmanFilter.cs`
