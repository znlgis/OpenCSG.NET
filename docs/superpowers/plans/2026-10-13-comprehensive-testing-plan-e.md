# Plan E — L6 性能/复杂度 + L7 稳健性/并行/跨运行时

> 承接 spec §13 与阶段表第 6 阶段：「L6 perf/复杂度 + L7 稳健性/并行/跨运行时 | 基线 JSON + 并行与跨运行时结论」。
> 依赖全部 TestKit（B1/B2/C/D 已并入 `main`）。
> 基线：Tests 342/0（+7 Explicit）；SystemTests 1093/0/160；`Results/` 60 STL + 15 JSON 零残留；0 警告 0 错误。

## Global Constraints

- `src/` 零改动；`samples/`（Runner.Examples 守卫）与 `tools/`、`perf/`、`tests/` 可动（spec 授权范围）。
- 基线 JSON 提交进仓（`perf/` 下）；门禁 = 25% 回归（nightly 用；本计划产出工具与基线）。
- 时间预算类断言必须「先实测、预算 = 实测 × 明确倍数并注明」；不得拍脑袋。
- 文件 UTF-8 无 BOM + LF；全量构建 0 警告 0 错误；缺陷登记 `DEFECT-010+`（只报告）。
- 过程产物在 `.superpowers/sdd/2026-10-13-comprehensive-testing-plan-e/`（gitignored）。

## 侦察结论

- `perf/OpenCSG.NET.PerfTest/`：net8.0、BenchmarkDotNet 0.13.5、现有 `PerfTest.Run()` 为**停表自测**双基准（sphere−sphere Resolution 10/50），无基线文件、无阈值。
- 机器 SDK：8.0.425 / 9.0.318 / 10.0.x（net10 可用 ⇒ 跨运行时对比可本地做）；仓库根**无 `global.json`**（Plan F 才建 CI 钉扎）。
- `Tessellation.ChordTolerance` 为 `[ThreadStatic]` 且已有 `TessellationScope`（B1）；`ViewArcFitter.DiagnosticsEnabled`/`LastDiagnostics` 非 ThreadStatic（= DEFECT-001，已登记）；B1 已建 `GlobalStateGuard`。
- 观察项承接：SystemTests 单次偶发失败（26 次未复现）→ 本计划 soak；`Runner.Examples` 的 `Process.Start` 未守卫 → 本计划补。

## 文件结构

- Modify: `perf/OpenCSG.NET.PerfTest/`（扩展基准 + 基线导出 + 门禁模式）
- Create: `perf/baseline.json`（提交）
- Create: `tests/OpenCSG.NET.SystemTests/Robustness/`（L7 测试）
- Create: `tools/OpenCSG.NET.CrossRuntime/`（跨运行时哈希工具，net8.0+net10.0 双目标）
- Modify: `samples/Runner.Examples/Program.cs`（环境守卫）
- Modify: `tests/DEFECTS.md`（若登记）、`docs/testing/TEST-REPORT-2026-10-13.md`

---

### Task 1: L6 perf 基准扩展 + 基线 JSON + 25% 门禁工具 + 复杂度/边界预算

**范围（spec §7 L6 行逐条）**：
1. **基准扩展（11 项操作，~14 条基准）**：在 `perf/OpenCSG.NET.PerfTest/` 增**停表自测模式**（确定性输入、N 次重复取中位数，避开 BDN 长运行）：
   `Union`（中号语料实体）、`Intersect`、深树 `Evaluate`（`NodeGen` 中档树）、`ToJson`、`FromJson`、`WriteAscii`、`WriteBinary`（临时文件）、`CsgMesh.TrianglesOf`、`ViewProjector.Project`（三视图）、`ViewArcFitter.Fit`（`ViewSegGen` 大样本）、`ViewSegmentCleanup.Clean`。
   每条记录 `medianMs`（可加 `bytes` 分配）；输出 `perf/baseline.json`（结构 `{"version":1,"env":{...},"items":[{"op":...,"medianMs":...}]}`）。
2. **25% 门禁模式**：`--gate <baseline.json>` ⇒ 重跑并对每条 `new > old × 1.25` 判失败（exit 1，列出明细）；`--write-baseline <path>` 生成基线。文档：nightly 用法 + 「机器噪声 >25% 时不阻塞 PR」的口径（写进 `perf/README` 或脚本头注释）。
3. **复杂度标度（写进同一工具的一条 `--complexity` 模式）**：`Clean` 在 n=5k 与 20k 的耗时预算（预算 = 实测 × 4，注明）；`ViewProjector.Project` 在 5k/20k 三角形的 900² 栅格耗时预算（同法）。**O(n²) 判定**：记录 20k/5k 耗时比（理论 ≈16×），断言 `< 32`（宽界，防忽然多项式升级；先实测再定）。
4. **边界（SystemTests `[Category("Perf")]` 用例，~6）**：
   - 薄板 z-fighting：厚度/尺度比低于栅格量化的薄板（如 1000mm 板上 0.01mm 厚凸台）投影行为——先实测（是否出现 V/H 抖动/丢面），按结论写「行为钉」并引用 DEFECT-004/005 族；
   - `MaxEdgeSamples=4000` 边界：1e6 长圆柱棱边（采样被 4000 截断）——断言不挂起 + 输出拓扑计数合理（实测后钉）；
   - `Clean` n=5k/20k 的预算断言（与 3 同源，测试侧复核）；
   - `Fit` 大输入预算；
   - 深树 Evaluate 预算；
   - `DepthRaster` 2700 格（900×3 视图复用）下 `Project` 预算。
   预算一律 = 实测 × 5（测试比工具放宽一档，抵 CI 抖动），注释写实测值。

