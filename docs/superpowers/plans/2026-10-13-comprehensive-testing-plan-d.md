# Plan D — L4 Golden + L5 集成端到端

> 承接 spec §13「Plan D（golden + 集成）」与阶段表第 5 阶段：「L5 集成端到端（STL 闭环 + CPurlin + 凸包周长 oracle）；样例侧改动：抽取 `CpurlinModel` + `Process.Start` 环境守卫（`OPENCSG_OPEN_VIEWER`，默认不打开）」。
> 依赖 B1/B2/C（已并入 `main`）：`Corpus`/`ViewBridge`/`ConvexHull`/`OcclusionAssert` 等 TestKit 资产。
> 基线（开工时）：Tests 302/0（+4 Explicit）；SystemTests 1057/0/160；`Results/` 45 `.stl` 零残留；0 警告 0 错误。

## Global Constraints

- **`src/` 零改动**（Plan C 的 A1/A2/A4 是唯一批准的例外，已用完）。本计划的样例侧改动（`samples/`）与测试侧改动均已由 spec 授权。
- **既有 45 个 `.stl` golden 必须逐字节不变**（不得重基线化已接受的基线）；新增 golden 是 Plan D 的目的（L4 约 30 个，STL+JSON）。
- `tests/OpenCSG.NET.Tests/SolidTest.cs` 仍不可改（B1 冻结条款）；**`GoldenGuard.cs` 本计划按 spec §13 授权扩展**（JSON 门禁是其 Plan D 明列工作项）——冻结条款对该文件在 Plan D 解除。
- 文件 UTF-8 无 BOM + LF；全量构建 0 警告 0 错误；缺陷只报告（`DEFECT-008+` 登记 + `[Explicit]`）。
- 过程产物在 `.superpowers/sdd/2026-10-13-comprehensive-testing-plan-d/`（gitignored）。

## 侦察结论（控制器实测）

- `GoldenGuard.FindPendingGoldens`（`GoldenGuard.cs:23-36`）只扫 `Results/*.stl` 的 `_.stl` 后缀；`tools/promote-goldens.sh` 已支持 `--yes`（`${1:-} != "--yes"` 跳过交互）但**头部注释未文档化**，且升级前无内容摘要（盲升）。
- `[SetUpFixture]` 位于 Tests 程序集、命名空间 `Csg.Test`——Plan D 的 JSON golden 也写在 Tests（同一 Results 目录、同一门禁覆盖）⇒ **无需程序集级改造**（若实施中发现需要，再按 spec 预案处理并在报告说明）。
- `samples/Runner.CPurlin/Program.cs`（144 行，顶层语句）：几何常量 web=152 / flange=64 / lip=18.5 / thickness=2.4 / height=1000 / 冲孔 z∈{35(腹板),70(翼缘),105(中心)}、r=5；`s.WriteStl(...)` 写文本+二进制；`:102` 无守卫 `Process.Start(文本路径, UseShellExecute)` 打开查看器。SystemTests 当前无 `Runner.CPurlin` 引用。
- `SolidTest` 的 golden 写出模式：缺失基线 ⇒ 写 `<Fixture>.<Test>_.stl` 并 `Assert.Inconclusive`；门禁在套件末把任何 `_.stl` 残留判失败；`promote-goldens.sh` 升级（改名去掉 `_`）。

## 文件结构

- Modify: `tests/OpenCSG.NET.Tests/GoldenGuard.cs`（JSON 谓词）、`tests/OpenCSG.NET.Tests/Infrastructure/GoldenGuardTest.cs`（JSON 用例）、`tools/promote-goldens.sh`（JSON + `--yes` 文档 + 内容摘要）
- Create: `tests/OpenCSG.NET.Tests/Golden/`（L4 新模块 STL 基线 + HLR JSON 基线 + 写 golden 辅助）
- Create: `tests/OpenCSG.NET.SystemTests/Integration/`（L5 端到端）
- Create: `samples/Runner.CPurlin/CpurlinModel.cs`；Modify: `samples/Runner.CPurlin/Program.cs`（调用抽取类 + 环境守卫）
- Modify: `tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj`（+ `Runner.CPurlin` ProjectReference）
- Modify: `tests/DEFECTS.md`（若登记）、`docs/testing/TEST-REPORT-2026-10-13.md`

---

### Task 1: golden 门禁扩展（.stl + .json）+ promote 脚本增强

