# OpenCSG.NET 全面测试体系设计

日期：2026-10-13
状态：已批准（2026-10-13 对话确认）

## 1. 目标

为 OpenCSG.NET 建立可长期运行的全面测试体系，覆盖当前 0% 的 HLR/网格 IO/建模助手/自适应细分模块，引入程序化生成的随机测试数据与精确代数 oracle，并把结果接入 CI 与覆盖率门禁。

### 1.1 已确认的范围约束

| 项 | 决定 |
| --- | --- |
| 深度 | 全量测试金字塔：单元 + 契约/边界 + 属性/模糊 + golden + 集成端到端 + 性能 + 稳健性/并行 + 覆盖率 + CI |
| 「真实测试数据」 | 程序化生成的随机实体（种子可复现）。**不引入外部 STL 语料、不做 SVG/PNG 渲染、不做参数化工程模型库**（现有 `Runner.CPurlin` 几何作为固定真实案例复用） |
| 新增依赖 | 仅测试侧：`FsCheck.NUnit` + `coverlet.collector`。`src/` 保持 `netstandard2.0` + 仅 `System.Text.Json` |
| 产品代码 | 默认**只报告不改**。**例外（已批准）**：A1/A2/A4 三条「按文档/公开 API 正常使用即得到错误结果」的缺陷允许最小修复（各 ≤5 行）+ 回归测试 |
| 覆盖率工具 | `coverlet.collector`（本地缓存已有 6.0.4 / 8.0.1 / 10.0.1） |
| FsCheck | `FsCheck.NUnit` 3.4.0（nuget.org 已确认可达）。阶段 2 先做兼容性 spike，不通则降级 2.16.x，再不通则用手写种子化生成器（TestKit 接口不变） |

### 1.2 明确不做（YAGNI）

- 不写 DWG/DXF/OBJ/3MF 导出器（库内本就没有）
- 不改 `src/` 的坐标口径、容差、算法
- 不改现有 102 个用例的断言逻辑，不改 `SolidTest` 基类的比较逻辑
- 不做 mutation testing（Stryker 对 netstandard2.0 + 几何代码收益低、耗时长）
- 不做外部模型下载、不做可视化渲染产物
- 不做 ARM64 / 多架构验证

## 2. 现状基线

### 2.1 覆盖缺口

| 子系统 | 公共类型 | 有测试引用 | 覆盖 |
| --- | --- | --- | --- |
| HLR / 三视图（7 文件，约 2100 行） | 23 | 0 | 0% |
| `CsgHelpers`（16 方法）/ `HalfPlaneCut` | 2 | 0 | 0% |
| 网格 IO：`StlWriter` / `CsgMesh` / `MeshTriangle` / `MeshPoint` | 5 | 0 | 0% |
| `Tessellation` / `PrimitiveConvention` | 2 | 0 | 0% |
| `Matrix4x4` / `Vector2D` / `BoundingBox` / `BoundingSphere` / `Polygon.Flipped` / `Vertex.Transform` | 6 | 0（仅间接） | 浅 |
| CSG 建模助手链（`CsgHelpers` 全链） | 2 | 0 | 0% |

现有 102 个 `[Test]`，全部无参、无 `[TestCaseSource]`、无 `[SetUp]`/`[TearDown]`、无并行标注。

### 2.2 现有 golden 机制（`tests/OpenCSG.NET.Tests/SolidTest.cs`）

- `AssertAcceptedStl(csg, fixtureName)` + `[CallerMemberName]` ⇒ 路径 `Results/{fixture}.{test}.stl`
- 语义比对 6 项：头字符串（`Ordinal`，实际锁定测试方法名）、三角面数、总面积、有向体积、体积符号、bbox（绝对 1e-10）
- 行尾无关：`ParseTriangles` 按 `{'\r','\n'}` 切分，`GetStlHeader().Trim()`
- **accept 文件不存在 ⇒ 写 `*_.stl` + `Assert.Inconclusive`**（不是失败）
- 升级靠手工重命名；无 `--accept` 开关；`Results/` 与 `*_.stl` 均未被 gitignore（45 个 `.stl` 已入库）

### 2.3 CI / perf / samples

- `ci.yml`：ubuntu + .NET `8.0.x`，只跑 `build src/OpenCSG.NET` + `test tests/OpenCSG.NET.Tests`。**不编译 samples/perf，无覆盖率，无矩阵**
- `nuget-publish.yml`：`v*` tag 触发，复用同一测试套件作为发布门禁
- perf：仅 `sphere − sphere` 两项基准（`Resolution10` / `Resolution50`），无基线文件、无阈值，CI 不跑
- samples：两个 console app，末尾 `Process.Start(..., UseShellExecute = true)`（`:158` / `:102`）⇒ 无头环境会抛 `Win32Exception`；且 samples 从不被 CI 编译

### 2.4 确定性盘点

`src/` 内无 `new Random` / `Guid` / `DateTime`。需要隔离的全局态：

| 状态 | 位置 | 隔离性 |
| --- | --- | --- |
| `Tessellation._chordTolerance` | `Tessellation.cs:30` | `[ThreadStatic]`，注释自述「曾因全局静态被并行测试串扰」 |
| `ViewArcFitter.DiagnosticsEnabled` / `LastDiagnostics` | `ViewArcFitter.cs:129,132` | **非** `[ThreadStatic]`，并行下串扰 |
| `Solid.GetTag()` | `Solid.cs:839` | 进程级单调计数器，值随执行顺序变化（仅用于身份分组，不用于排序） |

## 3. 已确认的缺陷线索（读码确认，进入阶段 2/3 优先复现）

