# Plan C — L1/L2 单元与契约回填 + A1/A2/A4 最小修复

> 承接 spec §13「Plan C（单元/契约回填 + 最小修复）」与阶段表第 3 阶段：「L1/L2 单元与契约回填；A1/A2/A4 最小修复 + 回归测试 | 0% 模块覆盖关闭」。
> 依赖 B1/B2（已并入 `main`）：生成器（`SolidGen`/`Corpus`/`ViewSegGen`）与 oracle（`GeometryAssert`/`MeshAssert`/`IdentityAssert`/`HlrAssert`/`OcclusionAssert`）作为固定输入源与安全网。
> 基线（开工时）：SystemTests 1057/0/160；Tests 129/0/1；`Results/` 45/0；0 警告 0 错误。

## Global Constraints

- **`src/` 例外（本计划唯一、且已获用户批准）**：仅 A1/A2/A4 三条「按文档/公开 API 正常使用即得到错误结果」的缺陷允许**最小修复（各 ≤5 行）+ 回归测试**（spec §9/§16 已批准）。其余一切产品代码缺陷**只报告不改**。
- 修复前先有**复现用例**；B1/B2 全量套件（尤其布尔恒等式/网格/遮挡 oracle）是安全网，修复后全绿是硬门槛。
- `tests/OpenCSG.NET.Tests/SolidTest.cs`、`GoldenGuard.cs`、`TestKit.csproj` 不可改；`Results/` 恒为 45 `.stl`、零 `*_.stl`。
- 文件 UTF-8 无 BOM + LF；全量构建 0 警告 0 错误。
- 缺陷册：新缺陷 → `DEFECTS.md` 登记（`[Explicit]` + 最小复现）；**修好的** A1/A2/A4 → 写入 `DEFECTS.md` 的「## 已修复」表。
- 过程产物在 `.superpowers/sdd/2026-10-13-comprehensive-testing-plan-c/`（gitignored）。

## 侦察结论（控制器实测，写计划前已核实，勿重复摸索）

- **A1**（`Solid.cs`）：`public List<Polygon> Polygons;`（公共**字段**，无法做 setter 失效）；`BoundingBox? cachedBoundingBox` 惰性缓存（`:346-371`）；`FromPolygons` 直接别名持有（`:28-33`）。`Bounds` 为**私有**属性，全库仅 2 处使用（`:54` MayOverlap 短路、`:58-59` BSP 树构造）⇒ **去掉缓存后每次访问 O(n) 且每布尔运算仅 2 次**，代价可忽略 ⇒ 最小修复 = 删缓存 + `FromPolygons` 防御性复制。
- **A2**（`Solids.cs:136-137`）：`alpha = alpha > 360 ? alpha % 360 : alpha;` ⇒ `720→0`、`-90→-90` 都被 `alpha > 0` 判为假 ⇒ 静默空实体。`CylinderOptions.SectorAngle` 默认 360、无文档契约 ⇒ 修复需先钉契约（见 Task 4 的映射表）。
- **A4**（`StlWriter.cs`）：二进制默认头 `"调用方 STL export"` 经 `Encoding.ASCII` ⇒ '?' 乱码；截断到 79 字节已存在（`Math.Min(len, HeaderLength-1)`），缺 `\r`/`\n` 净化；ASCII 路径 `"solid " + (header ?? "调用方")` 同样未净化（`\n` 会破坏结构）。
- **空输入不对称**：`MeshAssert.AssertClosedOrientedManifold` 有 `TriangleCount > 0` 守卫（`MeshAssert.cs:187`）；`GeometryAssert.AssertClosedOrientedManifold(Solid)`（`:167`）无 → 空实体全零报告真空通过。`ManifoldReport` 无总多边形计数字段。
- **弱断言位置**：`tests/OpenCSG.NET.Tests/Views/ViewBridgeTest.cs:126`（默认方向仅比名字）与 `:139`（ToViewTriangles 仅比数量）。
- **缺陷册现状**：开放 5 条（001–005）+「## 已修复」空表；双向索引目前只是文档约定（`DEFECTS.md` 规则 3），`DefectRegistryTest` 不存在。

## 文件结构

