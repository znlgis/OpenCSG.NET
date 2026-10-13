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

## Plan B1 — 属性测试基础设施

### Plan B1 · Task 1：SystemTests 骨架与 FsCheck spike

| 项 | 结果 |
| --- | --- |
| `tests/OpenCSG.NET.SystemTests` | 已建立（net8.0 / NUnit / Nullable enable） |
| FsCheck 版本决策 | 降级 **2.16.6**（可用）。3.4.0 不可用：其要求 NUnit ≥ 4.0.0，与本仓 NUnit 3.13.3 冲突，还原阶段即失败（NU1605）。csproj 现引用 `FsCheck.NUnit` 2.16.6 |
| spike 证据 | 生成 / 失败 / 收缩三者均成立——内层 `Prop.ForAll<int>(v => Math.Abs(v) <= 1)` 的最小反例被收缩到 `2`（多次运行观察到 `Original: -2 → Shrunk: 2`、`Original: 3 → Shrunk: 2`）。3.4.0 的原始报错为 NU1605（见下） |
| 现有套件未受影响 | 122 通过 / 0 失败 / 0 跳过 |
| 解决方案与系统套件 | 含 SystemTests 共 **8** 个工程，0 警告 0 错误；骨架用例 1 通过 / 0 失败 / 0 跳过；`Results/` 仍为 45 个 `.stl`、0 个 `*_.stl` |

**spike 复现命令**（探针用例 `Spike_FsCheckCanGenerateAndShrink` 位于 `SolidSpecPropertyTest`；结论确定后已删除，不留在仓库）

```bash
dotnet add tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj package FsCheck.NUnit --version 3.4.0
dotnet remove tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj package FsCheck.NUnit
dotnet add tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj package FsCheck.NUnit --version 2.16.6
dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release --filter "FullyQualifiedName~Spike_FsCheckCanGenerateAndShrink"
```

**3.4.0 原始输出（逐字）**

```text
D:\self\code\OpenCSG.NET\tests\OpenCSG.NET.SystemTests\OpenCSG.NET.SystemTests.csproj : error NU1605: 错误形式的警告: 检测到包降级: NUnit 从 4.0.0 降级到 3.13.3。直接从项目引用包以选择不同版本。 
D:\self\code\OpenCSG.NET\tests\OpenCSG.NET.SystemTests\OpenCSG.NET.SystemTests.csproj : error NU1605:  OpenCSG.NET.SystemTests -> FsCheck.NUnit 3.4.0 -> NUnit (>= 4.0.0 && < 5.0.0) 
D:\self\code\OpenCSG.NET\tests\OpenCSG.NET.SystemTests\OpenCSG.NET.SystemTests.csproj : error NU1605:  OpenCSG.NET.SystemTests -> NUnit (>= 3.13.3)
  未能还原 D:\self\code\OpenCSG.NET\tests\OpenCSG.NET.SystemTests\OpenCSG.NET.SystemTests.csproj (用时 258 毫秒)。
```

**2.16.6 原始输出（逐字；补上 `using FsCheck;` 后首次可编译运行，即分支 (a)：能生成、能失败、能收缩）**

```text
失败 Spike_FsCheckCanGenerateAndShrink [136 ms]
  错误消息:
   Falsifiable, after 1 test (1 shrink) (StdGen (349168651,297685596)):
Original:
3
Shrunk:
0
with exception:
System.Exception: Falsifiable, after 1 test (1 shrink) (StdGen (349646196,297685596)):
Original:
-2
Shrunk:
2
```

**2.16.6 重复运行节选**（同一用例、不同种子；收缩稳定收敛到 ±2 这一最小反例）

```text
System.Exception: Falsifiable, after 1 test (1 shrink) (StdGen (1043014523,297685596)):
Original:
-2
Shrunk:
2
System.Exception: Falsifiable, after 1 test (1 shrink) (StdGen (1056960782,297685596)):
Original:
-2
Shrunk:
2
System.Exception: Falsifiable, after 2 tests (1 shrink) (StdGen (1085138062,297685596)):
Original:
3
Shrunk:
2
```

**结论与后续任务用法注意**

