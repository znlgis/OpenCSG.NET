# OpenCSG.NET 全面测试报告

日期：2026-10-13
Spec：`docs/superpowers/specs/2026-10-13-comprehensive-testing-design.md`

## 阶段 0 — 基线（改动前）

测量环境：Windows 10.0.26300 / .NET SDK 10.0.401 / `-c Release`
说明：仓库无 `global.json`，SDK 未固定，`dotnet` 浮动至本机最新安装版本；本报告测量数据均由上述 SDK 产出，SDK 固定已登记为 Plan F（CI）事项。

### 用例

| 指标 | 值 |
| --- | --- |
| 用例总数 | 102 |
| 通过 | 102 |
| 失败 | 0 |
| Inconclusive | 0 |
| 墙钟耗时 | 约 6 秒（含 NuGet 还原与构建；NUnit 执行 1.691 秒） |

### 覆盖率（改动前）

| 指标 | 值 |
| --- | --- |
| src/OpenCSG.NET 行覆盖率 | 53.51%（cobertura line-rate 0.5351） |
| src/OpenCSG.NET 分支覆盖率 | 50.46%（cobertura branch-rate 0.5046） |

### 模块级行覆盖率（改动前）

| 模块 | 行覆盖率 |
| --- | --- |
| ViewArcFitter | 0.00% |
| ViewProjector | 0.00% |
| ViewProjectorContracts | 0.00% |
| ViewSegmentCleanup | 0.00% |
| ViewMeshTopology | 0.00% |
| ViewMesh | 0.00% |
| ViewGeometry | 0.00% |
| CsgHelpers | 0.00% |
| HalfPlaneCut | 0.00% |
| StlWriter | 0.00% |
| CsgMesh | 0.00% |
| Tessellation | 26.31% |
| PrimitiveConvention | 0.00% |
