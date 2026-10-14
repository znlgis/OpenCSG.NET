# Plan F — 覆盖率门禁 + CI 改造 + 报告定稿（收官）

> 承接 spec §8（覆盖率与 CI，8.2 的 8 项已批准改动）与 §13「Plan F」。
> 依赖 corpus.json、TEST-REPORT、全部前序计划（A/B1/B2/C/D/E 已并入 `main`）。
> 基线：Tests 342/0（+8 Explicit）；SystemTests 1109/0/160；`Results/` 60 STL + 15 JSON 零残留；覆盖率（C 收口实测）src 类均值 98.5%、0% 类 0、未覆盖方法 1（死代码）。

## Global Constraints

- `src/` 零改动（除本计划明确批准的**构建配置**类改动：`tests/Directory.Build.props`、`global.json`、`.runsettings`、测试项目 `TargetFrameworks` —— `src/OpenCSG.NET.csproj` 本身**不动**；PublicApiAnalyzers 属备选方案，**未获批准，不做**，只登记为后续选项）。
- CI 工作流文件只能本地做 YAML 语法校验与「等价命令行」验证（GitHub Actions 不能本地跑）——报告如实声明该边界。
- 文件 UTF-8 无 BOM + LF；全量构建 0 警告 0 错误；`Results/` 零残留；缺陷登记 `DEFECT-011+`（只报告）。
- 过程产物在 `.superpowers/sdd/2026-10-13-comprehensive-testing-plan-f/`（gitignored）。

## 侦察结论

- `.github/workflows/ci.yml`：单 job ubuntu、`setup-dotnet 8.0.x`、只 `build src/OpenCSG.NET` + `test tests/OpenCSG.NET.Tests`（**不编译 slnx/samples/perf/ApiCompat；无覆盖率；无矩阵**）；`nuget-publish.yml`（tag 触发）无关。
- 仓库根**无 `global.json`**；本机 SDK 8/9/10 全有（10.0.400/401）。
- `tests/OpenCSG.NET.ApiCompat`：netstandard2.0 编译型金丝雀（`ApiSurface.cs` 233 行）。
- L6 的 perf 门禁工具（`--gate`）与 Long 档入口（`Corpus.LongCaseCount()` 环境变量覆盖，B1 建）已就绪 ⇒ nightly 可直接接线。

## 文件结构

- Create: `global.json`、`tests/Directory.Build.props`、`tests/coverage.runsettings`（或并入既有 runsettings 目录）、`tools/coverage-gate.py`（或 .sh——控制器倾向 python，解析 cobertura 省事；实施决定并报告）
- Modify: `tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj`、`tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj`（TargetFrameworks）
- Modify: `.github/workflows/ci.yml`；Create: `.github/workflows/nightly.yml`
- Modify: `tests/OpenCSG.NET.ApiCompat/ApiSurface.cs`（补缺口）
- Modify: `docs/testing/TEST-REPORT-2026-10-13.md`（定稿）、`docs/superpowers/specs/2026-10-13-comprehensive-testing-design.md`（Plan F 勾选）

---

### Task 1: `global.json` + `tests/Directory.Build.props` + 测试项目多目标（net8.0;net10.0）

1. `global.json`：钉 **10.0.4xx**（`rollForward: latestFeature`）。**spec 8.2 条目 1（9.0.x）与条目 3（net10 TFM）冲突的裁定**：构建 net10.0 目标需要 SDK 10 ⇒ SDK 与 CI 的 setup-dotnet 统一到 10.0.x；CI 另装 8.0.x 运行时跑 net8 测试。报告写明此裁定。
2. `tests/Directory.Build.props`：`Nullable=enable`、`LangVersion` 与主工程一致；`TreatWarningsAsErrors` **先测后定**（先出警告普查：若 ≤ 少量可修 ⇒ 开启并修净；若洪泛 ⇒ 报告数字并以 `Nullable` 单独先行）。
3. 两个测试项目 `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>`：**两 TFM 各跑全量**（Tests + SystemTests；perf/publication/`Results` 路径两 TFM 共用 ⇒ golden 比对必须两边都绿）。net10 上若出现行为差异 ⇒ 登记缺陷（CrossRuntime 工具已证哈希一致，但测试面更广）。本地默认全跑会翻倍时长——文档说明 `-f net8.0` 快速档。
4. 验证：`dotnet build OpenCSG.NET.slnx -c Release` 0/0；`dotnet test tests/OpenCSG.NET.Tests -c Release`（两 TFM 各 342/0）；`dotnet test tests/OpenCSG.NET.SystemTests -c Release`（两 TFM 各 1109/0）；`Results/` 零残留。

