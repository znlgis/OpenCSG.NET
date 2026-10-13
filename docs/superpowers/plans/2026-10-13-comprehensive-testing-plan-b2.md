# Plan B2 — Tier 5 遮挡 oracle（独立射线投射 + 遮挡属性测试）

> 承接 `docs/superpowers/specs/2026-10-13-comprehensive-testing-design.md` §13「Plan B（生成器 + 属性/模糊）」第一条：
> **「遮挡 oracle 是必做项：Plan A 没有任何测试产生 `HiddenCount > 0`，`DepthRaster.Split` 的可见/隐藏拆分、
> `ViewSegmentCleanup` 的合并/丢弃分支、`ViewArcFitter` 的接受路径从未非平凡执行；也没有任何测试断言单条线段的端点，
> 因此「包围盒正确但内部几何错误」的投影仍会通过。Tier-5 射线投射 oracle 是唯一能关闭它的手段。」**
>
> 依赖 B1（已在 `main`）：`SolidGen`、`Corpus`、`ViewBridge`、`ViewSegGen`、`HlrAssert`、`ConvexHull`。
> 基线（B2 开工时）：SystemTests 1033 通过 / 0 失败 / 101 NotExecuted；Tests 129/0/1。

## Global Constraints（与 B1 相同）

- `src/` **零改动**；仅动 `tests/`、`docs/testing/`、`tests/DEFECTS.md`。
- 全量构建 `dotnet build OpenCSG.NET.slnx -c Release` 0 警告 0 错误。
- 文件 UTF-8 无 BOM、LF；`tests/OpenCSG.NET.Tests/Results/` 恒为 45 个 `.stl`、零 `*_.stl`。
- `SolidTest.cs`、`GoldenGuard.cs`、`OpenCSG.NET.TestKit.csproj` 不可改。
- 缺陷处置：发现真实缺陷 → 登记 `DEFECTS.md`（下一个空闲编号）+ `[Explicit]` 用例 + 最小复现；**不改 `src/`**。
- `TEST-REPORT-2026-10-13.md` 追加 B2 章节，不得残留 `<填实际>`。
- 过程产物（简报/报告/审查包/探针）在 `.superpowers/sdd/2026-10-13-comprehensive-testing-plan-b2/` 与 `artifacts/`（均 gitignored）。

## 设计基线（控制器探针 `artifacts/b2probe` 预跑校准，勿重复摸索）

**管线事实**（`ViewProjector.ProjectView`）：
1. 候选特征边 = 边界边（`TriB<0`）∪ 轮廓边（两侧正对性不同）∪（两侧都正对且 `IsSharp`）。
   两侧都背对 ⇒ 不画。
2. 消隐：正对相机的三角形光栅化到 900×N 深度格（cell = 屏幕长边/900），每格存最小深度；
   边按长度采样（≤4000 点），`depth ≤ cellMin + bias` ⇒ 可见，`bias = 深度跨度×1e-3 + 1e-9`。
3. 输出经 `ViewArcFitter.Fit`（弧从 `Segments` 移出）→ `ViewSegmentCleanup.Clean`（按**同可见性**合并/丢弃）。

**Tier-5 oracle 设计（已原型验证）**：
- **射线投射**：Möller–Trumbore，独立标量实现（不调 `DepthRaster`）。从 3D 边点沿 `−Look` 投射；
  跳过该边相邻三角形 + `t ≤ 1e-7` 自命中；无命中 ⇒ 可见；命中距离 `tHit ≤ bias` ⇒ 可见（深度偏置内不计）；否则隐藏。
- **匹配**：对每条输出线段（含弧）的内部采样点 q：找 2D 重合的候选边（各自独立重建），
  把每条候选边在 q 的 3D 插值点做 **9 探针**（q 与 q±cell 的 8 邻域）分类：
  - 全探针可见 ⇒ **robust V**；全探针隐藏 ⇒ **robust H**；混合 ⇒ INDET（格量化敏感带）。
- **双侧判定**（对每个采样点）：
  - 稳健类集 `R` 必须 ⊆ 投影类集 `P`（含弧的 `IsVisible` 按角域计入）——「丢了该有的类」= 失败；
  - 投影类集 `P` 必须 ⊆ 可能类集 `M`——「出现了没有任何边能解释的类」= 失败；
  - `R` 为空 ⇒ 视为 INDET，跳过并计数。
- **轴对齐夹具全绿**（32 种子 × 3 视图预跑）：`back.Union(front.Translate(3,0,0))` 左视 H=2；L 形 俯视/左视 H=2；
  带通孔方板 主视 H=5、俯视 H=3 —— **首次让 `HiddenCount > 0` 非平凡出现**。
- **已知边界（写进 oracle 文档）**：掠射轮廓/孔缘（视线与面几乎平行）处，射线沿面平面travel（det≈0 不命中）而投影器按格判定，
  会出现 robust-V vs proj-H 的分歧（`artifacts/b2probe`：带孔板**侧视** 14/975 采样点）。该现象与 **DEFECT-004 机制②**
  （「消隐栅格把掠射轮廓切成可见/隐藏交替碎段」）同族 —— 属已登记缺陷的行为域，**夹具避开这类视角**，
  语料属性测试遇到时按 Task 4 的豁免流程处置（登记/引用 DEFECT-004，不静默放宽容差）。

