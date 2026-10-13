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

## Task 3 — MeshAssert + CsgMesh 反射补偿回归（2026-10-13）

新增 `tests/OpenCSG.NET.TestKit/MeshAssert.cs`（网格侧断言库）与 `tests/OpenCSG.NET.Tests/MeshIo/CsgMeshTest.cs`（6 个用例）。

| 用例 | 结论 |
| --- | --- |
| Cube100_FromCsg_HasPositiveSignedVolume | 有向体积 +1,000,000（绕序补偿正确，法线朝外） |
| Cube100_FromCsg_SwapsYAndZ | 网格尺寸 (10, 30, 20)，确证 Y↔Z 交换 |
| Cube100_FromCsg_AreaIsPreserved | 60,000（镜像不改变面积） |
| Cube100_FromCsg_IsClosedOrientedManifold | 封闭定向流形：边界边 / 非流形边 / 同向重复边均为 0 |
| TwoSolids_Overload_TriangleCountIsSum | 多实体重载三角形数 = 各实体之和 |
| Cube100_GridArea_MatchesSolidPolygonArea | Solid 侧手算面积与网格侧面积一致（1e-9） |

- `CsgMesh.cs:49-51` 的声称（100mm 立方体：有向体积 +1,000,000 → 仅交换后 −1,000,000 → 反转绕序后回到 +1,000,000）**首次由测试验证为真**，STL 导出绕序方向正确。
- 套件结果：108 通过 / 0 失败 / 0 跳过（基线 102 + 新增 6）。
- 缺陷：无新增（`tests/DEFECTS.md` 未变更，仍为空表）。