- `FindPendingGoldens`：同时回收 `_.stl` 与 `_.json` 残留（保留显式后缀过滤的 8.3 短名规避）；失败消息改为通用措辞（不再只写 `_.stl`），并保留「测试名以 `_` 结尾」的排查提示。
- `GoldenGuardTest`：补 JSON 残留被检出 / 混合残留 / JSON 通过（无残留）用例；既有 .stl 用例不得改语义。
- `promote-goldens.sh`：`pending` 收集两种后缀；头部注释文档化 `--yes`；升级前列出**每个文件的字节数 + 内容摘要**（JSON 取首 120 字符单行、STL 取前 8 字节 hex 与三角形数——用 `wc -c` 与 `head`；不依赖 python）。
- 验证：造 1 个假 `_.json` + 1 个假 `_.stl` ⇒ 门禁红且两者都列出；`promote-goldens.sh` 干跑（不 `--yes`，用 echo 管道喂 `n`）打印摘要后取消；随后清理假文件、全套件转绿。
- Commit: `test(golden): 门禁与升级脚本扩展到 JSON golden（含 --yes 文档与升级前摘要）`

### Task 2: L4 新模块语义基线（~15 个 STL golden）

- `tests/OpenCSG.NET.Tests/Golden/ModuleBaselineGoldenTest.cs`（+ 需要时的局部辅助；若 `SolidTest` 的写 golden 辅助可复用则复用，否则按同约定最小复制并在报告说明）。
- 覆盖 Plan A 之后新增/修复语义的模块（每个至少一条基线，命名 `<Fixture>.<Test>`）：
  `CsgHelpers` 族（PrismXY/PrismXZ/CornerCut/CornerWedge/TabbedPlate/TabbedPlateSlantAngleDeg/FilletConcave/RoundConvexCorner）、A2 后的扇形柱（SectorAngle 90/270）、定向图元（`CubeOptions`/`SphereOptions` 的非默认定向各一）、`Tessellation.ChordTolerance` 变体（经 `TessellationScope`：默认/0/更小容差各一）。
- 全流程：首轮全部 Inconclusive + `_.stl` 残留 ⇒ `promote-goldens.sh --yes` 升级（升级前逐个人工确认摘要——首轮 15 个；升级内容与命令输出入报告）⇒ 复跑全绿。
- **既有 45 个 .stl 逐字节不变**（升级命令前后 `sha256sum` 对照入报告）。
- Commit: `test(golden): L4 新模块语义基线（CsgHelpers/扇形柱/定向图元/细分容差）`

### Task 3: L4 HLR JSON 基线（~15 个 JSON golden）

- `tests/OpenCSG.NET.Tests/Golden/ViewJsonGoldenTest.cs` + 测试侧序列化器 `ViewJson.Serialize(IReadOnlyList<ViewProjection>)`：
  稳定 JSON（固定字段顺序、`InvariantCulture`、`"R"` 往返格式数字、线段按输出顺序、弧按输出顺序）；结构：`[{view, bounds:[minX,minY,maxX,maxY], segments:[{a:[x,y], b:[x,y], visible}], arcs:[{cx,cy,r,start,sweep,visible,closed}]}]`。
- 夹具（每个一条 golden）：cube、box 10×20×30、L 形、通孔板（主/俯视，避侧视掠射）、球、圆柱/圆锥、A2 扇形柱、CPurlin 简化版（若运行时长可接受，见 Task 5 的口径）。
- 首轮 `_.json` 残留 ⇒ 被扩展后的门禁抓住 ⇒ `promote-goldens.sh --yes` 升级 ⇒ 复跑全绿。
- **稳定性自证**：同一 golden 连续两次运行逐字节一致；`Segment/Arc` 数变化时 golden 天然失败（自证判别力：临改一个字段再还原，红/绿各一次，入报告）。
- Commit: `test(golden): L4 HLR 三视图 JSON 基线与稳定序列化器`

### Task 4: 样例侧 —— `CpurlinModel` 抽取 + 环境守卫

- `samples/Runner.CPurlin/CpurlinModel.cs`：`public static class CpurlinModel { public static Solid Build(...); + 常量/冲孔参数作为公开常量或可选参数 }`；`Program.cs` 改为调用它（顶层语句只留 CLI/写出/查看器部分）。
- `Process.Start` 守卫：仅当环境变量 `OPENCSG_OPEN_VIEWER=1` 时打开查看器（默认不打开）；注释说明用途。
- SystemTests csproj 加 `Runner.CPurlin` 的 `ProjectReference`（Exe 可作库引用；若 slnx 构建顺序/入口冲突 ⇒ 报告并给出替代（如 `<ReferenceOutputAssembly>` 方案）。
- 验证：`dotnet build OpenCSG.NET.slnx -c Release` 0/0；`dotnet run --project samples/Runner.CPurlin` 正常产出 STL 文件且**不**弹查看器（默认）；设 `OPENCSG_OPEN_VIEWER=1` 时行为保持（可只在脚本层检查逻辑，不实际弹窗）。
- Commit: `refactor(samples): 抽取 CpurlinModel 并加 OPENCSG_OPEN_VIEWER 环境守卫`

### Task 5: L5 集成端到端（~20）