- Create: `tests/OpenCSG.NET.Tests/Defects/DefectRegistryTest.cs`（静态扫描双向索引 + 纯函数单测）
- Create: `tests/OpenCSG.NET.Tests/Defects/A1SolidCacheTest.cs`、`A2SectorAngleTest.cs`、`A4StlHeaderTest.cs`
- Create: `tests/OpenCSG.NET.Tests/Unit/`（L1 回填，按模块分组）与 `tests/OpenCSG.NET.Tests/Contracts/`（L2 回填）
- Modify: `src/OpenCSG.NET/Solid.cs`（A1）、`Solids.cs`（A2）、`StlWriter.cs`（A4）——**仅此三处**
- Modify: `tests/OpenCSG.NET.TestKit/Oracles/GeometryAssert.cs`（空输入守卫 + `ManifoldReport.PolygonCount`）
- Modify: `tests/OpenCSG.NET.Tests/Views/ViewBridgeTest.cs`（两处弱断言补强）
- Modify: `tests/DEFECTS.md`（新缺陷 + 已修复表）、`docs/testing/TEST-REPORT-2026-10-13.md`

---

### Task 1: 覆盖率复测 + L1/L2 工作清单定稿

- 用 coverlet 采集 `OpenCSG.NET.Tests` + `OpenCSG.NET.SystemTests` 对 `src/` 的行覆盖（`--collect:"XPlat Code Coverage"`，结果落 `artifacts/planCcoverage/`，gitignored）。
- 产出**现状清单**：仍 0% 或近 0% 的模块/类型（spec 的 L1 清单写于 B1/B2 之前，`View*` 有多处已被间接覆盖，需按实测收窄），与 L2 的 throw 路径对照表（spec §7 的 L2 清单逐条标注「已覆盖/待回填」）。
- 报告写明：每个待回填模块的目标用例数与归组（T7/T8 的批次划分以实测为准）。
- **不写测试、不提交代码**；产出进报告 + 台账。

### Task 2: DefectRegistryTest（双向索引强制）

- `tests/OpenCSG.NET.Tests/Defects/DefectRegistryTest.cs`：
  - 纯函数 `ParseOpenDefectIds(string markdown)`：从 `tests/DEFECTS.md` 解析「开放」表的 `DEFECT-NNN` id 集（表格中在「## 已修复」之前的行）；
  - 纯函数 `ParseReferencedIds(IEnumerable<string> sources)`：从测试源码文本收集 `Explicit("DEFECT-NNN…")` 引用的 id 集；
  - 双向断言：每个开放的 id 至少被一个测试引用（防「登记了但没人钉」）；每个被引用的 id 必须在册（防「引用不存在的号」）；
  - 两个纯函数用内联字符串做单元测试（正/负/空/多号一行等）。
- 源码收集：沿 `GoldenPaths.ResultsDirectory` 同款锚点发现 repo 根（`tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj`），遍历 `tests/**/*.cs`（skip `bin`/`obj`），读文本。
- **自检**：当前 5 条开放缺陷应全部有引用（001→Tests 的 `[Explicit]`、003/004/005→SystemTests）；故意删一条引用 ⇒ 测试变红（临时验证后还原）。

### Task 3: A1 最小修复 + 回归

- 复现用例先行（`A1SolidCacheTest`，pre-fix 必红）：
  1. 建 `a = Solids.Cube(10)`，**先读一次** `a` 的相关路径（触发缓存：对 `a` 做一次布尔或访问内部路径——用公开 API：`var probe = a.Union(new Solid());` 使它读 `Bounds`），随后 `a.Polygons.Clear(); a.Polygons.Add(大位移大盒的 Polygon)`（或 `a.Polygons = 新列表`），再 `a.Union(b)`：**修复前** MayOverlap 用旧缓存可能误判不相交 ⇒ 输出错误（用 `GeometryAssert.SignedVolume` 量化断言）；
  2. `FromPolygons` 别名：`var list = new List<Polygon>{...}; var s = Solid.FromPolygons(list); list.Add(...);` ⇒ 断言 `s.Polygons.Count` 不受影响。