| ID | 位置 | 现象 | 严重度 | 处置 |
| --- | --- | --- | --- | --- |
| A1 | `Solid.cs:19,31,344-367,371-376` | `Polygons` 为公共可变 `List<Polygon>`；`cachedBoundingBox` 惰性缓存且**无失效机制**；`FromPolygons` **别名持有**调用方 list。缓存过期后 `MayOverlap` 可能错误判定「不相交」而跳过裁剪。`Polygon.Vertices`/`BoundingSphere`/`BoundingBox` 同模式 | 严重 | 最小修复 + 回归测试 |
| A2 | `Solids.cs:136-137,164,184` | `SectorAngle` 只处理 `>360`：`-90` 与 `720 % 360 = 0` ⇒ `alpha > 0` 为假 ⇒ **静默返回零多边形空实体**（720° 本该是完整圆柱） | 高 | 最小修复 + 回归测试 |
| A3 | `Solids.cs:133-134` | `RadiusStart/End` 被 `Math.Abs` 静默取绝对值，而 `EvaluateCone` 对负半径抛异常 —— 同一概念两条相反契约 | 中 | 只报告 |
| A4 | `StlWriter.cs:78-81` | 默认二进制头 `"调用方 STL export"` 经 `Encoding.ASCII` ⇒ **中文变 `?`，默认头是乱码**；头未做长度/换行净化（79/80/81 边界、含 `\n` 破坏 ASCII STL 结构） | 高 | 最小修复 + 回归测试 |
| A5 | `StlWriter.cs:152` | `WriteAsciiPoint` 用 `{0:0.######}` ⇒ 坐标 < 1e-6 塌陷为 `0`，无科学计数法；与 `Formats.ToStlString`（全精度）口径不一致 | 中 | 只报告 |
| A6 | `ViewProjectorContracts.cs:32,47,50,55,63,80` | 残留宿主项目引用（`CadCsgEvaluator`/`ViewExportBridge`/`CrateOrientation`/`znlgis/GStarCad.Net.Demo` `OrthoProjector`）；引用不存在于本仓库的 `ViewExportTests`；`:64` 乱码日期 `202某分图0-09`。与 commit `be61639` 声明冲突 | 中 | 只报告 + 加防回归 fitness 测试 |
| A7 | `ViewProjectorContracts.cs:11-19` | `ViewDirection` ctor 不校验自己文档宣称的不变量 `Right × Up == −Look`（三个内置方向实测均满足） | 中 | 只报告 |
| A8 | `Tessellation.cs:54,63` | 文档称分段数「夹在 [MinSegments, MaxSegments]」= [16,128]，legacy 分支却返回 `DefaultResolution3D` = 12；`SegmentsFor` legacy=12、`SegmentsForArc` legacy=16，口径不一 | 低 | 只报告 |

`ViewDirections` 三视图硬编码宿主装箱朝向（汽机侧/电机侧/人孔侧）—— 库自称宿主无关，记入报告（设计层面）。

## 4. 架构：三项目分层 + 共享 TestKit

```
tests/
  OpenCSG.NET.TestKit/          net8.0 classlib；引用核心库 + NUnit（见 D4），不引 FsCheck
                                （Plan F 阶段与 Tests 一同加 net10.0 目标）
    Generation/   SolidSpec.cs  SolidGen.cs  NodeSpec.cs  NodeGen.cs
                  ViewSegGen.cs  TriangleGen.cs  Corpus.cs
    Oracles/      MeshAssert.cs  IdentityAssert.cs  HlrAssert.cs
                  ConvexHull.cs  RayCast.cs
    Bridge/       ViewBridge.cs            ← 库缺失的 Solid→ViewTriangle 适配器
    Io/           StlRoundTrip.cs  GoldenResult.cs  ViewGoldenAssert.cs
    GeometryAssert.cs                      ← 从 Tests 迁入，internal→public
  OpenCSG.NET.Tests/
    GoldenGuard.cs                         ← [SetUpFixture]，残留 *_.stl 即失败
                                             （必须在本程序集：NUnit 不扫描被引用程序集的 SetUpFixture）
    TestData/corpus.json       ← 提交：种子 + spec + 期望指标
    Results/                   ← 现有 45 个 golden + 新增 golden
  OpenCSG.NET.SystemTests/      属性 / 模糊 / 集成 / 稳健 / 并发 / 长时
    Property/  Fuzz/  Integration/  Robustness/  Concurrency/
  OpenCSG.NET.ApiCompat/        引用库、TargetFramework=netstandard2.0 的编译期消费工程
tools/
  OpenCSG.NET.DataGen/          console：物化数据集到 artifacts/dataset/（gitignored）
  promote-goldens.sh            把 Results/*_.stl 重命名为 *.stl
perf/OpenCSG.NET.PerfTest/      扩展基准 + 提交基线 JSON
```

### 4.1 关键设计决策

**D1 — `GeometryAssert` 迁入 TestKit 时保持命名空间 `Csg.Test`、仅 `internal`→`public`。**
现有 19 个测试文件零改动即可继续使用（它们本就在 `Csg.Test` 命名空间）。只新增一个 `ProjectReference`。不改其比较逻辑。

**D2 — `SolidTest` 基类不移动、不改比较逻辑。**
其 `Assembly.GetCallingAssembly()` + 4 层 `Path.GetDirectoryName` 的路径推导在迁移后会失效。它留在 Tests 项目内，15 行 `[SetUpFixture]` 门禁以外挂方式补强制力。

**D3 — 生成器采用 Spec-then-materialize。**
FsCheck 生成的是**参数 record**（`SolidSpec`/`NodeSpec`），不是 `Solid`。这使 FsCheck 的 shrink 产出「人能读懂、能一行复现的最小参数反例」而非无法解释的随机网格。失败输出 `seed + spec` 的 JSON。