### Task 2: 覆盖率门禁

1. `tests/coverage.runsettings`（coverlet 配置，供 CI/本地一致）+ `tools/coverage-gate.py`：
   - 运行两个测试项目 `--collect:"XPlat Code Coverage"`（net8 档即可，CI 说明）；
   - 解析 cobertura（并集口径与 C 计划一致），计算：**`src/` 整体行覆盖 ≥ 85%**；**`View*` / `CsgHelpers` / `StlWriter` / `CsgMesh` / `Tessellation` 各组 ≥ 90%**（组 = 文件名/类名前缀聚合，规则写死在脚本并注释）；
   - 输出对照表（组 / 行数 / 覆盖数 / 百分比 / 阈值 / 通过与否）与总体数字；不达标 exit 1；
   - 判别力自证：人为把某组阈值抬到不可能的 0.999 ⇒ 失败（或反向：用一份截断的 XML 探针）——不碰真数据。
2. 本地实测出**官方对照数字**（供报告定稿）：总体与五组的行覆盖；与 C 收口的 98.5%/0% 类口径对齐说明。
3. `reportgenerator` HTML 为可选（若装则生成 `artifacts/coverage-html`；未装则只出 cobertura + 表格，报告说明）。

### Task 3: ci.yml 改造 + nightly.yml

1. `ci.yml`（按 spec 8.2 逐条，含裁定）：
   - `setup-dotnet`：`10.0.x`（SDK）；另装 `8.0.x` 运行时（net8 测试用）；
   - `dotnet build OpenCSG.NET.slnx -c Release`（**含 samples/perf/ApiCompat——公共 API 破坏可见**）；
   - `dotnet test tests/OpenCSG.NET.Tests -c Release`（**不筛选/不排除 `GoldenGuardTest`**，全量运行——注释写明 NUnit3TestAdapter 4.4.2 的 fixture-teardown 退出码陷阱）；
   - `dotnet test tests/OpenCSG.NET.SystemTests -c Release`；
   - 覆盖率收集 + `tools/coverage-gate.py` 门禁 + cobertura 工件上传；
   - **矩阵**：`ubuntu-latest` + `windows-latest`（golden 依赖 `Environment.NewLine` 与路径推导；`Results/**/*.json` 已有 `.gitattributes eol=lf`）。
   - 测试多目标已在 Task 1 ⇒ CI 默认跑两 TFM（或按 `-f` 显式跑两遍并注释；实施决定）。
2. `nightly.yml`：`schedule` cron；jobs：(a) Long 档模糊（`Corpus.LongCaseCount()` 环境变量放大 + 全量 SystemTests）；(b) perf 门禁（`--gate perf/baseline.json`，**非阻塞 PR**——workflow 独立于 ci.yml）；报告 artifact 上传。注释写口径（机噪/重跑/基线重生成须 diff）。
3. 验证（本地可做部分）：`python tools/coverage-gate.py`（真跑）；YAML 语法校验（`python -c "import yaml..."` 或 ruby/在线不可用则用 `dotnet` 无 → 用 python yaml 若装，否则人工 + 缩进检查，报告说明）；把 ci.yml 里的每条 run 命令行**手工在本地执行一遍**（等价验证）。

### Task 4: ApiCompat 金丝雀补缺口

- `ApiSurface.cs` 补 spec §13 列出的五类命名缺口（`BoundingBox`、`BoundingSphere`、`PolygonShared`、`CsgEvaluationException`、`ViewVector`）与 `CsgSerialization` 的三个 `Utf8JsonReader` 辅助；跑该工程（它是编译型探针——按现有机制验证，实施报告说明机制）。
- 报告注明：`Microsoft.CodeAnalysis.PublicApiAnalyzers` + `PublicAPI.Shipped.txt` 的彻底方案**未获批准**（需动 `src/` csproj），登记为后续选项。
- Commit 独立：`test(apicompat): 补五类命名缺口与 CsgSerialization Utf8JsonReader 辅助`