- 最小修复（`src/OpenCSG.NET/Solid.cs`，≤5 行改动面）：
  - `FromPolygons`: `csg.Polygons = new List<Polygon>(polygons);`
  - 删 `cachedBoundingBox` 字段与缓存分支，`Bounds` 每次现算（附一行注释说明「公共可变字段无法失效缓存；调用点仅 2 处，O(n) 可忽略」）。
- 回归：B1/B2 全量套件 + 上述 2 用例；`DEFECTS.md`「已修复」表新增 A1 行（修复方式 + 回归用例名）。

### Task 4: A2 最小修复 + 回归

- 先钉契约（写进 `CylinderOptions.SectorAngle` 的注释 + 测试）：**非零角 mod 360 归一到 (0, 360]；0 保持 0（空）**：
  | 输入 | 期望 |
  | --- | --- |
  | 0 | 空实体（0 多边形） |
  | 90 / 180 | 扇形（侧面数 < 整圆柱；体积 ≈ 比例×整柱） |
  | 360 / 720 / -360 | 完整圆柱（体积 ≈ πr²h，与 360 完全一致的多边形结构） |
  | -90 | 270° 扇形（= 270 的多边形结构逐位一致） |
- 复现用例先行（`A2SectorAngleTest`，pre-fix 必红）：720 与 -90 当前返回 0 多边形。
- 最小修复（`src/OpenCSG.NET/Solids.cs:136-137`，≤3 行）：
  ```csharp
  var alpha = options.SectorAngle;
  if (alpha != 0) {
      alpha -= 360 * Math.Floor(alpha / 360);   // 归一到 [0, 360)
      if (alpha == 0) alpha = 360;              // 非零整圈（720/-360）⇒ 完整圆柱
  }
  ```
- 回归：A2 映射表 + 全量套件；`DEFECTS.md`「已修复」表新增 A2 行。

### Task 5: A4 最小修复 + 回归

- 复现用例先行（`A4StlHeaderTest`，pre-fix 必红）：默认二进制头写盘后读回字节 0–79 ⇒ 当前含 `?`（乱码）。
- 最小修复（`src/OpenCSG.NET/StlWriter.cs`，两条路径，≤5 行）：
  - 二进制：默认头改为 ASCII（如 `"STL export"`）+ `header` 的 `\r`/`\n` 净化为空格（截断已有）；
  - ASCII：`solid`/`endsolid` 行的头同样净化 + 默认值改 ASCII（保持 `"\u0000"` 无关）。
- 回归测试矩阵：默认头 / 空串 / 79 / 80 / 81 字节 / 含 `\n` / 含非 ASCII / 含 `\r\n` —— 断言：二进制头 80 字节结构完好（含 ASCII STL 结构不被破坏）；**写→读往返**：`StlWriter.WriteAscii` 写出的文件用 `ViewMeshReader`（src 公开读取器）读回 ⇒ 三角形数一致（钉「`\n` 不破坏结构」）；`DEFECTS.md`「已修复」表新增 A4 行。

### Task 6: GeometryAssert 空输入对称 + 两处弱断言补强

- `GeometryAssert`：`ManifoldReport` 增 `PolygonCount` 字段（`Analyze` 填充）；`AssertClosedOrientedManifold(Solid)` 增 `PolygonCount > 0` 守卫（消息同 MeshAssert 口径「空实体不是合法实体（若无本断言，空输入会真空通过）」）。
- 测试：`Analyze(empty).PolygonCount == 0` 可区分；`AssertClosedOrientedManifold(new Solid())` 抛 `AssertionException`（前置 `Throws`）；非空合法实体照旧通过。
- `ViewBridgeTest.ToViewTriangles_TriangleCountMatchesCsgMesh` 补强：逐三角形比较 `ViewBridge.ToViewTriangles` 与 `CsgMesh.TrianglesOf` 的 **A/B/C 坐标**（`Within(0)` 或精确相等——先实测两条链是否逐位一致，若因坐标换算有 1e-12 级差则用 `Within(1e-12)` 并在注释说明）与 `Normal`；数量断言保留。
- `ViewBridgeTest.DefaultDirections_AreOrthographicThree` 补强：断言默认调用的返回**逐项引用同一** `ViewDirections.OrthographicThree[i]` 实例（强钉透传）；追加自定义列表用例：`ProjectViews(cube, new[] { ViewDirections.Left })` ⇒ 恰 1 个视图且 `ReferenceEquals` 该实例、名 "左视"。