- 本 spike 的落点是「(b) 3.4.0 不兼容 → 降级 2.16.6 后成立」，既不是 3.4.0 直接可用，也不是「不可用」。Task 7-9 的属性测试**保留生成 + 收缩能力**，无需落入「纯语料驱动、以种子复现」的降级路径。
- 2.16.6 下 `QuickCheckThrowOnFailure` 是 `FsCheck.CheckExtensions` 上的**扩展方法**（命名空间 `FsCheck`），属性测试文件需 `using FsCheck;`——若照抄 3.x 文档且缺该 using，会得到 `CS1061`（本 spike 首次尝试即此报错，补 using 后通过）。`[FsCheck.NUnit.Property(MaxTest = n)]` 在 2.16.6 可用，`MaxTest` / `Replay` / `Verbose` / `Arbitrary` 等属性齐备。
- 骨架用例与后续生成器均不依赖 FsCheck；FsCheck 只提供「生成 + 收缩」。

## Plan B1 · Task 3B — 体积 oracle 的数值口径修正（2026-10-13）

| 项 | 结果 |
| --- | --- |
| `GeometryAssert.SignedVolume` 参考点 | 全局原点 → 包围盒中心 |
| `MeshAssert.SignedVolume` 参考点 | 同上 |
| r=1 球（`SphereOptions{r=1,res=48}`）均匀平移 1e6 的修正前实测值 | 780.3961111704508（网格侧 780.39609481891）；真值（res=48 内接多面体）4.1589714363410595 |
| 修正后同一位移的实测值 | 4.158971436274735（网格侧 4.158971436274734；与真值相对误差 1.6e-11） |
| 对角线位移 (1e6, −2e6, 3e6) 修正前 / 后 | −6089.980642278989 / 4.158971435753264（网格侧 −6089.980601032575 / 4.158971435753263） |
| `LargeCoordinateBooleanTest` / `LargeCoordinateUnionTest` 重跑结果 | 全过（6/6，含 3 条体积断言：差集 0.5、交集 0.5、并集 1.5，修正前后读数逐位相同） |
| 既有大坐标用例的通过是否可信 | 未涉及 oracle 遮蔽：这些断言用的是轴对齐整数坐标立方体，行列式坍缩为（平面坐标）×（平面内双倍面积），最大中间量 ~3e6（非 ~1e18）且逐项精确可表示 ⇒ 原点口径与包围盒中心口径的求和**按构造逐位相同**，「修复前通过」是可靠而非侥幸。故修正后的 oracle **未对它们新增确认**任何东西。库在测绘坐标下对**曲/旋转**形状的正确性改由 `SignedVolume_SurveyTierPrimitives_AreAllPositive`（512 样本）与 Task 6 的语料验证覆盖 |

**补充实测事实（与计划草案预期不符处）**

- **单轴位移不触发灾难性抵消**：平移 (1e6, 0, 0) 与 (0, 1e6, 0) 修正前就读到 4.15897143…（偏差 ~3e-9），因为叉积分量的两个乘积中只有一个达到 ~1e12 量级；灾难性抵消需要**至少两个大分量**（对角位移）才出现。故计划 Step 2 预期的「两个新用例都失败」实测只有 Solid 侧成立：`MeshSignedVolume_IsTranslationInvariant` 用的位移是 `(1e6, 0, 0)`，修正前即通过（修正后仍通过）。网格 oracle 修正的 RED 证据由探针给出（对角位移 −6089.98 → 4.15897）。
- **计划测试首条断言 `4.18879 ± 0.02` 无法成立**（与参考点无关的计划缺陷）：res=48 内接多面体体积实测 4.15897，与 4π/3 相差 0.0298（0.71% 内接亏格），超出 0.02 容差。按最小改动只把容差改为 0.05（期望值 4.18879 与断言消息未动），该断言仅为量级合理性检查。
- **Normal 档无行为变化**：原点处读数修正前后逐位相同（4.1589714363410595），`GeometryAssert` 里包围盒中心的参考点对居中原点实体退化为原点；空实体仍走 `return 0.0` 早退。
- 全量 `OpenCSG.NET.Tests` 129 通过 / 0 失败（基线 126 + 新 3），`OpenCSG.NET.SystemTests` 11 通过 / 0 失败；`Results/` 仍为 45 个 `.stl`、0 个 `*_.stl`；`src/` 零改动。

