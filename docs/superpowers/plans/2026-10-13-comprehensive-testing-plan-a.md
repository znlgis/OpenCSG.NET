# OpenCSG.NET 全面测试体系 — Plan A（阶段 0–1：基础设施）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 OpenCSG.NET 建立测量基线、共享测试工具库（TestKit）、缺失的 Solid→ViewTriangle 桥、golden 强制升级门禁与 netstandard2.0 消费面编译工程 —— 让后续 5 个计划（生成器/单元回填/集成/性能/CI）有地基。

**Architecture:** 新增 `tests/OpenCSG.NET.TestKit`（测试支撑类库，持有生成器/oracle/桥/断言）与 `tests/OpenCSG.NET.ApiCompat`（编译期消费工程）。`GeometryAssert` 从测试项目迁入 TestKit 并保持命名空间 `Csg.Test`、仅放宽可见性 ⇒ 现有 19 个测试文件零改动。`GoldenGuard` 必须放在测试程序集内（NUnit 不扫描被引用程序集里的 `[SetUpFixture]`），它把「新 golden 用例静默 Inconclusive 通过」这一漏洞变成硬失败。

**Tech Stack:** .NET 8.0 SDK（本地亦可用 9/10）、NUnit 3.13.3、NUnit3TestAdapter 4.4.2、Microsoft.NET.Test.Sdk 17.6.0、coverlet.collector。核心库 `netstandard2.0`，测试侧 `net8.0`。

**Spec:** `docs/superpowers/specs/2026-10-13-comprehensive-testing-design.md`

## Global Constraints

- 核心库 `src/OpenCSG.NET` 是 `netstandard2.0`，唯一依赖 `System.Text.Json 8.0.6`。**本计划禁止给 `src/` 加任何依赖、禁止改其坐标口径/容差/算法。**
- `src/OpenCSG.NET/OpenCSG.NET.csproj` 有 `TreatWarningsAsErrors=true` + `GenerateDocumentationFile=true`（仅 `1591` 抑制）。**任何 `src/` 改动引入新警告即构建失败。**
- 库的根命名空间是 **`Csg`**（不是 `OpenCSG.NET`）；程序集名是 `OpenCSG.NET`。
- 测试命名空间是 **`Csg.Test`**。现有 19 个测试文件与 102 个用例**不得修改其断言逻辑**。
- 新增测试依赖只允许加在 `tests/` 与 `tools/` 的项目上。
- **`GeometryAssert` 与 `SolidTest` 的比较逻辑不得改动。** `SolidTest` 基类不迁移、不改路径推导。
- **提交策略**：用户已授权按本计划的 commit 步骤自动提交（每任务一次提交）。提交信息用计划中给出的原文；提交前先 `git status` 确认没有夹带无关文件。
- 所有新增文本文件用 UTF-8 无 BOM；`.cs` 用 LF（按 `.editorconfig` 的 `end_of_line = lf`）。
- 命令统一在仓库根目录 `D:\self\code\OpenCSG.NET` 执行。

## 文件结构

| 文件 | 职责 |
| --- | --- |
| `tests/OpenCSG.NET.TestKit/OpenCSG.NET.TestKit.csproj` | 新建；`net8.0`；引用核心库 + NUnit（因 `GeometryAssert` 的断言方法用 NUnit） |
| `tests/OpenCSG.NET.TestKit/GeometryAssert.cs` | **从 `tests/OpenCSG.NET.Tests/GeometryAssert.cs` 迁移**；仅 `internal static class` → `public static class` |
| `tests/OpenCSG.NET.TestKit/GoldenPaths.cs` | 新建；自底向上定位 `Results/`（替代脆弱的 4 层 `GetDirectoryName`） |
| `tests/OpenCSG.NET.TestKit/MeshAssert.cs` | 新建；在 `List<MeshTriangle>` 上做有向体积 / 边界边 / 非流形边 / 欧拉特征 |
| `tests/OpenCSG.NET.TestKit/ViewBridge.cs` | 新建；库内缺失的 `Solid` → `IReadOnlyList<ViewTriangle>` 适配器 |
| `tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj` | 改；加 TestKit 引用 + coverlet.collector |
| `tests/OpenCSG.NET.Tests/GoldenGuard.cs` | 新建；`[SetUpFixture]`，残留 `*_.stl` 即整套件失败 |
| `tests/OpenCSG.NET.Tests/MeshIo/CsgMeshTest.cs` | 新建；`CsgMesh` 反射补偿回归测试（用 `MeshAssert`） |
| `tests/OpenCSG.NET.Tests/Views/ViewBridgeTest.cs` | 新建；桥 + 坐标约定 oracle |
| `tests/OpenCSG.NET.Tests/Infrastructure/GoldenGuardTest.cs` | 新建；验证门禁本身有效 |
| `tests/OpenCSG.NET.ApiCompat/OpenCSG.NET.ApiCompat.csproj` | 新建；`netstandard2.0` 编译期消费工程 |
| `tests/OpenCSG.NET.ApiCompat/ApiSurface.cs` | 新建；消费公共 API 的编译期探针 |
| `tools/promote-goldens.sh` | 新建；把 `Results/*_.stl` 升级为 `*.stl` |
| `.gitignore` | 改；加一行忽略 `*_.stl` |
| `OpenCSG.NET.slnx` | 改；登记 TestKit 与 ApiCompat |
| `docs/testing/TEST-REPORT-2026-10-13.md` | 新建；从阶段 0 基线开始记录，后续阶段持续追加 |

---

### Task 1: 阶段 0 基线测量

建立"改进前"的真实数字。没有基线，"覆盖率从 X 提到 Y" 无法验证。

**Files:**
- Modify: `tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj`
- Create: `docs/testing/TEST-REPORT-2026-10-13.md`

**Interfaces:**
- Consumes: 无（第一个任务）
- Produces: 无代码接口。产出 `TEST-REPORT` 的「阶段 0 基线」章节，含 5 个数字：用例总数、通过数、失败数、Inconclusive 数、墙钟耗时，以及覆盖率基线（行/分支）。

- [ ] **Step 1: 给测试项目加 coverlet.collector，并建立产物目录**

把 `tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj` 改为（新增的只有 `coverlet.collector` 一行）：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="nunit" Version="3.13.3" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.4.2" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.6.0" />
    <PackageReference Include="coverlet.collector" Version="6.0.4">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\OpenCSG.NET\OpenCSG.NET.csproj" />
  </ItemGroup>

</Project>
```

`6.0.4` 是本地 NuGet 缓存已有版本，避免额外网络往返。

`.gitignore` 末尾追加（产物目录与本计划后续所有命令的输出位置）：

```
# 测试产物（覆盖率报告、物化数据集、基准输出）
artifacts/
```

- [ ] **Step 2: 跑基线测试并记录通过数**

```bash
cd /d/self/code/OpenCSG.NET
mkdir -p artifacts
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release \
  --logger "console;verbosity=normal" 2>&1 | tee artifacts/baseline-test.log
tail -5 artifacts/baseline-test.log
```

预期：`Passed!` 且 `Inconclusive` 计数为 0（45 个 golden 全部已同步）。记录实际数字，**如果出现任何 `Failed` 或 `Inconclusive`，先停下来报告，不要继续** —— 那意味着基线本身不干净。

- [ ] **Step 3: 跑基线覆盖率**

```bash
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release \
  --collect:"XPlat Code Coverage" \
  --results-directory artifacts/baseline-coverage 2>&1 | tail -20