## 文件结构

- Create: `tests/OpenCSG.NET.TestKit/Oracles/RayCast.cs`（射线-三角形相交 + 9 探针分类）
- Create: `tests/OpenCSG.NET.TestKit/Oracles/OcclusionAssert.cs`（候选边重建 + 2D 重合匹配 + 双侧判定）
  — 或作为 `HlrAssert` 的扩展方法；实施时由 Task 1/2 决定，优先独立文件（单一职责）
- Create: `tests/OpenCSG.NET.SystemTests/Property/Tier5OcclusionFixtureTest.cs`（手工夹具 + 解析真值）
- Create: `tests/OpenCSG.NET.SystemTests/Property/Tier5OcclusionCorpusTest.cs`（语料属性测试）
- Modify: `tests/DEFECTS.md`（若登记新缺陷）、`docs/testing/TEST-REPORT-2026-10-13.md`

---

### Task 1: RayCast 核心 + 候选边重建 + 单元测试

**Files:**
- Create: `tests/OpenCSG.NET.TestKit/Oracles/RayCast.cs`
- Create: `tests/OpenCSG.NET.SystemTests/Property/Tier5RayCastUnitTest.cs`

**Interfaces（Produces）**：`public static class Csg.Test.RayCast`
- `bool Intersect(ViewTriangle t, ViewPoint origin, ViewVector direction, out double t)` — Möller–Trumbore 标量实现
- `double NearestHitDistance(IReadOnlyList<ViewTriangle> triangles, ViewPoint origin, ViewVector direction, int skipA, int skipB, double minT)` — 命中距离（无命中返回 `double.PositiveInfinity`）
- `ViewDirection` 无关（纯几何）

**参考实现**：`artifacts/b2probe/Program.cs` 的 `Intersect`（已实测：轴对齐夹具全绿；跳过 `TriA/TriB`、`t > 1e-7`）。

**Step 1（TDD）**：`Tier5RayCastUnitTest`（手工三角形汤，不依赖生成器）：
- 正对观察者的单三角形：从三角形后方（+Look 侧）沿 `−Look` 投射命中，`t` 等于距离；从前方投射不命中（`t<0` 丢弃）。
- 两块平行板（相距 5mm）：点夹在中间时，`NearestHitDistance` 返回近板的距离（0.5mm），跳过自身面后返回远板距离。
- 退化三角形（面积 0）：不命中、不除零。
- 射线平行于三角形平面：不命中（`det ≈ 0` 分支）。
- 射线穿过三角形共享边（T 接缝数值）：命中或按容差判定 —— 断言「至少一命中」（不得两边都漏）。
- 命中点在三角形内/边上（u,v∈[0,1] 边界）：`u=0`、`u+v=1` 均命中。

**Step 2–4**：常规红→绿→提交。

**Commit**: `test: Tier 5 RayCast 核心（Möller–Trumbore + 近命中查询）`

---

### Task 2: OcclusionAssert —— 候选边重建 + 2D 重合匹配 + 双侧判定 + 轴对齐夹具验证

**Files:**
- Create: `tests/OpenCSG.NET.TestKit/Oracles/OcclusionAssert.cs`
- Create: `tests/OpenCSG.NET.SystemTests/Property/Tier5OcclusionOracleTest.cs`

**Interfaces（Produces）**：`public static class Csg.Test.OcclusionAssert`
- `void AssertVisibilityMatchesRayCast(IReadOnlyList<ViewProjection> views, IReadOnlyList<ViewTriangle> triangles, string? message = null)` — 对每个视图执行双侧判定；返回/记录统计（checked / indet / 失败明细）。
- 内部：`CandidateEdges`（独立重建：从 `ViewMeshTopology` 的 `Edges` + 正对性规则，规则文本与 `ViewProjector` 文档对齐）、`ClassifyProbes`（9 探针）、`ProjClassesAt`（含弧按角域计入）。

**接口设计注意**（探针实测的两个坑，必须处理）：
1. **弧要计入投影类集**：被弧消费的几何不在 `Segments` 里；点落在弧圆上（`|dist−R| ≤ 0.06` = 容差×1.2）且角域内 ⇒ 把 `arc.IsVisible` 加入投影类集。漏掉它会让带孔板/球体等弧多的视图大量误报（实测 465 → 14）。
2. **INDET 不参与双侧判定但必须计数**：`robustClasses` 为空 ⇒ skip + `indetCount++`；最终断言 `checkedCount > 0`（防空洞）并在消息里报告 `indet` 比例。

**验证（Step 4）**：`Tier5OcclusionOracleTest` 上跑三个轴对齐夹具（与探针对齐）：
- `back.Union(front.Translate(3,0,0))` 三视图：0 失败；
- L 形（方块减方块）三视图：0 失败；
- 带通孔方板 主视 + 俯视：0 失败（侧视不在夹具内，理由写注释：掠射孔缘属 DEFECT-004 机制②行为域）。
- 每视图断言 `HiddenCount > 0`（除自遮挡不存在的视图外）——这是本计划的核心目标（关闭「零 HiddenCount」盲区）。