**验证**：`--write-baseline` 生成、`--gate` 对自身基线通过（1.0×）、人为把某条基线数字改小 30% ⇒ 门禁失败（判别力自证）；复杂度模式输出入报告；SystemTests +6 用例全绿。
**Commit**: `perf: L6 基准扩展、基线 JSON 与 25% 门禁；复杂度/边界预算用例`

### Task 2: L7 稳健性/并行/跨运行时（~18）

1. **并行整套件**：加 `tests/ParallelRun.runsettings`（`NUnit.NumberOfTestWorkers` 4）并**实测**：`dotnet test tests/OpenCSG.NET.Tests -c Release --settings tests/ParallelRun.runsettings` 与 SystemTests 同法各跑 1–2 次，全绿且与串行计数一致 ⇒ 报告（不入 CI 默认；Plan F 决定）。**若并行暴露串扰/偶发失败 ⇒ 按程序规则登记/归因**（`Tessellation.ChordTolerance` 的 `[ThreadStatic]`+scope、`ViewArcFitter.DiagnosticsEnabled` 非 ThreadStatic 的 DEFECT-001 是重点怀疑对象——用现有 `GlobalStateGuard` 模式定位）。
2. **TessellationScope 跨线程**：并发任务各置不同 `ChordTolerance`（scoped）⇒ 互不串扰（`[ThreadStatic]` 语义验证）；scope 退出恢复原值（含异常路径——B1 已测单线程，此处补并发）。
3. **DEFECT-001 的并行演示**：并行下 `ViewArcFitter.DiagnosticsEnabled`/`LastDiagnostics` 串扰的可复现证据（`[Explicit("DEFECT-001: …")]`——若 B1 已有用例则补并行语境一例并引用）。
4. **`Solid.GetTag()` 审计 + 用例**：仓库内无用例依赖 tag 值（grep 证据）；补 tag 读写语义用例（含默认值）。
5. **跨运行时确定性**：新工具 `tools/OpenCSG.NET.CrossRuntime/`（`<TargetFrameworks>net8.0;net10.0</TargetFrameworks>`）：对固定输入集（语料子集求值 + 三视图投影 + 序列化 + STL 写出）打印组合 SHA256；任务内实测两 TFM 哈希一致（不一致 ⇒ 登记缺陷并给出首个差异定位）；命令文档化（nightly 可跑）。
6. **soak**：SystemTests 连跑 ~25 次（TRX 全开）猎捕历史偶发失败；结果（含未复现结论或新证据）入报告。
7. **Runner.Examples 守卫**（承接观察项）：同 `OPENCSG_OPEN_VIEWER == "1"` 门控。

**验证**：并行两次全绿；跨运行时哈希一致；soak 结论；构建 0/0。
**Commit**: `test: L7 稳健性——并行整套件/跨线程 scope/跨运行时哈希/soak；Runner.Examples 守卫`

### Task 3: 收口

- `TEST-REPORT` 追加「Plan E」节：perf 基线与门禁、复杂度标度实测、并行/跨运行时/soak 结论、预算用例表、新缺陷（若有）；零占位符。
- spec §13 Plan E 相关项勾选（L6/L7 行在 §7）；计划完成定义勾选；全量验证。
**Commit**: `docs(test): Plan E 收口——perf 基线/并行与跨运行时结论`

## 完成定义（Plan E）

**状态：已全部完成（2026-10-14，分支 `test/comprehensive-suite-plan-e`）。** 逐项核对：

- [x] 11 项操作基准可复跑、`perf/baseline.json` 提交、25% 门禁判别力自证（×0.7 ⇒ exit 1；快操作信息项地板与失败重测路径实测触发；噪声限口径已文档化）。
- [x] 复杂度标度（Clean 5k=33.2/20k=572.1min，比值 17.59 ≈ O(n²) 16、上限 32）与 6 个边界用例按「实测 ×5」预算落地；**边界发现一并登记 DEFECT-010**（薄板分解缝幻线）。
- [x] 并行整套件 8+4 次全绿（首跑即实证 **DEFECT-001 串扰**并完成隔离/复现/登记）；跨线程 scope 3 用例；跨运行时哈希两 TFM 一致（97f2f11d…）；soak 25/25。
- [x] `Runner.Examples` 环境守卫补齐；`Solid.GetTag` 审计+5 用例。
- [x] 全量构建 0 警告 0 错误；`Results/` 零残留；`src/` 零改动。

> 逐任务评审/fix round/核对记录与遗留观察项见 `.superpowers/sdd/2026-10-13-comprehensive-testing-plan-e/progress.md`。
> 两处审查改判案例：E1 首轮「无 C/无 I」后由控制器补齐评审、E2 的 Critical（`[NonParallelizable]` 只写注释未应用）均由评审抓获并以 TRX 区间分析实证修复。
