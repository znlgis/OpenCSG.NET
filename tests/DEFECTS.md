# 缺陷登记表

测试过程中发现的缺陷统一登记在此。**产品代码默认只报告不改**；例外仅限 spec 第 9 节批准的最小修复（A1/A2/A4）。

## 登记规则

1. 每个缺陷一个 `DEFECT-nnn` 编号，按发现顺序递增，编号不复用。
2. 对应测试用例必须标注 `[Explicit("DEFECT-nnn: <摘要>")]`，使套件保持绿色、缺陷可一行复现。
3. **双向索引**：`DEFECTS.md` 里每个编号必须能在测试代码里 grep 到；测试代码里每个 `[Explicit("DEFECT-` 必须能在本表找到行。由 `DefectRegistryTest` 强制校验。
4. 最小复现必须给到「seed + 一行命令」或「独立可编译的代码片段」。
5. 修复后：删掉 `[Explicit]` 标注 → 从「未修复」区移到「已修复」区，并注明修复方式。

## 未修复

| ID | 模块 | 严重度 | 现象 | 最小复现 | 期望 vs 实际 | 影响面 | 处置 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| DEFECT-001 | `ViewArcFitter` | 低 | `DiagnosticsEnabled`/`LastDiagnostics` 是进程级静态而非 `[ThreadStatic]`，并行测试会互相串扰（同族开关 `Tessellation.ChordTolerance` 已按线程隔离并有注释记录该问题） | 删掉 `ViewArcFitterDiagnosticsTest.DiagnosticsEnabled_IsNotVisibleOnOtherThreads` 的 `[Explicit]` 标注后，运行 `dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release --filter "FullyQualifiedName~DiagnosticsEnabled_IsNotVisibleOnOtherThreads"`，该用例失败并把「另一线程读到 true」打进断言消息 | 期望：诊断开关是线程局部状态，另一线程读不到；实际：读到 `true` | 并行执行测试时会互相污染诊断输出；单线程串行运行不受影响 | 只报告（Plan C 决定） |

## 已修复

| ID | 模块 | 修复方式 | 回归用例 |
| --- | --- | --- | --- |
| （暂无） | | | |