**D4 — TestKit 依赖 NUnit，但不依赖 FsCheck。**
规划时发现 `GeometryAssert` 的断言方法直接调用 `NUnit.Framework.Assert`，而它要迁入 TestKit。强行剥离会割裂「分析 API」与「断言 API」并让 SystemTests 无处复用。折中：TestKit 引 NUnit（两个消费方本就是 NUnit 工程），但**不引 FsCheck** —— 生成器与 oracle 的纯逻辑部分不触碰 NUnit，`tools/OpenCSG.NET.DataGen` 只使用纯逻辑部分。

**D5 — 缺陷登记制。**
`tests/DEFECTS.md` 一张表（ID / 模块 / 严重度 / 最小复现 seed+命令 / 期望 vs 实际 / 影响面）。对应禁用例标注 `[Explicit("DEFECT-00x: ...")]`。**双向索引由一条元测试保证**：grep `src` 与 md 交叉校验，不漂移。修复后删 `[Explicit]` 即自动转绿。

**D6 — `Runner.CPurlin` 模型复用需要一次样例侧抽取。**
sample 是顶层语句 console app，几何代码无法被测试项目引用。L5 要把真实模型跑进管线，需把模型构建从 `samples/Runner.CPurlin/Program.cs` 抽到一个 `public static class CpurlinModel`（新文件，同目录），`Program.cs` 改为调用它，`SystemTests` 加对 `Runner.CPurlin` 的 `ProjectReference`。
这是**样例代码改动，不是产品代码**，且与已批准的 `Process.Start` 环境守卫属同一次样例改动。抽取必须保持几何逐位不变（用抽取前后导出的 STL 语义比对验证）。

## 5. 测试数据生成体系

### 5.1 采样档位

| 档 | 范围 | 理由 |
| --- | --- | --- |
| 尺寸 | log-uniform `[1e-2, 1e3]` | 覆盖容差敏感的小尺寸与常规工程尺寸 |
| 大坐标 | 整体偏移 `±1e6` | 内核合并容差是**绝对** 1e-5，大坐标是历史 bug 高发区（`Solid.cs:929-940` int→long 修复） |
| 分辨率 | `{4, 8, 12, 24, 48}` | 4 是 `Sphere` 下限钳制点 |
| 扇形角 | `{0, 90, 180, 360, 720, -90}` | `Solids.cs:184-189` 部分扇形分支 0 覆盖；720/-90 是 A2 |
| 变换 | 刚体 / 均匀缩放 / **负行列式镜像** / 非均匀缩放 | 镜像触发绕序与法线翻转 |
| 退化 | 零尺寸、共面面、自交 `PolygonProfile`、重复点、共线点、空列表 | 打 `Triangulate` 的 ear-clipping 失败路径 |

### 5.2 语料三档

| 档 | 规模 | 触发 | 用途 |
| --- | --- | --- | --- |
| Smoke | 64 固定种子 | 每次 `dotnet test` | 秒级回归 |
| Corpus | 512 固定种子 | CI | `TestData/corpus.json` 提交种子+spec+期望指标；`DataGen` 可物化为 STL |
| Long | `OPENCSG_FUZZ_CASES`（默认 5000） | nightly | 深度模糊 |

### 5.3 生成器

| 类型 | 产出 |
| --- | --- |
| `SolidGen` | 随机 `Solid`：基本体（含 `CylinderOptions` 扇形、`SphereOptions` 定向轴）+ 随机布尔组合 + 变换 |
| `NodeGen` | 随机 `CsgNode` 树（深度 ≤4，布尔元数 1..4），覆盖全部 8 种 `Profile2D` 与 10 种节点 |
| `ViewSegGen` | 随机 2D 线段集：链、圆采样弧、重叠共线段、短残段、混合可见性 |
| `TriangleGen` | 随机封闭三角网格；或经 `CsgMesh.TrianglesOf(Solid)` 取得 |

## 6. Oracle 分层

### Tier 1 — 精确代数恒等式（任意随机输入成立，无需 golden）

- `V(A∪B) + V(A∩B) = V(A) + V(B)`
- `V(A−B) = V(A) − V(A∩B)`
- 有向体积 `> 0`（法线朝外）
- `A∪∅ = A`、`A−∅ = A`、`A∩A = A`、`A−A = ∅`、`A∪A = A`
- `V(A∪B) = V(B∪A)`、`V(A∩B) = V(B∩A)`
- `bbox(A∪B) = bbox(A) ∪ bbox(B)`、`bbox(A∩B) ⊆ bbox(A) ∩ bbox(B)`
- `V(k·A) = k³V(A)`、`V(rotate A) = V(A)`、`V(mirror A) = V(A)` 且仍朝外、`V(translate A) = V(A)` 且 bbox 精确平移
- `mirror(mirror A) ≅ A`
- `Evaluate(FromJson(ToJson(n))) ≅ Evaluate(n)`
- `Plane.SplitPolygon`：任意多边形 P 与任意平面 Q，`area(P) = area(front) + area(back)`（或 coplanar 分支）；5 个 `Type` 码各自可达
- 布尔运算**不修改输入**（深度快照对比）
- `Vector3D`/`Vector2D` 的 `Equals`/`GetHashCode` 契约一致

### Tier 2 — 解析解对照（有闭式答案）