### Task 5: 报告定稿（TEST-REPORT 终版）

- 校正逐任务用例数（A→F 各计划段落的自报数与 TRX 终值核对；已知口径差：console 不计 NotExecuted、TRX 才权威）。
- 覆盖率前后对照用 Task 2 的**实际输出**（不得估算；标注「本地 SDK 10.0.4xx + coverlet 6.0.4，命令与 CI 相同；CI 首跑数字以 artifacts 为准」）。
- 新增「已知限制」章：① `modreq`→库内 `internal IsExternalInit` 的隐性运行时契约（已编译 netstandard2.0 消费方依赖它；改 `CsgNode.cs` 前必读）；② DEFECT-001..010 的登记摘要与「只报告」政策；③ perf 门禁的机器噪声口径；④ 并行/soak 结论；⑤ 跨运行时哈希一致结论；⑥ golden 的 `.gitattributes` 适用范围（JSON=LF；STL 字节级基线依赖 Windows `Environment.NewLine`——autocrlf=false 克隆须知）。
- `grep 填实际` 零匹配；全文数字与仓库实态一致。

### Task 6: 终验与收官

- 全量终验：build 0/0；Tests 两 TFM 342/0；SystemTests 两 TFM 1109/0/160；`Results/` 零残留；coverage gate exit 0；perf gate exit 0；cross-runtime 哈希一致。
- spec §13 Plan F 四条 + §8.2 八条逐项勾选；本计划完成定义勾选。
- Commit: `docs(test): Plan F 收官——CI/覆盖率/报告定稿与完成定义`

## 完成定义（Plan F）

> 收官勾选（2026-10-14，终验实测；GitHub Actions 本体不可本地运行——CI 相关项的验证边界见各条括注）。

- [x] `global.json` 钉 SDK 10.0.4xx（含与 spec 8.2 条目 1 冲突的裁定记录）；`tests/Directory.Build.props` 落地；测试项目双 TFM 全绿。 ——实测：`dotnet --version` = 10.0.402；slnx 全量 0 警告 0 错误；Tests 345/0、SystemTests 1109/0，net8.0 与 net10.0 逐位相同。
- [x] 覆盖率门禁（≥85% 总体、五组 ≥90%）工具落地 + 判别力自证 + 官方对照数字入报告。 ——实测：`GATE PASS (exit 0)`；src 98.43%（2942/2989）、`View*` 98.25%（896/912）、四组 100%；判别力探针按预期 exit 1/exit 2；数字已入 TEST-REPORT（本地命令与 CI 逐字相同，CI 首跑以 artifacts 为准）。
- [x] `ci.yml` 满足 8.2 全部 8 条（含 GoldenGuardTest 不过滤、windows 矩阵、slnx 全构建、ApiCompat 编译）；`nightly.yml` 就绪。 ——实测：YAML 语法校验通过；每条 `run:` 命令在 scratch 副本本地等价真跑通过（GitHub Actions 本体不可本地运行，边界已声明；Long 档无消费用例已如实登记）。
- [x] ApiCompat 五类缺口补齐；PublicApiAnalyzers 记为未批准后续项。 ——实测：`ApiSurface.cs` +163 行（4 个 Touch 方法 + 1 个嵌套转换器）；单工程与 slnx 全量编译 0/0；命名负控 CS0246 按预期红；PublicApiAnalyzers 未动 `src/`、仅登记。
- [x] TEST-REPORT 定稿（零占位符、已知限制六项、覆盖率实际数字）；spec §13 Plan F 勾选。 ——实测：`grep 填实际` 零匹配；终稿含逐计划用例数终审（TRX 权威口径、342→345 勘误一行）、覆盖率前后对照与实际命令、「已知限制」六项；spec §8.2 八条与 §13 Plan F 四条已勾选（见 spec 文件）。
- [x] 全量终验全绿；`src/` 除构建配置外的零改动保持。 ——实测：build 0/0；Tests 345/0、SystemTests 1109/0（双 TFM；TRX total=353 / 1269，逐结果 NotExecuted×8 / ×160）；coverage gate exit 0；perf gate exit 0；CrossRuntime 双 TFM 哈希一致（`97f2f11d…def75`）；`Results/` 残留 0（60 STL + 15 JSON 常备资产在册）；`src/` 零改动（Plan F 全程 git diff 实证）。
