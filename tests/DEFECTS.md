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

## 已修复

| ID | 模块 | 修复方式 | 回归用例 |
| --- | --- | --- | --- |
| （暂无） | | | |