- 轴对齐立方体主视图 = 恰好 4 条线段构成边长 `s` 的正方形
- 球体任意方向轮廓 = 半径 `r` 的完整圆（`IsClosed && SweepDegrees == 360`）
- 圆锥侧影 = 等腰梯形；圆柱俯视 = 圆
- **凸包周长守恒**：对凸实体，可见线段总长 = 投影顶点集 2D 凸包周长（`ConvexHull.cs` 独立实现）
- `SegmentsFor(r, tol)` ≈ 解析值 `ceil(2π / (2·acos(1 − tol/r)))`
- `ViewArcFitter` 从已知圆采样点串 ⇒ 恢复圆心/半径误差 ≤ 容差
- 不相交两实体 ⇒ 全部线段可见；小实体完全在大实体内部 ⇒ 小实体全隐藏、大实体全可见

### Tier 3 — 结构 / 单调 / 幂等

- `ViewProjection.VisibleCount` == 独立遍历 `Segments` 数出的 `IsVisible` 条数
- 原拟 oracle「`VisibleCount` 与 `HiddenCount` 之和 == `Segments.Count`」**舍弃**：`HiddenCount` 定义为 `Segments.Count - VisibleCount`（`ViewProjectorContracts.cs:197`），该等式是同义反复，永远不会失败
- `Bounds` 包含所有线段端点；`ViewBounds2.Of(∅) == Empty`
- `ViewSegmentCleanup.Clean`：只延长不缩短、不跨可见性合并、不丢覆盖（输入线段中点被输出覆盖）
- `ViewArcFitter.MergeConcyclic` 幂等；`Fit` 的 arcs + 残余 segments 联合覆盖输入，偏差 ≤ tolerance
- `ViewMeshTopology`：封闭流形无边界边、`V − E + F = 2`、焊接幂等
- `SegmentsFor` 对 tol 单调不增、对 radius 单调不减
- `StlWriter`：二进制大小 `= 84 + 50·n`；ASCII 与二进制解析结果一致

### Tier 4 — Golden 语义基线

沿用现有 6 项语义比对（STL）；HLR 新增 `Results/Views/{fixture}.{test}.json`，记录线段/弧数量 + 总长 + bounds，按数值解析后比对（不做逐字节，避免行尾/文化差异）。

### Tier 5 — 射线投射语义真值

`RayCast.cs` 提供点-在-体内判定（独立实现，不调库内代码）。用于凸体可见性真值：体内点对应处必须有可见线；体外点不得出现。

## 7. 分层测试计划

| 层 | 内容 | 用例数(估) |
| --- | --- | --- |
| L1 单元 | 补 0% 模块：`View*`(23 类型)、`CsgHelpers`(16 方法)、`HalfPlaneCut`、`StlWriter`、`CsgMesh`、`Tessellation`、`PrimitiveConvention`、`Matrix4x4`(含从未测过的矩阵×矩阵复合)、`Vector2D`、`BoundingBox`、`Polygon.Flipped`、`Vertex.Transform`、`Plane.SplitPolygon` 的 5 个 Type 码、`CubeOptions`/`SphereOptions`(含定向球) | ~140 |
| L2 契约与边界 | 全部已记录 throw 路径（当前 0 覆盖）：`Vector3D` NaN、`Matrix4x4` <16/null、`Plane` 共线、`CsgEvaluator` 6 条、`HalfPlaneCut` 2 条、`RevolvedStack` 4 条、`ValidateProbeFactor` 3 条、`ViewTriangle` 索引越界、6 处 null 校验、`StlWriter` 空路径。退化：`1e-5` 容差临界对、`1e12` 尺度、±0、扇形角 0/720/−90、超深 JSON（**StackOverflow 不可捕获 ⇒ `[Explicit]` 或独立进程**）。fitter 5 个门限 ±ε：`MinRadius 0.5`/`MaxRadiusToExtent 5.0`/`MinSweep 10°`/`MaxClosingGap 25°`/`SameCircleRel 1e-3`。`StlWriter` 头注入：空/79/80/81 字节/含 `\n`/非 ASCII | ~80 |
| L3 属性/模糊 | FsCheck 驱动 Tier 1–3、5 全部 oracle，跑 Smoke + Corpus 两档，`[Category("Slow")]` 标注并设时间预算 | ~45 `[Property]`，展开数千 case |
| L4 Golden | 新模块语义基线 + HLR JSON 基线 | ~30 |
| L5 集成端到端 | 完整管线 `CsgNode → Evaluate → Solid → CsgMesh → ViewProjector.Project → OrthographicViews`；STL 闭环（`Solid → 文件 → ViewMeshReader → ViewProjector`，打通库内唯一读入器与两个写出器）；JSON 闭环；凸包周长 oracle；真实模型 `Runner.CPurlin` 几何（断言前视图 bounds 出现 web 152 / flange 64 / height 1000，冲孔位置正确） | ~20 |
| L6 性能与复杂度 | 补 `Union`/`Intersect`、深树 `Evaluate`、`ToJson`/`FromJson`、ASCII+二进制 STL 写、`CsgMesh.TrianglesOf`、`ViewProjector.Project`、`ViewArcFitter.Fit`、`ViewSegmentCleanup.Clean`。提交基线 JSON，25% 回归门禁（nightly）。复杂度标度：`Clean` 的 O(n²)（`ViewSegmentCleanup.cs:59-76,97-146`）与 `DepthRaster` 900² 栅格在 n=5k/20k 的耗时预算。薄板 z-fighting（厚度/尺度比低于栅格量化）；`MaxEdgeSamples=4000` 超采样边界（1e6 长圆柱） | ~14 基准 + 6 边界 |
| L7 稳健性 | `Tessellation.ChordTolerance` 用 `IDisposable` scope 保证复位 + 跨线程不串扰；`ViewArcFitter.DiagnosticsEnabled`/`LastDiagnostics` 非 ThreadStatic ⇒ 预期上报缺陷；`Solid.GetTag()` 无用例依赖 tag 值；**并行跑整套件**（现套件从未并行验证）；跨运行时确定性（`net8.0` vs `net10.0` 输出哈希对比） | ~18 |