### Task 7: L2 契约与边界回填（throw 路径 + 退化输入 + 阈值）

- 按 Task 1 的对照表，逐条落地 spec §7 的 L2 清单（当前 0 覆盖的 throw 路径）：
  `Vector3D` NaN / `Matrix4x4` <16 与 null / `Plane` 共线 / `CsgEvaluator` 6 条 / `HalfPlaneCut` 2 条 / `RevolvedStack` 4 条 /
  `ValidateProbeFactor` 3 条 / `ViewTriangle` 索引越界 / 6 处 null 校验 / `StlWriter` 空路径。
- 退化输入：`1e-5` 容差临界对、`1e12` 尺度、±0、扇形角 0/720/−90（A2 后按新契约）、超深 JSON ⇒ **StackOverflow 不可捕获：`[Explicit]` 或独立进程**（二选一，报告说明）。
- fitter 5 个门限 ±ε：`MinRadius 0.5` / `MaxRadiusToExtent 5.0` / `MinSweep 10°` / `MaxClosingGap 25°` / `SameCircleRel 1e-3`——两侧各一例（恰在门限、±ε）。
- **throw 路径未按文档抛异常 ⇒ 是真实缺陷**：登记 `DEFECT-006+` + `[Explicit]`（只报告）。测试组织：`tests/OpenCSG.NET.Tests/Contracts/`，按被测类型分文件。

### Task 8: L1 单元回填（按 Task 1 实测清单，分 1–2 批）

- 仅覆盖 Task 1 判定仍为 0%/近 0% 的模块：`CsgHelpers`(16 方法)、`HalfPlaneCut`、`StlWriter`（内容级）、`CsgMesh`（三角化属性：面数/绕序/法向一致性——用解析实体）、`Tessellation`（含 `ChordTolerance` 全局态经 `TessellationScope`）、`PrimitiveConvention`、`Matrix4x4`（**含从未测过的矩阵×矩阵复合**——用一个手工可核算的复合（旋转×平移）钉数值）、`Vector2D`、`BoundingBox`、`Polygon.Flipped`/`Vertex.Transform`、`Plane.SplitPolygon` 的 5 个 Type 码、`CubeOptions`/`SphereOptions`（含定向球：轴 ≠ Z 的球体体积/包络）。
- 每个模块：解析可核算的期望值优先（体积/包络/法向/顶点集合），避免「镜子测试」；发现缺陷 → 登记（只报告）。
- 用例数目标以 Task 1 实测清单为准（spec 估 ~140，实测收窄后如实记录）。

### Task 9: 收口

- `TEST-REPORT` 追加「Plan C」节：覆盖率前后对照（Task 1 基线 vs 收口复测）、A1/A2/A4 修复摘要与回归证据、L1/L2 回填模块表、新缺陷（若有）、时间预算、全量验证输出；零占位符。
- `DEFECTS.md`：开放表 + 已修复表一致性核对（DefectRegistryTest 强制）。
- spec §13 的 Plan C 项逐条勾选；本计划完成定义勾选。
- 全量验证：build 0/0；Tests + SystemTests 全绿；`Results/` 45/0。

## 完成定义（Plan C）

- [ ] `DefectRegistryTest` 落地且双向索引强制（含纯函数单测与负向验证）。
- [ ] A1/A2/A4 三条最小修复（各 ≤5 行）+ 复现用例（修复前红）+ 回归，已写入「已修复」表。
- [ ] `GeometryAssert` 空输入对称；两处弱断言补强（含 `ReferenceEquals` 透传钉与逐三角形内容比对）。
- [ ] L2 throw 路径与退化输入按 spec 清单回填（或发现缺陷并登记）。
- [ ] L1 0% 模块按实测清单回填；覆盖率数字前后对照入报告。
- [ ] 全量构建 0 警告 0 错误；`Results/` 45/0；`src/` 除 A1/A2/A4 外零改动。