## Plan B1 · Task 6 — DataGen 列数修正与 `meshEuler = −8` 归因（fix round 1，2026-10-13）

### `metrics.csv` 表头/行字段数不一致（已修复）

`tools/OpenCSG.NET.DataGen --emit` 的表头是 8 列（`index,tier,kind,polygons,volume,meshTriangles,meshEuler,stl`），
但数据行只写 7 个字段（漏 `tier`），按键读列的使用方会整体错位。修正后每行补上 `tier`，并在
`Emit` 末尾加入自检：任一行的字段数与表头不一致时当场 `throw InvalidOperationException`
（负向测试：临时退回 7 字段行，实测抛出
「metrics.csv 列数不一致：表头 8 列，第 2 行 7 列 → 0,Sphere,32,48.95625050356767,48,2,0000-Sphere.stl」）。

| 检查 | 修正前 | 修正后 |
| --- | --- | --- |
| 表头字段数 / 数据行字段数 | 8 / 7 | 8 / 8（513 行全部 8 列） |
| 按表头读「最小体积」 | 8（错位读了 `meshTriangles` 列） | **1.7133669625621211e-06**（行 117，Normal，Cone） |

### 九个 `meshEuler = −8` 的方管 spec：不是缺陷，网格本就不是严格 2-流形

九个 spec（64/72/82/136 Normal，323/396/428/470/471 Survey）全部为 `Extrude` + `SquareTube`，
即 `SolidGen.BuildSquareTube` 的 `outer.Subtract(inner)` 布尔路径；其余 503 条 χ = 2。
一次性探针（`artifacts/probe`，gitignored，未提交、不入库）对 index 64 实测：

| 指标 | 值 |
| --- | --- |
| MeshAssert（网格侧）V / E / F | 24 / 64 / 32 |
| MeshAssert BoundaryEdges / NonManifoldEdges / DuplicatedEdges | **32 / 32 / 0** |
| MeshAssert EulerCharacteristic（V−E+F） | −8 |
| 网格无向边使用直方图 | 32 条被 1 个面使用、32 条被恰好 2 个面使用 |
| GeometryAssert（Solid 侧）SingleUseEdges / TJunctions / SeamEdges | 32 / **16** / 16 |
| Solid 侧面性质 | 16 个面全部为凸且共面的四边形，fanOverlapRatio=1.0，无环形/非凸面 |

- 结论：网格有 32 条边界边（只被一个三角面使用）与 16 处 Solid 侧 T 顶点结构 —— 即 README
  「Numerics」记载的布尔 T 顶点接缝。`V−E+F` 在非流形网格上不是拓扑欧拉特征，`meshEuler = −8`
  是**无意义**而非错误 → **不登记缺陷**（未新增 `DEFECT-003`）。
- index 136 同现象（12 四边形 + 4 个凸五边形且共面，F=36、boundary=44、T-junctions=20/seam=24、χ 仍 −8）；
  所有九个 spec χ 全部为 −8、边界边全部 > 0，随分辨率 4–48 与两档坐标不变。
- `meshEuler` 只在严格流形网格上可解释：该 caveat 已写入 DataGen 的指标注释
  （`tools/OpenCSG.NET.DataGen/Program.cs`，仅注释，corpus.json 保持逐字节相同，
  sha256 `2c3793c3…aff3`）。九个异常 spec 按要求**保留**在 corpus.json 中。

## Plan B1 · Task 7：Tier 1 布尔恒等式

`tests/OpenCSG.NET.TestKit/Oracles/IdentityAssert.cs`（恒等式 oracle，相对容差 1e-9·V）+
`tests/OpenCSG.NET.SystemTests/Property/Tier1BooleanIdentityTest.cs`（10 个方法：8 个语料驱动 + 1 个 FsCheck 驱动 + 1 个空操作数）。
下表为**默认运行**（`dotnet test tests/OpenCSG.NET.SystemTests -c Release`）的口径：豁免用例计跳过不计失败。