ls artifacts/baseline-coverage/*/coverage.cobertura.xml
```

覆盖率 XML 落在 `artifacts/baseline-coverage/<guid>/coverage.cobertura.xml`。

**不用 `dotnet tool install -g reportgenerator`** —— 那会往全局环境装工具。覆盖率数字直接从 cobertura XML 读。

- [ ] **Step 4: 从 cobertura XML 提取总体与模块级覆盖率**

```bash
node -e '
const fs = require("fs"), path = require("path");
const dir = "artifacts/baseline-coverage";
const sub = fs.readdirSync(dir).find(d => fs.existsSync(path.join(dir, d, "coverage.cobertura.xml")));
const xml = fs.readFileSync(path.join(dir, sub, "coverage.cobertura.xml"), "utf8");

const totalTag = (xml.match(/<coverage\s[^>]*>/) || [""])[0];
const tlr = /\bline-rate="([^"]*)"/.exec(totalTag);
const tbr = /\bbranch-rate="([^"]*)"/.exec(totalTag);
console.log(`TOTAL  line-rate=${tlr ? tlr[1] : "n/a"}  branch-rate=${tbr ? tbr[1] : "n/a"}`);

const want = ["ViewArcFitter","ViewProjector","ViewProjectorContracts","ViewSegmentCleanup",
              "ViewMeshTopology","ViewMesh","ViewGeometry","CsgHelpers","HalfPlaneCut",
              "StlWriter","CsgMesh","Tessellation","PrimitiveConvention"];
for (const tag of xml.match(/<class\s[^>]*>/g) || []) {
  const n = /\bname="([^"]*)"/.exec(tag); if (!n) continue;
  if (!want.some(w => n[1].includes(w))) continue;
  const lr = /\bline-rate="([^"]*)"/.exec(tag);
  const br = /\bbranch-rate="([^"]*)"/.exec(tag);
  console.log(`${n[1]}  line=${lr ? lr[1] : "n/a"}  branch=${br ? br[1] : "n/a"}`);
}
'
```

抄下 `TOTAL` 的行/分支覆盖率（对应 `src/OpenCSG.NET` 整体），以及 13 个模块各自的行覆盖率（预期 `View*`/`CsgHelpers`/`HalfPlaneCut`/`StlWriter`/`CsgMesh`/`Tessellation`/`PrimitiveConvention` 接近 0）。

**如果某个模块没出现在输出里**：说明该文件在本次运行中一行都没被执行（coverlet 可能仍列出但 `line-rate="0"`；若完全缺失，记为 `0`）。

- [ ] **Step 5: 建立缺陷登记表**

创建 `tests/DEFECTS.md`：

```markdown
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
| （暂无） | | | | | | | |

## 已修复

| ID | 模块 | 修复方式 | 回归用例 |
| --- | --- | --- | --- |
| （暂无） | | | |
```

`DefectRegistryTest` 在 Plan C（L1/L2 回填）中随首个缺陷登记一起实现；本步骤只建立表与规则。

- [ ] **Step 6: 写基线章节**

创建 `docs/testing/TEST-REPORT-2026-10-13.md`：

```markdown
# OpenCSG.NET 全面测试报告

日期：2026-10-13
Spec：`docs/superpowers/specs/2026-10-13-comprehensive-testing-design.md`

## 阶段 0 — 基线（改动前）

测量环境：Windows 10.0.26300 / .NET SDK 10.0.401 / `-c Release`
说明：仓库无 `global.json`，SDK 未固定，`dotnet` 浮动至本机最新安装版本；本报告测量数据均由上述 SDK 产出，SDK 固定已登记为 Plan F（CI）事项。

### 用例

| 指标 | 值 |
| --- | --- |
| 用例总数 | <填实际> |
| 通过 | <填实际> |
| 失败 | <填实际> |
| Inconclusive | <填实际> |
| 墙钟耗时 | <填实际> |

### 覆盖率（改动前）

| 指标 | 值 |
| --- | --- |
| src/OpenCSG.NET 行覆盖率 | <填实际> |
| src/OpenCSG.NET 分支覆盖率 | <填实际> |

### 模块级行覆盖率（改动前）

| 模块 | 行覆盖率 |
| --- | --- |
| ViewArcFitter | <填实际> |
| ViewProjector | <填实际> |
| ViewProjectorContracts | <填实际> |
| ViewSegmentCleanup | <填实际> |
| ViewMeshTopology | <填实际> |
| ViewMesh | <填实际> |
| ViewGeometry | <填实际> |
| CsgHelpers | <填实际> |
| HalfPlaneCut | <填实际> |
| StlWriter | <填实际> |
| CsgMesh | <填实际> |
| Tessellation | <填实际> |
| PrimitiveConvention | <填实际> |
```

把 `<填实际>` 全部换成 Step 2–4 的真实数字。**不允许估算。**

- [ ] **Step 7: 验证报告里没有占位符**

```bash
grep -n "填实际" docs/testing/TEST-REPORT-2026-10-13.md
```

预期：无输出（退出码 1，`grep` 无匹配）。

- [ ] **Step 8: 提交（需用户确认）**

```bash
git add tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj .gitignore \
        tests/DEFECTS.md docs/testing/TEST-REPORT-2026-10-13.md
git commit -m "test: 接入 coverlet、建立缺陷登记表并记录测试/覆盖率基线"
```

---

### Task 2: 创建 TestKit 并迁移 GeometryAssert

**Files:**
- Create: `tests/OpenCSG.NET.TestKit/OpenCSG.NET.TestKit.csproj`
- Move: `tests/OpenCSG.NET.Tests/GeometryAssert.cs` → `tests/OpenCSG.NET.TestKit/GeometryAssert.cs`
- Create: `tests/OpenCSG.NET.TestKit/GoldenPaths.cs`
- Modify: `tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj`（加 ProjectReference）
- Modify: `OpenCSG.NET.slnx`（登记 TestKit）
- Test: 现有 102 个用例必须全绿（这就是本任务的验证）

**Interfaces:**
- Consumes: Task 1 的测试项目状态
- Produces:
  - `Csg.Test.GeometryAssert`（`public static class`，命名空间不变）—— 全部原有静态方法签名不变：`MergeTolerance`、`ManifoldReport`、`SignedVolume(Solid)`、`Analyze(Solid)`、`AssertClosedOrientedManifold(Solid, string)`、`AssertOrientationConsistent(Solid, string)`、`AssertPlanesMatchWinding(Solid, string)`、`AssertVolume(Solid, double, double, string)`、`AssertBounds(Solid, Vector3D, Vector3D, double, string)`、`Bounds(Solid)`、`AssertSolidInvariants(Solid, string)`
  - `Csg.Test.GoldenPaths.ResultsDirectory(string anchorPath) → string?`

- [ ] **Step 1: 建 TestKit 项目文件**

`tests/OpenCSG.NET.TestKit/OpenCSG.NET.TestKit.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="nunit" Version="3.13.3" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\OpenCSG.NET\OpenCSG.NET.csproj" />
  </ItemGroup>

</Project>
```

NUnit 是必需依赖：`GeometryAssert` 的 `Assert*` 方法直接调用 `NUnit.Framework.Assert`。TestKit 不依赖 FsCheck 与任何 test SDK —— 生成器与 oracle 的纯逻辑部分不触碰 NUnit，`tools/OpenCSG.NET.DataGen` 只使用纯逻辑部分。

- [ ] **Step 2: 移动 GeometryAssert 并放宽可见性**

```bash
git mv tests/OpenCSG.NET.Tests/GeometryAssert.cs tests/OpenCSG.NET.TestKit/GeometryAssert.cs
```

然后把 `tests/OpenCSG.NET.TestKit/GeometryAssert.cs:20` 的

```csharp
    internal static class GeometryAssert
```

改为

```csharp
    public static class GeometryAssert
```

**然后做 7 处纯注解改动。** TestKit 开了 `<Nullable>enable</Nullable>`（本步骤的 csproj），而 `GeometryAssert` 写于可空注解之前 —— 该文件的 6 个 `string message = null` 参数会产生 6 个 `CS8625` 告警，把 `message` 传给 `Describe` 又会级联 `CS8604`。所以把这 7 处参数类型放宽为可空引用：

| 行 | 改动 |
| --- | --- |
| 154 | `AssertClosedOrientedManifold(Solid solid, string? message = null)` |
| 172 | `AssertOrientationConsistent(Solid solid, string? message = null)` |
| 184 | `AssertPlanesMatchWinding(Solid solid, string? message = null)` |
| 213 | `AssertVolume(Solid solid, double expected, double tolerance, string? message = null)` |
| 220 | `AssertBounds(Solid solid, Vector3D expectedMin, Vector3D expectedMax, double tolerance, string? message = null)` |
| 251 | `AssertSolidInvariants(Solid solid, string? message = null)` |
| 361 | `static string Describe(string? message) => message ?? "solid";` |

**这 7 处只允许插入 `?`，不得改动任何标识符、默认值、表达式或语句。** 可空引用注解在运行时被擦除，函数体不引用 `message`（除 `Describe` 的 `?? "solid"`），所以这是**语义零变化**的改动；调用方全是位置传参或省略，签名兼容。

**除此之外，文件其余内容（含其私有 helper、`ManifoldReport`、`MergeTolerance`、所有断言的比较逻辑与容差）逐字节不动。** 嵌套的 `ManifoldReport` 原本已是 `public sealed class`，外层转 `public` 后自然对外可见。

**不要用 `#nullable disable` 或降低 TestKit 的 `Nullable` 设置来规避** —— TestKit 后续会加入生成器与 oracle 新代码，可空检查在那里有价值；把 7 处注解改对是正确解法。若出现这 7 处之外的任何告警，停下来报告，不要自行扩大改动范围。

- [ ] **Step 3: 新建 GoldenPaths**

`tests/OpenCSG.NET.TestKit/GoldenPaths.cs`：

```csharp
using System;
using System.IO;

namespace Csg.Test
{
    /// <summary>
    /// 定位测试 golden 目录。
    /// 既有 SolidTest 用 4 层 Path.GetDirectoryName 从程序集位置反推，在单文件发布、
    /// 自定义输出路径下会静默失效；这里改为自底向上找 csproj 标记文件。
    /// </summary>
    public static class GoldenPaths
    {
        const string Marker = "OpenCSG.NET.Tests.csproj";

        /// <summary>
        /// 从 anchorPath（程序集文件路径或目录路径均可）向上找包含 csproj 标记的目录，
        /// 返回其下 Results/；找不到返回 null。
        /// </summary>
        public static string? ResultsDirectory(string anchorPath)
        {
            if (string.IsNullOrEmpty(anchorPath))
                return null;

            var start = Directory.Exists(anchorPath) ? anchorPath : Path.GetDirectoryName(anchorPath);
            if (string.IsNullOrEmpty(start))
                return null;

            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, Marker)))
                    return Path.Combine(dir.FullName, "Results");
                dir = dir.Parent;
            }
            return null;
        }
    }
}
```

- [ ] **Step 4: 给测试项目加 TestKit 引用**

`tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj` 的 `<ProjectReference>` ItemGroup 改为：

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\OpenCSG.NET\OpenCSG.NET.csproj" />
    <ProjectReference Include="..\OpenCSG.NET.TestKit\OpenCSG.NET.TestKit.csproj" />
  </ItemGroup>
```

- [ ] **Step 5: 登记到解决方案**

`OpenCSG.NET.slnx` 的 `</Solution>` 之前插入 TestKit（按字母序放在 `OpenCSG.NET.Tests` 之前）：

```xml
  <Folder Name="/tests/">
    <Project Path="tests/OpenCSG.NET.ApiCompat/OpenCSG.NET.ApiCompat.csproj" />
    <Project Path="tests/OpenCSG.NET.TestKit/OpenCSG.NET.TestKit.csproj" />
    <Project Path="tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj" />
  </Folder>
```

`OpenCSG.NET.ApiCompat` 此时尚不存在（Task 6 创建）。**本步骤先只加 TestKit 一行**，Task 6 再加 ApiCompat 一行，避免中间状态构建失败：

```xml
  <Folder Name="/tests/">
    <Project Path="tests/OpenCSG.NET.TestKit/OpenCSG.NET.TestKit.csproj" />
    <Project Path="tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj" />
  </Folder>
```

- [ ] **Step 6: 构建并验证现有 102 个用例全部仍然通过**

```bash
dotnet build OpenCSG.NET.slnx -c Release 2>&1 | tail -20
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release 2>&1 | tail -10
```

预期：构建 0 warning 0 error；测试通过数与 Task 1 Step 2 记录的基线**完全一致**，Inconclusive 仍为 0。

若出现 `CS0122`（不可访问）说明可见性没改全；若出现 `NUnit.Framework` 找不到，说明 `Assert` 的 `using` 在迁移后丢了 —— TestKit 的 csproj 已引 NUnit，检查文件内 `using NUnit.Framework;` 是否在位。

- [ ] **Step 7: 提交（需用户确认）**

```bash
git add tests/OpenCSG.NET.TestKit tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj OpenCSG.NET.slnx
git commit -m "test: 新增 TestKit 项目并迁入 GeometryAssert（命名空间与断言逻辑不变）"
```

---

### Task 3: MeshAssert + CsgMesh 反射补偿回归测试

`CsgMesh.TrianglesOf(Solid)` 把 CSG 坐标做 Y↔Z 交换（`MeshPoint` 是 Z-up），这是一个**行列式为 −1 的镜像**，靠反转绕序补偿。`CsgMesh.cs:49-51` 的注释声称：100mm 立方体有向体积 +1,000,000，交换后 −1,000,000，反转绕序后回到 +1,000,000。**这段声称从未被验证**，而它是所有 STL 导出正确性的前提。

**Files:**
- Create: `tests/OpenCSG.NET.TestKit/MeshAssert.cs`
- Create: `tests/OpenCSG.NET.Tests/MeshIo/CsgMeshTest.cs`

**Interfaces:**
- Consumes: `Csg.CsgMesh.TrianglesOf(Solid) → List<MeshTriangle>`、`Csg.MeshTriangle`（属性 `A`/`B`/`C`，类型 `MeshPoint`）、`Csg.MeshPoint`（属性 `X`/`Y`/`Z`）、`GeometryAssert.Bounds(Solid)`
- Produces:
  - `Csg.Test.MeshAssert.SignedVolume(IReadOnlyList<MeshTriangle>) → double`
  - `Csg.Test.MeshAssert.TotalArea(IReadOnlyList<MeshTriangle>) → double`
  - `Csg.Test.MeshAssert.EdgeReport`（结构：`EdgeCount`、`BoundaryEdges`、`NonManifoldEdges`、`VertexCount`、`TriangleCount`、`EulerCharacteristic`）
  - `Csg.Test.MeshAssert.Analyze(IReadOnlyList<MeshTriangle>) → EdgeReport`
  - `Csg.Test.MeshAssert.Bounds(IReadOnlyList<MeshTriangle>) → (MeshPoint Min, MeshPoint Max)`
  - `Csg.Test.MeshAssert.AssertClosedOrientedManifold(IReadOnlyList<MeshTriangle>, string? message = null)`
    （TestKit 开了 `<Nullable>enable</Nullable>`，新代码必须自带可空注解，否则 `string message = null` 会产生 CS8625）

- [ ] **Step 1: 写失败的测试**

`tests/OpenCSG.NET.Tests/MeshIo/CsgMeshTest.cs`：

```csharp
using System.Collections.Generic;
using System.Linq;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    [TestFixture]
    public class CsgMeshTest : SolidTest
    {
        [Test]
        public void Cube100_FromCsg_HasPositiveSignedVolume()
        {
            var cube = Solids.Cube(100, center: true);
            var triangles = CsgMesh.TrianglesOf(cube);

            // CsgMesh 做 Y↔Z 交换（行列式 −1 的镜像）并反转绕序补偿；
            // 补偿正确则有向体积必须为正（法线朝外），数值为 100³。
            Assert.That(MeshAssert.SignedVolume(triangles), Is.EqualTo(1_000_000.0).Within(1e-6),
                "CsgMesh 的绕序补偿失败 ⇒ STL 导出会内外翻转");
        }

        [Test]
        public void Cube100_FromCsg_SwapsYAndZ()
        {
            var cube = Solids.Cube(new Vector3D(10, 20, 30), center: true);
            var triangles = CsgMesh.TrianglesOf(cube);
            var (min, max) = MeshAssert.Bounds(triangles);

            // CSG 尺寸 (10, 20, 30) → 网格尺寸 (10, 30, 20)：Y 与 Z 互换
            Assert.That(max.X - min.X, Is.EqualTo(10.0).Within(1e-9));
            Assert.That(max.Y - min.Y, Is.EqualTo(30.0).Within(1e-9));
            Assert.That(max.Z - min.Z, Is.EqualTo(20.0).Within(1e-9));
        }

        [Test]
        public void Cube100_FromCsg_AreaIsPreserved()
        {
            var cube = Solids.Cube(100, center: true);
            var triangles = CsgMesh.TrianglesOf(cube);

            // 6 面 × 100 × 100；镜像不改变面积
            Assert.That(MeshAssert.TotalArea(triangles), Is.EqualTo(60_000.0).Within(1e-6));
        }

        [Test]
        public void Cube100_FromCsg_IsClosedOrientedManifold()
        {
            var cube = Solids.Cube(100, center: true);
            var triangles = CsgMesh.TrianglesOf(cube);

            MeshAssert.AssertClosedOrientedManifold(triangles);
        }

        [Test]
        public void TwoSolids_Overload_TriangleCountIsSum()
        {
            var a = Solids.Cube(10, center: true);
            var b = Solids.Cube(10, center: true).Translate(x: 100);

            var single = CsgMesh.TrianglesOf(new[] { a, b });

            Assert.That(single.Count,
                Is.EqualTo(CsgMesh.TrianglesOf(a).Count + CsgMesh.TrianglesOf(b).Count));
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```bash
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release --filter "FullyQualifiedName~CsgMeshTest" 2>&1 | tail -20
```

预期：编译失败，`error CS0246: 未能找到类型或命名空间名"MeshAssert"`。

- [ ] **Step 3: 实现 MeshAssert**

`tests/OpenCSG.NET.TestKit/MeshAssert.cs`：

```csharp
using System;
using System.Collections.Generic;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// 三角网格（MeshTriangle 序列）上的不变量断言。
    /// GeometryAssert 只能吃 Solid（读 Polygon.Vertices）；STL 写出/读入、CsgMesh、
    /// ViewMeshTopology 这条链上的对象是扁平三角面，需要独立的分析口径。
    /// 顶点聚簇容差与 ViewMeshTopology 的 WeldTolerance (1e-6) 取齐，使结论可交叉验证。
    /// </summary>
    public static class MeshAssert
    {
        public const double MergeTolerance = 1e-6;

        /// <summary>欧拉特征与边统计。</summary>
        public readonly struct EdgeReport
        {
            public EdgeReport(int vertexCount, int edgeCount, int triangleCount,
                              int boundaryEdges, int nonManifoldEdges, int duplicatedEdges,
                              int eulerCharacteristic)
            {
                VertexCount = vertexCount;
                EdgeCount = edgeCount;
                TriangleCount = triangleCount;
                BoundaryEdges = boundaryEdges;
                NonManifoldEdges = nonManifoldEdges;
                DuplicatedEdges = duplicatedEdges;
                EulerCharacteristic = eulerCharacteristic;
            }

            public int VertexCount { get; }
            public int EdgeCount { get; }
            public int TriangleCount { get; }
            public int BoundaryEdges { get; }      // 只被一个三角面使用的无向边
            public int NonManifoldEdges { get; }   // 被 != 2 个三角面使用的无向边
            public int DuplicatedEdges { get; }    // 同向重复出现的有向边（绕序不一致）
            public int EulerCharacteristic { get; } // V − E + F

            public override string ToString()
                => $"V={VertexCount} E={EdgeCount} F={TriangleCount} "
                 + $"boundary={BoundaryEdges} nonManifold={NonManifoldEdges} "
                 + $"duplicated={DuplicatedEdges} chi={EulerCharacteristic}";
        }

        /// <summary>散度定理有向体积（右手绕序 ⇒ 法线朝外 ⇒ 正值）。</summary>
        public static double SignedVolume(IReadOnlyList<MeshTriangle> triangles)
        {
            double volume = 0;
            for (int i = 0; i < triangles.Count; i++)
            {
                var a = triangles[i].A;
                var b = triangles[i].B;
                var c = triangles[i].C;
                volume += a.X * (b.Y * c.Z - b.Z * c.Y)
                        - a.Y * (b.X * c.Z - b.Z * c.X)
                        + a.Z * (b.X * c.Y - b.Y * c.X);
            }
            return volume / 6.0;
        }

        /// <summary>三角面积之和（镜像不改变面积，故可与 Solid 侧交叉校验）。</summary>
        public static double TotalArea(IReadOnlyList<MeshTriangle> triangles)
        {
            double area = 0;
            for (int i = 0; i < triangles.Count; i++)
            {
                var a = triangles[i].A;
                var b = triangles[i].B;
                var c = triangles[i].C;
                var ux = b.X - a.X; var uy = b.Y - a.Y; var uz = b.Z - a.Z;
                var vx = c.X - a.X; var vy = c.Y - a.Y; var vz = c.Z - a.Z;
                var nx = uy * vz - uz * vy;
                var ny = uz * vx - ux * vz;
                var nz = ux * vy - uy * vx;
                area += Math.Sqrt(nx * nx + ny * ny + nz * nz) / 2.0;
            }
            return area;
        }

        public static (MeshPoint Min, MeshPoint Max) Bounds(IReadOnlyList<MeshTriangle> triangles)
        {
            if (triangles.Count == 0)
                return (new MeshPoint(0, 0, 0), new MeshPoint(0, 0, 0));

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            for (int i = 0; i < triangles.Count; i++)
            {
                for (int k = 0; k < 3; k++)
                {
                    var p = k == 0 ? triangles[i].A : k == 1 ? triangles[i].B : triangles[i].C;
                    if (p.X < minX) minX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Z < minZ) minZ = p.Z;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y > maxY) maxY = p.Y;
                    if (p.Z > maxZ) maxZ = p.Z;
                }
            }
            return (new MeshPoint(minX, minY, minZ), new MeshPoint(maxX, maxY, maxZ));
        }

        /// <summary>顶点按容差聚簇后统计边使用次数与欧拉特征。</summary>
        public static EdgeReport Analyze(IReadOnlyList<MeshTriangle> triangles)
        {
            var points = new List<MeshPoint>(triangles.Count * 3);
            for (int i = 0; i < triangles.Count; i++)
            {
                points.Add(triangles[i].A);
                points.Add(triangles[i].B);
                points.Add(triangles[i].C);
            }

            var cluster = Cluster(points);
            int vertexCount = 0;
            foreach (var c in cluster)
                if (c + 1 > vertexCount)
                    vertexCount = c + 1;

            // 无向边使用计数 + 有向边计数
            var undirected = new Dictionary<long, int>();
            var directed = new Dictionary<long, int>();
            for (int i = 0; i < triangles.Count; i++)
            {
                int b0 = i * 3;
                for (int k = 0; k < 3; k++)
                {
                    int a = cluster[b0 + k];
                    int b = cluster[b0 + (k + 1) % 3];
                    if (a == b)
                        continue;
                    long ukey = a < b ? Pack(a, b) : Pack(b, a);
                    undirected.TryGetValue(ukey, out var uc);
                    undirected[ukey] = uc + 1;
                    long dkey = Pack(a, b);
                    directed.TryGetValue(dkey, out var dc);
                    directed[dkey] = dc + 1;
                }
            }

            int boundary = 0, nonManifold = 0, duplicated = 0;
            foreach (var kv in undirected)
            {
                if (kv.Value == 1) boundary++;
                if (kv.Value != 2) nonManifold++;
            }
            foreach (var kv in directed)
                if (kv.Value != 1)
                    duplicated++;

            return new EdgeReport(
                vertexCount,
                undirected.Count,
                triangles.Count,
                boundary,
                nonManifold,
                duplicated,
                vertexCount - undirected.Count + triangles.Count);
        }

        /// <summary>封闭定向流形：每条无向边恰好两个面、每条有向边恰好一次。</summary>
        public static void AssertClosedOrientedManifold(IReadOnlyList<MeshTriangle> triangles, string? message = null)
        {
            var report = Analyze(triangles);
            var prefix = message ?? "mesh";

            Assert.That(report.BoundaryEdges, Is.EqualTo(0),
                $"{prefix}: 有 {report.BoundaryEdges} 条边界边（网格不封闭）| {report}");
            Assert.That(report.NonManifoldEdges, Is.EqualTo(0),
                $"{prefix}: 有 {report.NonManifoldEdges} 条边不是恰好被两个面共享 | {report}");
            Assert.That(report.DuplicatedEdges, Is.EqualTo(0),
                $"{prefix}: 有 {report.DuplicatedEdges} 条有向边重复（绕序不一致）| {report}");
        }

        /// <summary>网格哈希 + 27 邻域精确比较的容差聚簇（与 GeometryAssert.Cluster 同口径）。</summary>
        static int[] Cluster(List<MeshPoint> points)
        {
            var cell = MergeTolerance;
            var buckets = new Dictionary<(long, long, long), List<int>>();
            var centers = new List<MeshPoint>();
            var result = new int[points.Count];

            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                long cx = (long)Math.Floor(p.X / cell), cy = (long)Math.Floor(p.Y / cell), cz = (long)Math.Floor(p.Z / cell);
                int found = -1;
                for (long dx = -1; dx <= 1 && found < 0; dx++)
                for (long dy = -1; dy <= 1 && found < 0; dy++)
                for (long dz = -1; dz <= 1 && found < 0; dz++)
                {
                    if (!buckets.TryGetValue((cx + dx, cy + dy, cz + dz), out var list))
                        continue;
                    foreach (var candidate in list)
                    {
                        var c = centers[candidate];
                        if (Math.Abs(c.X - p.X) <= cell && Math.Abs(c.Y - p.Y) <= cell && Math.Abs(c.Z - p.Z) <= cell)
                        {
                            found = candidate;
                            break;
                        }
                    }
                }
                if (found < 0)
                {
                    centers.Add(p);
                    found = centers.Count - 1;
                }
                result[i] = found;
                var key = (cx, cy, cz);
                if (!buckets.TryGetValue(key, out var bucket))
                {
                    bucket = new List<int>(2);
                    buckets[key] = bucket;
                }
                bucket.Add(found);
            }
            return result;
        }

        static long Pack(int a, int b) => ((long)a << 32) | (uint)b;
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

```bash
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release --filter "FullyQualifiedName~CsgMeshTest" 2>&1 | tail -20
```

预期：5 个用例全过。

**如果 `Cube100_FromCsg_HasPositiveSignedVolume` 失败**（有向体积为 −1,000,000）：这是**真实缺陷**（`CsgMesh` 的绕序补偿反了 ⇒ STL 导出内外翻转），不是测试写错。此时**不要改测试**，按 spec 第 9 节登记到 `tests/DEFECTS.md` 并给该用例加 `[Explicit("DEFECT-001: CsgMesh 绕序补偿反转")]`，然后在 `TEST-REPORT` 里记录。同类失败（面积/封闭性）照此处理。

- [ ] **Step 5: 交叉校验 Solid 侧与网格侧的面积口径**

在 `CsgMeshTest` 追加一个用例，把两套口径对上：

```csharp
        [Test]
        public void Cube100_GridArea_MatchesSolidPolygonArea()
        {
            var cube = Solids.Cube(100, center: true);
            var triangles = CsgMesh.TrianglesOf(cube);

            double solidArea = 0;
            foreach (var polygon in cube.Polygons)
            {
                var v = polygon.Vertices;
                var a = v[0].Pos;
                for (int i = 1; i + 1 < v.Count; i++)
                {
                    var u = v[i].Pos - a;
                    var w = v[i + 1].Pos - a;
                    var n = u.Cross(w);
                    solidArea += n.Length / 2.0;
                }
            }

            Assert.That(MeshAssert.TotalArea(triangles), Is.EqualTo(solidArea).Within(1e-9));
        }
```

`Vector3D.Cross(Vector3D)` 是实例方法（`Vector.cs:53`），返回 `Vector3D`，有 `.Length`。

- [ ] **Step 6: 跑全部用例确认无回归**

```bash
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release 2>&1 | tail -10
```

预期：通过数 = Task 1 基线 + 6，失败 0，Inconclusive 0（若 Task 3 Step 4 登记了缺陷，则 Inconclusive/跳过数按登记数增加，需在报告中说明）。

- [ ] **Step 7: 提交（需用户确认）**

```bash
git add tests/OpenCSG.NET.TestKit/MeshAssert.cs tests/OpenCSG.NET.Tests/MeshIo tests/DEFECTS.md docs/testing
git commit -m "test: 新增 MeshAssert 并回归 CsgMesh 的 Y↔Z 反射补偿"
```

---

### Task 4: ViewBridge + 坐标约定 oracle

库内存在一个断裂的接口：`CsgMesh.TrianglesOf(Solid)` 产出 `List<MeshTriangle>`（`MeshPoint` 坐标，Z-up），而 `ViewProjector` 消费 `IReadOnlyList<ViewTriangle>`（`ViewPoint` 坐标）。**没有任何转换 helper**，所以 HLR 管线在库内是不可达的 —— 这正是该子系统 0 测试的结构性原因。

`ViewBridge` 填这个洞，并立刻用坐标约定 oracle 把整条链（CSG 的 Z/Y 轴 ↔ 网格 Z-up ↔ 三视图取轴）钉死。

**Files:**
- Create: `tests/OpenCSG.NET.TestKit/ViewBridge.cs`
- Create: `tests/OpenCSG.NET.Tests/Views/ViewBridgeTest.cs`

**Interfaces:**
- Consumes: `Csg.CsgMesh.TrianglesOf(Solid) → List<MeshTriangle>`；`Csg.ViewPoint(double x, double y, double z)`；`Csg.ViewTriangle(ViewPoint a, ViewPoint b, ViewPoint c)`；`Csg.ViewProjector(IReadOnlyList<ViewTriangle>)`；`Csg.ViewProjector.Project(IReadOnlyList<ViewDirection>?) → IReadOnlyList<ViewProjection>`；`Csg.ViewDirections.{Front,Top,Left}`；`Csg.ViewProjection.Bounds → ViewBounds2`（属性 `MinX`/`MinY`/`MaxX`/`MaxY`/`Width`/`Height`）；`Csg.ViewProjection.Segments`
- Produces:
  - `Csg.Test.ViewBridge.ToViewTriangles(Solid) → IReadOnlyList<ViewTriangle>`
  - `Csg.Test.ViewBridge.ProjectViews(Solid, IReadOnlyList<ViewDirection>? directions = null) → IReadOnlyList<ViewProjection>`

- [ ] **Step 1: 写失败的测试**

`tests/OpenCSG.NET.Tests/Views/ViewBridgeTest.cs`：

```csharp
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// 钉死 CSG → 网格 → 三视图 的坐标约定链。
    /// CSG 空间：Cube 的 size 沿 X/Y/Z；CsgMesh 做 Y↔Z 交换（Z-up）；
    /// 三视图取轴见 ViewProjectorContracts.ViewDirections。
    /// </summary>
    [TestFixture]
    public class ViewBridgeTest : SolidTest
    {
        const double Tol = 1e-9;

        [Test]
        public void Cube_AllThreeViews_AreSquaresOfSideS()
        {
            var cube = Solids.Cube(10, center: true);
            var views = ViewBridge.ProjectViews(cube, ViewDirections.OrthographicThree);

            Assert.That(views.Count, Is.EqualTo(3));
            foreach (var view in views)
            {
                Assert.That(view.Bounds.Width, Is.EqualTo(10.0).Within(Tol), $"{view.Direction.Name} 宽");
                Assert.That(view.Bounds.Height, Is.EqualTo(10.0).Within(Tol), $"{view.Direction.Name} 高");
            }
        }

        [Test]
        public void Box10x20x30_ViewExtentsFollowAxisMapping()
        {
            // CSG 尺寸 (X=10, Y=20, Z=30) → 网格 (X=10, Y=30, Z=20)
            // 主视 Look=+X, Right=−Y_mesh, Up=+Z_mesh  ⇒ 宽 = csg Z = 30, 高 = csg Y = 20
            // 俯视 Look=−Z, Right=−Y_mesh, Up=+X_mesh  ⇒ 宽 = csg Z = 30, 高 = csg X = 10
            // 左视 Look=−Y, Right=−X_mesh, Up=+Z_mesh  ⇒ 宽 = csg X = 10, 高 = csg Y = 20
            var box = Solids.Cube(new Vector3D(10, 20, 30), center: true);
            var views = ViewBridge.ProjectViews(box, ViewDirections.OrthographicThree);

            var front = views[0];
            var top = views[1];
            var left = views[2];

            Assert.That(front.Direction.Name, Is.EqualTo("主视"));
            Assert.That(front.Bounds.Width, Is.EqualTo(30.0).Within(Tol));
            Assert.That(front.Bounds.Height, Is.EqualTo(20.0).Within(Tol));

            Assert.That(top.Direction.Name, Is.EqualTo("俯视"));
            Assert.That(top.Bounds.Width, Is.EqualTo(30.0).Within(Tol));
            Assert.That(top.Bounds.Height, Is.EqualTo(10.0).Within(Tol));

            Assert.That(left.Direction.Name, Is.EqualTo("左视"));
            Assert.That(left.Bounds.Width, Is.EqualTo(10.0).Within(Tol));
            Assert.That(left.Bounds.Height, Is.EqualTo(20.0).Within(Tol));
        }

        [Test]
        public void Cube_ProducesVisibleSegments()
        {
            var cube = Solids.Cube(10, center: true);
            var views = ViewBridge.ProjectViews(cube, ViewDirections.OrthographicThree);

            foreach (var view in views)
            {
                Assert.That(view.Segments.Count, Is.GreaterThan(0), $"{view.Direction.Name} 无线段");
                Assert.That(view.VisibleCount, Is.GreaterThan(0), $"{view.Direction.Name} 无可见线段");
                Assert.That(view.VisibleCount + view.HiddenCount, Is.EqualTo(view.Segments.Count));
            }
        }

        [Test]
        public void DefaultDirections_AreOrthographicThree()
        {
            var cube = Solids.Cube(10, center: true);

            var byDefault = ViewBridge.ProjectViews(cube);
            var explicitThree = ViewBridge.ProjectViews(cube, ViewDirections.OrthographicThree);

            Assert.That(byDefault.Count, Is.EqualTo(explicitThree.Count));
            for (int i = 0; i < byDefault.Count; i++)
                Assert.That(byDefault[i].Direction.Name, Is.EqualTo(explicitThree[i].Direction.Name));
        }

        [Test]
        public void ToViewTriangles_TriangleCountMatchesCsgMesh()
        {
            var cube = Solids.Cube(10, center: true);

            Assert.That(ViewBridge.ToViewTriangles(cube).Count,
                Is.EqualTo(CsgMesh.TrianglesOf(cube).Count));
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```bash
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release --filter "FullyQualifiedName~ViewBridgeTest" 2>&1 | tail -20
```

预期：编译失败，`error CS0246: 未能找到类型或命名空间名"ViewBridge"`。

- [ ] **Step 3: 实现 ViewBridge**

`tests/OpenCSG.NET.TestKit/ViewBridge.cs`：

```csharp
using System.Collections.Generic;
using Csg;

namespace Csg.Test
{
    /// <summary>
    /// Solid → HLR 管线的适配器。
    /// 库内没有这座桥：CsgMesh 产出 MeshPoint 坐标（已完成 Y↔Z 交换与绕序补偿），
    /// ViewProjector 消费 ViewPoint 坐标，两者之间没有任何转换 helper，
    /// 导致 HLR 子系统在库内不可达。这里补上，并把约定链固化。
    /// </summary>
    public static class ViewBridge
    {
        public static IReadOnlyList<ViewTriangle> ToViewTriangles(Solid solid)
        {
            var mesh = CsgMesh.TrianglesOf(solid);
            var triangles = new ViewTriangle[mesh.Count];
            for (int i = 0; i < mesh.Count; i++)
            {
                triangles[i] = new ViewTriangle(
                    ToViewPoint(mesh[i].A),
                    ToViewPoint(mesh[i].B),
                    ToViewPoint(mesh[i].C));
            }
            return triangles;
        }

        public static IReadOnlyList<ViewProjection> ProjectViews(
            Solid solid, IReadOnlyList<ViewDirection>? directions = null)
        {
            var projector = new ViewProjector(ToViewTriangles(solid));
            return projector.Project(directions);
        }

        static ViewPoint ToViewPoint(MeshPoint p) => new ViewPoint(p.X, p.Y, p.Z);
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

```bash
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release --filter "FullyQualifiedName~ViewBridgeTest" 2>&1 | tail -30
```

预期：5 个用例全过。

**如果 `Box10x20x30_ViewExtentsFollowAxisMapping` 的某个数字不符**：三种可能 —— (a) `ViewDirections` 的取轴与文档不符，(b) `ViewProjector` 的 2D 映射有误，(c) 我上面的约定推导有误。**不要改测试去迎合实现**。把实际值记录到 `TEST-REPORT`，用以下探针定位：

```csharp
// 临时探针：把三视图的实际 bounds 打出来
foreach (var v in views)
    TestContext.WriteLine($"{v.Direction.Name}: W={v.Bounds.Width:R} H={v.Bounds.Height:R} " +
                          $"X=[{v.Bounds.MinX:R},{v.Bounds.MaxX:R}] Y=[{v.Bounds.MinY:R},{v.Bounds.MaxY:R}]");
```

再逐个视图与 `ViewProjectorContracts.cs:88-102` 的取轴定义对照，判定是文档、取轴定义还是投影代码的问题，登记为缺陷。

- [ ] **Step 5: 记录 HLR 管线的首通状态**

在 `docs/testing/TEST-REPORT-2026-10-13.md` 追加：

```markdown
## 阶段 1 — HLR 管线首次打通

`ViewBridge` 让 `Solid → CsgMesh → ViewProjector → OrthographicViews` 在库内首次可达。

| 视图 | 立方体边长 10 的 bounds | 是否符合解析解 |
| --- | --- | --- |
| 主视 | <填实际 W×H> | <是/否> |
| 俯视 | <填实际 W×H> | <是/否> |
| 左视 | <填实际 W×H> | <是/否> |
```

填入真实值。

- [ ] **Step 6: 跑全部用例确认无回归**

```bash
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release 2>&1 | tail -10
```

预期：失败 0。

- [ ] **Step 7: 提交（需用户确认）**

```bash
git add tests/OpenCSG.NET.TestKit/ViewBridge.cs tests/OpenCSG.NET.Tests/Views docs/testing
git commit -m "test: 补 ViewBridge 打通 Solid→HLR 管线并固化坐标约定"
```

---

### Task 5: GoldenGuard + gitignore + promote 脚本

**这是本计划里最高价值的一项。** 现状：`SolidTest.AssertAcceptedStl` 在 accept 文件不存在时写 `*_.stl` 然后 `Assert.Inconclusive` —— **NUnit 的 Inconclusive 不导致 `dotnet test` 失败**。所以新增 golden 用例若忘记升级基线，测试报 Inconclusive、CI 报绿，**实际零保护**。后续阶段要加约 30 个 golden，不修这个洞等于白加。

**Files:**
- Create: `tests/OpenCSG.NET.Tests/GoldenGuard.cs`
- Create: `tests/OpenCSG.NET.Tests/Infrastructure/GoldenGuardTest.cs`
- Create: `tools/promote-goldens.sh`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: `Csg.Test.GoldenPaths.ResultsDirectory(string) → string?`（Task 2）
- Produces: 无代码接口。行为契约：`Results/` 下存在任何以 `_.stl` 结尾的文件时，整个测试程序集的运行以失败结束。

- [ ] **Step 1: 写失败的测试**

`tests/OpenCSG.NET.Tests/Infrastructure/GoldenGuardTest.cs`：

```csharp
using System;
using System.IO;
using System.Reflection;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// 验证 golden 门禁本身有效：门禁检查逻辑必须能发现残留的 *_ .stl，
    /// 且 GoldenPaths 必须解析到真实的 Results 目录（否则门禁形同虚设）。
    /// </summary>
    [TestFixture]
    public class GoldenGuardTest
    {
        [Test]
        public void GoldenPaths_ResolvesToExistingResultsDirectory()
        {
            var dir = GoldenPaths.ResultsDirectory(Assembly.GetExecutingAssembly().Location);

            Assert.That(dir, Is.Not.Null, "GoldenPaths 未能从测试程序集位置向上找到 OpenCSG.NET.Tests.csproj");
            Assert.That(Directory.Exists(dir), Is.True, $"解析出的 Results 目录不存在：{dir}");
        }

        [Test]
        public void GoldenPaths_ResolvesToSameDirectoryAsSolidTestAssumes()
        {
            // SolidTest 用 4 层 GetDirectoryName；两者必须指向同一处，
            // 否则门禁守的是另一个目录。
            var asmPath = Assembly.GetExecutingAssembly().Location;
            var repoPath = Path.GetDirectoryName(Path.GetDirectoryName(
                Path.GetDirectoryName(Path.GetDirectoryName(asmPath))));
            var solidTestPath = Path.Combine(repoPath, "Results");

            var guardPath = GoldenPaths.ResultsDirectory(asmPath);

            Assert.That(guardPath, Is.EqualTo(solidTestPath));
        }

        [Test]
        public void FindPendingGoldens_DetectsRejectedFile()
        {
            var results = GoldenPaths.ResultsDirectory(Assembly.GetExecutingAssembly().Location);
            var probe = Path.Combine(results, "__GoldenGuardProbe___.stl");
            File.WriteAllText(probe, "solid probe\nendsolid probe\n");

            try
            {
                var pending = GoldenGuard.FindPendingGoldens(results);

                Assert.That(pending, Does.Contain(probe));
            }
            finally
            {
                File.Delete(probe);
            }
        }

        [Test]
        public void FindPendingGoldens_IgnoresAcceptedFiles()
        {
            var results = GoldenPaths.ResultsDirectory(Assembly.GetExecutingAssembly().Location);
            // 注意：残留判定为文件名匹配末五位字符 "_.stl"，故「已接受」探针的文件名不能以 "_" 结尾，
            // 否则会被判成残留，本断言永远不成立。
            var probe = Path.Combine(results, "__GoldenGuardProbe.stl");
            File.WriteAllText(probe, "solid probe\nendsolid probe\n");

            try
            {
                var pending = GoldenGuard.FindPendingGoldens(results);

                Assert.That(pending, Does.Not.Contain(probe));
            }
            finally
            {
                File.Delete(probe);
            }
        }

        [Test]
        public void FindPendingGoldens_OnCleanTree_IsEmpty()
        {
            var results = GoldenPaths.ResultsDirectory(Assembly.GetExecutingAssembly().Location);

            var pending = GoldenGuard.FindPendingGoldens(results);

            Assert.That(pending, Is.Empty,
                "存在未升级的 golden 残留；先运行 tools/promote-goldens.sh 人工确认后升级");
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```bash
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release --filter "FullyQualifiedName~GoldenGuardTest" 2>&1 | tail -20
```

预期：编译失败，`error CS0103: 当前上下文中不存在名称"GoldenGuard"`。

- [ ] **Step 3: 实现 GoldenGuard**

`tests/OpenCSG.NET.Tests/GoldenGuard.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// golden 升级门禁。
    /// SolidTest.AssertAcceptedStl 在 accept 缺失时只 Assert.Inconclusive——NUnit 的
    /// Inconclusive 不会让 dotnet test 失败，于是"新增用例忘了升级 golden"会静默通过、零保护。
    /// 本 SetUpFixture 在整套件结束时扫描 Results/，任何 *_ .stl 残留都判失败。
    /// 必须放在测试程序集内：NUnit 不扫描被引用程序集里的 [SetUpFixture]。
    /// </summary>
    [SetUpFixture]
    public class GoldenGuard
    {
        /// <summary>回收未升级的 golden 结果文件名（以 "_.stl" 结尾）。</summary>
        public static IReadOnlyList<string> FindPendingGoldens(string resultsDirectory)
        {
            if (string.IsNullOrEmpty(resultsDirectory) || !Directory.Exists(resultsDirectory))
                return Array.Empty<string>();

            var pending = new List<string>();
            // 不用 Directory.GetFiles(dir, "*_.stl")：Windows 的 8.3 短名通配匹配有过歧义历史，
            // 显式后缀过滤更可靠。
            foreach (var file in Directory.GetFiles(resultsDirectory, "*.stl"))
                if (file.EndsWith("_.stl", StringComparison.Ordinal))
                    pending.Add(file);
            pending.Sort(StringComparer.Ordinal);
            return pending;
        }

        [OneTimeTearDown]
        public void FailOnUnacceptedGoldens()
        {
            var results = GoldenPaths.ResultsDirectory(Assembly.GetExecutingAssembly().Location);
            var pending = FindPendingGoldens(results);

            if (pending.Count == 0)
                return;

            var names = new List<string>();
            foreach (var path in pending)
                names.Add(Path.GetFileName(path));

            Assert.Fail(
                $"有 {pending.Count} 个 golden 结果未升级：\n  {string.Join("\n  ", names)}\n"
                + "含义：新增用例从未被断言（零保护），或既有用例输出已变。\n"
                + $"确认输出正确后运行 tools/promote-goldens.sh 升级；目录：{results}");
        }
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

```bash
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release --filter "FullyQualifiedName~GoldenGuardTest" 2>&1 | tail -20
```

预期：5 个用例全过。

- [ ] **Step 5: 写 promote 脚本**

`tools/promote-goldens.sh`：

```bash
#!/usr/bin/env bash
# 把 Results/ 下未升级的 golden 残留（<Fixture>.<Test>_.stl）升级为正式基线。
# 升级即"接受当前输出为正确"——请先逐个确认 *_ .stl 的内容确实正确。
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
results="$repo_root/tests/OpenCSG.NET.Tests/Results"

if [[ ! -d "$results" ]]; then
  echo "找不到 Results 目录：$results" >&2
  exit 1
fi

shopt -s nullglob
pending=("$results"/*_.stl)
shopt -u nullglob

if [[ ${#pending[@]} -eq 0 ]]; then
  echo "没有待升级的 golden 残留。"
  exit 0
fi

echo "待升级 ${#pending[@]} 个："
for f in "${pending[@]}"; do
  echo "  $(basename "$f")"
done

if [[ "${1:-}" != "--yes" ]]; then
  echo
  read -r -p "确认这些输出正确并升级为基线？[y/N] " reply
  [[ "$reply" == "y" || "$reply" == "Y" ]] || { echo "已取消。"; exit 1; }
fi

for f in "${pending[@]}"; do
  target="${f%_.stl}.stl"
  mv -- "$f" "$target"
  echo "升级 $(basename "$f") -> $(basename "$target")"
done

echo "完成。接着跑一次全套件确认门禁转绿："
echo "  dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release"
```

赋予执行权限：

```bash
chmod +x tools/promote-goldens.sh
```

- [ ] **Step 6: 忽略 golden 残留**

`.gitignore` 末尾追加：

```
# golden 未升级残留（由 tests/OpenCSG.NET.Tests/GoldenGuard.cs 强制清理）
tests/**/Results/*_.stl
```

- [ ] **Step 7: 端到端验证门禁真的会拦下来**

先造一个假的待升级用例，确认门禁失败，再撤掉。

```bash
# 7a. 制造残留
touch tests/OpenCSG.NET.Tests/Results/__GuardEndToEnd___.stl

# 7b. 跑整套件，预期失败
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release 2>&1 | tail -20
```

预期：`Failed!` 且错误信息含 `__GuardEndToEnd___.stl` 与 `tools/promote-goldens.sh`。

```bash
# 7c. 撤掉残留，确认转绿
rm tests/OpenCSG.NET.Tests/Results/__GuardEndToEnd___.stl
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release 2>&1 | tail -10
```

预期：`Passed!`，失败 0。

```bash
# 7d. 确认残留文件不被 git 看见
touch tests/OpenCSG.NET.Tests/Results/__IgnoreProbe___.stl
git status --short tests/OpenCSG.NET.Tests/Results/ | grep -c "__IgnoreProbe___.stl" || echo "OK: 已被忽略"
rm tests/OpenCSG.NET.Tests/Results/__IgnoreProbe___.stl
```

预期输出 `OK: 已被忽略`（`grep -c` 返回 0 时走 `||` 分支）。

- [ ] **Step 8: 提交（需用户确认）**

```bash
git add tests/OpenCSG.NET.Tests/GoldenGuard.cs tests/OpenCSG.NET.Tests/Infrastructure tools/promote-goldens.sh .gitignore
git commit -m "test: golden 残留强制失败门禁 + 升级脚本 + 忽略残留文件"
```

---

### Task 6: ApiCompat —— netstandard2.0 消费面编译工程

现有 4 个消费方（tests / samples×2 / perf）**全部 target `net8.0`**。库自己声明 `netstandard2.0`，但从来没有一个消费者从 `netstandard2.0` 侧编译过。`CsgNode` 用 C# 9 `record` + `init`，其 `IsExternalInit` polyfill 是 `internal` 的 —— 消费方能否直接用这套 API 从未验证。

**Files:**
- Create: `tests/OpenCSG.NET.ApiCompat/OpenCSG.NET.ApiCompat.csproj`
- Create: `tests/OpenCSG.NET.ApiCompat/ApiSurface.cs`
- Modify: `OpenCSG.NET.slnx`

**Interfaces:**
- Consumes: 库的公共 API 面（`Solids`、`Solid`、`Vector3D`、`CsgNodes`、`CsgEvaluator`、`CsgSerialization`、`Formats`、`CsgMesh`、`StlWriter`、`Profiles`、`ViewProjector`、`ViewBridge` 不用 —— ApiCompat 不引用 TestKit）
- Produces: 一个编译期契约：库的公共 API 必须能从 `netstandard2.0` 消费

- [ ] **Step 1: 建工程文件**

`tests/OpenCSG.NET.ApiCompat/OpenCSG.NET.ApiCompat.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\OpenCSG.NET\OpenCSG.NET.csproj" />
  </ItemGroup>

</Project>
```

`LangVersion 9.0` 与库一致。`TreatWarningsAsErrors` 让 API 面退化（例如误加了 `net8.0` 专属 API）立刻暴露。

- [ ] **Step 2: 写消费探针（先不加 polyfill，观察真实编译结果）**

`tests/OpenCSG.NET.ApiCompat/ApiSurface.cs`：

```csharp
// netstandard2.0 消费面探针：本工程不产出可执行文件，唯一目的是让编译器
// 从 netstandard2.0 侧校验库的公共 API 面。任何 API 不再可用 ⇒ 本工程编译失败。
// 所有返回值都用弃元 `_` 消费：本工程开了 TreatWarningsAsErrors，
// 命名局部变量"已赋值未使用"会触发 CS0219 而变成错误。
using System.Collections.Generic;
using System.IO;
using Csg;

namespace OpenCSG.NET.ApiCompat
{
    internal static class ApiSurface
    {
        internal static void TouchPrimitives()
        {
            var cube = Solids.Cube(1, center: true);
            var box = Solids.Cube(new Vector3D(1, 2, 3), center: true);
            var sphere = Solids.Sphere(1);
            var cylinder = Solids.Cylinder(0.5, 2);
            var cylinderOptions = Solids.Cylinder(new CylinderOptions
            {
                Start = new Vector3D(0, -1, 0),
                End = new Vector3D(0, 1, 0),
                RadiusStart = 1,
                RadiusEnd = 0,
                SectorAngle = 360,
                Resolution = 12,
            });
            var sphereOptions = Solids.Sphere(new SphereOptions
            {
                XAxis = new Vector3D(1, 0, 0),
                YAxis = new Vector3D(0, -1, 0),
                ZAxis = new Vector3D(0, 0, 1),
                Center = new Vector3D(0, 0, 0),
                Radius = 1,
                Resolution = 12,
            });
            _ = box.Polygons.Count + cylinderOptions.Polygons.Count + sphereOptions.Polygons.Count;

            var combined = Solids.Union(cube, sphere);
            var cut = Solids.Difference(combined, cylinder);
            var shared = Solids.Intersection(cut, combined);

            var moved = shared.Translate(x: 1, y: 2, z: 3).RotateX(10).RotateY(20).RotateZ(45).Scale(2);
            var mirrored = moved.Scale(-1, 1, 1);
            var scaledByVector = mirrored.Scale(new Vector3D(1, 1, 1));
            var transformed = scaledByVector.Transform(Matrix4x4.RotationX(30));

            if (transformed.Polygons.Count > 0)
            {
                var plane = transformed.Polygons[0].Plane;
                _ = plane.Normal.X + plane.W + plane.Flipped().W;
            }

            _ = Matrix4x4.Scaling(new Vector3D(2, 2, 2)).IsMirroring;
            _ = Matrix4x4.Translation(new Vector3D(1, 2, 3)).IsMirroring;
            _ = (Matrix4x4.RotationX(30) * Matrix4x4.RotationY(30)).Elements.Length;
        }

        internal static void TouchNodeTree()
        {
            CsgNode tree = CsgNodes.Union(
                CsgNodes.Box(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1)),
                CsgNodes.Cylinder(new Vector3D(2, 0, 0), 0.5, 1),
                CsgNodes.Cone(new Vector3D(4, 0, 0), 0.2, 0.8, 1),
                CsgNodes.Extrude(Profiles.HBeam(100, 80, 6, 8), 20),
                CsgNodes.Wedge(new Vector3D(6, 0, 0), 1, 1, 1),
                CsgNodes.Transform(new Vector3D(1, 1, 1), new Vector3D(0, 0, 45),
                    CsgNodes.Sphere(new Vector3D(8, 0, 0), 0.5)));

            var subtract = CsgNodes.Subtract(
                CsgNodes.Box(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1)),
                CsgNodes.Sphere(new Vector3D(0, 0, 0), 0.4));
            var intersect = CsgNodes.Intersect(
                CsgNodes.Box(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1)),
                CsgNodes.Sphere(new Vector3D(0, 0, 0), 0.6));

            var solid = CsgEvaluator.Evaluate(tree);
            var all = CsgEvaluator.EvaluateAll(new List<CsgNode> { tree, subtract, intersect });

            var json = CsgSerialization.ToJson(tree);
            var compact = CsgSerialization.ToJson(tree, indented: false);
            var roundTripped = CsgSerialization.FromJson(compact);
            var arrayJson = CsgSerialization.ToJson(new List<CsgNode> { tree }, indented: false);
            var array = CsgSerialization.FromJsonArray(arrayJson);

            _ = new Profile2D[] { Profiles.Rectangle(1, 1), Profiles.Channel(1, 1, 1, 1) }.Length;
            _ = new Profile2D[] { Profiles.SquareTube(1, 1, 1), Profiles.Trapezoid(1, 2, 1) }.Length;
            _ = new Profile2D[] { Profiles.Capsule(1, 0.5), Profiles.LShape(1, 1, 1) }.Length;
            _ = new Profile2D[]
            {
                new PolygonProfile(new Vector2D[] { new Vector2D(0, 0), new Vector2D(1, 0), new Vector2D(1, 1) }),
            }.Length;

            _ = all.Count + array.Count + json.Length + solid.Polygons.Count + roundTripped.GetHashCode();
        }

        internal static void TouchMeshIo(string directory)
        {
            var solid = Solids.Cube(10, center: true);

            var triangles = CsgMesh.TrianglesOf(solid);
            var many = CsgMesh.TrianglesOf(new List<Solid> { solid });

            var binary = StlWriter.Write(Path.Combine(directory, "api-compat-binary.stl"), triangles);
            var ascii = StlWriter.Write(Path.Combine(directory, "api-compat-ascii.stl"),
                triangles, StlFormat.Ascii, "api-compat");

            _ = binary.TriangleCount + binary.Bytes + ascii.TriangleCount + many.Count;
            _ = StlWriter.NormalOf(triangles[0]).X;

            using (var textWriter = new StringWriter())
            {
                solid.WriteStl("api-compat", textWriter);
                _ = textWriter.ToString().Length;
            }

            using (var polygonWriter = new StringWriter())
            {
                solid.Polygons[0].WriteStl(polygonWriter);
                _ = polygonWriter.ToString().Length;
            }

            _ = new Vector3D(1, 2, 3).ToStlString();
            _ = solid.Polygons[0].Vertices[0].ToStlString();
            _ = solid.ToStlString("api-compat");

            _ = ViewMeshReader.ReadStlFile(binary.Path).Count;
        }

        internal static void TouchViews()
        {
            var solid = Solids.Cube(10, center: true);
            var mesh = CsgMesh.TrianglesOf(solid);

            var triangles = new List<ViewTriangle>();
            foreach (var triangle in mesh)
                triangles.Add(new ViewTriangle(
                    new ViewPoint(triangle.A.X, triangle.A.Y, triangle.A.Z),
                    new ViewPoint(triangle.B.X, triangle.B.Y, triangle.B.Z),
                    new ViewPoint(triangle.C.X, triangle.C.Y, triangle.C.Z)));

            var topology = new ViewMeshTopology(triangles);
            _ = topology.Edges.Count + topology.Points.Count;

            var projector = new ViewProjector(triangles);
            var views = projector.Project(ViewDirections.OrthographicThree);
            var single = projector.ProjectView(ViewDirections.Front);

            var bundle = new OrthographicViews("api-compat", "API 兼容探针", views);
            _ = bundle.Bounds.Width + bundle.Views.Count;
            _ = single.VisibleCount + single.HiddenCount + single.ArcCount;
            _ = projector.TriangleCount;

            var segments = new List<ViewSegment>
            {
                new ViewSegment(new ViewPoint2(0, 0), new ViewPoint2(1, 0), true),
                new ViewSegment(new ViewPoint2(1, 0), new ViewPoint2(1, 1), false),
            };
            var curves = ViewArcFitter.Fit(segments);
            var merged = ViewArcFitter.MergeConcyclic(curves.Arcs);
            var cleaned = ViewSegmentCleanup.Clean(segments, new ViewBounds2(0, 0, 1, 1));

            _ = curves.Arcs.Count + curves.Segments.Count + merged.Count + cleaned.Count;
            _ = ViewBounds2.Of(new[] { new ViewPoint2(0, 0) }).Width;
            _ = ViewBounds2.Empty.Offset(1, 1).Union(new ViewBounds2(0, 0, 1, 1)).Height;
        }

        internal static void TouchHelpers()
        {
            _ = CsgHelpers.Place(CsgNodes.Box(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1)),
                new Vector3D(0, 0, 0), new Vector3D(0, 0, 0));
            _ = CsgHelpers.RotateAboutCenter(
                CsgNodes.Box(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1)),
                new Vector3D(0, 0, 0), new Vector3D(0, 0, 45));
            _ = CsgHelpers.PrismXY(
                new List<(double X, double Y)> { (0, 0), (1, 0), (1, 1) }, 0, 1);
            _ = CsgHelpers.PrismXZ(
                new List<(double X, double Z)> { (0, 0), (1, 0), (1, 1) }, 0, 1);
            _ = CsgHelpers.CylinderAlongX(new Vector3D(0, 0, 0), 1, 2);
            _ = CsgHelpers.CylinderAlongZ(new Vector3D(0, 0, 0), 1, 2);
            _ = CsgHelpers.CornerWedge(0, 0, 1, 1, 1, 0, 1);
            _ = CsgHelpers.CornerCut(0, 0, 0, 1, 1);
            _ = CsgHelpers.TabbedPlateSlantAngleDeg(10, 0, 1, 0.5);
            _ = CsgHelpers.TabbedPlate(0, 1, 0, 10, 0, 1, 0.5, 0.2);
            _ = CsgHelpers.RevolvedStack(
                new List<(double Radius, double Y)> { (1, 0), (2, 1), (1, 2) }, 0, 0, 0);
            _ = CsgHelpers.RoundConvexCorner(
                CsgNodes.Box(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1)), 1, 1, 0.5, 1, 1, 1);
            _ = CsgHelpers.FilletConcave(
                CsgNodes.Box(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1)), 1, 1, 0.5, 1, 1, 1);
            _ = CsgHelpers.ArcSegment(0, 90, 4, 0, 1);
            _ = CsgHelpers.PlaceRotateThenTranslate(
                CsgNodes.Box(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1)),
                new Vector3D(0, 0, 0), new Vector3D(0, 0, 0));

            var arcPoints = new List<(double X, double Y)> { (0, 0) };
            CsgHelpers.AddArc(arcPoints, 0, 0, 1, 0, 90, 4);
            _ = arcPoints.Count;

            _ = HalfPlaneCut.Build(0, 0, 0, 0, 1, 10, 1, 1);
            _ = HalfPlaneCut.BuildXY(0, 0, 1, 0, 10, 1, 1, 0);

            _ = Tessellation.SegmentsFor(5.0, 0.05);
            _ = Tessellation.SegmentsForArc(5.0, 90, 0.05);
            _ = Tessellation.IsAdaptive;
            Tessellation.ChordTolerance = 0.05;
            var previous = Tessellation.ChordTolerance;
            Tessellation.ChordTolerance = 0;

            _ = PrimitiveConvention.OriginBased.BoxX;
            _ = PrimitiveConvention.FrustumRadii(1, 0);
            _ = previous;
        }
    }
}
```

**注意**：`ApiSurface` 是 `internal static` 且方法无人调用 —— 编译器不为未使用的 `internal` 方法报错，所以这不成问题。但 `TreatWarningsAsErrors=true` 下**命名局部变量"已赋值未使用"会触发 CS0219**，所以上面所有返回值一律用弃元 `_` 消费。若仍有告警，按 Step 3 的 (c) 分支处理。

- [ ] **Step 3: 构建，记录真实结果**

```bash
dotnet build tests/OpenCSG.NET.ApiCompat/OpenCSG.NET.ApiCompat.csproj -c Release 2>&1 | tail -30
```

三种可能的结果，各自处置：

- **(a) 构建成功** — 记录「netstandard2.0 消费面可用（含 record + init）」，进入 Step 4。
- **(b) 报 `CS0518 / CS0656: 缺少 System.Runtime.CompilerServices.IsExternalInit`** — 规划时预判会命中，**实测证伪**：从 `netstandard2.0` 消费本库（含 `with` 表达式与 `init` 对象初始化器）编译 0 警告 0 错误。
  `IsExternalInit` 的缺失只在**消费方自己声明** `record`/`init` 成员时出现，与是否引用本库无关 —— 已在「不引用 OpenCSG.NET 的 netstandard2.0 工程」中复现同样的 CS0518，故它是一条 C#/TFM 约束，**不是本库的 API 缺陷**，不登记 `DEFECTS.md`。
  若将来真出现该错误，按下列流程处置（保留以备 Plan B+ 新消费方）：读 `tests/DEFECTS.md` 取下一个空闲 `DEFECT-nnn`（首次即 `DEFECT-001`）；按 Task 1 Step 5 的表头填一整行（模块 `CsgNode/CsgSerialization`、严重度 `中`、现象「netstandard2.0 消费方无法直接使用 record 的 init 访问器」、最小复现、影响面「所有 netstandard2.0 / .NET Framework 消费方须自行声明同名类型」、处置 `只报告`）；再新建 `tests/OpenCSG.NET.ApiCompat/IsExternalInit.cs`（**不要混进 `ApiSurface.cs`**）并引用该具体编号：

  ```csharp
  // netstandard2.0 没有 IsExternalInit；库内的 polyfill 是 internal，消费方必须自带。
  // 见 tests/DEFECTS.md 的 DEFECT-001（编号以实际登记为准）。
  namespace System.Runtime.CompilerServices
  {
      internal static class IsExternalInit
      {
      }
  }
  ```
- **(c) 其他编译错误** — 说明探针代码写错了（例如某个 API 签名与规划时的假设不符）。**逐个按库的真实签名修正探针**，因为探针的职责是消费真实 API 面，不是重新定义它。修正依据是 `src/OpenCSG.NET/` 里的实际声明。**不要把不匹配的行删掉** —— 若某个成员确实无法从 `netstandard2.0` 使用，那是 (b) 或新缺陷，应登记而非删除。
- **探针必须覆盖 record + init 的消费路径。** 规划时的 `ApiSurface` 只用位置构造函数并读属性，**从不触碰 init 访问器**，所以它无法发现 init 消费面的问题。必须补一段消费 `with` 表达式与 `init` 对象初始化器的代码，否则本工程存在的意义（验证 record + init 的可消费性）落空。

- [ ] **Step 4: 确认构建成功且零告警**

```bash
dotnet build tests/OpenCSG.NET.ApiCompat/OpenCSG.NET.ApiCompat.csproj -c Release 2>&1 | grep -E "Warning|Error|警告|错误" | head -20
```

预期：无输出（`TreatWarningsAsErrors` 下任何告警都会变成错误，所以能构建成功即等价于零告警）。

- [ ] **Step 5: 登记到解决方案**

`OpenCSG.NET.slnx` 的 `/tests/` 文件夹里，在 `OpenCSG.NET.TestKit` 之前插入 ApiCompat：

```xml
  <Folder Name="/tests/">
    <Project Path="tests/OpenCSG.NET.ApiCompat/OpenCSG.NET.ApiCompat.csproj" />
    <Project Path="tests/OpenCSG.NET.TestKit/OpenCSG.NET.TestKit.csproj" />
    <Project Path="tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj" />
  </Folder>
```

- [ ] **Step 6: 整解决方案构建 + 全套件测试**

```bash
dotnet build OpenCSG.NET.slnx -c Release 2>&1 | tail -20
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release 2>&1 | tail -10
```

预期：解决方案 5+2 = 7 个工程全部构建成功（含 samples 与 perf —— 这是**首次**在本地把 samples 纳入构建）；测试失败 0。

若 samples/perf 构建失败，那是**新发现**：说明它们与库的当前 API 已脱节（CI 从不编译它们）。登记为缺陷，但**本任务不修**（超出 Plan A 范围），记入 `TEST-REPORT` 待后续计划处理。

- [ ] **Step 7: 提交（需用户确认）**

```bash
git add tests/OpenCSG.NET.ApiCompat OpenCSG.NET.slnx tests/DEFECTS.md docs/testing
git commit -m "test: 新增 netstandard2.0 消费面编译工程 ApiCompat"
```

---

## 完成定义（Plan A）

全部满足才算完成：

- [ ] `dotnet build OpenCSG.NET.slnx -c Release` 成功，0 警告 0 错误（含 samples 与 perf）
- [ ] `dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release` 失败 0、Inconclusive 0
- [ ] `docs/testing/TEST-REPORT-2026-10-13.md` 含阶段 0 基线的**真实**数字（行/分支覆盖率 + 13 个模块级数字），无占位符
- [ ] `GeometryAssert.cs` 已在 TestKit 内且命名空间仍为 `Csg.Test`，现有 19 个测试文件的断言逻辑零改动（`git diff` 可验证）
- [ ] `SolidTest.cs` 未被修改（`git diff --stat` 不含它）
- [ ] `GoldenGuard` 端到端验证通过（含 Step 7 的"制造残留 ⇒ 失败 ⇒ 撤掉 ⇒ 转绿"三步实证）
- [ ] `tools/promote-goldens.sh` 可执行
- [ ] `tests/OpenCSG.NET.ApiCompat` 构建成功；netstandard2.0 消费面的真实结论（含 IsExternalInit 事件，如有）已记入报告
- [ ] `tests/DEFECTS.md` 已建立（即使为空表也要有表头与双向索引规则说明）
- [ ] `ViewBridge` 打通 `Solid → HLR` 管线，三视图解析解 oracle 结果已记录

## 后续计划（不在本计划范围）

| 计划 | 内容 | 依赖 Plan A 的 |
| --- | --- | --- |
| Plan B | 生成器 + Corpus + DataGen + FsCheck spike + Tier 1–3/5 属性测试 | `MeshAssert`、`ViewBridge`、`GoldenPaths` |
| Plan C | L1/L2 单元与契约回填 + A1/A2/A4 最小修复 + 回归测试 | `MeshAssert`、`GeometryAssert`（TestKit 内） |
| Plan D | L4 golden + L5 集成端到端（含 `CpurlinModel` 抽取） | `GoldenGuard`、`ViewBridge`、`promote-goldens.sh` |
| Plan E | L6 perf/复杂度 + L7 稳健性/并行/跨运行时 | TestKit 全部 |
| Plan F | 覆盖率门禁 + CI 改造（.NET 9/10、windows 矩阵、nightly）+ 报告定稿 | `ApiCompat`、`TEST-REPORT` |