`tests/OpenCSG.NET.SystemTests/Integration/`：
1. **完整管线等价**：`CsgNode 树 → Evaluate → Solid → CsgMesh.TrianglesOf → new ViewProjector(...).Project(...)` 与 `ViewBridge.ProjectViews` 两条路径的输出（段集合 + 弧集合 + 包络）等价（同一三角形源 ⇒ 逐段 `Within(1e-9)`；实现前先读两路径共用口径）。
2. **STL 闭环**：`Solid → StlWriter（二进制与 ASCII）→ ViewMeshReader.ReadStlFile → ViewProjector.Project` ⇒ 与「原 Solid 直接投影」的段数/可见性构成一致（float32 存储 ⇒ 坐标 `Within(1e-3)`、段数允许 ±k 由实测定并解释）；再经 `OcclusionAssert` 复核三视图（若 CPurlin/球体触发 DEFECT-005 族 ⇒ 用轴对齐夹具子集）。
3. **JSON 闭环**：`CsgNode → ToJson → FromJson → Evaluate` 与原树求值结果一致（体积/多边形数/网格三角形数）。
4. **凸包周长 oracle**：对 `ConvexHull`（TestKit）在真实管线输出上——凸实体（盒/棱柱/圆柱轴向视图）的三视图可见段总长 == 投影凸包周长（Tier 2 已解析覆盖立方体；此处覆盖更一般凸体，绕开 DEFECT-005 的斜掠面视角仍成立的部分——**先实测哪些视角成立**，成立的写断言、不成立的记录并引用缺陷）。
5. **CPurlin 真实模型**：`CpurlinModel.Build()` ⇒ 三视图：
   - 主视（Right=−Y, Up=+Z）bounds：宽 ≈152（web）、高 ≈1000（height）（容差 ±0.5）；
   - 左视（Right=−X, Up=+Z）bounds：宽 ≈64（flange）、高 ≈1000；
   - 冲孔可观察量：按 `Punching` 参数（z∈{35,70,105}、孔 18/22、webCenter=60）核对——用**可见段/段的端点坐标**在对应高度处出现孔轮廓特征（实现前先实测孔在视图里的可观察投影，再定断言；避开 H/V 分类断言以防 DEFECT-005 干扰）。
   - 运行时长入报告（≥ 数秒则给该用例 `[Category("Corpus")]`）。
- Commit: `test(integration): L5 端到端——管线等价/STL 与 JSON 闭环/凸包 oracle/CPurlin 真实模型`

### Task 6: 收口

- `TEST-REPORT` 追加「Plan D」节：黄金总数前后对照（45 → 45+N，既有 45 逐字节不变证明）、L4 基线清单、L5 结果（含 CPurlin bounds/冲孔实测）、门禁与脚本变更、时间预算、全量验证输出；零占位符。
- `DEFECTS.md`：新缺陷（若有）+ 双向索引（`DefectRegistryTest` 强制）。
- spec §13 Plan D 三项勾选；本计划完成定义勾选；全量验证（build 0/0、Tests/SystemTests 全绿、Results 残留 0）。
- Commit: `docs(test): Plan D 收口——L4/L5 纪要、门禁扩展与完成定义核对`

## 完成定义（Plan D）

**状态：已全部完成（2026-10-14，分支 `test/comprehensive-suite-plan-d`，10 commits）。** 逐项核对：

- [x] 门禁**递归**覆盖 `.stl` 与 `.json` 两类残留（含子目录）；`promote-goldens.sh` 文档化 `--yes` 且升级前打印摘要（含空格安全 / `find -print0`）。首轮实现只扫顶层的缺口由评审抓获并在修正轮修复（红/绿实测）。
- [x] L4：15 条新模块 STL 基线 + 15 条 HLR JSON 基线经 `--yes` 升级入库；**既有 45 STL 逐字节不变**（sha256 + git blob 双重对照）；JSON 序列化器两次运行逐字节稳定 + 判别力自证。
- [x] L5：管线贯通（逐位一致 + 确定性 + 非空钉）、STL 闭环（实测容差 + 独立射线 oracle 在回读管线上全过）、JSON 闭环（Within(0)）、凸包 oracle 成立表（含不成立记录与缺陷引用）、CPurlin（bounds 1000×152 / 1000×64 / 64×152 + 冲孔去对照验证）——36 用例全绿。
- [x] `CpurlinModel` 抽取完成（STL 输出字节级不变，评审独立复现）；`Process.Start` 默认不打开（`OPENCSG_OPEN_VIEWER == "1"` 门控）；slnx 0/0。
- [x] 全量测试全绿（Tests 342/0；SystemTests 1093/0/160）、`Results/` 零 `_.stl`/`_.json` 残留；`src/` 零改动；新缺陷 DEFECT-008/009 已登记 + `[Explicit]` 复现。

> 逐任务评审/fix round/核对记录与遗留观察项见 `.superpowers/sdd/2026-10-13-comprehensive-testing-plan-d/progress.md`。