**失败分类**：oracle 语义问题（探针口径）→ 按 Task 10 的经验带测量修正；真实缺陷 → 走 DEFECTS.md。

**Commit**: `test: Tier 5 遮挡 oracle（候选边重建 + 重合匹配 + 9 探针双侧判定）`

---

### Task 3: 遮挡夹具与解析真值

**Files:**
- Create: `tests/OpenCSG.NET.SystemTests/Property/Tier5OcclusionFixtureTest.cs`

**内容（每个夹具 = 解析可算的期望 + ray-cast 复核）**：
1. **前后双盒**：`back = Cube(10)`；`front = Cube(10).Translate(3,0,20)`（分开 20 ± 5 → 前块截面 [−2,8]×[−5,5]。
   左视（沿 −Y 观察、Right=−X、Up=+Z）：后块的 y=−5 面棱边被前块遮挡的区间可解析计算（前块 x∈[−2,8] → 2D h=−x ∈ [−8,2]），
   断言隐藏段的总长与端点（容差 1e-3，cell 级精度）与解析值一致；且 `AssertVisibilityMatchesRayCast` 通过。
2. **L 形**（方块减右上角）：俯视/左视的隐藏段 = 阶梯远侧棱边，解析区间可算。
3. **带通孔方板**：主视（孔轴与视线平行）的隐藏段 = 孔壁远侧轮廓；断言 H>0 且 ray-cast 通过。
4. 每个夹具同时断言 `VisibleCount + HiddenCount == Segments.Count` 与 `HiddenCount ≥ 1`。

**风险**：解析值与 Clean 的合并/阈值交互（碎段合并）—— 断言容忍：总长 ±0.5%、端点为「包含关系」而非精确相等（写清理由）。

**Commit**: `test: Tier 5 遮挡夹具与解析真值（前后盒/L 形/通孔板）`

---

### Task 4: 语料属性测试（Corpus × 三视图，豁免流程）

**Files:**
- Create: `tests/OpenCSG.NET.SystemTests/Property/Tier5OcclusionCorpusTest.cs`
- Modify: `tests/DEFECTS.md`（若发现新缺陷族）

**内容**：
- `Corpus` 全量（512 specs 的 Normal/Survey 档按 B1 Task 6 的既有采样口径）× 三视图，逐视图跑 `AssertVisibilityMatchesRayCast`。
- **豁免流程**（照 Tier 1 模式）：若某 spec 失败 → 解剖归因：
  - 掠射/孔缘族（与 DEFECT-004 机制②同族）⇒ 引用 DEFECT-004 写进该用例的 `[Explicit]`，**不登记新号**；
  - 其他真实缺陷 ⇒ 登记 `DEFECT-005+` + `[Explicit]` + 最小复现；
  - oracle 语义问题 ⇒ 修正 oracle（带测量证据）。
- **覆盖门（防退化）**：语料跑完后断言聚合指标：
  - 经历至少 1 个 `HiddenCount > 0` 视图的 spec 数 ≥ 某个实测下限（首次运行确定后写死，注明「下限 = 实测 × 0.8」）；
  - `checkedCount` 总数 ≥ 实测下限；
  - 全语料 `indet` 比例 ≤ 实测值 + 5pp。
- **时间预算**：全量若 > Smoke 预算（8 分钟内含 Tier1-3），则本用例加 `[Category("Corpus")]`（移出 Smoke），报告写明。

**Commit**: `test: Tier 5 语料遮挡属性测试与覆盖门`

---

### Task 5: 收口（报告 + 全量验证）

- `TEST-REPORT-2026-10-13.md` 追加「Plan B2」章节：oracle 口径、夹具表、语料指标（checked/indet/失败/豁免）、时间预算、DEFECTS 变更；
  零 `<填实际>`。
- 全量验证：build 0/0；Tests 129/0/1；SystemTests 全绿（新基线数字如实记录）；45 stl / 0 残留。
- 更新 spec §13：勾掉遮挡 oracle 必做项（注明由 Plan B2 承接完成）。

**Commit**: `docs(test): Plan B2 收口——Tier 5 遮挡 oracle 报告与承接勾选`

---

## 完成定义（Plan B2）

- [ ] `RayCast`/`OcclusionAssert` 落地且**独立于** `DepthRaster`（不调其任何成员）。
- [ ] 至少 3 个手工夹具 × 对应视图出现 `HiddenCount > 0`，且 ray-cast 双侧判定 0 失败。
- [ ] 语料属性测试全绿（含豁免）且覆盖门写死为实测下限。
- [ ] 全量构建 0 警告 0 错误；Tests 129/0/1；`Results/` 45/0。
- [ ] `src/` 零改动；新缺陷（若有）已登记 + `[Explicit]` + 最小复现。
- [ ] `TEST-REPORT` 零占位符；spec §13 遮挡项已勾。