合计约 **330–360 个新用例**（现有 102），加数千模糊 case。SystemTests 目标墙钟 ≤ 8 分钟（Smoke+Corpus），Long 档 ≤ 40 分钟。

## 8. 覆盖率与 CI

### 8.1 覆盖率

`coverlet.collector` + `--collect:"XPlat Code Coverage"` + reportgenerator ⇒ HTML/cobertura。`.runsettings` 阈值：

- `src/` 整体行覆盖 ≥ 85%
- `View*` / `CsgHelpers` / `StlWriter` / `CsgMesh` / `Tessellation` ≥ 90%

交付前后对比数字必须来自实际 CI 输出，不得估算。

### 8.2 CI 改动（已批准）

1. `ci.yml`：`setup-dotnet` `8.0.x` → `9.0.x`，并把 `dotnet build OpenCSG.NET.slnx` 加入 —— 现有 CI **从不编译 samples/perf**，公共 API 破坏不会被发现
   > ✅ 已落地（Plan F，2026-10-14）：ci.yml 全构建步骤 = `dotnet build OpenCSG.NET.slnx -c Release`（10 工程（13 个产物行；Tests/SystemTests/CrossRuntime 各双 TFM）全被编译：src/ApiCompat/samples/perf/tools；本地等价实测 0 警告 0 错误）。`setup-dotnet` 多行 `10.0.x` + `8.0.x` —— 本条的 `9.0.x` 与条目 3（net10 TFM）冲突，裁定统一 **10.0.x**（见条目 7）。
2. 矩阵加 `windows-latest`（golden 机制依赖 `Environment.NewLine` 与 4 层路径推导，只在 ubuntu 验证过）
   > ✅ 已落地：`os: [ubuntu-latest, windows-latest]`（`fail-fast: false`；注释含「某平台因换行机制转红 = 真发现，先调查取证并登记，不得静默删平台」纪律）。GitHub Actions 矩阵本体不可本地运行——两平台 golden 机制的本地等价验证（含 CRLF/LF 判别力探针）与首跑观察项见 TEST-REPORT「Plan F」节与「已知限制」⑥。
3. 测试项目多目标 `net8.0;net10.0`
   > ✅ 已落地：两测试项目 `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>`；两 TFM 各跑全量，终验 Tests 345/0、SystemTests 1109/0 两边逐位相同（TRX 亦逐位相同）；`-f net8.0` 本地快速档已写入工程注释。
4. 新增 nightly workflow：Long 档模糊 + perf 基线对比（非阻塞 PR）
   > ✅ 已落地：`nightly.yml`（cron 18:30 UTC + `workflow_dispatch`）：Long 档 `OPENCSG_FUZZ_CASES=20000` + perf 门禁（`--gate perf/baseline.json`，windows-latest——基线录制机为 Windows）；独立工作流 ⇒ 非阻塞 PR。如实登记：Long 档 `Corpus.LongCaseCount()` 入口就绪且 env 覆盖经用例验证，但当前无消费用例（该步现阶段 ≈ 全量 SystemTests 复跑）。
5. 覆盖率收集 + 阈值门禁 + 报告上传
   > ✅ 已落地：`tests/coverage.runsettings` + `tools/coverage-gate.py`（判定口径 = 两份 cobertura 逐行并集、行计数加权；阈值 src≥85%、五组≥90%；fail-closed 退出码 0/1/2）+ cobertura 工件上传。官方对照数字（src 98.43%、`View*` 98.25%、CsgHelpers/StlWriter/CsgMesh/Tessellation 100%）已入 TEST-REPORT；判别力自证（阈值 0.999 ⇒ exit 1）与截断报告 fail-closed（exit 2）实测。数字为本地运行（命令与 CI 逐字相同），CI 首跑以 artifacts 为准。
6. 编译 `tests/OpenCSG.NET.ApiCompat`（netstandard2.0 消费面）
   > ✅ 已落地：由条目 1 的 slnx 全构建覆盖（ApiCompat 为 10 工程之一，编译型金丝雀）；五类命名缺口（`BoundingBox`/`BoundingSphere`/`PolygonShared`/`CsgEvaluationException`/`ViewVector`）+ `CsgSerialization` 消费侧两层钉已补齐。遗留登记：PublicApiAnalyzers + `PublicAPI.Shipped.txt` 方案需动 `src/` ⇒ 未获批准（且其盯可见 API 面，盖不住 internal `JsonReadHelpers` 的三辅助缺口）。
7. 新增 `global.json` 固定 SDK 版本 —— 仓库当前**没有** `global.json`，`dotnet` 浮动到本机最新 SDK；阶段 0 基线实测由 SDK 10.0.401 产出，而 CI 固定 8.0.x，本地与 CI 的工具链口径不一致（由阶段 0 记录暴露）
   > ✅ 已落地：`global.json` = `{ "sdk": { "version": "10.0.400", "rollForward": "latestFeature" } }`（本机解析 10.0.402；`latestFeature` 语义备注见 plan-f task-1-report）。`nuget-publish.yml` 因该钉导致的必然失败已一并修复（setup-dotnet 多行 10.0.x + 8.0.x）。