| 恒等式 | 用例数 | 通过 | 失败 | 备注 |
| --- | --- | --- | --- | --- |
| 包含排斥 V(A∪B)=V(A)+V(B)−V(A∩B) | 65 | 49 | 0 | 含 1 个 FsCheck 驱动用例（MaxTest=200，通过）；16 个 Survey 种子命中 DEFECT-003 → `[Explicit]` |
| 差集 V(A−B)=V(A)−V(A∩B) | 64 | 64 | 0 | |
| 单调性 | 64 | 64 | 0 | |
| 交换律 | 64 | 64 | 0 | 容差与 IdentityAssert 同口径按体积缩放（实测唯一失败 seed=41 相对偏差 4.8e-11，在 1e-9 预算内） |
| 不修改操作数 | 64 | 64 | 0 | |
| 自反/单位元/零元 | 192 | 109 | 0 | A∪A：19 个 Survey 种子命中 DEFECT-003 → `[Explicit]`；A∩A/A−A 全部通过；空操作数 64 个 → `[Explicit]` DEFECT-002 |
| 结果绕序一致 | 64 | 64 | 0 | 允许 T 顶点接缝 |

发现的缺陷：DEFECT-003（新发现，见 `tests/DEFECTS.md`）—— Survey 档（|坐标| ≈ 1e6）`Solid.Union` 对重合面重复保留或整体丢失，
A∪A 体积偏差 8%–47%，最小片段 `Solids.Cube(10, center: true).RotateY(30).Translate(1e6, 0, 0)` 的自并集体积为 7/6·V(A)；
Normal 档 40 个种子全部通过，A∩A/A−A/差集恒等式全部通过 —— 破坏限定在并集路径。

### Task 7 的一次性归因（探针在 `artifacts/probe`，gitignored，不入库）

- **DEFECT-003（类 (i) 真实缺陷）**：最小片段在平移 1e5 时并集精确、1e6 时损坏（阈值在两档之间）；
  并集输出含**逐位相同**的重复多边形（oracle 无法制造重复面）⇒ 输出数据本身损坏，与体积 oracle 无关。
  语料面：A∪A 失败 19/24 个 Survey 种子（8%–47%），包含排斥失败 16 个种子（2e-9…9.1e-4 相对，其中 14 个交集为空集 ——
  不相交实体对的并集多计/少计体积同样违反恒等式）。Normal 40 个种子全过。
- **类 (ii) 口径修正 I（oracle 参考系）**：FsCheck 收缩反例（`sizeSeed=6 offsetSeed=0 rotationSeed=0 kindSeed=94`）是一对
  bbox 不相交的实体，其并集走「不相交拼接」路径：多边形数据逐位相同（delta ≤ 1e-15），
  但布尔构造件（方管 = outer−inner）带 T 顶点接缝，有向体积读数随参考点线性漂移（实测 A 的读数斜率 −6.7e-6/单位，沿 X 不漂移），
  恒等式两侧各用自身包围盒中心 ⇒ 参考系不一致产生 1.3e-3 的假偏差。修正：`IdentityAssert` 每个恒等式的所有项改用**同一参考点**
  测量（并集/差集自身包围盒中心；`SignedVolumeAt` 与 `GeometryAssert.SignedVolume` 在同一参考点上逐位相同），容差口径未动。
- **类 (ii) 口径修正 II（绝对容差不可达）**：交换律（seed=41_Survey，V≈1.49e9）与自反 A∩A（seed=56_Survey，V≈7.26e6，
  相对偏差 4.2e-12）在绝对 1e-6 下失败 —— 1e-6/1.49e9 = 6.7e-16 低于 double 精度。修正：这两处比较改用
  `max(Tol, 1e-9·V)`（与 IdentityAssert 同口径），相对预算未放松。
- 语料豁免：`CasesWithDefectExclusions` 按 seed 下标对 16 + 19 个具体失败用例打 `TestCaseData.Explicit("DEFECT-003: …")`
  （brief Step 4 允许的唯一豁免形式，理由写在代码注释与 DEFECTS.md）；A∪A 与 A∩A/A−A 拆成两个方法，
  使仍有效的断言不连坐（controller 决定 2(i) 的拆分偏好）。

