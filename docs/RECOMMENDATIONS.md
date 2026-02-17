# คำแนะนำการปรับปรุงโปรเจกต์ (Recommendations)

## 1. ค่าคงที่ (Constants) — ลด magic number ✅ ทำแล้ว

- **ทำแล้ว:** เพิ่ม `AppConstants.SegmentHexLength = 4128` และใช้ใน Flux/Calibration แทนตัวเลขตรง ๆ
- Observation ใช้ `AppConstants.PacketHexLength` (512) สำหรับ packet อีกแบบอยู่แล้ว

## 2. HemgFittingService — แก้ compiler warning

- **CS8625 (บรรทัด ~162):** มีการส่ง `null` เข้า parameter ที่ประกาศเป็น non-nullable  
  แก้โดยเปลี่ยน signature ของ parameter นั้นเป็น nullable (เช่น `double[]? residualsOut`) และเช็ค null ข้างในเมธอด
- **CS0219 (บรรทัด ~148):** ตัวแปร `improved` ถูก assign แต่ไม่เคยใช้  
  แก้โดยใช้ตัวแปรนี้ในเงื่อนไข (เช่น early break) หรือลบออกถ้าไม่ต้องการใช้

## 3. async void

- **ObservationViewModel.FileOperations:** `LoadFiles(string[] fileNames)` เป็น `async void`  
  ถ้าเรียกจาก UI event (เช่น ปุ่ม) การใช้ `async void` ยอมได้ แต่ exception จะไม่ถูก return กลับไป  
  **แนะนำ:** เก็บเป็น `async void` สำหรับ event handler ได้ แต่ให้มี try/catch ในเมธอดและแสดงข้อความ/ล็อก error ให้ชัดเจน เพื่อไม่ให้ exception หลุดออกจาก async void

## 4. Logic ที่ซ้ำกัน (DRY)

- การตัด segment จากข้อความ (regex E225, ชิ้นละ 4128 ตัวอักษร) ทำคล้ายกันใน:
  - Flux: `ProcessData` ใน FluxViewModel.Commands
  - Calibration: `ProcessData` ใน CalibrationViewModel.Commands
  - Observation: `FilterSegmentsAsync` (ใช้ `AppConstants.PacketHexLength`)
- **แนะนำ:** ถ้า Flux/Calibration ใช้ segment ขนาด 4128 เหมือนกัน ให้พิจารณาแยก helper ร่วม เช่น ใน `FileHelper` หรือ static helper ใน `Core`  
  ตัวอย่าง: `Task<List<string>> FilterSegmentsByE225Async(string[] fileNames, int segmentHexLength, CancellationToken ct)`  
  แล้วให้ Flux และ Calibration เรียก helper นี้แทนการเขียนลูปซ้ำ

## 5. การยกเลิกงาน (Cancellation)

- ในคำสั่งที่รันนาน (ProcessData, ReadData) มีการใช้ `_cts.Token` อยู่แล้ว ดีแล้ว
- **แนะนำ:** ส่ง `CancellationToken` ลงไปใน helper ที่อ่านไฟล์/ประมวลผลด้วย (เช่น `FilterSegmentsAsync`) เพื่อให้ยกเลิกได้ตั้งแต่ขั้นอ่านไฟล์ ไม่ต้องรอจบทุกไฟล์

## 6. โครงสร้างที่ทำไปแล้ว

- ViewModels แบ่งเป็น partial ตามหน้าที่ (FileOperations, Commands, DataProcessing, Plotting) แล้ว
- มี `Presentation/ViewModels/README.md` อธิบายโครงสร้างแล้ว
- มี `Core/README.md` และ `Infrastructure/README.md` อธิบายโครงสร้าง Core กับ Infrastructure แล้ว

## 7. Core และ Infrastructure

- **Core:** เก็บเฉพาะ Interfaces, Models (Shared / Baseline / Observation / Flux), Helpers (เช่น ColorHelper). ไม่มี implementation ทำงานกับไฟล์/ฮาร์ดแวร์จริง — ดูรายละเอียดใน `Core/README.md`
- **Infrastructure:** เป็น implementation ของ interface ใน Core (FileHelper, MathService, HemgFittingService, ObservationDataProcessor ฯลฯ) — ดูตารางจับคู่ใน `Infrastructure/README.md`
- **แนะนำ (ถ้าต้องการจัดโฟลเดอร์ให้ชัด):** ย้าย `KalmanFilter.cs` จาก `Infrastructure/Services/` ไป `Infrastructure/Services/Observation/` เพราะใช้เฉพาะใน Observation

---

สรุปสั้น ๆ: (1) Constant 4128 ทำแล้ว (2) แก้ warning ใน HemgFittingService (3) ดึง logic ตัด segment ร่วมกันถ้าต้องการลดการซ้ำ (4) Core/Infrastructure มี README แยกแล้ว