8. **CI 必须以不筛选（或不含 `--filter` 排除 `GoldenGuardTest`）的方式运行 `tests/OpenCSG.NET.Tests`** —— 阶段 1 实测：NUnit3TestAdapter 4.4.2 把 `[SetUpFixture]` 的 `OneTimeTearDown` 失败记为 fixture 级错误，**不计入测试用例计数、不影响退出码**。因此 `GoldenGuard` 的 teardown 在 `--filter` 下单独存在时退出码为 0（已实测），真正让运行失败的是它的伴随用例 `GoldenGuardTest.FindPendingGoldens_OnCleanTree_IsEmpty`。全量运行（CI 的实际形态）安全；一旦 CI 改为按类别筛选，必须保证该用例不被排除
   > ✅ 已落地：ci.yml 的 Tests 命令无任何 `--filter`（注释记录两条理由：fixture-teardown 退出码陷阱 + `--filter` 会连带**执行** Explicit 豁免种子——Task 1 实测的第二条独立佐证）；终验以全量裸命令执行（345/0 ×2 TFM，TRX NotExecuted×8 正确跳过）。

### 8.3 golden 门禁（新增强制力）

`GoldenGuard.cs`：`[SetUpFixture]` 的 `OneTimeTearDown` 扫描 `Results/`，存在任何 `*_.stl` 即 `Assert.Fail`。一条规则同时兜住「新用例忘记 promote」（现在是 `Inconclusive`，CI 视为通过 ⇒ 零保护却绿灯）与「旧用例输出已变」。

配套 `.gitignore` 加一行 `tests/**/Results/*_.stl`。**不做** 45 个 golden 的重新规范化（`StlStringEquals` 本就跨行尾，Windows 矩阵不需要 `.gitattributes`）。

## 9. 缺陷处置

默认**只报告不改**。已批准的最小修复例外（各 ≤5 行 + 回归测试）：**A1**（`Solid` 缓存失效/别名）、**A2**（`SectorAngle` 负数与 720）、**A4**（`StlWriter` 头编码与净化）。

其余（A3、A5、A6、A7、A8 及运行中新发现）只报告：
- 登记到 `tests/DEFECTS.md`
- 对应用例 `[Explicit("DEFECT-00x: ...")]`，套件保持绿色
- 双向索引由元测试校验
- 产品代码一行不改

修复后删 `[Explicit]` + 从 DEFECTS.md 移入「已修复」区。

## 10. 交付物

1. `tests/OpenCSG.NET.TestKit/`、`tests/OpenCSG.NET.SystemTests/`、`tests/OpenCSG.NET.ApiCompat/`、`tools/OpenCSG.NET.DataGen/`、`tools/promote-goldens.sh`
2. `tests/OpenCSG.NET.Tests/` 新增子目录用例 + `TestData/corpus.json` + 新 golden
3. `perf/OpenCSG.NET.PerfTest/` 扩展基准 + 基线 JSON
4. `tests/DEFECTS.md`（含最小复现 seed 与一行命令）
5. `docs/testing/TEST-REPORT-2026-10-13.md`：用例数、通过/失败、覆盖率前后对比、perf 基线、已知限制
6. `.github/workflows/` 改造 + `.runsettings` + `.gitignore` 一行
7. `artifacts/dataset/`（gitignored，可由 `DataGen` 随时重建）

## 11. 执行顺序

| 阶段 | 内容 | 产出 |
| --- | --- | --- |
| **0** | **基线**：跑现有 102 用例，记录通过 / Inconclusive 数、耗时、覆盖率基线 | 基线数字（写入报告） |
| 1 | TestKit 骨架 + `GeometryAssert` 迁入 + `MeshAssert` + `ViewBridge` + `GoldenGuard` + `.gitignore` 一行 + `promote-goldens.sh` | TestKit 可用，`tests/` 全绿 |
| 2 | 生成器 + Corpus + DataGen + **FsCheck 兼容 spike** + Tier 1–3/5 属性测试 | 首批真实缺陷（含 A1/A2/A4 复现） |
| 3 | L1/L2 单元与契约回填；A1/A2/A4 最小修复 + 回归测试 | 0% 模块覆盖关闭 |
| 4 | L4 golden + promote | 新 golden 入库，`*_.stl` 清零 |
| 5 | L5 集成端到端（STL 闭环 + CPurlin + 凸包周长 oracle）；样例侧改动：抽取 `CpurlinModel` + `Process.Start` 环境守卫（`OPENCSG_OPEN_VIEWER`，默认不打开） | 端到端通路验证 |
| 6 | L6 perf/复杂度 + L7 稳健性/并行/跨运行时 | 基线 JSON + 并行与跨运行时结论 |
| 7 | 覆盖率 + CI 改造 + ApiCompat + 两份报告 | CI 门禁上线 |

每阶段结束跑全套件并向用户汇报一次；缺陷即时登记不修（除 A1/A2/A4）。

**每层完成定义**：所有新用例绿色；发现的缺陷已登记 `DEFECTS.md` 并带可复现命令；新增 golden 已 promote 且 `*_.stl` 清零；覆盖率阈值达标；报告数字与实际 CI 输出一致。

## 12. 风险与缓解

| 风险 | 缓解 |
| --- | --- |
| FsCheck 3.x 与 NUnit 3.13.3 兼容性未验证 | 阶段 2 开头 spike；降级 2.16.x；再不通则手写种子化生成器，TestKit 接口不变 |
| 属性测试可能大面积失败（0% 模块从未被测） | 失败即登记缺陷 + `[Explicit]`，套件保持绿色，不阻塞后续阶段 |
| 并行跑暴露全局态串扰 | 这正是要测的；作为缺陷上报（预期命中 A-ViewArcFitter 静态诊断态） |
| 跨运行时浮点差异导致既有 golden 失败（bbox 在 1e12 是**绝对** 1e-10） | 这正是要验的；若失败，作为「金标容差过紧」缺陷报告，不改 golden |
| perf 基准在 CI 抖动 | perf 门禁只放 nightly，PR 不阻塞 |
| A1 最小修复触及布尔内核 | 修复限于缓存失效 + 防御性复制，不改算法；修复前先有复现用例；`GeometryInvariantTest`/`LargeCoordinate*` 全套件作为安全网 |
| SystemTests 用例数膨胀拖慢 PR | Smoke 档控 64 种子，目标 SystemTests ≤ 8 分钟；超出则把属性测试移入 nightly |
| `CsgNode` 的 `internal IsExternalInit` 通过 `modreq` 进入**已编译** netstandard2.0 消费方的 MemberRef | 低概率但真实：阶段 1 的 ApiCompat 实测确认消费方的 init setter MemberRef 其 `modreq` TypeRef 指向库内的 `internal` 类型。将来若库不再提供该 polyfill，**已编译**的消费方会在运行时失败，而公共 API 毫无变化。缓解：`src/` 只要仍声明 `record`/`init` 就必须继续携带该 polyfill —— 这是隐性契约，改 `CsgNode.cs` 的 `IsExternalInit` 声明前必须意识到它 |