## Plan B1 · Task 8：Tier 1（续）—— 变换等价 / 包围盒 / 序列化往返 / 相等契约 / 平面分割

`tests/OpenCSG.NET.TestKit/Oracles/TransformAssert.cs`（变换/包围盒 oracle，5 个公共入口）
+ `tests/OpenCSG.NET.SystemTests/Property/Tier1TransformIdentityTest.cs`（9 个方法：5 个语料驱动 × 64 Smoke 种子 = 320 用例，
加 4 个单例 = 324 用例）。下表为默认运行口径。

| 恒等式家族 | 用例数 | 通过 | 失败 | 备注 |
| --- | --- | --- | --- | --- |
| 包围盒代数 bbox(A∪B) = bbox(A)∪bbox(B)、bbox(A∩B) ⊆ bbox(A)∩bbox(B) | 64 | 64 | 0 | **Survey 档全部通过，无需 DEFECT-003 豁免**（见下） |
| 变换等价 V(k·A)=k³V(A) / 旋转 / 镜像（仍朝外）/ 平移 + bbox 精确平移 | 64 | 64 | 0 | 单实体路径，无布尔；Survey 24 个种子全过 |
| 镜像对合 mirror(mirror A) ≅ A | 64 | 64 | 0 | 多边形数/顶点数精确相等，面积/体积/bbox 按 0 容差基准比 |
| 序列化往返 Evaluate(FromJson(ToJson(n))) ≅ Evaluate(n) | 64 | 64 | 0 | NodeGen 树（extent ≈1–10、±10 平移，Normal 量级） |
| 紧凑/缩进 JSON 求值同形 | 64 | 64 | 0 | 同上 |
| `Plane.SplitPolygon` 分区面积守恒 | 1 | 1 | 0 | 见「输入修正」说明 |
| 5 个 Type 码可达且逐码语义正确 | 1 | 1 | 0 | 实测 seen={0,1,2,3,4}；见「输入修正」说明 |
| Vector3D/Vector2D Equals/GetHashCode 契约一致 | 1 | 1 | 0 | 含 ±0.0 组合；无缺陷 |
| Vector3D 拒绝 NaN（3 个分量各一） | 1 | 1 | 0 | `ArgumentOutOfRangeException` 按文档抛出 |

合计 324 用例 / 324 通过 / 0 失败 / 0 豁免（`SystemTests` 全套：826 通过、0 失败、100 个 DEFECT 豁免用例 NotExecuted）。

**与计划预期的偏差（实测事实）**

1. **DEFECT-003 未在包围盒代数行触发，因此没有新增失败 seed、也没有任何逐 case 豁免。** brief 警告块预期
   `bbox(A∪B)`/`bbox(A∩B)` 两行会在 Survey 档撞上 DEFECT-003；实测 64/64 全过。原因（探针 `artifacts/task8probe`，
   gitignored、不入库）：DEFECT-003 的破坏是**面级**的（重合面重复保留或整面丢失），而 bbox 是**顶点集级**的量 ——
   去掉一个面不会去掉撑住极值的顶点（其余相邻面仍带这些顶点）。探针对 24 个 Survey 种子逐对跑 `GeometryAssert.Analyze`：
   18 个种子的并集确有损坏指纹（如 index 41 = 142 条单用边、index 53 = 1158 个多边形），但 24 个种子的
   `bbox(A∪B)` 期望值与 `bbox(A∩B)` 包含关系全部成立。
2. **变换等价族 Survey 全过**（与 brief 预期一致）：`Translate(12.5,−7.25,3.125)` 与沿轴 k=2.5 缩放都不经过布尔路径；
   镜像分支的 `Scale(−1,1,1)` 与 `SolidGen.Materialize` 的镜像路径（Task 3 已验收）一致，体积仍为正。
