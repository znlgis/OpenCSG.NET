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

新增 `tests/OpenCSG.NET.TestKit/MeshAssert.cs`（网格侧断言库）与 `tests/OpenCSG.NET.Tests/MeshIo/CsgMeshTest.cs`（8 个用例）。

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

## 阶段 1 — HLR 管线首次打通

`ViewBridge` 让 `Solid → CsgMesh → ViewProjector → OrthographicViews` 在库内首次可达。

| 视图 | 立方体边长 10 的 bounds | 是否符合解析解 |
| --- | --- | --- |
| 主视 | 10 × 10（X=[−5,5] Y=[−5,5]） | 是 |
| 俯视 | 10 × 10（X=[−5,5] Y=[−5,5]） | 是 |
| 左视 | 10 × 10（X=[−5,5] Y=[−5,5]） | 是 |

剖分与线段：立方体网格 12 个三角形；三视图各输出 4 条线段，全部判定可见（可见 4 / 隐藏 0 / 还原圆弧 0）。
凸体外轮廓的正交视图没有遮挡 —— `ViewProjector` 在候选阶段就丢弃「两面皆背对相机」的边
（`ViewProjector.cs:86-87`），故隐藏段数为 0 是算法设计口径，非丢线。

### 坐标约定链（`Box10x20x30_ViewExtentsFollowAxisMapping`）

CSG 尺寸 (X=10, Y=20, Z=30) → `CsgMesh` 做 Y↔Z 后网格包围盒 X=[−5,5] Y=[−15,15] Z=[−10,10]，
实测三视图与按 `ViewProjectorContracts.cs:88-102` 取轴定义的手推导**逐位相符**（误差 0，容差 1e-9 未动用）：

| 视图 | 取轴（网格坐标） | 期望 W×H | 实测 W×H | 实测 MinX/MaxX/MinY/MaxY |
| --- | --- | --- | --- | --- |
| 主视 | Right=−Y_mesh, Up=+Z_mesh | 30 × 20 | 30 × 20 | −15/15, −10/10 |
| 俯视 | Right=−Y_mesh, Up=+X_mesh | 30 × 10 | 30 × 10 | −15/15, −5/5 |
| 左视 | Right=−X_mesh, Up=+Z_mesh | 10 × 20 | 10 × 20 | −5/5, −10/10 |

三个方向的 `Right × Up == −Look` 不变量均在实测中成立（无镜像）。

- 新增用例：`tests/OpenCSG.NET.Tests/Views/ViewBridgeTest.cs`（7 个用例，全过）。
- 套件结果：115 通过 / 0 失败 / 0 跳过（本次基线 110 + 新增 5）。
- 缺陷：无新增（`tests/DEFECTS.md` 未变更，仍为空表）—— 手推导与实现一致，无「文档 vs 实现」冲突。

## Task 6 — netstandard2.0 消费面（ApiCompat）+ 整解决方案构建

新增 `tests/OpenCSG.NET.ApiCompat`（`netstandard2.0` + `LangVersion 9.0` + `Nullable enable` + `TreatWarningsAsErrors`，仅引用库工程，**不加任何依赖**）—— 仓库首个从 `netstandard2.0` 侧编译的消费方。

### 结论：消费面可用（brief Step 3 的分支 (a)，不是 (b)）

| 检查 | 实测 |
| --- | --- |
| brief 原文探针（未加 polyfill） | `OpenCSG.NET.ApiCompat -> ...\netstandard2.0\OpenCSG.NET.ApiCompat.dll`；**0 警告 0 错误** |
| 补测：消费库 record 的 `init` 访问器（`with` 表达式 + 对象初始化器） | **0 警告 0 错误**（探针内 `TouchRecordInitSurface`） |
| 补测：消费方在自身 netstandard2.0 编译单元内**声明** `record` / `init` 成员 | **CS0518**「预定义类型 System.Runtime.CompilerServices.IsExternalInit 未定义或导入」 |
| 隔离复现：一个**不引用**本库、只声明 `record IsoRecord(double X)` 的 netstandard2.0 工程 | 同样 **CS0518**（2 个错误） |

- 库的公共 API 面（含 `CsgNode` / `Profile2D` 全部 record 的 `init` 访问器）可从 `netstandard2.0` 正常**消费**：编译器从库元数据解析 `modreq(IsExternalInit)` 并完成绑定，消费方无需自备 polyfill；库内那份 polyfill 是 `internal`（`src/OpenCSG.NET/CsgNode.cs:3-7`）也不影响消费。
- CS0518 只在消费方于自身编译单元内**声明** `record` / `init` 成员时出现，且与本库无关（隔离复现已证明）。这是 C# 语言在 netstandard2.0 / .NET Framework 上的既有要求，**不是本库的 API 缺陷**，故**未登记 `DEFECT-nnn`**，`tests/DEFECTS.md` 未变更（仍为空表）。
- 结论措辞：库不需要为消费者补 `public IsExternalInit`；需要自带 polyfill 的是「自己声明 record / init 的 netstandard2.0 消费方」，与是否引用本库无关。

### 整解决方案构建（含 ApiCompat 共 7 个工程）

`dotnet build OpenCSG.NET.slnx -c Release`：7 个工程全部成功，**0 警告 0 错误**。

| 工程 | TFM | 结果 |
| --- | --- | --- |
| `src/OpenCSG.NET` | netstandard2.0 | 成功 |
| `tests/OpenCSG.NET.ApiCompat` | netstandard2.0 | 成功 |
| `tests/OpenCSG.NET.TestKit` | net8.0 | 成功 |
| `tests/OpenCSG.NET.Tests` | net8.0 | 成功 |
| `perf/OpenCSG.NET.PerfTest` | net8.0 | 成功 |
| `samples/Runner.CPurlin` | net8.0 | 成功 |
| `samples/Runner.Examples` | net8.0 | 成功 |

samples 与 perf 编译通过，**无新发现**。注：CI 至今只构建 `src/OpenCSG.NET/OpenCSG.NET.csproj`，但这两者在本地已由 Task 2 的 `dotnet build OpenCSG.NET.slnx`（task-2-report.md §Step 6，0 警告 0 错误）编译过；其后 `src/` 无任何提交，故本轮为对同一 API 面的复现，而非首次编译。

### 套件与结果目录

| 指标 | 值 |
| --- | --- |
| 通过 / 失败 / 跳过 / 总计 | 122 / 0 / 0 / 122（与 122/0/0 基线一致） |
| `tests/OpenCSG.NET.Tests/Results/` | 45 个 `.stl`，目录条目 45，构建与测试后均无残留 |
| ApiCompat 单独构建 | 0 警告 0 错误（Step 4 的 grep 仅命中汇总行「0 个警告 / 0 个错误」） |