## 13. 后续计划必须承接的事项

以下是 Plan A 终审（2026-10-13）确认的承接事项，均为实证结论，Plan B–F 实现时直接执行；未列出的内容不构成新范围。

### Plan B（生成器 + 属性/模糊）

> **已拆为两个计划**（2026-10-13）：**B1** = 阶段 2 的生成器/Corpus/DataGen + Tier 1–3 属性测试（`docs/superpowers/plans/2026-10-13-comprehensive-testing-plan-b1.md`）；**B2** = Tier 5 遮挡 oracle（下列第一条，独立成计划）。拆分理由：Tier 5 需要独立的 `RayCast` 独立实现与遮挡属性测试，可完全叠加在 B1 之上，且它是 HLR 子系统唯一的遮挡真值来源，值得单独的计划与审查注意力。

- **遮挡 oracle 是必做项**：Plan A 没有任何测试产生 `HiddenCount > 0`，`DepthRaster.Split` 的可见/隐藏拆分、`ViewSegmentCleanup` 的合并/丢弃分支、`ViewArcFitter` 的接受路径从未非平凡执行；也没有任何测试断言单条线段的端点，因此「包围盒正确但内部几何错误」的投影仍会通过。Tier-5 射线投射 oracle 是唯一能关闭它的手段。
  > **已完成（Plan B2，2026-10-13）**：`RayCast` 独立射线投射 + `OcclusionAssert` 9 探针双侧判定 + 手工夹具（前后双盒/L 形/通孔板，解析包络 + 回归钉）已在 5 个视图配置上产生 `HiddenCount > 0` 并逐点验证 V/H 拆分；语料 Smoke 64 种子铺开后**首次非平凡执行暴露了新的系统性缺陷 DEFECT-005**（`ViewProjector` 栅格量化自遮挡误判 `cell·tanφ > bias`，59/64 种子受影响，含普通平面），已登记并按种子豁免。细节见 `docs/testing/TEST-REPORT-2026-10-13.md` 的「Plan B2」节。**遗留改进**：语料测试的 59 个豁免使 oracle 实际只跑 5 个种子——后续计划可引入**计数式采样级 shard 规则**（≤2 格 H 碎段且与更长的同线 V 段重合 ⇒ 计数放行）恢复全量覆盖（评审 2026-10-13 建议，未列入本计划范围）。
- TestKit 目录布局按 §4 落位（`Generation/`、`Oracles/`、`Bridge/`、`Io/`）；Plan A 落的是扁平文件，加生成器前先定下来。
- `GoldenPaths` 的标记文件名要参数化（当前硬编码 `OpenCSG.NET.Tests.csproj`，`tests/OpenCSG.NET.TestKit/GoldenPaths.cs:11`），否则 SystemTests 无法复用。
- 先处理全局态，否则每个属性测试都继承隐患：`Tessellation.ChordTolerance`（`[ThreadStatic]`）需要 `IDisposable` scope 辅助；`ViewArcFitter.DiagnosticsEnabled`/`LastDiagnostics` 非 ThreadStatic，先登记缺陷。

### Plan C（单元/契约回填 + 最小修复）

> **已完成（Plan C，2026-10-14）**：以下三项全部落地 + A1/A2/A4 最小修复 + L1/L2 回填（覆盖率：0% 类 7→0、类平均 98.5%、未覆盖方法 82→1（死代码））；过程另登记 DEFECT-006/007。细节见 `docs/testing/TEST-REPORT-2026-10-13.md` 的「Plan C」节。
> **遗留建议（后续计划）**：T8 审查的四条 Minor（斜面探测顺序敏感、两处离散值可精确化、Formats 法向差异未钉住、ToString 断言偏松）；SystemTests 单次偶发失败（26 次未复现）列入 Plan E soak。

- 首个缺陷登记要同时落地 `DefectRegistryTest` —— `tests/DEFECTS.md` 已声明双向索引由它强制，但目前两侧皆空、约定未经检验。 ✅ 已落地（静态扫描双向索引 + 纯函数单测 + 负向控制；覆盖开放缺陷 001–007）。
- oracle 不对称：`MeshAssert.AssertClosedOrientedManifold` 已在 Plan A 拒绝空网格输入，但 `GeometryAssert.AssertClosedOrientedManifold(Solid)` 对空实体仍真空通过，两个 `Analyze(empty)` 也都返回看起来干净的全零报告。Plan A 冻结了 `GeometryAssert` 的断言逻辑，Plan C 可以扩展。 ✅ 已扩展（`PolygonCount` + 守卫；1 个既有测试的真空依赖已修正）。
- 补强两个偏弱的断言：`ToViewTriangles_TriangleCountMatchesCsgMesh`（仅比数量）与 `DefaultDirections_AreOrthographicThree`（未钉住 `directions` 透传）。 ✅ 已补强（144 项逐元素比对；`ReferenceEquals` 透传钉 + 自定义方向列表用例）。