3. 序列化往返族 64/64 全过（brief 预期一致，未发现 `CsgSerialization`/`CsgEvaluator` 缺陷）。
4. **两个平面分割用例的输入与库的 `SplitPolygon` 契约不符（brief 文本缺陷，不是库缺陷；未登记缺陷）。** 探针原始输出：
   `polygon.Plane → Type=0`、`Plane(x=100) → Type=3`、`Plane(x=−100) → Type=2`、`Plane(x=0) → Type=2`。
   - `PlaneSplitPolygon_PartitionAreasMatch` 里 `polygon.Plane.SplitPolygon(polygon, …)`（局部 `plane` 未使用）⇒
     `this` 等于多边形自身平面 ⇒ 每次都得 Type=0 并落入 `default: Assert.Fail`。改为按注释意图调用 `plane`。
   - 同用例按 `result.Front == null ? 0.0 : …` 取面积，隐含「Type=2/3 会填 Front/Back」；库的契约相反：
     Type=2/3 表示整片落在该侧、`Front`/`Back` 保持 null（`Tree.SplitPolygonByPlane` 的 case 2/3 直接把原多边形入桶），
     只有 Type=4 产出两片。按契约修正取片口径（`front`/`back` 的 6 条断言逐字未动）。
   - `PlaneSplitPolygon_AllFiveTypeCodesAreReachable` 的三条输入与断言错位：正方形占 x∈[0,4]，`Plane(x=+100)`
     整片在后（实际 3，而该变量按「全前」断言 2）、`Plane(x=−100)` 整片在前（实际 2，断言 3）、`Plane(x=0)` 只与一条边相切
     （实际 2，断言 4）⇒ 5 个码里 **4 不可达**。修正：两条单侧平面的偏移镜像互换、跨越平面改用 x=2（严格穿过多边形内部）。
     5 条 `Assert.That(…Type…)` 逐字保留，实测全部成立、5 码全可达 ⇒ 判为 brief 的输入错误而**非**「注释与实现不符」。
   - 三处改动的共同点：只改**输入取值/取片口径**，不改任何期望值、不删任何用例。
5. **`Vector3D`/`Vector2D` 相等契约与 NaN 校验全部通过（无缺陷）。** `Vector3D` 未重写 `GetHashCode`，±0.0 由 `==` 判为相等；
   实测本运行时 `ValueType.GetHashCode` 对 `(−0,0,0)`、`(0,−0,0)`、`(0,0,0)` 返回**同一个**哈希（−1838133773），故契约成立。
   该一致性依赖运行时实现（按字段低 32 位取哈希），非语言保证 —— 仅作观察项，不登记缺陷。

**松弛结论（controller 决定 3 要求）**：`AssertBoundingBoxAlgebra` 逐轴用 `tolerance · max(1,|坐标|)` 缩放（brief 原文，
含 `max(|minA.X|,|maxB.X|)` 这一混取写法）；实测 64 个种子（含 24 个 1e6 量级）**无一失败** ⇒ 判定为松弛足够、未作任何修改。

**遗留问题（不在本任务范围，已按发现上报）**：Task 7 的 `InclusionExclusion_OnFsCheckGeneratedSpecs` 是随机探索用例
（每次运行换 FsCheck 种子，`MaxTest=200`），在本任务的改动**整体移出工程后**复测 8 次仍失败 4 次（≈50%），
反例 `(sizeSeed=13, offsetSeed=10, rotationSeed=0, kindSeed=183)` 与 `(6, 11, 0, 4)`。
归因（探针 `artifacts/task8probe fscheck`）：T 顶点接缝实体的体积读数随参考点线性漂移，并集实测 `dV_U/dref = (0, 1.00e-5, 0)`，
残差 ≈ 裂缝面积(1.0e-5) × 参考点距裂缝距离(~75 单位) ≈ 7.5e-4；同一操作数换序 `B∪A` 无漂移（1e-12 量级），
A、B 与交集自身严格闭合（singleUse=0），并集输出无重复面、无退化面，隐含裂缝宽度 = |g|/总边长 = 6.1e-9，
低于库容差 1e-5 五个数量级。⇒ 判为 **oracle 精度/参考点课题（Task 7 已记录过的 class (ii)）**，不是 DEFECT-003
（Normal 档、无重复面），也**未登记新缺陷**；修复涉及 Task 7 的冻结文件（`Tier1BooleanIdentityTest.cs`），交 controller 决定。
