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
| DEFECT-002 | `Solid` 布尔运算（`SubtractSub` / `IntersectSub`） | 高 | 操作数为**空实体**（0 个多边形）时结果错误且不抛异常：`empty.Subtract(X)` 返回**绕序翻转**的 `X`（有向体积为负、法线朝内）；`empty.Intersect(X)` 与 `X.Intersect(empty)` 都返回 `X`（等价于并集而非空集）。根因：这两条路径把「空 BSP 树取反」当成「全空间」，而全库只有 `UnionSubLocal` 用 `MayOverlap`（`src/OpenCSG.NET/Solid.cs:369-377`）对空操作数短路，故 `Union` 语义正确 —— 空操作数应按空集处理的意图明确，`Subtract`/`Intersect` 未对齐 | 一行命令：`dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release --filter "FullyQualifiedName~Random_TreesAreEvaluableAndOutwardFacing"`，在 `seed=12` 处失败，断言消息已带 seed 与完整树 JSON（有向体积 `−664.57612966255658`）。独立最小片段（不依赖生成器）：`var e = CsgNodes.Intersect(CsgNodes.Box(new Vector3D(0,0,0), new Vector3D(1,1,1)), CsgNodes.Box(new Vector3D(10,0,0), new Vector3D(1,1,1)));` 求值为空（正确，0 个多边形）；随后 `CsgEvaluator.Evaluate(CsgNodes.Subtract(e, CsgNodes.Box(new Vector3D(0,0,0), new Vector3D(2,2,2))))` 得 6 个多边形、`SignedVolume = −8`；`CsgEvaluator.Evaluate(CsgNodes.Intersect(e, CsgNodes.Box(new Vector3D(0,0,0), new Vector3D(2,2,2))))` 得 6 个多边形、`SignedVolume = +8` | 期望：`empty − X = ∅`、`empty ∩ X = X ∩ empty = ∅`；实际：`empty − X` = 反向的 `X`（体积取负），`empty ∩ X = X ∩ empty = X`（体积取正）。`Union` 与「空 op 空」三种组合均正确 | 任何「左/被减操作数求值为空」的树都会静默产出错误几何（减法侧法线朝内、体积为负），按体积或法线判定的下游会拿到反向零件；`NodeGen` 默认深度 512 个种子里 13 个（12, 20, 134, 176, 177, 188, 206, 225, 274, 301, 406, 441, 466）因此读数为非正，另有 17 个种子的树**合法**求值为空（0 个多边形）—— 修复后 `Random_TreesAreEvaluableAndOutwardFacing` 的「非空」断言需一并复核 | 只报告（Plan C 决定；本任务不改 `src/`） |

| DEFECT-003 | `Solid.Union`（BSP 并集 + 重剖分/规范化路径） | 高 | Survey 档（\|坐标\| ≈ 1e6）下并集会损坏几何：A∪A 时**重合面被重复保留或整体丢失** —— 6 面立方体的自并集产出 11 个多边形（含 1 对逐位相同的重复面与 3 对同平面薄片/整面重叠）或只剩 5 个多边形（缺一个面），有向体积偏差 8%–47%；不相交实体对的 A∪B 也会多计/少计 2e-9…9.1e-4（相对）的体积。破坏**限定在并集路径**：同一批种子上 A∩A = A、A−A = ∅、A−B = A−A∩B、及「不修改操作数」全部通过；Normal 档 40 个种子全部通过 | 独立片段（不依赖生成器）：`var a = Solids.Cube(10, center: true).RotateY(30).Translate(1e6, 0, 0);` → `SignedVolume(a) = 1000.0000000081947`，而 `a.Union(a)` 为 **1166.666666679203**（= 7/6·V(A)，11 个多边形、其中 1 对逐位相同的重复面与 3 对同平面薄片/整面重叠，见 task-7-report.md §归因）。平移量改为 1e5 时并集精确（阈值在 1e5 与 1e6 之间）。语料视角：一行命令 `dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release --filter "FullyQualifiedName~Tier1BooleanIdentityTest"`（`[Explicit]` 用例被过滤显式选中时会执行，断言消息自带 seed 与四个体积）；A∪A 失败种子（Smoke 下标，Survey）：41,43,44,45,46,47,48,49,51,52,53,54,55,56,58,59,60,61,63；包含排斥失败种子：41,43,45,47,48,49,52,53,54,55,56,57,58,59,60,63 | 期望：A∪A = A 在任意坐标成立（Normal 档即如此）；实际：Survey 档并集输出含重复/缺失面，体积最多偏差 47%。独立的 A∪A 最小片段证明输出多边形数据本身损坏（重复面逐位相同），与体积 oracle 无关 | 测绘坐标下任何含重合/共面曲面的并集（自并、相贴实体、布尔构造件）静默产出错误几何；按体积或法线判定的下游会拿到错误零件。语料豁免方式：`Tier1BooleanIdentityTest.CasesWithDefectExclusions` 按 seed 下标给具体失败用例打 `TestCaseData.Explicit("DEFECT-003: …")`（brief Step 4 允许的唯一豁免形式）。**修正轮 2（2026-10-13，`InclusionExclusion` 增补无参考点的面积包含排斥恒等式）**：16 个包含排斥豁免种子中，体积恒等式接住 9 个（45/47/48/49/55/56/57/60/63），面积恒等式接住 7 个（41/43/52/54/57/58/59）—— 修正轮 1 被自校准漂移预算吞掉的 7 个里，6 个（41/43/52/54/58/59）由面积口径独立接住（面积残差 −3.9e4…4.3e-5，相对残差 1.4e-9…2.9e-3，全部超出 slack，最紧的 seed 52 也超出 1.4 倍）。**残留盲区只剩 seed 53 一个：两个恒等式都接不住**（其 A∪B 输出 1158 面而 A=1152、B=8，带 8 条单用边/8 条非流形边、无重复有向边；面积残差 −4.1e-9（相对 −2.3e-12）在面积 slack 内，体积残差 6.08e-6 被漂移预算 3.92e-5 吸收；豁免标注保留、不撤销）。A∪A 的 19 个种子全部仍按体积失败（`Idempotence_UnionSelf` 本轮未改动）。面积恒等式同时接入 FsCheck 属性（Normal 档）以防回归。逐种子判定见 `task-8-report.md`「Fix round 2 — 面积恒等式」一节 | 只报告（Plan C 决定；本任务不改 `src/`） |

## 已修复

| ID | 模块 | 修复方式 | 回归用例 |
| --- | --- | --- | --- |
| （暂无） | | | |