### Plan D（golden + 集成）

> **已完成（Plan D，2026-10-14）**：以下三项全部落地 + L4（15 STL + 15 JSON 基线；既有 45 STL 逐字节不变）+ L5（36 集成用例：全管线贯通 / STL 与 JSON 闭环 / 凸包 oracle 成立表 / CPurlin 真实模型）+ 样例侧（CpurlinModel 抽取、字节级不变；OPENCSG_OPEN_VIEWER 守卫）。**新缺陷 DEFECT-008**（TabbedPlate 浅斜角肩部欠覆盖）与 **DEFECT-009**（Bounds 忽略弧）。细节见 `docs/testing/TEST-REPORT-2026-10-13.md` 的「Plan D」节。
> **遗留（后续计划）**：`Runner.Examples` 的 `Process.Start` 无守卫；D2 字节级 STL 基线在 autocrlf=false 克隆上会失配（写出器 `Environment.NewLine`；需决策 `.gitattributes`/写出器口径）；DEFECT-009 修复后的 JSON golden 重基线化。

- 加 JSON golden **之前**必须先扩门禁：`GoldenGuard.FindPendingGoldens` 只扫顶层 `*.stl`，`promote-goldens.sh` 只 glob `"$results"/*_.stl`，而 Tier 4 要引入 `Results/Views/{fixture}.{test}.json`。先扩谓词/脚本或加平行 JSON 门禁，否则那些 golden 无人看守、无法升级。 ✅ 已扩展（且评审发现并修正了首轮实现只扫顶层的缺口——现为**递归**扫描 `Results/**`，含 `.gitignore`/`.gitattributes` 配套；负向控制实测红）。
- `promote-goldens.sh` 补 `--yes` 文档与升级前的内容摘要 —— 约 30 个 golden 会经它升级，否则等于盲升。 ✅ 已补（递归收集、逐文件字节数+内容摘要、`--yes` 文档化；D2/D3 共 30 个新基线经其升级）。
- `[SetUpFixture]` 目前只覆盖 `Csg.Test` 命名空间；若出现第二个命名空间根或第二个写 golden 的程序集，改程序集级。 ✅ 无需改造（JSON golden 写在同一 Tests 程序集同一命名空间下；若将来出现第二写 golden 程序集再按预案处理——已在计划中记录该判据）。

### Plan F（覆盖率 + CI + 报告定稿）

> **已完成（Plan F，2026-10-14，收官）**：四条全部落地；`ci.yml`/`nightly.yml` 就绪（GitHub Actions 本体不可本地运行——验证 = YAML 语法校验 + 每条 `run:` 命令的本地等价真跑）；覆盖率门禁与官方对照数字入 TEST-REPORT。各条的落地实测见下方子项与 TEST-REPORT「Plan F」节。

- CI 不得过滤掉 `GoldenGuardTest`（§8.2 第 8 项）—— 实测：NUnit3TestAdapter 4.4.2 下 fixture 级 teardown 失败不影响退出码。
  > ✅ 已落地：ci.yml 的 `dotnet test tests/OpenCSG.NET.Tests -c Release` 无任何 `--filter`（§8.2 条目 8）；终验全量裸命令 345/0 ×2 TFM（TRX：NotExecuted×8 正确跳过、total=353）。
- ApiCompat 是手写金丝雀，有五类命名缺口（`BoundingBox`、`BoundingSphere`、`PolygonShared`、`CsgEvaluationException`、`ViewVector`，以及 `CsgSerialization` 的三个 `Utf8JsonReader` 辅助）；更彻底的方案是给 `src/` 上 `Microsoft.CodeAnalysis.PublicApiAnalyzers` + `PublicAPI.Shipped.txt`。
  > ✅ 已落地：五类缺口 + `CsgSerialization` 消费侧两层钉已补齐（4 个 Touch 方法 + 1 个嵌套转换器，+163 行）；`PublicApiAnalyzers` 需动 `src/` ⇒ **未获批准**、登记为后续选项（其盯可见 API 面，盖不住 internal `JsonReadHelpers` 三辅助——实测 CS0122）。
- 报告定稿要校正逐任务用例数，并把 `modreq` → 库内 `internal IsExternalInit` 的隐性运行时契约写进「已知限制」。
  > ✅ 已落地：TEST-REPORT 终稿含「逐计划用例数终审」（A–F 段自报数与 TRX 终值逐段闭合；342→345 勘误一行）与「已知限制」六项，第 ① 条即 `modreq` 契约（含「改 `CsgNode.cs` 前必读」与消费方声明侧的边界）。
- 考虑 `tests/Directory.Build.props` 统一 `Nullable`/`TreatWarningsAsErrors`/`LangVersion` —— 当前 TestKit 开了可空检查，而它的主要消费方 `Tests` 没开。
  > ✅ 已落地：`tests/Directory.Build.props` 统一 `Nullable=enable` + `LangVersion=9.0`（与主工程一致）；`TreatWarningsAsErrors` 经 77 处警告普查裁定**保持关闭**（洪泛档，理由入 props 注释），77 处已按编译期零语义变化修净 ⇒ 全量构建 0 警告 0 错误。

### 留给人决定的产品问题

- Plan A 让约 2100 行的 HLR 子系统**从测试代码**可达，但库的**使用者**仍没有 `Solid → ViewTriangle` 的通道（`ViewBridge` 只是测试侧约 30 行适配器，`tests/OpenCSG.NET.TestKit/ViewBridge.cs`）。是否把等价适配器提升进 `src/` 是产品变更，应显式决策，而不是由适配器恰好落在哪一侧决定。
