# OpenCSG.NET 全面测试体系 — Plan B1（生成器 / Corpus / DataGen + Tier 1–3 属性测试）实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立程序化测试数据体系（种子可复现的实体/节点树/线段集生成器 + 三档语料 + 物化工具）与 Tier 1–3 代数 oracle，并用属性测试首次系统性搜索布尔内核与 HLR 前端的缺陷。

**Architecture:** 新增 `tests/OpenCSG.NET.SystemTests` 承载属性/模糊测试（NUnit + FsCheck），TestKit 提供生成器与 oracle。核心是 **spec-then-materialize**：生成器产出**参数 record**（`SolidSpec`），再由纯函数物化成 `Solid` —— 这样 FsCheck 的收缩给出的是「人能读懂、能一行复现的最小参数反例」，而不是无法解释的随机网格。属性断言全部走**精确代数恒等式**（如 `V(A∪B)+V(A∩B)=V(A)+V(B)`），不依赖 golden 数据。

**Tech Stack:** .NET 8.0、NUnit 3.13.3、`FsCheck.NUnit` 3.4.0（含 2.16.x 与手写降级路径）、`coverlet.collector`。核心库保持 `netstandard2.0` 零新增依赖。

**Spec:** `docs/superpowers/specs/2026-10-13-comprehensive-testing-design.md`（§5 数据生成体系、§6 Oracle 分层、§7 L3 层、§13 Plan B 承接事项）

## Global Constraints

- 核心库 `src/OpenCSG.NET` 是 `netstandard2.0`，唯一依赖 `System.Text.Json 8.0.6`。**本计划禁止给 `src/` 加任何依赖、禁止改其坐标口径/容差/算法。**
- `src/OpenCSG.NET/OpenCSG.NET.csproj` 有 `TreatWarningsAsErrors=true` + `GenerateDocumentationFile=true`（仅 `1591` 抑制）。任何 `src/` 改动引入新警告即构建失败。
- 库的根命名空间是 **`Csg`**；测试命名空间是 **`Csg.Test`**。
- 现有 20 个测试文件与 122 个用例**不得修改其断言逻辑**；`SolidTest.cs` 不得修改；`tests/OpenCSG.NET.Tests/Results/` 必须始终恰好 45 个 `.stl` 且零 `*_.stl` 残留（`GoldenGuard` 会硬失败）。
- `tests/OpenCSG.NET.TestKit` 与新增的 `SystemTests` 都开 `<Nullable>enable</Nullable>`：任何 `string p = null` 未加 `?` 都会产生 CS8625 并破坏 0 告警门禁。
- 新增测试依赖只允许加在 `tests/` 与 `tools/` 的项目上。
- **产品代码默认只报告不改。** 本计划若发现缺陷：登记到 `tests/DEFECTS.md`（取下一个空闲 `DEFECT-nnn`），对应用例标 `[Explicit("DEFECT-nnn: <摘要>")]`，套件保持绿色。**不改 `src/`，不改期望值或容差去迎合实现。**
- 全部构建要 0 警告 0 错误：`dotnet build OpenCSG.NET.slnx -c Release`。
- 新文本文件 UTF-8 无 BOM；`.cs` 用 LF。
- 命令统一在仓库根目录 `D:\self\code\OpenCSG.NET` 执行。
- **提交策略**：用户已授权按本计划的 commit 步骤自动提交（每任务一次）。提交前先 `git status` 确认没有夹带无关文件。
- 已知会被生成器**主动排除**的输入（因为它们是被 Plan C 承接的已知缺陷，纳入随机采样只会制造噪音）：`CylinderOptions.SectorAngle` ∈ {0, 720, 负数}（`Solids.cs:136-137` 静默返回空实体）、`RadiusStart/RadiusEnd` 为负数（被 `Math.Abs` 静默取绝对值）。

## 与 spec 的偏离与范围边界（记录理由）

1. **`corpus.json` 放在 TestKit 而不是 spec §4 树里的 `tests/OpenCSG.NET.Tests/TestData/`。** 理由：语料是生成器的产物，而生成器在 TestKit；三处消费方（Tests / SystemTests / DataGen）通过 TestKit 统一访问，无需跨项目复制文件；用 `CopyToOutputDirectory` + `typeof(Corpus).Assembly.Location` 定位，彻底避开 spec 批评过的 4 层 `GetDirectoryName` 脆弱路径。
2. **`TriangleGen` 不单独实现。** 三角网格输入直接由 `CsgMesh.TrianglesOf(SolidGen.Materialize(spec))` 产生 —— 覆盖更真实（走库内真实转换链），代码更少。`ViewSegGen`（2D 线段集）仍需单独实现，因为 Tier 3 要测 `ViewArcFitter`/`ViewSegmentCleanup` 的输入不是网格。
3. **退化输入空间（spec §5.1 的「退化」档）不在本计划的生成器采样范围内。** spec §5.1 列的退化情形（零尺寸、共面面、自交 `PolygonProfile`、重复点、共线点、空列表）属于 **spec §7 的 L2 契约与边界层**，由 Plan C 用显式用例覆盖。理由：这些输入大多使代数恒等式**真空成立**（空实体让 `V(A∪B)=V(A)+V(B)−V(A∩B)` 退化成 `0=0`），纳入随机采样只会稀释信号而不是增加信息；它们需要的是「断言抛出的异常类型与消息」这类精确契约断言，而不是属性断言。本计划的生成器只覆盖**良构空间** —— 这才是代数恒等式有信息量的地方。**这条边界必须保持：不要为了「覆盖更多」而把退化输入塞进 SolidGen。**


## 文件结构

| 文件 | 职责 |
| --- | --- |
| `tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj` | 新建；`net8.0`；NUnit + FsCheck.NUnit + TestKit + 核心库 |
| `tests/OpenCSG.NET.SystemTests/Generation/SolidSpecPropertyTest.cs` | FsCheck 冒烟：验证 `SolidSpec` 可被生成与收缩 |
| `tests/OpenCSG.NET.TestKit/Generation/SplitMix64.cs` | 新建；零依赖确定性 PRNG（`internal`） |
| `tests/OpenCSG.NET.TestKit/Generation/SolidSpec.cs` | 新建；实体参数 record + `PrimitiveKind`/`ProfileKind` 枚举 + `CoordTier` 枚举 |
| `tests/OpenCSG.NET.TestKit/Generation/SolidGen.cs` | 新建；`Random`/`RandomPair`（种子→spec）+ `Materialize`（spec→Solid） |
| `tests/OpenCSG.NET.TestKit/Generation/NodeGen.cs` | 新建；种子→`CsgNode` 树（10 种节点 × 8 种 profile） |
| `tests/OpenCSG.NET.TestKit/Generation/ViewSegGen.cs` | 新建；种子→2D 线段集（链/弧采样/重叠共线/短残段/混合可见性） |
| `tests/OpenCSG.NET.TestKit/Generation/Corpus.cs` | 新建；三档语料入口（Smoke/Corpus/Long）+ 读 `corpus.json` |
| `tests/OpenCSG.NET.TestKit/Generation/corpus.json` | 新建（由 DataGen 生成后提交）；种子 + spec + 观测指标 |
| `tests/OpenCSG.NET.TestKit/Oracles/IdentityAssert.cs` | 新建；Tier 1 代数恒等式断言 |
| `tests/OpenCSG.NET.TestKit/Oracles/ConvexHull.cs` | 新建；Tier 2 独立 2D 凸包 + 周长 |
| `tests/OpenCSG.NET.TestKit/Oracles/HlrAssert.cs` | 新建；Tier 3 HLR 结构不变量与覆盖度断言 |
| `tests/OpenCSG.NET.TestKit/Infrastructure/TessellationScope.cs` | 新建；`IDisposable` 复位 `Tessellation.ChordTolerance` |
| `tests/OpenCSG.NET.TestKit/GoldenPaths.cs` | 改；标记文件名参数化 |
| `tests/OpenCSG.NET.Tests/Views/ViewArcFitterDiagnosticsTest.cs` | 新建；`DEFECT-001` 的 `[Explicit]` 复现用例 |
| `tests/OpenCSG.NET.SystemTests/Property/Tier1BooleanIdentityTest.cs` | 新建；布尔恒等式属性测试 |
| `tests/OpenCSG.NET.SystemTests/Property/Tier2AnalyticTest.cs` | 新建；解析解属性测试 |
| `tests/OpenCSG.NET.SystemTests/Property/Tier3StructureTest.cs` | 新建；结构/单调/幂等属性测试 |
| `tools/OpenCSG.NET.DataGen/OpenCSG.NET.DataGen.csproj` + `Program.cs` | 新建；生成 `corpus.json` 与物化数据集 |
| `OpenCSG.NET.slnx` | 改；登记 SystemTests 与 DataGen |
| `tests/DEFECTS.md` | 改；登记 `DEFECT-001` |
| `docs/testing/TEST-REPORT-2026-10-13.md` | 改；追加 Plan B1 章节 |

---

### Task 1: SystemTests 骨架 + FsCheck 兼容性 spike

**Files:**
- Create: `tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj`
- Create: `tests/OpenCSG.NET.SystemTests/Generation/SolidSpecPropertyTest.cs`
- Modify: `OpenCSG.NET.slnx`

**Interfaces:**
- Consumes: `tests/OpenCSG.NET.TestKit`（已存在，提供 `GeometryAssert` 等）
- Produces: 一个可构建、可运行的 `SystemTests` 工程，以及**已决的 FsCheck 版本**（后续任务的属性测试依赖它）

- [ ] **Step 1: 建工程（先不带 FsCheck）**

`tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
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
    <ProjectReference Include="..\OpenCSG.NET.TestKit\OpenCSG.NET.TestKit.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: 写一个不依赖 FsCheck 的基准测试**

`tests/OpenCSG.NET.SystemTests/Generation/SolidSpecPropertyTest.cs`：

```csharp
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>SystemTests 骨架自检：工程可构建、TestKit 可引用、断言库可用。</summary>
    [TestFixture]
    public class SolidSpecPropertyTest
    {
        [Test]
        public void Skeleton_CanEvaluateSolidAndAssertInvariants()
        {
            var cube = Solids.Cube(2, center: true);

            GeometryAssert.AssertSolidInvariants(cube);
            GeometryAssert.AssertVolume(cube, 8.0, 1e-9);
        }
    }
}
```

- [ ] **Step 3: 登记到解决方案并跑通**

`OpenCSG.NET.slnx` 的 `/tests/` 文件夹里，在 `OpenCSG.NET.TestKit` 之后、`OpenCSG.NET.Tests` 之前插入：

```xml
    <Project Path="tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj" />
```

```bash
dotnet build OpenCSG.NET.slnx -c Release 2>&1 | tail -6
dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release 2>&1 | tail -5
```

预期：构建 0 警告 0 错误；测试 1 通过。

**保持仓库其余部分不受影响**：同一批命令里再跑一次
```bash
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release 2>&1 | tail -3
```
预期仍是 122 通过 / 0 失败 / 0 跳过。

- [ ] **Step 4: FsCheck 兼容性 spike —— 解析这个窄分支**

加包并写一个会用到「生成 + 收缩」的最小属性：

```bash
dotnet add tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj package FsCheck.NUnit --version 3.4.0
```

在 `SolidSpecPropertyTest.cs` 追加：

```csharp
        [FsCheck.NUnit.Property(MaxTest = 50)]
        public void Spike_FsCheckCanGenerateAndShrink(int a)
        {
            // 这条断言故意会被违反（当 |a| > 1 时），用来确认 FsCheck 能生成、能失败、能收缩出最小反例。
            FsCheck.Prop.ForAll<int>(value => Math.Abs(value) <= 1)
                .QuickCheckThrowOnFailure();
        }
```

跑：

```bash
dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release --filter "FullyQualifiedName~Spike_FsCheckCanGenerateAndShrink" 2>&1 | tail -25
```

**三种结果与处置（本步必须给出结论并写进报告）：**

- **(a) 能编译、能失败、报告里出现收缩后的最小反例**（形如 `Falsifiable, after N tests (M shrinks) ... 2` —— 值为 ±2）⇒ **FsCheck 3.4.0 可用**。记录版本，然后**把这条 spike 用例删掉**（它是探针，不是测试），进入 Step 5。
- **(b) 报 NUnit 版本不兼容或 `[Property]` 特性找不到**（`CS0246` 或运行期 `TypeLoadException`/`MissingMethodException`）⇒ 降级：`dotnet remove ... package FsCheck.NUnit` 后 `dotnet add ... package FsCheck.NUnit --version 2.16.6`，重跑本步判定。2.16.x 下 `[Property]` 的用法相同。
- **(c) 2.16.6 也不行** ⇒ **走手写降级路径**：`dotnet remove ... package FsCheck.NUnit`，本计划后续任务的 FsCheck 相关步骤**全部跳过**，属性测试改为纯语料驱动（`[TestCaseSource]` 遍历 `Corpus` 的固定种子），并在 `TEST-REPORT` 里记录「FsCheck 不可用，收缩能力缺失；失败时以种子复现」。**TestKit 的生成器与 oracle 接口不受影响** —— 这是本分支被刻意做窄的原因：生成器不依赖 FsCheck，FsCheck 只提供「生成 + 收缩」。

- [ ] **Step 5: 记录决策**

在 `docs/testing/TEST-REPORT-2026-10-13.md` 追加：

```markdown
## Plan B1 — 属性测试基础设施

### Plan B1 · Task 1：SystemTests 骨架与 FsCheck spike

| 项 | 结果 |
| --- | --- |
| `tests/OpenCSG.NET.SystemTests` | 已建立（net8.0 / NUnit / Nullable enable） |
| FsCheck 版本决策 | <填实际：3.4.0 可用 / 降级 2.16.6 / 不可用走手写降级> |
| spike 证据 | <填实际：观察到的收缩反例或错误信息> |
| 现有套件未受影响 | 122 通过 / 0 失败 / 0 跳过 |
```

把 `<填实际>` 换成真实观察值。

- [ ] **Step 6: 验证无占位符**

```bash
grep -n "填实际" docs/testing/TEST-REPORT-2026-10-13.md
```

预期：无输出。

- [ ] **Step 7: 提交（已授权）**

```bash
git add tests/OpenCSG.NET.SystemTests OpenCSG.NET.slnx docs/testing/TEST-REPORT-2026-10-13.md
git commit -m "test: 新增 SystemTests 工程并确定 FsCheck 兼容性结论"
```

---

### Task 2: TestKit 布局落位 + GoldenPaths 参数化 + TessellationScope + DEFECT-001

spec §13 明确要求：加生成器**之前**先把 TestKit 目录布局按 §4 落位，并先处理全局态。

**Files:**
- Move: `tests/OpenCSG.NET.TestKit/GeometryAssert.cs` → `Oracles/GeometryAssert.cs`
- Move: `tests/OpenCSG.NET.TestKit/MeshAssert.cs` → `Oracles/MeshAssert.cs`
- Move: `tests/OpenCSG.NET.TestKit/VertexCluster.cs` → `Oracles/VertexCluster.cs`
- Move: `tests/OpenCSG.NET.TestKit/ViewBridge.cs` → `Bridge/ViewBridge.cs`
- Move: `tests/OpenCSG.NET.TestKit/GoldenPaths.cs` → `Io/GoldenPaths.cs`
- Modify: `tests/OpenCSG.NET.TestKit/Io/GoldenPaths.cs`（标记文件参数化）
- Create: `tests/OpenCSG.NET.TestKit/Infrastructure/TessellationScope.cs`
- Create: `tests/OpenCSG.NET.Tests/Views/ViewArcFitterDiagnosticsTest.cs`
- Modify: `tests/DEFECTS.md`

**Interfaces:**
- Consumes: 无
- Produces:
  - `Csg.Test.GoldenPaths.ResultsDirectory(string anchorPath, string markerFileName = "OpenCSG.NET.Tests.csproj") → string?`
  - `Csg.Test.TessellationScope : IDisposable`，ctor `(double chordTolerance)`；`Dispose()` **恢复为构造时捕获的先前值**（不是复位为 0 —— 用例会嵌套 scope，复位会静默关掉外层）
  - `DEFECT-001`（`ViewArcFitter` 静态态非线程隔离）

- [ ] **Step 1: 纯搬迁（几何不变）**

```bash
cd /d/self/code/OpenCSG.NET
mkdir -p tests/OpenCSG.NET.TestKit/Oracles tests/OpenCSG.NET.TestKit/Bridge tests/OpenCSG.NET.TestKit/Io tests/OpenCSG.NET.TestKit/Infrastructure tests/OpenCSG.NET.TestKit/Generation
git mv tests/OpenCSG.NET.TestKit/GeometryAssert.cs tests/OpenCSG.NET.TestKit/Oracles/GeometryAssert.cs
git mv tests/OpenCSG.NET.TestKit/MeshAssert.cs     tests/OpenCSG.NET.TestKit/Oracles/MeshAssert.cs
git mv tests/OpenCSG.NET.TestKit/VertexCluster.cs  tests/OpenCSG.NET.TestKit/Oracles/VertexCluster.cs
git mv tests/OpenCSG.NET.TestKit/ViewBridge.cs     tests/OpenCSG.NET.TestKit/Bridge/ViewBridge.cs
git mv tests/OpenCSG.NET.TestKit/GoldenPaths.cs    tests/OpenCSG.NET.TestKit/Io/GoldenPaths.cs
```

**只移动，不改一个字节。** 命名空间保持 `Csg.Test`，所以所有消费方（`tests/OpenCSG.NET.Tests` 的 20 个文件）无需任何修改。SDK 风格工程的 globbing 会自动包含子目录。

验证：`dotnet build OpenCSG.NET.slnx -c Release` 必须 0 警告 0 错误，`dotnet test tests/OpenCSG.NET.Tests/... -c Release` 必须仍是 122 通过。**若测试数变化，说明搬迁动了内容 —— 停下报告。**

- [ ] **Step 2: 写失败的测试（GoldenPaths 参数化）**

在 `tests/OpenCSG.NET.Tests/Infrastructure/GoldenGuardTest.cs` 追加：

```csharp
        [Test]
        public void GoldenPaths_MarkerIsParameterizable()
        {
            var asmPath = System.Reflection.Assembly.GetExecutingAssembly().Location;

            // 用当前工程的标记文件，结果必须与默认调用一致
            var viaDefault = GoldenPaths.ResultsDirectory(asmPath);
            var viaExplicit = GoldenPaths.ResultsDirectory(asmPath, "OpenCSG.NET.Tests.csproj");

            Assert.That(viaExplicit, Is.EqualTo(viaDefault));

            // 用一个不存在的标记文件，必须返回 null 而不是抛异常
            var missing = GoldenPaths.ResultsDirectory(asmPath, "__NoSuchMarker__.csproj");

            Assert.That(missing, Is.Null);
        }
```

- [ ] **Step 3: 跑测试确认失败**

```bash
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release --filter "FullyQualifiedName~GoldenPaths_MarkerIsParameterizable" 2>&1 | tail -12
```

预期：编译失败 —— `CS1501: 方法"ResultsDirectory"不采用 2 个参数`。

- [ ] **Step 4: 参数化 GoldenPaths**

`tests/OpenCSG.NET.TestKit/Io/GoldenPaths.cs` 的 `ResultsDirectory` 改为：

```csharp
        /// <summary>
        /// 从 anchorPath（程序集文件路径或目录路径均可）向上找包含 markerFileName 的目录，
        /// 返回其下 Results/；找不到返回 null。
        /// marker 可参数化，使 SystemTests 等其它测试程序集也能复用。
        /// </summary>
        public static string? ResultsDirectory(string anchorPath, string markerFileName = "OpenCSG.NET.Tests.csproj")
        {
            if (string.IsNullOrEmpty(anchorPath) || string.IsNullOrEmpty(markerFileName))
                return null;

            var start = Directory.Exists(anchorPath) ? anchorPath : Path.GetDirectoryName(anchorPath);
            if (string.IsNullOrEmpty(start))
                return null;

            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, markerFileName)))
                    return Path.Combine(dir.FullName, "Results");
                dir = dir.Parent;
            }
            return null;
        }
```

删除原文件中私有常量 `const string Marker`。

- [ ] **Step 5: 确认转绿**

```bash
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release 2>&1 | tail -4
```

预期：123 通过（122 + 1 新增）/ 0 失败 / 0 跳过。

- [ ] **Step 6: 写失败的测试（TessellationScope）**

`tests/OpenCSG.NET.Tests/Tessellation/TessellationScopeTest.cs`（新目录）：

```csharp
using System;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    [TestFixture]
    public class TessellationScopeTest
    {
        [Test]
        public void Scope_SetsToleranceAndRestoresOnDispose()
        {
            Assert.That(Tessellation.ChordTolerance, Is.EqualTo(0.0), "前置：默认必须关闭自适应");

            using (new TessellationScope(0.05))
            {
                Assert.That(Tessellation.ChordTolerance, Is.EqualTo(0.05));
                Assert.That(Tessellation.IsAdaptive, Is.True);
            }

            Assert.That(Tessellation.ChordTolerance, Is.EqualTo(0.0), "Dispose 必须复位，否则污染后续用例");
            Assert.That(Tessellation.IsAdaptive, Is.False);
        }

        [Test]
        public void Scope_RestoresEvenWhenBodyThrows()
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                using (new TessellationScope(0.05))
                    throw new InvalidOperationException("模拟用例体抛异常");
            });

            Assert.That(Tessellation.ChordTolerance, Is.EqualTo(0.0));
        }

        [Test]
        public void AdaptiveTolerance_ChangesSegmentCount()
        {
            // 关闭自适应 ⇒ 旧口径 12 段；开启 ⇒ 按弦高容差加密（下限 16）
            Assert.That(Tessellation.SegmentsFor(100.0), Is.EqualTo(Solid.DefaultResolution3D));

            using (new TessellationScope(0.05))
                Assert.That(Tessellation.SegmentsFor(100.0),
                    Is.GreaterThan(Solid.DefaultResolution3D));
        }
    }
}
```

- [ ] **Step 7: 跑测试确认失败**

```bash
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release --filter "FullyQualifiedName~TessellationScopeTest" 2>&1 | tail -12
```

预期：编译失败 —— `CS0246: 未能找到类型或命名空间名"TessellationScope"`。

- [ ] **Step 8: 实现 TessellationScope**

`tests/OpenCSG.NET.TestKit/Infrastructure/TessellationScope.cs`：

```csharp
using System;
using Csg;

namespace Csg.Test
{
    /// <summary>
    /// <see cref="Tessellation.ChordTolerance"/> 的作用域守卫。
    /// 该开关是 [ThreadStatic] 可变全局态 —— 忘记复位会污染同线程后续用例，
    /// 而自适应一旦打开会把每个基本体的分段数从 12/16 改成 16..128，
    /// 从而改变所有既有断言的结果。用 using 强制成对。
    /// </summary>
    public sealed class TessellationScope : IDisposable
    {
        readonly double _previous;

        public TessellationScope(double chordTolerance)
        {
            _previous = Tessellation.ChordTolerance;
            Tessellation.ChordTolerance = chordTolerance;
        }

        public void Dispose() => Tessellation.ChordTolerance = _previous;
    }
}
```

- [ ] **Step 9: 确认转绿**

```bash
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release 2>&1 | tail -4
```

预期：126 通过（123 + 3）/ 0 失败 / 0 跳过。

- [ ] **Step 10: 登记 DEFECT-001（ViewArcFitter 静态态非线程隔离）**

`ViewArcFitter.DiagnosticsEnabled` / `LastDiagnostics` 是普通 `static`（非 `[ThreadStatic]`），而 `Tessellation` 的同族开关有注释记录「曾因全局静态被并行测试串扰」。写一条**描述正确行为**的用例（今天会失败，故标 `[Explicit]`；将来修好删标注即转绿）：

`tests/OpenCSG.NET.Tests/Views/ViewArcFitterDiagnosticsTest.cs`：

```csharp
using System.Threading;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    [TestFixture]
    public class ViewArcFitterDiagnosticsTest
    {
        [Test]
        [Explicit("DEFECT-001: ViewArcFitter.DiagnosticsEnabled/LastDiagnostics 非线程隔离，并行测试下串扰")]
        public void DiagnosticsEnabled_IsNotVisibleOnOtherThreads()
        {
            var segments = new[]
            {
                new ViewSegment(new ViewPoint2(0, 0), new ViewPoint2(1, 0), true),
                new ViewSegment(new ViewPoint2(1, 0), new ViewPoint2(2, 0), true),
            };

            ViewArcFitter.DiagnosticsEnabled = true;
            ViewArcFitter.Fit(segments);   // 触发一次写入 LastDiagnostics

            bool? seenOnOtherThread = null;
            var thread = new Thread(() => seenOnOtherThread = ViewArcFitter.DiagnosticsEnabled);
            thread.Start();
            thread.Join();

            try
            {
                // 期望：诊断开关是线程局部状态，另一个线程不应看到它。
                // 实际（DEFECT-001）：普通 static ⇒ 另一线程读到 true，本断言失败。
                Assert.That(seenOnOtherThread, Is.False,
                    "DEFECT-001：另一线程读到了 DiagnosticsEnabled=true，说明它是进程级全局态");
            }
            finally
            {
                ViewArcFitter.DiagnosticsEnabled = false;
            }
        }
    }
}
```

在 `tests/DEFECTS.md` 的「未修复」表里把 `（暂无）` 行替换为：

```markdown
| DEFECT-001 | `ViewArcFitter` | 低 | `DiagnosticsEnabled`/`LastDiagnostics` 是进程级静态而非 `[ThreadStatic]`，并行测试会互相串扰（同族开关 `Tessellation.ChordTolerance` 已按线程隔离并有注释记录该问题） | 删掉 `ViewArcFitterDiagnosticsTest.DiagnosticsEnabled_IsNotVisibleOnOtherThreads` 的 `[Explicit]` 标注后，运行 `dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release --filter "FullyQualifiedName~DiagnosticsEnabled_IsNotVisibleOnOtherThreads"`，该用例失败并把「另一线程读到 true」打进断言消息 | 期望：诊断开关是线程局部状态，另一线程读不到；实际：读到 `true` | 并行执行测试时会互相污染诊断输出；单线程串行运行不受影响 | 只报告（Plan C 决定） |
```

**关于最小复现的写法**：刻意不写 `dotnet test -- NUnit.ExplicitMode=None` —— 那不是可靠开关（`ExplicitMode` 属于 NUnit 适配器设置，通常要经 `.runsettings` 提供，直接跟在 `--` 后面未必生效）。「删标注再跑」是**必然可复现**的一行操作，且不需要额外文件。

- [ ] **Step 11: 确认套件仍绿且该用例默认被跳过**

```bash
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release 2>&1 | tail -4
```

预期：**126 通过 / 0 失败 / 1 跳过**（`[Explicit]` 用例默认不计入运行）。**如果出现失败，说明 `[Explicit]` 没生效或那个用例真的在跑 —— 停下报告。**

- [ ] **Step 12: 提交（已授权）**

```bash
git add tests/OpenCSG.NET.TestKit tests/OpenCSG.NET.Tests tests/DEFECTS.md
git commit -m "test: TestKit 目录落位、GoldenPaths 参数化、TessellationScope 与 DEFECT-001 登记"
```

---

### Task 3: SplitMix64 + SolidSpec + SolidGen

**Files:**
- Create: `tests/OpenCSG.NET.TestKit/Generation/SplitMix64.cs`
- Create: `tests/OpenCSG.NET.TestKit/Generation/SolidSpec.cs`
- Create: `tests/OpenCSG.NET.TestKit/Generation/SolidGen.cs`
- Test: `tests/OpenCSG.NET.SystemTests/Generation/SolidGenTest.cs`

**Interfaces:**
- Consumes: `GeometryAssert`（Oracles/）、`MeshAssert`（Oracles/）
- Produces:
  - `internal sealed class Csg.Test.SplitMix64`（ctor `(ulong seed)`；`NextUInt64()`、`NextDouble()`、`NextInt(int exclusiveMax)`、`NextLogUniform(double min, double max)`、`NextSigned()`）
  - `public enum Csg.Test.PrimitiveKind { Cube, Sphere, Cylinder, Cone, Extrude, Wedge }`
  - `public enum Csg.Test.ProfileKind { Rectangle, HBeam, Channel, SquareTube, Trapezoid, Capsule, LShape }`
  - `public enum Csg.Test.CoordTier { Normal, Survey }`
  - `public sealed record Csg.Test.SolidSpec`（属性见下）
  - `public static class Csg.Test.SolidGen`：`Random(long seed, Csg.Test.CoordTier tier) → SolidSpec`、`RandomPair(long seed, Csg.Test.CoordTier tier) → (SolidSpec A, SolidSpec B)`、`Materialize(SolidSpec spec) → Solid`

- [ ] **Step 1: 写失败的测试**

`tests/OpenCSG.NET.SystemTests/Generation/SolidGenTest.cs`：

```csharp
using System.Collections.Generic;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    [TestFixture]
    public class SolidGenTest
    {
        [Test]
        public void Random_SameSeed_ProducesIdenticalSpec()
        {
            var a = SolidGen.Random(12345, CoordTier.Normal);
            var b = SolidGen.Random(12345, CoordTier.Normal);

            Assert.That(b, Is.EqualTo(a), "同种子必须产出完全相同的 spec（可复现性的基础）");
        }

        [Test]
        public void Random_DifferentSeeds_ProduceDifferentSpecs()
        {
            var seen = new HashSet<SolidSpec>();
            for (long seed = 0; seed < 64; seed++)
                seen.Add(SolidGen.Random(seed, CoordTier.Normal));

            Assert.That(seen.Count, Is.GreaterThan(32), "64 个种子应产出多数互异的 spec");
        }

        [Test]
        public void Materialize_IsDeterministic()
        {
            var spec = SolidGen.Random(777, CoordTier.Normal);

            var first = SolidGen.Materialize(spec);
            var second = SolidGen.Materialize(spec);

            Assert.That(GeometryAssert.SignedVolume(second),
                Is.EqualTo(GeometryAssert.SignedVolume(first)).Within(1e-12));
            Assert.That(second.Polygons.Count, Is.EqualTo(first.Polygons.Count));
        }

        [Test]
        public void Materialize_EveryPrimitiveKind_IsANonEmptyOrientedSolid()
        {
            foreach (var kind in new[]
            {
                PrimitiveKind.Cube, PrimitiveKind.Sphere, PrimitiveKind.Cylinder,
                PrimitiveKind.Cone, PrimitiveKind.Extrude, PrimitiveKind.Wedge,
            })
            {
                var spec = SolidGen.Random(42, CoordTier.Normal) with { Kind = kind };
                var solid = SolidGen.Materialize(spec);

                Assert.That(solid.Polygons.Count, Is.GreaterThan(0), $"{kind}: 不得为空实体");
                Assert.That(GeometryAssert.SignedVolume(solid), Is.GreaterThan(0),
                    $"{kind}: 有向体积必须为正（法线朝外）");
                GeometryAssert.AssertPlanesMatchWinding(solid, kind.ToString());
            }
        }

        [Test]
        public void Materialize_SurveyTier_KeepsVolumeAndShiftsBounds()
        {
            var spec = SolidGen.Random(2026, CoordTier.Normal) with
            {
                Kind = PrimitiveKind.Cube,
                SizeX = 10, SizeY = 20, SizeZ = 30,
                Resolution = 12,
                OffsetX = 1_000_000, OffsetY = -2_000_000, OffsetZ = 3_000_000,
                RotateX = 0, RotateY = 0, RotateZ = 0, Scale = 1, Mirror = false,
            };

            var solid = SolidGen.Materialize(spec);

            Assert.That(GeometryAssert.SignedVolume(solid), Is.EqualTo(6000.0).Within(1e-6));

            var (min, max) = GeometryAssert.Bounds(solid);
            Assert.That(min.X, Is.EqualTo(1_000_000 - 5).Within(1e-6));
            Assert.That(max.Z, Is.EqualTo(3_000_000 + 15).Within(1e-6));
        }

        [Test]
        public void Materialize_MirrorFlagsStillOutwardFacing()
        {
            var spec = SolidGen.Random(9, CoordTier.Normal) with
            {
                Kind = PrimitiveKind.Cube,
                Scale = 1, RotateX = 0, RotateY = 0, RotateZ = 0, Mirror = true,
            };

            var solid = SolidGen.Materialize(spec);

            Assert.That(GeometryAssert.SignedVolume(solid), Is.GreaterThan(0),
                "镜像后法线必须仍朝外（库内 Transform 会翻转绕序补偿）");
        }

        [Test]
        public void RandomPair_OffsetsAreCorrelated_SoSomePairsOverlap()
        {
            var overlapping = 0;
            for (long seed = 0; seed < 64; seed++)
            {
                var (a, b) = SolidGen.RandomPair(seed, CoordTier.Normal);
                var (minA, maxA) = GeometryAssert.Bounds(SolidGen.Materialize(a));
                var (minB, maxB) = GeometryAssert.Bounds(SolidGen.Materialize(b));

                bool disjoint = maxA.X < minB.X || maxB.X < minA.X
                             || maxA.Y < minB.Y || maxB.Y < minA.Y
                             || maxA.Z < minB.Z || maxB.Z < minA.Z;
                if (!disjoint) overlapping++;
            }

            Assert.That(overlapping, Is.GreaterThan(0), "必须存在相交对，否则布尔恒等式毫无信息量");
            Assert.That(overlapping, Is.LessThan(64), "也必须存在不相交对，覆盖 MayOverlap 快速路径");
        }

        [Test]
        public void Random_ExcludesKnownDefectiveInputs()
        {
            for (long seed = 0; seed < 512; seed++)
            {
                var spec = SolidGen.Random(seed, CoordTier.Normal);

                Assert.That(spec.SectorAngle, Is.EqualTo(360.0),
                    "已知缺陷：SectorAngle ∈ {0, 720, 负数} 会静默产出空实体（Plan C 承接），随机采样必须排除");
                Assert.That(spec.RadiusEnd, Is.GreaterThan(0));
            }
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```bash
dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release --filter "FullyQualifiedName~SolidGenTest" 2>&1 | tail -12
```

预期：编译失败 —— `CS0246: 未能找到类型或命名空间名"SolidGen"`。

- [ ] **Step 3: 实现 SplitMix64**

`tests/OpenCSG.NET.TestKit/Generation/SplitMix64.cs`：

```csharp
using System;

namespace Csg.Test
{
    /// <summary>
    /// SplitMix64 确定性伪随机数发生器（零依赖、跨运行时逐位可复现）。
    /// 刻意不引入 System.Random：它的算法在不同 .NET 版本间不保证一致，
    /// 而语料的可复现性依赖「同一种子在同一实现下永远产出同一结果」。
    /// </summary>
    internal sealed class SplitMix64
    {
        ulong _state;

        public SplitMix64(ulong seed) => _state = seed;

        public ulong NextUInt64()
        {
            _state += 0x9E3779B97F4A7C15UL;
            var z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>[0,1) 均匀。</summary>
        public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

        /// <summary>[0, exclusiveMax) 整数。</summary>
        public int NextInt(int exclusiveMax) => (int)(NextUInt64() % (ulong)exclusiveMax);

        /// <summary>[min,max] 对数均匀（覆盖跨数量级的尺寸）。</summary>
        public double NextLogUniform(double min, double max)
            => Math.Exp(Math.Log(min) + NextDouble() * (Math.Log(max) - Math.Log(min)));

        /// <summary>[-1,1]。</summary>
        public double NextSigned() => NextDouble() * 2.0 - 1.0;
    }
}
```

- [ ] **Step 4: 实现 SolidSpec**

`tests/OpenCSG.NET.TestKit/Generation/SolidSpec.cs`：

```csharp
namespace Csg.Test
{
    /// <summary>基本体种类。与生产 API 的对应关系见 <see cref="SolidGen.Materialize"/>。</summary>
    public enum PrimitiveKind
    {
        Cube = 0,
        Sphere = 1,
        Cylinder = 2,
        Cone = 3,
        Extrude = 4,
        Wedge = 5,
    }

    /// <summary>拉伸截面种类（对应 Profiles 工厂的 7 个形状）。</summary>
    public enum ProfileKind
    {
        Rectangle = 0,
        HBeam = 1,
        Channel = 2,
        SquareTube = 3,
        Trapezoid = 4,
        Capsule = 5,
        LShape = 6,
    }

    /// <summary>
    /// 坐标档位。Survey 档模拟测绘坐标（±1e6）—— 内核布尔合并是**绝对** 1e-5 容差，
    /// 大坐标是历史缺陷高发区（见 Solid.cs 的 int→long 模糊键修复）。
    /// </summary>
    public enum CoordTier
    {
        /// <summary>常规档：偏移 0，尺寸 log-uniform [1e-2, 1e3]。</summary>
        Normal = 0,

        /// <summary>测绘档：偏移 ±1e6，尺寸 [1, 1e3]（亚毫米尺寸在此档不可分辨，故不采样）。</summary>
        Survey = 1,
    }

    /// <summary>
    /// 实体参数（spec-then-materialize 的「spec」）。
    /// 刻意只用基元类型：FsCheck 可直接生成与**收缩**它，收缩结果是「最小的参数反例」，
    /// 人能读懂、能一行复现 —— 而不是一个无法解释的随机网格。
    /// </summary>
    public sealed record SolidSpec
    {
        public PrimitiveKind Kind { get; init; }

        // 基本尺寸（按 Kind 取用）
        public double SizeX { get; init; }
        public double SizeY { get; init; }
        public double SizeZ { get; init; }
        public double Radius { get; init; }
        public double RadiusEnd { get; init; }
        public double Height { get; init; }

        // 曲面细分
        public int Resolution { get; init; }

        /// <summary>扇形角（度）。随机采样固定为 360；其余取值是 Plan C 承接的已知缺陷。</summary>
        public double SectorAngle { get; init; }

        // 放置
        public double OffsetX { get; init; }
        public double OffsetY { get; init; }
        public double OffsetZ { get; init; }

        /// <summary>绕全局原点、X→Y→Z 顺序（度）。</summary>
        public double RotateX { get; init; }
        public double RotateY { get; init; }
        public double RotateZ { get; init; }

        /// <summary>均匀缩放因子（恒 &gt; 0）。</summary>
        public double Scale { get; init; }

        /// <summary>是否对 X 轴取负（负行列式镜像，触发绕序与法线翻转路径）。</summary>
        public bool Mirror { get; init; }

        /// <summary>Extrude 专用截面。</summary>
        public ProfileKind Profile { get; init; }
    }
}
```

- [ ] **Step 5: 实现 SolidGen**

`tests/OpenCSG.NET.TestKit/Generation/SolidGen.cs`：

```csharp
using System;
using System.Collections.Generic;
using Csg;

namespace Csg.Test
{
    /// <summary>
    /// 实体生成器：种子 → <see cref="SolidSpec"/>（纯参数）→ <see cref="Solid"/>（物化）。
    /// 物化只用公开 API，不触碰内核内部状态。
    /// </summary>
    public static class SolidGen
    {
        static readonly PrimitiveKind[] AllKinds =
        {
            PrimitiveKind.Cube, PrimitiveKind.Sphere, PrimitiveKind.Cylinder,
            PrimitiveKind.Cone, PrimitiveKind.Extrude, PrimitiveKind.Wedge,
        };

        static readonly ProfileKind[] AllProfiles =
        {
            ProfileKind.Rectangle, ProfileKind.HBeam, ProfileKind.Channel,
            ProfileKind.SquareTube, ProfileKind.Trapezoid, ProfileKind.Capsule, ProfileKind.LShape,
        };

        static readonly int[] Resolutions = { 4, 8, 12, 24, 48 };

        /// <summary>由种子确定性生成一个 spec。</summary>
        public static SolidSpec Random(long seed, CoordTier tier)
        {
            var rng = new SplitMix64((ulong)seed);

            var (minSize, maxSize, maxOffset) = tier == CoordTier.Survey
                ? (1.0, 1e3, 1e6)
                : (1e-2, 1e3, 0.0);

            var x = rng.NextLogUniform(minSize, maxSize);
            var y = rng.NextLogUniform(minSize, maxSize);
            var z = rng.NextLogUniform(minSize, maxSize);
            var radius = rng.NextLogUniform(minSize, maxSize);
            var height = rng.NextLogUniform(minSize, maxSize);

            return new SolidSpec
            {
                Kind = AllKinds[rng.NextInt(AllKinds.Length)],
                SizeX = x,
                SizeY = y,
                SizeZ = z,
                Radius = radius,
                RadiusEnd = radius * (0.25 + 0.75 * rng.NextDouble()),
                Height = height,
                Resolution = Resolutions[rng.NextInt(Resolutions.Length)],
                // 随机采样固定 360：其余取值会静默产出空实体（Plan C 承接的已知缺陷），
                // 纳入随机采样只会制造噪音而不会增加信息。
                SectorAngle = 360.0,
                OffsetX = maxOffset == 0 ? 0 : rng.NextSigned() * maxOffset,
                OffsetY = maxOffset == 0 ? 0 : rng.NextSigned() * maxOffset,
                OffsetZ = maxOffset == 0 ? 0 : rng.NextSigned() * maxOffset,
                RotateX = rng.NextSigned() * 180.0,
                RotateY = rng.NextSigned() * 180.0,
                RotateZ = rng.NextSigned() * 180.0,
                Scale = 0.25 + 1.75 * rng.NextDouble(),
                // 约 1/8 的样本走镜像路径
                Mirror = rng.NextInt(8) == 0,
                Profile = AllProfiles[rng.NextInt(AllProfiles.Length)],
            };
        }

        /// <summary>
        /// 生成一对 spec，B 的偏移以 A 为基准加一个小扰动 —— 这样既会产生相交对
        /// （布尔恒等式有信息量），也会产生不相交对（覆盖 MayOverlap 快速路径）。
        /// </summary>
        public static (SolidSpec A, SolidSpec B) RandomPair(long seed, CoordTier tier)
        {
            var a = Random(seed, tier);
            var rng = new SplitMix64((ulong)seed ^ 0xA5A5A5A5A5A5A5A5UL);

            var scale = Math.Max(Math.Max(a.SizeX, Math.Max(a.SizeY, a.SizeZ)),
                                 Math.Max(a.Radius, a.Height));
            var reach = rng.NextInt(4) == 0
                ? 4.0 * scale          // 1/4 概率明显分离
                : 1.2 * scale;         // 其余概率可能相交

            var b = Random(seed * 31 + 7, tier) with
            {
                OffsetX = a.OffsetX + rng.NextSigned() * reach,
                OffsetY = a.OffsetY + rng.NextSigned() * reach,
                OffsetZ = a.OffsetZ + rng.NextSigned() * reach,
            };
            return (a, b);
        }

        /// <summary>把 spec 物化成实体（确定性、无随机）。</summary>
        public static Solid Materialize(SolidSpec spec)
        {
            if (spec.Scale <= 0)
                throw new ArgumentOutOfRangeException(nameof(spec), spec.Scale, "Scale 必须为正");

            var body = BuildPrimitive(spec);

            if (spec.RotateX != 0) body = body.RotateX(spec.RotateX);
            if (spec.RotateY != 0) body = body.RotateY(spec.RotateY);
            if (spec.RotateZ != 0) body = body.RotateZ(spec.RotateZ);

            if (spec.Scale != 1.0) body = body.Scale(spec.Scale);
            // 负行列式镜像：库内 Transform 会翻顶点绕序并翻法线，镜像后仍朝外
            if (spec.Mirror) body = body.Scale(-1, 1, 1);

            if (spec.OffsetX != 0 || spec.OffsetY != 0 || spec.OffsetZ != 0)
                body = body.Translate(spec.OffsetX, spec.OffsetY, spec.OffsetZ);

            return body;
        }

        static Solid BuildPrimitive(SolidSpec spec)
        {
            switch (spec.Kind)
            {
                case PrimitiveKind.Cube:
                    return Solids.Cube(new Vector3D(spec.SizeX, spec.SizeY, spec.SizeZ), center: true);

                case PrimitiveKind.Sphere:
                    return Solids.Sphere(new SphereOptions
                    {
                        Center = new Vector3D(0, 0, 0),
                        Radius = spec.Radius,
                        Resolution = spec.Resolution,
                    });

                case PrimitiveKind.Cylinder:
                    // 轴向沿 ±Y，Center 为轴线中点
                    return Solids.Cylinder(new CylinderOptions
                    {
                        Start = new Vector3D(0, -spec.Height / 2, 0),
                        End = new Vector3D(0, spec.Height / 2, 0),
                        RadiusStart = spec.Radius,
                        RadiusEnd = spec.Radius,
                        SectorAngle = spec.SectorAngle,
                        Resolution = spec.Resolution,
                    });

                case PrimitiveKind.Cone:
                    return CsgEvaluator.Evaluate(CsgNodes.Cone(
                        new Vector3D(0, 0, 0), spec.RadiusEnd, spec.Radius, spec.Height));

                case PrimitiveKind.Extrude:
                    return CsgEvaluator.Evaluate(CsgNodes.Extrude(
                        BuildProfile(spec.Profile, spec), spec.Height));

                case PrimitiveKind.Wedge:
                    return CsgEvaluator.Evaluate(CsgNodes.Wedge(
                        new Vector3D(0, 0, 0), spec.SizeX, spec.SizeY, spec.SizeZ));

                default:
                    throw new ArgumentOutOfRangeException(nameof(spec), spec.Kind, "未知基本体种类");
            }
        }

        static Profile2D BuildProfile(ProfileKind kind, SolidSpec spec)
        {
            switch (kind)
            {
                case ProfileKind.Rectangle: return Profiles.Rectangle(spec.SizeX, spec.SizeY);
                case ProfileKind.HBeam: return Profiles.HBeam(spec.SizeY, spec.SizeX,
                    Math.Max(spec.SizeX / 10, 1e-3), Math.Max(spec.SizeY / 10, 1e-3));
                case ProfileKind.Channel: return Profiles.Channel(spec.SizeY, spec.SizeX,
                    Math.Max(spec.SizeX / 10, 1e-3), Math.Max(spec.SizeY / 10, 1e-3));
                case ProfileKind.SquareTube: return Profiles.SquareTube(spec.SizeX, spec.SizeY,
                    Math.Max(Math.Min(spec.SizeX, spec.SizeY) / 10, 1e-3));
                case ProfileKind.Trapezoid: return Profiles.Trapezoid(spec.SizeX / 2, spec.SizeX, spec.SizeY);
                case ProfileKind.Capsule: return Profiles.Capsule(spec.SizeX, Math.Max(spec.SizeY / 2, 1e-3));
                case ProfileKind.LShape: return Profiles.LShape(spec.SizeY, spec.SizeX,
                    Math.Max(Math.Min(spec.SizeX, spec.SizeY) / 4, 1e-3));
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知截面种类");
            }
        }
    }
}
```

- [ ] **Step 6: 跑测试确认通过**

```bash
dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release --filter "FullyQualifiedName~SolidGenTest" 2>&1 | tail -25
```

预期：8 个用例全过。

**如果 `Materialize_EveryPrimitiveKind_IsANonEmptyOrientedSolid` 报某一种基本体体积为负或为空**：那是**真实缺陷**（该基本体的绕序或生成有误），不是测试写错。**不要改期望值**。按 Global Constraints 登记 `DEFECT-002`（或下一个空闲编号），给该断言加 `[Explicit]`，并在报告里记下。同类失败照此处理。

**如果 `RandomPair_OffsetsAreCorrelated...` 断言相交对为 0**：说明扰动不够，调 `reach` 的倍数使其确有相交对（这是测试夹具参数问题，可以调）；但若调完仍不相交，报告出来。

- [ ] **Step 7: 跑全套件确认无回归**

```bash
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release 2>&1 | tail -3
dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release 2>&1 | tail -3
```

预期：Tests 仍是 126 通过 / 0 失败 / 1 跳过；SystemTests 9 通过（1 骨架 + 8 新增）。

- [ ] **Step 8: 提交（已授权）**

```bash
git add tests/OpenCSG.NET.TestKit/Generation tests/OpenCSG.NET.SystemTests tests/DEFECTS.md
git commit -m "test: SolidSpec/SolidGen 生成器（种子可复现、档位采样、已知缺陷输入排除）"
```

---

### Task 3B: 修复 SignedVolume 的大坐标数值不稳定（新增任务）

**为什么新增**：Task 3 实测发现 `GeometryAssert.SignedVolume` 用**全局原点**做散度定理参考点，坐标 ~1e6 时每项量级 ~1e18 而真实体积只有 ~1e1 ⇒ 双精度（约 15.9 位有效数字，1e18 处 ULP≈128）下体积被抵消误差吞掉。证据：`Solids.Sphere(r=1, res=48)` 平移 1e6 后读到 **780.4**（真值 4.159）；平移到 (1e6, −2e6, 3e6) 读到 **−6089.98**（负体积）；3072 个 Survey 探针里 12 个读到 ≤0，改用**相对包围盒中心**重算后 0/3072。这是**oracle 缺陷，不是库缺陷**（同一批实体相对自身包围盒中心都测得正值，说明绕序正确）。

**为什么必须现在修**：Tasks 6/7/8/10 都要在 Survey 档上断言体积（Task 6 的语料校验遍历 512 条含 Survey 的 spec 并断言 `SignedVolume > 0`）。不修会产生**假缺陷** —— 把 oracle 的抵消误差记成库的 bug。这是 plan defect，不是打磨。

**为什么可以改 `GeometryAssert`**：Plan A 冻结过它的断言逻辑，目的是保证那次搬迁是纯搬迁、既有 122 个用例语义不变。现在这是一次**有裁定、有理由、有验证计划**的定向修改，且 §13 本就写明 Plan C 可以扩展它 —— 只是提前到 B1 执行。改动只针对 `SignedVolume` 的积分参考点，不动任何阈值、不动断言强度。

- [ ] **Step 1: 写失败的测试**

`tests/OpenCSG.NET.Tests/Oracles/SignedVolumeInvarianceTest.cs`（新目录）：

```csharp
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// 体积 oracle 的数值口径。
    /// 散度定理用**全局原点**做参考点时，离原点很远的实体（测绘坐标 ±1e6）会出现灾难性抵消：
    /// 每项量级 ~1e18 而真实体积 ~1e1，双精度在 1e18 处的 ULP≈128，体积被舍入噪声吞掉。
    /// 改用**相对包围盒中心**的局部参考点后，对闭合曲面数值不变而条件数大幅改善。
    /// </summary>
    [TestFixture]
    public class SignedVolumeInvarianceTest
    {
        const double SurveyOffset = 1_000_000.0;

        [Test]
        public void SignedVolume_IsTranslationInvariant()
        {
            var sphere = Solids.Sphere(new SphereOptions
            {
                Center = new Vector3D(0, 0, 0), Radius = 1, Resolution = 48,
            });
            var moved = sphere.Translate(SurveyOffset, -2 * SurveyOffset, 3 * SurveyOffset);

            var atOrigin = GeometryAssert.SignedVolume(sphere);
            var displaced = GeometryAssert.SignedVolume(moved);

            Assert.That(atOrigin, Is.EqualTo(4.18879).Within(0.02), "原点处应接近球体积 4π/3");
            Assert.That(displaced, Is.EqualTo(atOrigin).Within(System.Math.Abs(atOrigin) * 1e-6),
                $"平移不改变体积：原点 {atOrigin:R}，平移后 {displaced:R}");
            Assert.That(displaced, Is.GreaterThan(0),
                "平移 1e6 后仍必须为正 —— 负值说明积分参考点仍在全局原点");
        }

        [Test]
        public void SignedVolume_SurveyTierPrimitives_AreAllPositive()
        {
            for (var i = 0; i < 32; i++)
            {
                var spec = SolidGen.Random(i * 977 + 13, CoordTier.Survey);
                var solid = SolidGen.Materialize(spec);

                Assert.That(GeometryAssert.SignedVolume(solid), Is.GreaterThan(0),
                    $"seed-index {i}: Survey 档实体的有向体积必须为正；spec={spec}");
            }
        }

        [Test]
        public void MeshSignedVolume_IsTranslationInvariant()
        {
            var sphere = Solids.Sphere(new SphereOptions
            {
                Center = new Vector3D(0, 0, 0), Radius = 1, Resolution = 48,
            });
            var moved = sphere.Translate(SurveyOffset, 0, 0);

            var atOrigin = MeshAssert.SignedVolume(CsgMesh.TrianglesOf(sphere));
            var displaced = MeshAssert.SignedVolume(CsgMesh.TrianglesOf(moved));

            Assert.That(displaced, Is.EqualTo(atOrigin).Within(System.Math.Abs(atOrigin) * 1e-6),
                $"网格侧平移不改变体积：{atOrigin:R} vs {displaced:R}");
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```bash
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release --filter "FullyQualifiedName~SignedVolumeInvarianceTest" 2>&1 | tail -20
```

预期：`SignedVolume_IsTranslationInvariant` 与 `MeshSignedVolume_IsTranslationInvariant` 失败，且失败信息里平移后的值约 780（不是 4.19）—— 这正是要修的症状。

- [ ] **Step 3: 实现平移不变的 `GeometryAssert.SignedVolume`**

`tests/OpenCSG.NET.TestKit/Oracles/GeometryAssert.cs` 里把 `SignedVolume` 替换为：

```csharp
        /// <summary>
        /// 散度定理净体积 V = Σ v0·(v1×v2)/6（多边形按首顶点扇形剖分）。
        /// **参考点取包围盒中心**，而不是全局原点：对闭合曲面该值与参考点无关，
        /// 但用原点做参考时，离原点很远的实体（测绘坐标 ±1e6）每项量级 ~1e18、
        /// 真实体积仅 ~1e1，双精度在 1e18 处的 ULP≈128 会直接把体积吞掉
        /// （实测：r=1 球平移 1e6 后读到 780.4，平移 (1e6,−2e6,3e6) 后读到 −6089.98）。
        /// </summary>
        public static double SignedVolume(Solid solid)
        {
            if (solid.Polygons.Count == 0)
                return 0.0;

            var (boundMin, boundMax) = Bounds(solid);
            var reference = (boundMin + boundMax) * 0.5;

            double volume = 0;
            foreach (var polygon in solid.Polygons)
            {
                var v = polygon.Vertices;
                if (v.Count < 3)
                    continue;
                var v0 = v[0].Pos - reference;
                for (int i = 1; i < v.Count - 1; i++)
                    volume += Dot(v0, Cross(v[i].Pos - reference, v[i + 1].Pos - reference));
            }
            return volume / 6.0;
        }
```

**只改参考点，不改公式结构、不改 `/6.0`、不新增阈值。** 其余方法一律不动。

- [ ] **Step 4: 实现平移不变的 `MeshAssert.SignedVolume`**

`tests/OpenCSG.NET.TestKit/Oracles/MeshAssert.cs` 里把 `SignedVolume` 替换为：

```csharp
        /// <summary>
        /// 散度定理有向体积（右手绕序 ⇒ 法线朝外 ⇒ 正值）。
        /// 参考点取包围盒中心 —— 理由与 <see cref="GeometryAssert.SignedVolume"/> 相同：
        /// 全局原点在测绘坐标下会因抵消误差失去全部精度。
        /// </summary>
        public static double SignedVolume(IReadOnlyList<MeshTriangle> triangles)
        {
            if (triangles.Count == 0)
                return 0.0;

            var (boundMin, boundMax) = Bounds(triangles);
            var rx = (boundMin.X + boundMax.X) * 0.5;
            var ry = (boundMin.Y + boundMax.Y) * 0.5;
            var rz = (boundMin.Z + boundMax.Z) * 0.5;

            double volume = 0;
            for (int i = 0; i < triangles.Count; i++)
            {
                var a = triangles[i].A;
                var b = triangles[i].B;
                var c = triangles[i].C;

                var ax = a.X - rx; var ay = a.Y - ry; var az = a.Z - rz;
                var bx = b.X - rx; var by = b.Y - ry; var bz = b.Z - rz;
                var cx = c.X - rx; var cy = c.Y - ry; var cz = c.Z - rz;

                volume += ax * (by * cz - bz * cy)
                        - ay * (bx * cz - bz * cx)
                        + az * (bx * cy - by * cx);
            }
            return volume / 6.0;
        }
```

- [ ] **Step 5: 跑新测试确认通过**

```bash
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release --filter "FullyQualifiedName~SignedVolumeInvarianceTest" 2>&1 | tail -12
```

预期：3 个用例全过。

- [ ] **Step 6: 重跑受影响的大坐标用例并如实报告**

`LargeCoordinateBooleanTest` 与 `LargeCoordinateUnionTest` 在 1e6 上用同一个 oracle 断言体积 —— 它们此前的「通过」可能是运气。现在 oracle 可信了，重跑：

```bash
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release --filter "FullyQualifiedName~LargeCoordinate" 2>&1 | tail -25
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release 2>&1 | tail -4
```

**两种结果，各自处置：**
- **全过** ⇒ README 那句「坐标到 1e6 量级仍正确」得到可信确认。把这一点写进报告（此前只是「在不可信的 oracle 下通过」）。
- **有失败** ⇒ 这是**真实发现**：库在部分大坐标情形下确实算错（而此前的通过是 oracle 误差掩盖的假绿）。**不要改断言、不要改容差、不要改期望值。** 登记 `DEFECT-nnn`（读 `tests/DEFECTS.md` 取下一个空闲编号），把该用例标 `[Explicit]`，并在报告里给出失败用例名 + 断言消息里的实测值。

- [ ] **Step 7: 记录结果**

在 `TEST-REPORT` 追加：

```markdown
### Plan B1 · Task 3B：体积 oracle 的数值口径修正

| 项 | 结果 |
| --- | --- |
| `GeometryAssert.SignedVolume` 参考点 | 全局原点 → 包围盒中心 |
| `MeshAssert.SignedVolume` 参考点 | 同上 |
| r=1 球平移 1e6 的修正前实测值 | <填实际：约 780.4> |
| 修正后同一位移的实测值 | <填实际：约 4.159> |
| `LargeCoordinateBooleanTest` / `LargeCoordinateUnionTest` 重跑结果 | <填实际：全过 / 失败用例名 + DEFECT 编号> |
| 既有大坐标用例的通过是否可信 | <填实际：此前是「在不可信 oracle 下通过」，现在是「可信确认」/ 或已登记缺陷> |
```

替换全部 `<填实际>` 后：

```bash
grep -n "填实际" docs/testing/TEST-REPORT-2026-10-13.md
```

预期：无输出。

- [ ] **Step 8: 提交（已授权）**

```bash
git add tests/OpenCSG.NET.TestKit/Oracles tests/OpenCSG.NET.Tests/Oracles tests/DEFECTS.md docs/testing
git commit -m "fix(test): 体积 oracle 改用包围盒中心参考点，修复测绘坐标下的抵消误差"
```

**完成定义（Task 3B）**：两个 oracle 都平移不变；3 个新用例通过；既有大坐标用例已重跑且结果已如实记录（全过 → 记录可信确认；失败 → 已登记缺陷）；`TEST-REPORT` 零占位符；`src/` 零改动。

---

### Task 4: NodeGen（种子 → CsgNode 树）

**Files:**
- Create: `tests/OpenCSG.NET.TestKit/Generation/NodeGen.cs`
- Test: `tests/OpenCSG.NET.SystemTests/Generation/NodeGenTest.cs`

**Interfaces:**
- Consumes: `SplitMix64`（Task 3）、`SolidSpec` 的枚举（Task 3）、`CsgSerialization`
- Produces: `public static class Csg.Test.NodeGen`：`Random(long seed) → CsgNode`、`MaxDepth` 常量

**为什么节点树不用 FsCheck**：树不是可收缩的连续参数空间；对树做 shrink 得到的「最小反例」是一棵无法解释的树。这里的复现凭据是**种子 + JSON**（`CsgSerialization.ToJson` 已经能把树打成自包含文本），所以节点树一律走语料驱动。

- [ ] **Step 1: 写失败的测试**

`tests/OpenCSG.NET.SystemTests/Generation/NodeGenTest.cs`：

```csharp
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    [TestFixture]
    public class NodeGenTest
    {
        [Test]
        public void Random_SameSeed_ProducesEquivalentTree()
        {
            var jsonA = CsgSerialization.ToJson(NodeGen.Random(4242), indented: false);
            var jsonB = CsgSerialization.ToJson(NodeGen.Random(4242), indented: false);

            Assert.That(jsonB, Is.EqualTo(jsonA), "同种子必须产出同一棵树（JSON 相同即结构相同）");
        }

        [Test]
        public void Random_TreesAreEvaluableAndOutwardFacing()
        {
            for (long seed = 0; seed < 24; seed++)
            {
                var tree = NodeGen.Random(seed);
                var solid = CsgEvaluator.Evaluate(tree);

                Assert.That(solid.Polygons.Count, Is.GreaterThan(0), $"seed={seed} 产出空实体");
                Assert.That(GeometryAssert.SignedVolume(solid), Is.GreaterThan(0),
                    $"seed={seed} 有向体积必须为正；树：{CsgSerialization.ToJson(tree, indented: false)}");
            }
        }

        [Test]
        public void Random_CoversAllNodeAndProfileTypes()
        {
            var nodeTypes = new System.Collections.Generic.HashSet<string>();
            var profileTypes = new System.Collections.Generic.HashSet<string>();

            for (long seed = 0; seed < 512; seed++)
            {
                var json = CsgSerialization.ToJson(NodeGen.Random(seed), indented: false);
                foreach (var candidate in new[]
                {
                    "Box", "Sphere", "Cylinder", "Cone", "Extrude", "Wedge",
                    "Union", "Subtract", "Intersect", "Transform",
                })
                    if (json.Contains($"\"$type\": \"{candidate}\"") || json.Contains($"\"$type\":\"{candidate}\""))
                        nodeTypes.Add(candidate);

                foreach (var candidate in new[]
                {
                    "Rectangle", "HBeam", "Channel", "SquareTube", "Trapezoid", "Capsule", "LShape",
                })
                    if (json.Contains(candidate))
                        profileTypes.Add(candidate);
            }

            Assert.That(nodeTypes.Count, Is.GreaterThanOrEqualTo(9),
                $"512 个种子应覆盖至少 9 种节点类型，实际：{string.Join(",", nodeTypes)}");
            Assert.That(profileTypes.Count, Is.GreaterThanOrEqualTo(5),
                $"应覆盖至少 5 种截面，实际：{string.Join(",", profileTypes)}");
        }

        [Test]
        public void Random_RespectsDepthLimit()
        {
            for (long seed = 0; seed < 64; seed++)
            {
                var tree = NodeGen.Random(seed, maxDepth: 3);
                Assert.That(Depth(tree), Is.LessThanOrEqualTo(3), $"seed={seed} 超过深度上限");
            }
        }

        static int Depth(CsgNode node)
        {
            switch (node)
            {
                case UnionNode u: return 1 + MaxDepth(u.Children);
                case SubtractNode s: return 1 + MaxDepth(s.Children);
                case IntersectNode i: return 1 + MaxDepth(i.Children);
                case TransformNode t: return 1 + Depth(t.Child);
                default: return 1;
            }
        }

        static int MaxDepth(System.Collections.Generic.IReadOnlyList<CsgNode> children)
        {
            var max = 0;
            foreach (var child in children)
                max = System.Math.Max(max, Depth(child));
            return max;
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```bash
dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release --filter "FullyQualifiedName~NodeGenTest" 2>&1 | tail -12
```

预期：编译失败 —— `CS0246: 未能找到类型或命名空间名"NodeGen"`。

- [ ] **Step 3: 实现 NodeGen**

`tests/OpenCSG.NET.TestKit/Generation/NodeGen.cs`：

```csharp
using System;
using System.Collections.Generic;
using Csg;

namespace Csg.Test
{
    /// <summary>
    /// CSG 节点树生成器：种子 → <see cref="CsgNode"/> 树。
    /// 复现凭据是「种子 + <see cref="CsgSerialization.ToJson(CsgNode, bool)"/> 的输出」，
    /// 因此不走 FsCheck 收缩（树不是可收缩的连续参数空间）。
    /// </summary>
    public static class NodeGen
    {
        /// <summary>默认最大嵌套深度。深度越大布尔树越复杂，也越容易触发退化情形。</summary>
        public const int DefaultMaxDepth = 4;

        public static CsgNode Random(long seed, int maxDepth = DefaultMaxDepth)
        {
            var rng = new SplitMix64((ulong)seed ^ 0x5DEECE66DUL);
            return Build(rng, maxDepth);
        }

        static CsgNode Build(SplitMix64 rng, int depth)
        {
            // 到深度上限后只产出叶子
            if (depth <= 1)
                return Primitive(rng);

            var roll = rng.NextInt(10);
            switch (roll)
            {
                case 0:
                case 1:
                case 2:
                    return Bool(rng, depth, (children) => CsgNodes.Union(children.ToArray()));
                case 3:
                case 4:
                    return Bool(rng, depth, (children) => CsgNodes.Subtract(children.ToArray()));
                case 5:
                    return Bool(rng, depth, (children) => CsgNodes.Intersect(children.ToArray()));
                case 6:
                case 7:
                    // 平移 + 绕全局原点按 X→Y→Z 旋转（度）
                    return CsgNodes.Transform(
                        new Vector3D(rng.NextSigned() * 10.0, rng.NextSigned() * 10.0, rng.NextSigned() * 10.0),
                        new Vector3D(rng.NextSigned() * 180.0, rng.NextSigned() * 180.0, rng.NextSigned() * 180.0),
                        Build(rng, depth - 1));
                default:
                    return Primitive(rng);
            }
        }

        /// <summary>布尔节点的子节点数固定为 2..4（求值器要求至少 1 个，1 个时退化为恒等）。</summary>
        static CsgNode Bool(SplitMix64 rng, int depth, Func<List<CsgNode>, CsgNode> make)
        {
            var count = 2 + rng.NextInt(3);
            var children = new List<CsgNode>(count);
            for (var i = 0; i < count; i++)
                children.Add(Build(rng, depth - 1));
            return make(children);
        }

        static CsgNode Primitive(SplitMix64 rng)
        {
            var extent = 1.0 + rng.NextDouble() * 10.0;
            var center = new Vector3D(
                rng.NextSigned() * extent, rng.NextSigned() * extent, rng.NextSigned() * extent);

            switch (rng.NextInt(6))
            {
                case 0:
                    return CsgNodes.Box(center, new Vector3D(
                        extent, 1.0 + rng.NextDouble() * 10.0, 1.0 + rng.NextDouble() * 10.0));

                case 1:
                    return CsgNodes.Sphere(center, 0.5 + rng.NextDouble() * 5.0);

                case 2:
                    return CsgNodes.Cylinder(center, 0.5 + rng.NextDouble() * 5.0, 1.0 + rng.NextDouble() * 10.0);

                case 3:
                    var bottom = 0.5 + rng.NextDouble() * 5.0;
                    return CsgNodes.Cone(center, bottom * (0.1 + 0.9 * rng.NextDouble()), bottom,
                        1.0 + rng.NextDouble() * 10.0);

                case 4:
                    return CsgNodes.Extrude(Profile(rng), 1.0 + rng.NextDouble() * 10.0);

                default:
                    return CsgNodes.Wedge(new Vector3D(
                        rng.NextSigned() * 5.0, rng.NextSigned() * 5.0, rng.NextSigned() * 5.0),
                        extent, 1.0 + rng.NextDouble() * 10.0, 1.0 + rng.NextDouble() * 10.0);
            }
        }

        static Profile2D Profile(SplitMix64 rng)
        {
            var w = 1.0 + rng.NextDouble() * 20.0;
            var h = 1.0 + rng.NextDouble() * 20.0;
            var thin = Math.Max(Math.Min(w, h) / 8.0, 0.05);

            switch (rng.NextInt(8))
            {
                case 0: return Profiles.Rectangle(w, h);
                case 1: return Profiles.HBeam(h, w, thin, thin);
                case 2: return Profiles.Channel(h, w, thin, thin);
                case 3: return Profiles.SquareTube(w, h, thin);
                case 4: return Profiles.Trapezoid(w / 2, w, h);
                case 5: return Profiles.Capsule(w, Math.Max(h / 2, 0.05));
                case 6: return Profiles.LShape(h, w, Math.Max(Math.Min(w, h) / 4, 0.05));
                default:
                    return new PolygonProfile(new[]
                    {
                        new Vector2D(0, 0),
                        new Vector2D(w, 0),
                        new Vector2D(w, h),
                        new Vector2D(w / 2, h * 1.5),
                        new Vector2D(0, h),
                    });
            }
        }
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

```bash
dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release --filter "FullyQualifiedName~NodeGenTest" 2>&1 | tail -25
```

预期：4 个用例全过。

**如果 `Random_TreesAreEvaluableAndOutwardFacing` 报某 seed 体积为负或抛异常**：那是**真实缺陷**（布尔操作或某节点类型的绕序问题），复现凭据就是断言消息里的 seed 与 JSON。**不要改断言**，登记 `DEFECT-nnn` 并给该用例加 `[Explicit]`。

- [ ] **Step 5: 提交（已授权）**

```bash
git add tests/OpenCSG.NET.TestKit/Generation/NodeGen.cs tests/OpenCSG.NET.SystemTests tests/DEFECTS.md
git commit -m "test: NodeGen 节点树生成器（10 种节点 × 8 种截面，种子可复现）"
```

---

### Task 5: ViewSegGen（种子 → 2D 线段集）

**Files:**
- Create: `tests/OpenCSG.NET.TestKit/Generation/ViewSegGen.cs`
- Test: `tests/OpenCSG.NET.SystemTests/Generation/ViewSegGenTest.cs`

**Interfaces:**
- Consumes: `SplitMix64`（Task 3）；`ViewSegment`/`ViewPoint2`/`ViewBounds2`（`Csg`）
- Produces: `public static class Csg.Test.ViewSegGen`：
  - `Random(long seed, int maxSegments = 40) → IReadOnlyList<ViewSegment>`
  - `ArcSample(ViewPoint2 center, double radius, double startDeg, double sweepDeg, int steps, bool visible) → IReadOnlyList<ViewSegment>`
  - `BoundsOf(IReadOnlyList<ViewSegment>) → ViewBounds2`

**为什么需要它**：Tier 3 要测 `ViewArcFitter.Fit` 与 `ViewSegmentCleanup.Clean` 的结构不变量，而它们的输入是**二维线段集**，不是网格。用网格投影产生输入会把这些函数的行为和 `ViewProjector` 耦合在一起，无法单独判定谁出错。

- [ ] **Step 1: 写失败的测试**

`tests/OpenCSG.NET.SystemTests/Generation/ViewSegGenTest.cs`：

```csharp
using System.Collections.Generic;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    [TestFixture]
    public class ViewSegGenTest
    {
        [Test]
        public void Random_SameSeed_ProducesIdenticalSegments()
        {
            var a = ViewSegGen.Random(1717);
            var b = ViewSegGen.Random(1717);

            Assert.That(b.Count, Is.EqualTo(a.Count));
            for (var i = 0; i < a.Count; i++)
            {
                Assert.That(b[i].Start.X, Is.EqualTo(a[i].Start.X));
                Assert.That(b[i].Start.Y, Is.EqualTo(a[i].Start.Y));
                Assert.That(b[i].End.X, Is.EqualTo(a[i].End.X));
                Assert.That(b[i].IsVisible, Is.EqualTo(a[i].IsVisible));
            }
        }

        [Test]
        public void Random_ProducesBothVisibilitiesAndNoDegenerateSegments()
        {
            var visible = 0;
            var hidden = 0;

            for (long seed = 0; seed < 32; seed++)
                foreach (var segment in ViewSegGen.Random(seed))
                {
                    if (segment.IsVisible) visible++; else hidden++;
                    Assert.That(segment.Length, Is.GreaterThan(1e-6), $"seed={seed} 产出了零长线段");
                }

            Assert.That(visible, Is.GreaterThan(0), "必须有用例覆盖可见层");
            Assert.That(hidden, Is.GreaterThan(0), "必须有用例覆盖隐藏层（否则跨可见性合并的缺陷测不出来）");
        }

        [Test]
        public void Random_RespectsSegmentBudget()
        {
            for (long seed = 0; seed < 64; seed++)
                Assert.That(ViewSegGen.Random(seed, maxSegments: 20).Count,
                    Is.LessThanOrEqualTo(20), $"seed={seed} 超出线段预算");
        }

        [Test]
        public void ArcSample_LiesOnTheCircle()
        {
            var center = new ViewPoint2(3, -4);
            const double radius = 7.5;

            var segments = ViewSegGen.ArcSample(center, radius, 0, 90, 8, visible: true);

            Assert.That(segments.Count, Is.EqualTo(8));
            foreach (var segment in segments)
            {
                Assert.That(DistanceToCircle(center, radius, segment.Start), Is.LessThan(1e-12));
                Assert.That(DistanceToCircle(center, radius, segment.End), Is.LessThan(1e-12));
                Assert.That(segment.IsVisible, Is.True);
            }
        }

        [Test]
        public void BoundsOf_CoversEveryEndpoint()
        {
            var segments = ViewSegGen.Random(31337);
            var bounds = ViewSegGen.BoundsOf(segments);

            foreach (var segment in segments)
            {
                Assert.That(segment.Start.X, Is.GreaterThanOrEqualTo(bounds.MinX - 1e-9));
                Assert.That(segment.Start.X, Is.LessThanOrEqualTo(bounds.MaxX + 1e-9));
                Assert.That(segment.End.Y, Is.GreaterThanOrEqualTo(bounds.MinY - 1e-9));
                Assert.That(segment.End.Y, Is.LessThanOrEqualTo(bounds.MaxY + 1e-9));
            }
        }

        static double DistanceToCircle(ViewPoint2 center, double radius, ViewPoint2 p)
        {
            var dx = p.X - center.X;
            var dy = p.Y - center.Y;
            return System.Math.Abs(System.Math.Sqrt(dx * dx + dy * dy) - radius);
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```bash
dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release --filter "FullyQualifiedName~ViewSegGenTest" 2>&1 | tail -12
```

预期：编译失败 —— `CS0246: 未能找到类型或命名空间名"ViewSegGen"`。

- [ ] **Step 3: 实现 ViewSegGen**

`tests/OpenCSG.NET.TestKit/Generation/ViewSegGen.cs`：

```csharp
using System;
using System.Collections.Generic;
using Csg;

namespace Csg.Test
{
    /// <summary>
    /// 2D 线段集生成器：为 <see cref="ViewArcFitter"/> 与 <see cref="ViewSegmentCleanup"/>
    /// 提供输入。刻意构造四类结构 —— 直链、圆采样折线、重叠共线簇、短残段 ——
    /// 因为这正是这两个函数的四个分支来源（成链 → 拟合；共线 → 合并；过短 → 丢弃）。
    /// </summary>
    public static class ViewSegGen
    {
        public static IReadOnlyList<ViewSegment> Random(long seed, int maxSegments = 40)
        {
            var rng = new SplitMix64((ulong)seed ^ 0xC0FFEEUL);
            var result = new List<ViewSegment>(maxSegments);

            while (result.Count < maxSegments)
            {
                var remaining = maxSegments - result.Count;
                var block = Math.Min(remaining, 1 + rng.NextInt(12));
                if (block <= 1)
                    break;

                var visible = rng.NextInt(3) != 0;   // 约 2/3 可见
                switch (rng.NextInt(4))
                {
                    case 0: AddChain(result, rng, block, visible); break;
                    case 1: AddArcSampled(result, rng, block, visible); break;
                    case 2: AddCollinearCluster(result, rng, block, visible); break;
                    default: AddShortStubs(result, rng, Math.Min(remaining, 3), visible); break;
                }
            }

            if (result.Count == 0)
                AddChain(result, rng, 2, visible: true);

            return result;
        }

        /// <summary>把一段圆按给定角度范围等分成 count 条首尾相接的弦。</summary>
        public static IReadOnlyList<ViewSegment> ArcSample(
            ViewPoint2 center, double radius, double startDeg, double sweepDeg, int steps, bool visible)
        {
            if (steps < 1) throw new ArgumentOutOfRangeException(nameof(steps), steps, "至少 1 段");

            var segments = new List<ViewSegment>(steps);
            var startRad = startDeg * Math.PI / 180.0;
            var sweepRad = sweepDeg * Math.PI / 180.0;

            for (var i = 0; i < steps; i++)
            {
                var a = startRad + sweepRad * i / steps;
                var b = startRad + sweepRad * (i + 1) / steps;
                segments.Add(new ViewSegment(
                    OnCircle(center, radius, a),
                    OnCircle(center, radius, b),
                    visible));
            }
            return segments;
        }

        public static ViewBounds2 BoundsOf(IReadOnlyList<ViewSegment> segments)
        {
            var points = new List<ViewPoint2>(segments.Count * 2);
            foreach (var segment in segments)
            {
                points.Add(segment.Start);
                points.Add(segment.End);
            }
            return ViewBounds2.Of(points);
        }

        static ViewPoint2 OnCircle(ViewPoint2 center, double radius, double angleRad)
            => new ViewPoint2(center.X + radius * Math.Cos(angleRad),
                              center.Y + radius * Math.Sin(angleRad));

        static void AddChain(List<ViewSegment> output, SplitMix64 rng, int count, bool visible)
        {
            var x = rng.NextSigned() * 100.0;
            var y = rng.NextSigned() * 100.0;
            var angle = rng.NextDouble() * 2 * Math.PI;

            for (var i = 0; i < count; i++)
            {
                var length = 0.5 + rng.NextDouble() * 20.0;
                var nextX = x + length * Math.Cos(angle);
                var nextY = y + length * Math.Sin(angle);
                output.Add(new ViewSegment(new ViewPoint2(x, y), new ViewPoint2(nextX, nextY), visible));
                x = nextX;
                y = nextY;
                // 缓弯，保证仍是一条可拟合的弧
                angle += rng.NextSigned() * 0.25;
            }
        }

        static void AddArcSampled(List<ViewSegment> output, SplitMix64 rng, int count, bool visible)
        {
            var radius = 2.0 + rng.NextDouble() * 50.0;
            var center = new ViewPoint2(rng.NextSigned() * 50.0, rng.NextSigned() * 50.0);
            var startDeg = rng.NextDouble() * 360.0;
            var sweepDeg = 20.0 + rng.NextDouble() * 340.0;

            output.AddRange(ArcSample(center, radius, startDeg, sweepDeg, count, visible));
        }

        /// <summary>同一视线上的重叠共线段 —— 专门打 <c>ViewSegmentCleanup</c> 的合并/塌缩分支。</summary>
        static void AddCollinearCluster(List<ViewSegment> output, SplitMix64 rng, int count, bool visible)
        {
            var x0 = rng.NextSigned() * 50.0;
            var y0 = rng.NextSigned() * 50.0;
            var divisor = 2 + rng.NextInt(3);
            var dx = (rng.NextSigned() * 20.0) / divisor;
            var dy = (rng.NextSigned() * 20.0) / divisor;

            for (var i = 0; i < count; i++)
            {
                // 每段起点落在前一段内部 ⇒ 必然重叠共线
                var offset = rng.NextDouble() * 0.8;
                var a = new ViewPoint2(x0 + dx * (i + offset), y0 + dy * (i + offset));
                var b = new ViewPoint2(x0 + dx * (i + offset + 1.5), y0 + dy * (i + offset + 1.5));
                output.Add(new ViewSegment(a, b, visible));
            }
        }

        /// <summary>远短于清理阈值的残段 —— 打「丢弃过短线段」分支。</summary>
        static void AddShortStubs(List<ViewSegment> output, SplitMix64 rng, int count, bool visible)
        {
            for (var i = 0; i < count; i++)
            {
                var x = rng.NextSigned() * 50.0;
                var y = rng.NextSigned() * 50.0;
                output.Add(new ViewSegment(
                    new ViewPoint2(x, y),
                    new ViewPoint2(x + 1e-5, y + 1e-5),
                    visible));
            }
        }
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

```bash
dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release --filter "FullyQualifiedName~ViewSegGenTest" 2>&1 | tail -20
```

预期：5 个用例全过。

- [ ] **Step 5: 提交（已授权）**

```bash
git add tests/OpenCSG.NET.TestKit/Generation/ViewSegGen.cs tests/OpenCSG.NET.SystemTests
git commit -m "test: ViewSegGen 线段集生成器（链/弧采样/共线簇/短残段）"
```

---

### Task 6: Corpus 三档 + corpus.json + DataGen

**Files:**
- Create: `tests/OpenCSG.NET.TestKit/Generation/Corpus.cs`
- Create: `tests/OpenCSG.NET.TestKit/Generation/corpus.json`（由 DataGen 生成后提交）
- Modify: `tests/OpenCSG.NET.TestKit/OpenCSG.NET.TestKit.csproj`（`corpus.json` 复制到输出目录）
- Create: `tools/OpenCSG.NET.DataGen/OpenCSG.NET.DataGen.csproj` + `Program.cs`
- Modify: `OpenCSG.NET.slnx`
- Test: `tests/OpenCSG.NET.SystemTests/Generation/CorpusTest.cs`

**Interfaces:**
- Consumes: `SolidGen`（Task 3）
- Produces:
  - `public static class Csg.Test.Corpus`：
    - `SmokeCaseCount` = 64、`CorpusCaseCount` = 512 常量
    - `SmokeSeeds() → IReadOnlyList<long>`（64 个固定种子）
    - `CorpusSpecs() → IReadOnlyList<SolidSpec>`（从 `corpus.json` 读）
    - `LongCaseCount() → int`（读环境变量 `OPENCSG_FUZZ_CASES`，缺省 5000）
    - `LongSeed(long index) → long`
  - `tools/OpenCSG.NET.DataGen` 控制台：`--write-corpus`（重建 `corpus.json`）、`--emit <dir>`（物化 STL + metrics.csv）

- [ ] **Step 1: 写失败的测试**

`tests/OpenCSG.NET.SystemTests/Generation/CorpusTest.cs`：

```csharp
using System.Collections.Generic;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    [TestFixture]
    public class CorpusTest
    {
        [Test]
        public void SmokeSeeds_AreStableAndUnique()
        {
            var seeds = Corpus.SmokeSeeds();

            Assert.That(seeds.Count, Is.EqualTo(Corpus.SmokeCaseCount));
            Assert.That(new HashSet<long>(seeds).Count, Is.EqualTo(seeds.Count), "种子必须互异");
            Assert.That(Corpus.SmokeSeeds(), Is.EqualTo(seeds), "两次调用必须一致");
        }

        [Test]
        public void CorpusSpecs_AreLoadedFromCommittedJson()
        {
            var specs = Corpus.CorpusSpecs();

            Assert.That(specs.Count, Is.EqualTo(Corpus.CorpusCaseCount),
                "corpus.json 必须已由 tools/OpenCSG.NET.DataGen --write-corpus 生成并提交");
        }

        [Test]
        public void CorpusSpecs_AreAllMaterializableAndOutwardFacing()
        {
            foreach (var spec in Corpus.CorpusSpecs())
            {
                var solid = SolidGen.Materialize(spec);

                Assert.That(solid.Polygons.Count, Is.GreaterThan(0), "语料不得含空实体");
                Assert.That(GeometryAssert.SignedVolume(solid), Is.GreaterThan(0),
                    "语料必须全部法线朝外");
            }
        }

        [Test]
        public void CorpusSpecs_CoverBothCoordTiersAndAllPrimitiveKinds()
        {
            var kinds = new HashSet<PrimitiveKind>();
            var tiers = new HashSet<CoordTier>();

            foreach (var spec in Corpus.CorpusSpecs())
            {
                kinds.Add(spec.Kind);
                tiers.Add(spec.OffsetX != 0 || spec.OffsetY != 0 || spec.OffsetZ != 0
                    ? CoordTier.Survey
                    : CoordTier.Normal);
            }

            Assert.That(kinds.Count, Is.EqualTo(6), "语料必须覆盖全部 6 种基本体");
            Assert.That(tiers.Count, Is.EqualTo(2), "语料必须同时覆盖常规档与测绘档");
        }

        [Test]
        public void LongCaseCount_DefaultsTo5000AndIsOverridable()
        {
            var previous = System.Environment.GetEnvironmentVariable("OPENCSG_FUZZ_CASES");
            try
            {
                System.Environment.SetEnvironmentVariable("OPENCSG_FUZZ_CASES", null);
                Assert.That(Corpus.LongCaseCount(), Is.EqualTo(5000));

                System.Environment.SetEnvironmentVariable("OPENCSG_FUZZ_CASES", "123");
                Assert.That(Corpus.LongCaseCount(), Is.EqualTo(123));

                Assert.That(Corpus.LongSeed(0), Is.Not.EqualTo(Corpus.LongSeed(1)));
            }
            finally
            {
                System.Environment.SetEnvironmentVariable("OPENCSG_FUZZ_CASES", previous);
            }
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```bash
dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release --filter "FullyQualifiedName~CorpusTest" 2>&1 | tail -12
```

预期：编译失败 —— `CS0246: 未能找到类型或命名空间名"Corpus"`。

- [ ] **Step 3: 实现 Corpus**

`tests/OpenCSG.NET.TestKit/Generation/Corpus.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Csg.Test
{
    /// <summary>
    /// 三档语料入口。
    /// Smoke（64 固定种子）每次运行都跑；Corpus（512 个已提交的 spec）供 CI / nightly；
    /// Long 由环境变量 <c>OPENCSG_FUZZ_CASES</c> 驱动（缺省 5000），只在 nightly 跑。
    /// <para>
    /// corpus.json 随 TestKit 复制到输出目录，用程序集自身位置定位 ——
    /// 刻意避开 SolidTest/GeometryAssert 系列里那种 4 层 GetDirectoryName 反推，
    /// 后者在自定义输出路径下会静默失效。
    /// </para>
    /// </summary>
    public static class Corpus
    {
        public const int SmokeCaseCount = 64;
        public const int CorpusCaseCount = 512;
        public const int DefaultLongCaseCount = 5000;

        const string EnvVarName = "OPENCSG_FUZZ_CASES";
        const string CorpusFileName = "corpus.json";

        static IReadOnlyList<long>? _smokeSeeds;
        static IReadOnlyList<SolidSpec>? _corpusSpecs;

        /// <summary>Smoke 档的 64 个固定种子（确定性、跨运行稳定）。</summary>
        public static IReadOnlyList<long> SmokeSeeds()
        {
            if (_smokeSeeds != null)
                return _smokeSeeds;

            var seeds = new long[SmokeCaseCount];
            for (var i = 0; i < SmokeCaseCount; i++)
            {
                // 常规档前 40 个、测绘档后 24 个 —— 两个档位都在 Smoke 里有代表
                var tier = i < 40 ? CoordTier.Normal : CoordTier.Survey;
                seeds[i] = DeterministicSeed(i, tier);
            }
            _smokeSeeds = seeds;
            return _smokeSeeds;
        }

        /// <summary>Corpus 档的 512 个 spec（从已提交的 corpus.json 读取）。</summary>
        public static IReadOnlyList<SolidSpec> CorpusSpecs()
        {
            if (_corpusSpecs != null)
                return _corpusSpecs;

            var path = Path.Combine(
                Path.GetDirectoryName(typeof(Corpus).Assembly.Location) ?? ".",
                CorpusFileName);

            if (!File.Exists(path))
                throw new FileNotFoundException(
                    $"找不到语料文件 {CorpusFileName}。运行 "
                    + $"'dotnet run --project tools/OpenCSG.NET.DataGen -- --write-corpus' 生成并提交它。",
                    path);

            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);

            var specs = new List<SolidSpec>(CorpusCaseCount);
            foreach (var element in document.RootElement.GetProperty("specs").EnumerateArray())
                specs.Add(ReadSpec(element));

            _corpusSpecs = specs;
            return _corpusSpecs;
        }

        /// <summary>Long 档的用例数（环境变量可覆盖）。</summary>
        public static int LongCaseCount()
        {
            var raw = Environment.GetEnvironmentVariable(EnvVarName);
            if (!string.IsNullOrEmpty(raw)
                && int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                                System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                && parsed > 0)
                return parsed;

            return DefaultLongCaseCount;
        }

        /// <summary>Long 档第 index 个种子（与 Smoke/Corpus 不相交）。</summary>
        public static long LongSeed(long index) => DeterministicSeed(index, CoordTier.Normal) + 0x1000000000L;

        /// <summary>
        /// Corpus 档第 index 条 spec 的生成种子。仅由 <c>tools/OpenCSG.NET.DataGen</c> 用于**重建**
        /// corpus.json；测试侧一律以提交的 JSON 为准（这样重建之外没人能悄悄改变语料）。
        /// </summary>
        public static long CorpusSeed(int index)
            => DeterministicSeed(index,
                   index < CorpusCaseCount * 5 / 8 ? CoordTier.Normal : CoordTier.Survey) + 0x2000000000L;

        /// <summary>第 index 个种子所对应的档位（Smoke 的种子与之保持一致）。</summary>
        public static CoordTier TierOf(int index) => index < 40 ? CoordTier.Normal : CoordTier.Survey;

        static long DeterministicSeed(long index, CoordTier tier)
        {
            // 固定常数混合，保证种子列表本身不随实现细节漂移
            var mixed = (ulong)index * 0x9E3779B97F4A7C15UL + (ulong)tier * 0xD1B54A32D192ED03UL;
            return (long)(mixed & 0x7FFFFFFFFFFFFFFFL);
        }

        internal static SolidSpec ReadSpec(JsonElement element) => new SolidSpec
        {
            Kind = (PrimitiveKind)element.GetProperty("kind").GetInt32(),
            SizeX = element.GetProperty("sizeX").GetDouble(),
            SizeY = element.GetProperty("sizeY").GetDouble(),
            SizeZ = element.GetProperty("sizeZ").GetDouble(),
            Radius = element.GetProperty("radius").GetDouble(),
            RadiusEnd = element.GetProperty("radiusEnd").GetDouble(),
            Height = element.GetProperty("height").GetDouble(),
            Resolution = element.GetProperty("resolution").GetInt32(),
            SectorAngle = element.GetProperty("sectorAngle").GetDouble(),
            OffsetX = element.GetProperty("offsetX").GetDouble(),
            OffsetY = element.GetProperty("offsetY").GetDouble(),
            OffsetZ = element.GetProperty("offsetZ").GetDouble(),
            RotateX = element.GetProperty("rotateX").GetDouble(),
            RotateY = element.GetProperty("rotateY").GetDouble(),
            RotateZ = element.GetProperty("rotateZ").GetDouble(),
            Scale = element.GetProperty("scale").GetDouble(),
            Mirror = element.GetProperty("mirror").GetBoolean(),
            Profile = (ProfileKind)element.GetProperty("profile").GetInt32(),
        };
    }
}
```

- [ ] **Step 4: 让 corpus.json 随输出复制**

在 `tests/OpenCSG.NET.TestKit/OpenCSG.NET.TestKit.csproj` 的 `</Project>` 之前插入：

```xml
  <ItemGroup>
    <None Include="Generation\corpus.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 5: 实现 DataGen**

`tools/OpenCSG.NET.DataGen/OpenCSG.NET.DataGen.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\OpenCSG.NET\OpenCSG.NET.csproj" />
    <ProjectReference Include="..\..\tests\OpenCSG.NET.TestKit\OpenCSG.NET.TestKit.csproj" />
  </ItemGroup>

</Project>
```

`tools/OpenCSG.NET.DataGen/Program.cs`：

```csharp
using System.Globalization;
using System.Text;
using Csg;
using Csg.Test;

// 语料生成/物化工具。
//   --write-corpus          重建 tests/OpenCSG.NET.TestKit/Generation/corpus.json（并就地提交）
//   --emit <dir>            把语料物化成 STL + metrics.csv，供人工检视
// 语料本身由确定性种子驱动，所以重建是幂等的。
var corpusPath = args.Length > 0 && args[0] == "--write-corpus"
    ? LocateCorpusPath()
    : null;

if (corpusPath != null)
{
    WriteCorpus(corpusPath);
    return 0;
}

var emitIndex = Array.IndexOf(args, "--emit");
if (emitIndex >= 0 && emitIndex + 1 < args.Length)
{
    Emit(args[emitIndex + 1]);
    return 0;
}

Console.Error.WriteLine("用法：--write-corpus | --emit <目录>");
return 1;

static string LocateCorpusPath()
{
    // 从可执行文件位置向上找 tests/OpenCSG.NET.TestKit 目录
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        var candidate = Path.Combine(dir.FullName, "tests", "OpenCSG.NET.TestKit", "Generation");
        if (Directory.Exists(candidate))
            return Path.Combine(candidate, "corpus.json");
        dir = dir.Parent;
    }
    throw new DirectoryNotFoundException("找不到 tests/OpenCSG.NET.TestKit/Generation —— 请在仓库内运行");
}

static void WriteCorpus(string path)
{
    var normal = Corpus.CorpusCaseCount * 5 / 8;
    var builder = new StringBuilder();
    builder.Append("{\n  \"schemaVersion\": 1,\n  \"masterSeed\": 20261013,\n  \"specs\": [\n");

    for (var i = 0; i < Corpus.CorpusCaseCount; i++)
    {
        var tier = i < normal ? CoordTier.Normal : CoordTier.Survey;
        var spec = SolidGen.Random(Corpus.CorpusSeed(i), tier);
        var solid = SolidGen.Materialize(spec);

        builder.Append("    {");
        builder.Append($"\"index\": {i}, ");
        builder.Append($"\"tier\": \"{tier}\", ");
        builder.Append($"\"kind\": {(int)spec.Kind}, ");
        builder.Append($"\"sizeX\": {spec.SizeX.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"sizeY\": {spec.SizeY.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"sizeZ\": {spec.SizeZ.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"radius\": {spec.Radius.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"radiusEnd\": {spec.RadiusEnd.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"height\": {spec.Height.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"resolution\": {spec.Resolution}, ");
        builder.Append($"\"sectorAngle\": {spec.SectorAngle.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"offsetX\": {spec.OffsetX.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"offsetY\": {spec.OffsetY.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"offsetZ\": {spec.OffsetZ.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"rotateX\": {spec.RotateX.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"rotateY\": {spec.RotateY.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"rotateZ\": {spec.RotateZ.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"scale\": {spec.Scale.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"mirror\": {(spec.Mirror ? "true" : "false")}, ");
        builder.Append($"\"profile\": {(int)spec.Profile}, ");

        // 观测指标：记录生成当时的结果，供人工检视与 Plan D 的 golden 层使用。
        // 属性测试**不**依赖这些数字（那会让 oracle 退化成自证）；
        // 它们的作用是让「实现漂移」可见。
        var volume = GeometryAssert.SignedVolume(solid);
        var (min, max) = GeometryAssert.Bounds(solid);
        var mesh = CsgMesh.TrianglesOf(solid);
        var edges = MeshAssert.Analyze(mesh);
        builder.Append($"\"polygons\": {solid.Polygons.Count}, ");
        builder.Append($"\"volume\": {volume.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"minX\": {min.X.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"maxX\": {max.X.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"meshTriangles\": {edges.TriangleCount}, ");
        builder.Append($"\"meshEuler\": {edges.EulerCharacteristic}");
        builder.Append(i + 1 == Corpus.CorpusCaseCount ? "}\n" : "},\n");
    }

    builder.Append("  ]\n}\n");
    File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    Console.WriteLine($"已写入 {Corpus.CorpusCaseCount} 条 spec 到 {path}");
}

static void Emit(string directory)
{
    Directory.CreateDirectory(directory);
    var csv = new StringBuilder("index,tier,kind,polygons,volume,meshTriangles,meshEuler,stl\n");

    foreach (var spec in Corpus.CorpusSpecs())
    {
        var solid = SolidGen.Materialize(spec);
        var index = Corpus.CorpusSpecs().IndexOf(spec);
        var name = $"{index:D4}-{spec.Kind}.stl";
        var path = Path.Combine(directory, name);

        StlWriter.Write(path, CsgMesh.TrianglesOf(solid));

        var edges = MeshAssert.Analyze(CsgMesh.TrianglesOf(solid));
        csv.Append(CultureInfo.InvariantCulture,
            $"{index},{spec.Kind},{solid.Polygons.Count},"
            + $"{GeometryAssert.SignedVolume(solid):R},{edges.TriangleCount},{edges.EulerCharacteristic},{name}\n");
    }

    File.WriteAllText(Path.Combine(directory, "metrics.csv"), csv.ToString(),
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    Console.WriteLine($"已物化 {Corpus.CorpusSpecs().Count} 个模型到 {directory}");
}
```

**注意 `--emit` 里的 `Corpus.CorpusSpecs().IndexOf(spec)` 是 O(n²) 且对 record 值相等敏感**：改成带索引的 `for` 循环。把该处改为：

```csharp
    // 表头 8 列 ⇒ 每行也必须 8 个字段。早期草稿表头写了 tier 而行里没输出它，
    // 结果任何按键读列的使用方都会错位（连「最小体积」这类自检都会读到 meshTriangles 那一列）。
    var csv = new StringBuilder("index,tier,kind,polygons,volume,meshTriangles,meshEuler,stl\n");
    var specs = Corpus.CorpusSpecs();
    var normalCount = Corpus.CorpusCaseCount * 5 / 8;

    for (var index = 0; index < specs.Count; index++)
    {
        var spec = specs[index];
        var solid = SolidGen.Materialize(spec);
        var name = $"{index:D4}-{spec.Kind}.stl";
        var path = Path.Combine(directory, name);

        var triangles = CsgMesh.TrianglesOf(solid);   // 只构造一次（早期草稿建了两次）
        StlWriter.Write(path, triangles);

        var edges = MeshAssert.Analyze(triangles);
        var tier = index < normalCount ? "Normal" : "Survey";
        csv.Append(CultureInfo.InvariantCulture,
            $"{index},{tier},{spec.Kind},{solid.Polygons.Count},"
            + $"{GeometryAssert.SignedVolume(solid):R},{edges.TriangleCount},{edges.EulerCharacteristic},{name}\n");
    }
```

- [ ] **Step 6: 生成并提交 corpus.json**

```bash
dotnet run --project tools/OpenCSG.NET.DataGen/OpenCSG.NET.DataGen.csproj -c Release -- --write-corpus
ls -la tests/OpenCSG.NET.TestKit/Generation/corpus.json
```

预期：输出 `已写入 512 条 spec 到 ...corpus.json`；文件大小在百 KB 量级。

**幂等性检查**：再跑一次并把结果与上一次比对，必须逐字节相同（语料由确定性种子驱动，重建必须幂等）。

```bash
cp tests/OpenCSG.NET.TestKit/Generation/corpus.json /tmp/corpus-run1.json
dotnet run --project tools/OpenCSG.NET.DataGen/OpenCSG.NET.DataGen.csproj -c Release -- --write-corpus
diff /tmp/corpus-run1.json tests/OpenCSG.NET.TestKit/Generation/corpus.json && echo "IDEMPOTENT"
```

预期：输出 `IDEMPOTENT`。**若不同，说明生成器引入了非确定性 —— 停下报告。**

- [ ] **Step 7: 跑测试确认通过**

```bash
dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release --filter "FullyQualifiedName~CorpusTest" 2>&1 | tail -20
```

预期：5 个用例全过（`CorpusSpecs_AreAllMaterializableAndOutwardFacing` 会当场遍历 512 个模型，是这一步真正的验证）。

**若它报某条 spec 体积为负 / 为空**：那是**真实缺陷**；断言消息里会带索引，用
`dotnet run --project tools/OpenCSG.NET.DataGen -- --emit artifacts/dataset` 物化后人工核对，然后登记 `DEFECT-nnn`（含索引与 spec 参数）。**不要把它从语料里删掉来「修好」测试。**

- [ ] **Step 8: 物化一次数据集验证工具链**

```bash
dotnet run --project tools/OpenCSG.NET.DataGen/OpenCSG.NET.DataGen.csproj -c Release -- --emit artifacts/dataset
ls artifacts/dataset | head -5
wc -l artifacts/dataset/metrics.csv
```

预期：目录里有 512 个 `.stl` 与 `metrics.csv`（513 行含表头）。`artifacts/` 已被 gitignore，不会污染仓库。

- [ ] **Step 9: 登记到解决方案**

`OpenCSG.NET.slnx` 在 `</Solution>` 前插入（新建一个 `tools` 文件夹节点，放在 `/tests/` 之后）：

```xml
  <Folder Name="/tools/">
    <Project Path="tools/OpenCSG.NET.DataGen/OpenCSG.NET.DataGen.csproj" />
  </Folder>
```

- [ ] **Step 10: 全套件 + 全解决方案验证**

```bash
dotnet build OpenCSG.NET.slnx -c Release 2>&1 | tail -6
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release 2>&1 | tail -3
dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release 2>&1 | tail -3
```

预期：构建 0 警告 0 错误；Tests 126 通过 / 0 失败 / 1 跳过；SystemTests 全绿。

- [ ] **Step 11: 提交（已授权）**

```bash
git add tests/OpenCSG.NET.TestKit/Generation tests/OpenCSG.NET.TestKit/OpenCSG.NET.TestKit.csproj tools/OpenCSG.NET.DataGen OpenCSG.NET.slnx tests/OpenCSG.NET.SystemTests tests/DEFECTS.md
git commit -m "test: 三档语料（64/512/环境变量驱动）+ corpus.json + DataGen 物化工具"
```

---

### Task 7: Tier 1 —— IdentityAssert + 布尔恒等式属性测试

**Files:**
- Create: `tests/OpenCSG.NET.TestKit/Oracles/IdentityAssert.cs`
- Create: `tests/OpenCSG.NET.SystemTests/Property/Tier1BooleanIdentityTest.cs`

**Interfaces:**
- Consumes: `GeometryAssert`、`SolidGen`、`Corpus`
- Produces: `public static class Csg.Test.IdentityAssert`：
  - `AssertInclusionExclusion(Solid a, Solid b, double tol, string? message = null)`
  - `AssertDifferenceIdentity(Solid a, Solid b, double tol, string? message = null)`
  - `AssertBooleanMonotonicity(Solid a, Solid b, double tol, string? message = null)`
  - `AssertUnchangedByBoolean(Solid a, Solid b, string? message = null)`

- [ ] **Step 1: 写失败的测试**

`tests/OpenCSG.NET.SystemTests/Property/Tier1BooleanIdentityTest.cs`：

```csharp
using System;
using System.Collections.Generic;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// Tier 1：精确代数恒等式。对任意随机实体对都必须成立，不需要任何 golden 数据。
    /// 这是整个属性测试体系里最强的一层 —— 一旦某条恒等式被破坏，
    /// 说明布尔内核真的算错了（体积缺失或重复计数）。
    /// </summary>
    [TestFixture]
    public class Tier1BooleanIdentityTest
    {
        const double Tol = 1e-6;

        static IEnumerable<TestCaseData> SmokeCases()
        {
            var seeds = Corpus.SmokeSeeds();
            for (var i = 0; i < seeds.Count; i++)
                yield return new TestCaseData(seeds[i], Corpus.TierOf(i)).SetName($"Smoke_{i:D2}_{Corpus.TierOf(i)}");
        }

        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        public void InclusionExclusion(long seed, CoordTier tier)
        {
            var (specA, specB) = SolidGen.RandomPair(seed, tier);
            var a = SolidGen.Materialize(specA);
            var b = SolidGen.Materialize(specB);

            IdentityAssert.AssertInclusionExclusion(a, b, Tol, Describe(seed, tier));
        }

        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        public void DifferenceIdentity(long seed, CoordTier tier)
        {
            var (specA, specB) = SolidGen.RandomPair(seed, tier);
            var a = SolidGen.Materialize(specA);
            var b = SolidGen.Materialize(specB);

            IdentityAssert.AssertDifferenceIdentity(a, b, Tol, Describe(seed, tier));
        }

        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        public void Monotonicity(long seed, CoordTier tier)
        {
            var (specA, specB) = SolidGen.RandomPair(seed, tier);
            var a = SolidGen.Materialize(specA);
            var b = SolidGen.Materialize(specB);

            IdentityAssert.AssertBooleanMonotonicity(a, b, Tol, Describe(seed, tier));
        }

        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        public void Commutativity(long seed, CoordTier tier)
        {
            var (specA, specB) = SolidGen.RandomPair(seed, tier);
            var a = SolidGen.Materialize(specA);
            var b = SolidGen.Materialize(specB);

            var unionAb = a.Union(b);
            var unionBa = b.Union(a);

            Assert.That(GeometryAssert.SignedVolume(unionBa),
                Is.EqualTo(GeometryAssert.SignedVolume(unionAb)).Within(Tol),
                $"{Describe(seed, tier)}: ∪ 不可交换");

            var intersectAb = a.Intersect(b);
            var intersectBa = b.Intersect(a);

            Assert.That(GeometryAssert.SignedVolume(intersectBa),
                Is.EqualTo(GeometryAssert.SignedVolume(intersectAb)).Within(Tol),
                $"{Describe(seed, tier)}: ∩ 不可交换");
        }

        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        public void BooleanDoesNotMutateOperands(long seed, CoordTier tier)
        {
            var (specA, specB) = SolidGen.RandomPair(seed, tier);
            var a = SolidGen.Materialize(specA);
            var b = SolidGen.Materialize(specB);

            IdentityAssert.AssertUnchangedByBoolean(a, b, Describe(seed, tier));
        }

        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        public void Idempotence_UnionAndIntersect(long seed, CoordTier tier)
        {
            var (specA, _) = SolidGen.RandomPair(seed, tier);
            var a = SolidGen.Materialize(specA);

            // 自反：A ∪ A = A、A ∩ A = A、A − A = ∅（不涉及空操作数）
            Assert.That(GeometryAssert.SignedVolume(a.Union(a)),
                Is.EqualTo(GeometryAssert.SignedVolume(a)).Within(Tol),
                $"{Describe(seed, tier)}: A ∪ A ≠ A");

            Assert.That(GeometryAssert.SignedVolume(a.Intersect(a)),
                Is.EqualTo(GeometryAssert.SignedVolume(a)).Within(Tol),
                $"{Describe(seed, tier)}: A ∩ A ≠ A");

            Assert.That(GeometryAssert.SignedVolume(a.Subtract(a)),
                Is.EqualTo(0.0).Within(Tol),
                $"{Describe(seed, tier)}: A − A ≠ ∅");
        }

        /// <summary>
        /// 空集是 ∪ 的单位元、∩ 的零元。
        /// **与上面的自反性质分开成独立用例**：空操作数会命中 DEFECT-002（`empty.Subtract(X)` 返回翻转绕序的 X；
        /// `empty.Intersect(X)` 与 `X.Intersect(empty)` 都返回 X —— 见 `tests/DEFECTS.md`），
        /// 那组断言当前必须标 `[Explicit]`。若混在一个方法里，整条自反性质会被一起排除，
        /// 那会让真正能跑的断言失去保护。
        /// </summary>
        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        [Explicit("DEFECT-002: 空操作数布尔运算语义错误（empty.Subtract/Intersect 返回错误结果）")]
        public void IdentityElement_WithEmptyOperand(long seed, CoordTier tier)
        {
            var (specA, specB) = SolidGen.RandomPair(seed, tier);
            var a = SolidGen.Materialize(specA);
            var b = SolidGen.Materialize(specB);
            var empty = new Solid();

            Assert.That(GeometryAssert.SignedVolume(a.Union(empty)),
                Is.EqualTo(GeometryAssert.SignedVolume(a)).Within(Tol),
                $"{Describe(seed, tier)}: A ∪ ∅ ≠ A");
            Assert.That(GeometryAssert.SignedVolume(a.Subtract(empty)),
                Is.EqualTo(GeometryAssert.SignedVolume(a)).Within(Tol),
                $"{Describe(seed, tier)}: A − ∅ ≠ A");
            Assert.That(GeometryAssert.SignedVolume(a.Intersect(empty)),
                Is.EqualTo(0.0).Within(Tol),
                $"{Describe(seed, tier)}: A ∩ ∅ ≠ ∅");
            Assert.That(GeometryAssert.SignedVolume(b.Intersect(empty)),
                Is.EqualTo(0.0).Within(Tol),
                $"{Describe(seed, tier)}: B ∩ ∅ ≠ ∅");
        }

        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        public void ResultsAreOrientedConsistently(long seed, CoordTier tier)
        {
            var (specA, specB) = SolidGen.RandomPair(seed, tier);
            var a = SolidGen.Materialize(specA);
            var b = SolidGen.Materialize(specB);

            // 球面参与布尔会产生 T 顶点接缝（库内无 T 顶点清理），
            // 所以用允许接缝的 AssertOrientationConsistent，而不是严格封闭断言。
            GeometryAssert.AssertOrientationConsistent(a.Union(b), $"{Describe(seed, tier)} A∪B");
            GeometryAssert.AssertOrientationConsistent(a.Subtract(b), $"{Describe(seed, tier)} A−B");
            GeometryAssert.AssertOrientationConsistent(a.Intersect(b), $"{Describe(seed, tier)} A∩B");
        }

        static string Describe(long seed, CoordTier tier) => $"seed={seed} tier={tier}";
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```bash
dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release --filter "FullyQualifiedName~Tier1BooleanIdentityTest" 2>&1 | tail -12
```

预期：编译失败 —— `CS0246: 未能找到类型或命名空间名"IdentityAssert"`。

- [ ] **Step 3: 实现 IdentityAssert**

`tests/OpenCSG.NET.TestKit/Oracles/IdentityAssert.cs`：

```csharp
using System.Collections.Generic;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// Tier 1：精确代数恒等式。这些等式对**任意**输入成立，因此不需要 golden 数据，
    /// 也不受「金标容差是否够松」困扰 —— 它们是布尔内核正确性的硬约束。
    /// <para>
    /// 相对容差按已知体积量级缩放：Survey 档坐标达 1e6，绝对容差在那里没有意义。
    /// </para>
    /// </summary>
    public static class IdentityAssert
    {
        /// <summary>体积相对容差基准。</summary>
        public const double RelativeTolerance = 1e-9;

        /// <summary>A ∪ B = A + B − A ∩ B。</summary>
        public static void AssertInclusionExclusion(Solid a, Solid b, double tolerance, string? message = null)
        {
            var volumeA = GeometryAssert.SignedVolume(a);
            var volumeB = GeometryAssert.SignedVolume(b);
            var union = GeometryAssert.SignedVolume(a.Union(b));
            var intersection = GeometryAssert.SignedVolume(a.Intersect(b));

            var expected = volumeA + volumeB - intersection;
            var scale = Scale(volumeA, volumeB, union);
            var slack = System.Math.Max(tolerance, RelativeTolerance * scale);

            Assert.That(union, Is.EqualTo(expected).Within(slack),
                $"{Prefix(message)} 包含排斥不成立：V(A∪B)={union:R} 应为 V(A)+V(B)−V(A∩B)={expected:R}"
                + $"（V(A)={volumeA:R} V(B)={volumeB:R} V(A∩B)={intersection:R}）");
        }

        /// <summary>A − B = A − A ∩ B。</summary>
        public static void AssertDifferenceIdentity(Solid a, Solid b, double tolerance, string? message = null)
        {
            var volumeA = GeometryAssert.SignedVolume(a);
            var difference = GeometryAssert.SignedVolume(a.Subtract(b));
            var intersection = GeometryAssert.SignedVolume(a.Intersect(b));

            var expected = volumeA - intersection;
            var scale = Scale(volumeA, difference);
            var slack = System.Math.Max(tolerance, RelativeTolerance * scale);

            Assert.That(difference, Is.EqualTo(expected).Within(slack),
                $"{Prefix(message)} 差集恒等式不成立：V(A−B)={difference:R} 应为 V(A)−V(A∩B)={expected:R}");
        }

        /// <summary>V(A∪B) ≥ max(V(A),V(B))、V(A∩B) ≤ min(V(A),V(B))，且并集/交集非负。</summary>
        public static void AssertBooleanMonotonicity(Solid a, Solid b, double tolerance, string? message = null)
        {
            var volumeA = GeometryAssert.SignedVolume(a);
            var volumeB = GeometryAssert.SignedVolume(b);
            var union = GeometryAssert.SignedVolume(a.Union(b));
            var intersection = GeometryAssert.SignedVolume(a.Intersect(b));
            var slack = System.Math.Max(tolerance, RelativeTolerance * Scale(volumeA, volumeB));

            Assert.That(volumeA, Is.GreaterThan(0),
                $"{Prefix(message)} 输入 A 体积必须为正（法线朝外），实际 {volumeA:R}");
            Assert.That(volumeB, Is.GreaterThan(0),
                $"{Prefix(message)} 输入 B 体积必须为正（法线朝外），实际 {volumeB:R}");
            Assert.That(union, Is.GreaterThanOrEqualTo(System.Math.Max(volumeA, volumeB) - slack),
                $"{Prefix(message)} V(A∪B)={union:R} 应 ≥ max(V(A),V(B))={System.Math.Max(volumeA, volumeB):R}");
            Assert.That(intersection, Is.LessThanOrEqualTo(System.Math.Min(volumeA, volumeB) + slack),
                $"{Prefix(message)} V(A∩B)={intersection:R} 应 ≤ min(V(A),V(B))={System.Math.Min(volumeA, volumeB):R}");
            Assert.That(intersection, Is.GreaterThanOrEqualTo(-slack),
                $"{Prefix(message)} V(A∩B)={intersection:R} 不应为负");
        }

        /// <summary>布尔运算不得修改任一操作数（含缓存字段与多边形列表）。</summary>
        public static void AssertUnchangedByBoolean(Solid a, Solid b, string? message = null)
        {
            var snapshotA = Snapshot(a);
            var snapshotB = Snapshot(b);

            a.Union(b);
            a.Subtract(b);
            a.Intersect(b);

            Assert.That(Snapshot(a), Is.EqualTo(snapshotA),
                $"{Prefix(message)} 布尔运算修改了操作数 A");
            Assert.That(Snapshot(b), Is.EqualTo(snapshotB),
                $"{Prefix(message)} 布尔运算修改了操作数 B");
        }

        /// <summary>深度快照：多边形数、每片顶点数、总面积与有向体积。</summary>
        public static (int Polygons, int Vertices, double Area, double Volume) Snapshot(Solid solid)
        {
            var vertices = 0;
            double area = 0;

            foreach (var polygon in solid.Polygons)
            {
                var v = polygon.Vertices;
                vertices += v.Count;
                if (v.Count < 3)
                    continue;

                var origin = v[0].Pos;
                for (var i = 1; i + 1 < v.Count; i++)
                {
                    var u = v[i].Pos - origin;
                    var w = v[i + 1].Pos - origin;
                    area += u.Cross(w).Length / 2.0;
                }
            }

            return (solid.Polygons.Count, vertices, area, GeometryAssert.SignedVolume(solid));
        }

        static double Scale(params double[] volumes)
        {
            var max = 0.0;
            foreach (var volume in volumes)
                max = System.Math.Max(max, System.Math.Abs(volume));
            return max <= 0 ? 1.0 : max;
        }

        static string Prefix(string? message) => message == null ? "" : message + ":";
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

```bash
dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release --filter "FullyQualifiedName~Tier1BooleanIdentityTest" 2>&1 | tail -30
```

预期：7 个测试方法 × 64 个种子 = **448 个用例**全过。

**这是本计划最可能发现真缺陷的一步。** 若某条恒等式失败：
- 断言消息里已含 seed、tier 与全部四个体积，**这就是最小复现凭据**。
- **不要**改容差、不要改期望、不要把该 seed 从语料里剔除。
- 登记 `DEFECT-nnn` 到 `tests/DEFECTS.md`（现象写清哪条恒等式、差值多少、seed 与 tier），把**该具体失败**的 `[TestCaseData]` 用 `[Explicit]` 排除（在 `SmokeCases()` 里对该 index `yield break` 并注释引用 DEFECT 编号 —— 这是唯一被允许的语料豁免方式，且必须写明理由）。
- 在报告里单独成段记录。

- [ ] **Step 5: 记录 Tier 1 结果**

在 `docs/testing/TEST-REPORT-2026-10-13.md` 追加：

```markdown
### Plan B1 · Task 7：Tier 1 布尔恒等式

| 恒等式 | 用例数 | 通过 | 失败 | 备注 |
| --- | --- | --- | --- | --- |
| 包含排斥 V(A∪B)=V(A)+V(B)−V(A∩B) | <填实际> | <填实际> | <填实际> | |
| 差集 V(A−B)=V(A)−V(A∩B) | <填实际> | <填实际> | <填实际> | |
| 单调性 | <填实际> | <填实际> | <填实际> | |
| 交换律 | <填实际> | <填实际> | <填实际> | |
| 不修改操作数 | <填实际> | <填实际> | <填实际> | |
| 自反/单位元/零元 | <填实际> | <填实际> | <填实际> | |
| 结果绕序一致 | <填实际> | <填实际> | <填实际> | 允许 T 顶点接缝 |

发现的缺陷：<填实际：无，或 DEFECT-nnn 列表>
```

替换完 `<填实际>` 后验证没有残留：

```bash
grep -n "填实际" docs/testing/TEST-REPORT-2026-10-13.md
```

预期：无输出。

- [ ] **Step 6: 提交（已授权）**

```bash
git add tests/OpenCSG.NET.TestKit/Oracles/IdentityAssert.cs tests/OpenCSG.NET.SystemTests tests/DEFECTS.md docs/testing
git commit -m "test: Tier 1 布尔恒等式属性测试（448 用例）"
```

---

### Task 8: Tier 1（续）—— 变换等价 / 包围盒 / 序列化往返 / 相等契约 / 平面分割

spec §6 的 Tier 1 清单里还有五族恒等式没有被 Task 7 覆盖：包围盒代数、变换等价、镜像对合、序列化往返、平面分割，以及 `Vector3D`/`Vector2D` 的相等契约。它们同样对任意输入成立，且各自钉住一条独立路径。

**Files:**
- Create: `tests/OpenCSG.NET.TestKit/Oracles/TransformAssert.cs`
- Create: `tests/OpenCSG.NET.SystemTests/Property/Tier1TransformIdentityTest.cs`

**Interfaces:**
- Consumes: `GeometryAssert`、`IdentityAssert.Snapshot`（Task 7）、`SolidGen`、`NodeGen`、`Corpus`、`CsgSerialization`
- Produces: `public static class Csg.Test.TransformAssert`：
  - `AssertBoundingBoxAlgebra(Solid a, Solid b, double tol, string? message = null)`
  - `AssertTransformEquivariance(Solid a, double tol, string? message = null)`
  - `AssertMirrorIsInvolution(Solid a, string? message = null)`
  - `AssertSameShape(Solid expected, Solid actual, double tol, string? message = null)`
  - `PolygonArea(Polygon polygon) → double`

- [ ] **Step 1: 写失败的测试**

`tests/OpenCSG.NET.SystemTests/Property/Tier1TransformIdentityTest.cs`：

```csharp
using System;
using System.Collections.Generic;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// Tier 1 续：变换等价、包围盒代数、镜像对合、序列化往返、平面分割、相等契约。
    /// </summary>
    [TestFixture]
    public class Tier1TransformIdentityTest
    {
        const double Tol = 1e-6;

        static IEnumerable<TestCaseData> SmokeCases()
        {
            var seeds = Corpus.SmokeSeeds();
            for (var i = 0; i < seeds.Count; i++)
                yield return new TestCaseData(seeds[i], Corpus.TierOf(i)).SetName($"Smoke_{i:D2}_{Corpus.TierOf(i)}");
        }

        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        public void BoundingBoxAlgebra(long seed, CoordTier tier)
        {
            var (specA, specB) = SolidGen.RandomPair(seed, tier);
            var a = SolidGen.Materialize(specA);
            var b = SolidGen.Materialize(specB);

            TransformAssert.AssertBoundingBoxAlgebra(a, b, Tol, $"seed={seed} tier={tier}");
        }

        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        public void TransformEquivariance(long seed, CoordTier tier)
        {
            var a = SolidGen.Materialize(SolidGen.Random(seed, tier));

            TransformAssert.AssertTransformEquivariance(a, Tol, $"seed={seed} tier={tier}");
        }

        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        public void MirrorIsInvolution(long seed, CoordTier tier)
        {
            var a = SolidGen.Materialize(SolidGen.Random(seed, tier));

            TransformAssert.AssertMirrorIsInvolution(a, $"seed={seed} tier={tier}");
        }

        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        public void SerializationRoundTrip_PreservesGeometry(long seed, CoordTier tier)
        {
            var tree = NodeGen.Random(seed);

            var direct = CsgEvaluator.Evaluate(tree);
            var roundTripped = CsgEvaluator.Evaluate(CsgSerialization.FromJson(CsgSerialization.ToJson(tree)));

            TransformAssert.AssertSameShape(direct, roundTripped, Tol,
                $"seed={seed} tier={tier} 树：{CsgSerialization.ToJson(tree, indented: false)}");
        }

        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        public void SerializationCompactAndIndented_Agree(long seed, CoordTier tier)
        {
            var tree = NodeGen.Random(seed);

            var indented = CsgSerialization.ToJson(tree, indented: true);
            var compact = CsgSerialization.ToJson(tree, indented: false);

            var fromIndented = CsgEvaluator.Evaluate(CsgSerialization.FromJson(indented));
            var fromCompact = CsgEvaluator.Evaluate(CsgSerialization.FromJson(compact));

            TransformAssert.AssertSameShape(fromIndented, fromCompact, Tol, $"seed={seed} tier={tier}");
        }

        [Test]
        public void PlaneSplitPolygon_PartitionAreasMatch()
        {
            // 一个跨越平面的四边形（对角两个顶点在平面两侧）
            var polygon = Square(new Vector3D(0, 0, 0), 4.0);

            foreach (var offset in new[] { -1.5, -0.5, 0.0, 0.5, 1.5 })
            {
                var plane = new Plane(new Vector3D(1, 0, 0), offset);   // x = offset
                polygon.Plane.SplitPolygon(polygon, out var result);

                var total = TransformAssert.PolygonArea(polygon);
                var front = result.Front == null ? 0.0 : TransformAssert.PolygonArea(result.Front);
                var back = result.Back == null ? 0.0 : TransformAssert.PolygonArea(result.Back);

                switch (result.Type)
                {
                    case 2: // 全在前侧
                        Assert.That(front, Is.EqualTo(total).Within(1e-9), $"offset={offset} Type=2");
                        Assert.That(result.Back, Is.Null, $"offset={offset} Type=2 不应有后侧片");
                        break;

                    case 3: // 全在后侧
                        Assert.That(back, Is.EqualTo(total).Within(1e-9), $"offset={offset} Type=3");
                        Assert.That(result.Front, Is.Null, $"offset={offset} Type=3 不应有前侧片");
                        break;

                    case 4: // 跨平面：面积必须守恒
                        Assert.That(front + back, Is.EqualTo(total).Within(1e-9),
                            $"offset={offset} 跨平面分割面积不守恒：front={front:R} back={back:R} total={total:R}");
                        break;

                    default:
                        Assert.Fail($"offset={offset} 意外 Type={result.Type}（四边形跨越 x=offset 平面，不应共面）");
                        break;
                }
            }
        }

        [Test]
        public void PlaneSplitPolygon_AllFiveTypeCodesAreReachable()
        {
            var polygon = Square(new Vector3D(0, 0, 0), 4.0);
            var seen = new HashSet<int>();

            // 0/1 共面：用多边形自身平面与其翻转平面
            polygon.Plane.SplitPolygon(polygon, out var coplanarFront);
            seen.Add(coplanarFront.Type);

            polygon.Plane.Flipped().SplitPolygon(polygon, out var coplanarBack);
            seen.Add(coplanarBack.Type);

            // 2/3 单侧
            new Plane(new Vector3D(1, 0, 0), 100.0).SplitPolygon(polygon, out var front);
            seen.Add(front.Type);
            new Plane(new Vector3D(1, 0, 0), -100.0).SplitPolygon(polygon, out var back);
            seen.Add(back.Type);

            // 4 跨越
            new Plane(new Vector3D(1, 0, 0), 0.0).SplitPolygon(polygon, out var spanning);
            seen.Add(spanning.Type);

            Assert.That(seen, Is.EquivalentTo(new[] { 0, 1, 2, 3, 4 }),
                $"5 个分割类型码应全部可达，实际只见到 {string.Join(",", seen)}");

            Assert.That(coplanarFront.Type, Is.EqualTo(0), "同向共面应为 0");
            Assert.That(coplanarBack.Type, Is.EqualTo(1), "反向共面应为 1");
            Assert.That(front.Type, Is.EqualTo(2), "全前应为 2");
            Assert.That(back.Type, Is.EqualTo(3), "全后应为 3");
            Assert.That(spanning.Type, Is.EqualTo(4), "跨越应为 4");
        }

        [Test]
        public void VectorEquality_AgreesWithHashCode()
        {
            var pairs = new (Vector3D A, Vector3D B)[]
            {
                (new Vector3D(0, 0, 0), new Vector3D(0, 0, 0)),
                (new Vector3D(1.5, -2.25, 3e-7), new Vector3D(1.5, -2.25, 3e-7)),
                (new Vector3D(-0.0, 0.0, 0.0), new Vector3D(0.0, -0.0, 0.0)),
                (new Vector3D(1e6, -2e6, 3e6), new Vector3D(1e6, -2e6, 3e6)),
            };

            foreach (var (a, b) in pairs)
            {
                Assert.That(a.Equals(b), Is.True, $"{a} 与 {b} 应相等");
                Assert.That(b.Equals(a), Is.True, "Equals 应对称");
                Assert.That(a.Equals(a), Is.True, "Equals 应自反");
                Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()),
                    $"相等对象的哈希必须相同：{a} vs {b}");
            }

            var v2pairs = new (Vector2D A, Vector2D B)[]
            {
                (new Vector2D(0, 0), new Vector2D(0, 0)),
                (new Vector2D(1.5, -2.25), new Vector2D(1.5, -2.25)),
            };

            foreach (var (a, b) in v2pairs)
            {
                Assert.That(a.Equals(b), Is.True, $"{a} 与 {b} 应相等");
                Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()),
                    $"相等对象的哈希必须相同：{a} vs {b}");
            }
        }

        [Test]
        public void Vector3D_RejectsNaN()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Vector3D(double.NaN, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Vector3D(0, double.NaN, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Vector3D(0, 0, double.NaN));
        }

        /// <summary>z = z0 平面上的正方形（原点在 (x0,y0,z0)），顶点逆时针。</summary>
        static Polygon Square(Vector3D origin, double side)
        {
            var tex = new Vector2D(0, 0);
            return new Polygon(
                new Vertex(origin, tex),
                new Vertex(new Vector3D(origin.X + side, origin.Y, origin.Z), tex),
                new Vertex(new Vector3D(origin.X + side, origin.Y + side, origin.Z), tex),
                new Vertex(new Vector3D(origin.X, origin.Y + side, origin.Z), tex));
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```bash
dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release --filter "FullyQualifiedName~Tier1TransformIdentityTest" 2>&1 | tail -12
```

预期：编译失败 —— `CS0246: 未能找到类型或命名空间名"TransformAssert"`。

- [ ] **Step 3: 实现 TransformAssert**

`tests/OpenCSG.NET.TestKit/Oracles/TransformAssert.cs`：

```csharp
using System;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// Tier 1 续：变换等价、包围盒代数、镜像对合、同形判定。
    /// </summary>
    public static class TransformAssert
    {
        /// <summary>bbox(A∪B) = bbox(A) ∪ bbox(B)；bbox(A∩B) ⊆ bbox(A) ∩ bbox(B)。</summary>
        public static void AssertBoundingBoxAlgebra(Solid a, Solid b, double tolerance, string? message = null)
        {
            var (minA, maxA) = GeometryAssert.Bounds(a);
            var (minB, maxB) = GeometryAssert.Bounds(b);

            if (a.Polygons.Count == 0 || b.Polygons.Count == 0)
                return;   // 空实体的包围盒是 (0,0,0)-(0,0,0)，代数不适用

            var (minU, maxU) = GeometryAssert.Bounds(a.Union(b));
            var (minI, maxI) = GeometryAssert.Bounds(a.Intersect(b));

            var slackX = tolerance * Math.Max(1.0, Math.Max(Math.Abs(minA.X), Math.Abs(maxB.X)));
            var slackY = tolerance * Math.Max(1.0, Math.Max(Math.Abs(minA.Y), Math.Abs(maxB.Y)));
            var slackZ = tolerance * Math.Max(1.0, Math.Max(Math.Abs(minA.Z), Math.Abs(maxB.Z)));

            Assert.That(minU.X, Is.EqualTo(Math.Min(minA.X, minB.X)).Within(slackX), $"{Prefix(message)} ∪.Min.X");
            Assert.That(minU.Y, Is.EqualTo(Math.Min(minA.Y, minB.Y)).Within(slackY), $"{Prefix(message)} ∪.Min.Y");
            Assert.That(minU.Z, Is.EqualTo(Math.Min(minA.Z, minB.Z)).Within(slackZ), $"{Prefix(message)} ∪.Min.Z");
            Assert.That(maxU.X, Is.EqualTo(Math.Max(maxA.X, maxB.X)).Within(slackX), $"{Prefix(message)} ∪.Max.X");
            Assert.That(maxU.Y, Is.EqualTo(Math.Max(maxA.Y, maxB.Y)).Within(slackY), $"{Prefix(message)} ∪.Max.Y");
            Assert.That(maxU.Z, Is.EqualTo(Math.Max(maxA.Z, maxB.Z)).Within(slackZ), $"{Prefix(message)} ∪.Max.Z");

            // 交集必然落在两个包围盒的交内；空交集（零多边形）跳过
            if (a.Intersect(b).Polygons.Count == 0)
                return;

            Assert.That(minI.X, Is.GreaterThanOrEqualTo(Math.Max(minA.X, minB.X) - slackX), $"{Prefix(message)} ∩.Min.X");
            Assert.That(minI.Y, Is.GreaterThanOrEqualTo(Math.Max(minA.Y, minB.Y) - slackY), $"{Prefix(message)} ∩.Min.Y");
            Assert.That(minI.Z, Is.GreaterThanOrEqualTo(Math.Max(minA.Z, minB.Z) - slackZ), $"{Prefix(message)} ∩.Min.Z");
            Assert.That(maxI.X, Is.LessThanOrEqualTo(Math.Min(maxA.X, maxB.X) + slackX), $"{Prefix(message)} ∩.Max.X");
            Assert.That(maxI.Y, Is.LessThanOrEqualTo(Math.Min(maxA.Y, maxB.Y) + slackY), $"{Prefix(message)} ∩.Max.Y");
            Assert.That(maxI.Z, Is.LessThanOrEqualTo(Math.Min(maxA.Z, maxB.Z) + slackZ), $"{Prefix(message)} ∩.Max.Z");
        }

        /// <summary>V(k·A) = k³V(A)；V(旋转 A) = V(A)；V(镜像 A) = V(A) 且仍朝外；V(平移 A) = V(A) 且 bbox 精确平移。</summary>
        public static void AssertTransformEquivariance(Solid a, double tolerance, string? message = null)
        {
            var prefix = Prefix(message);
            var volume = GeometryAssert.SignedVolume(a);
            var (min, max) = GeometryAssert.Bounds(a);
            var slack = Math.Max(tolerance, 1e-9 * Math.Max(1.0, volume));

            // 均匀缩放：体积按 k³ 缩放
            const double k = 2.5;
            var scaled = a.Scale(k);
            Assert.That(GeometryAssert.SignedVolume(scaled),
                Is.EqualTo(volume * k * k * k).Within(slack * k * k * k),
                $"{prefix} V(k·A) 应为 k³V(A)：实际 {GeometryAssert.SignedVolume(scaled):R}，期望 {volume * k * k * k:R}");

            // 旋转：体积不变，且仍朝外（正值）
            var rotated = a.RotateX(37.0).RotateY(-23.0).RotateZ(91.0);
            Assert.That(GeometryAssert.SignedVolume(rotated),
                Is.EqualTo(volume).Within(slack),
                $"{prefix} 旋转不改变体积：实际 {GeometryAssert.SignedVolume(rotated):R}，期望 {volume:R}");

            // 镜像：体积数值不变、符号仍为正
            var mirrored = a.Scale(-1, 1, 1);
            var mirroredVolume = GeometryAssert.SignedVolume(mirrored);
            Assert.That(mirroredVolume, Is.EqualTo(volume).Within(slack),
                $"{prefix} 镜像不改变体积数值：实际 {mirroredVolume:R}，期望 {volume:R}");
            Assert.That(mirroredVolume, Is.GreaterThan(0),
                $"{prefix} 镜像后必须仍朝外，实际体积 {mirroredVolume:R}（负值说明绕序未翻回）");

            // 平移：体积不变，bbox 精确平移
            const double tx = 12.5, ty = -7.25, tz = 3.125;
            var translated = a.Translate(tx, ty, tz);
            Assert.That(GeometryAssert.SignedVolume(translated),
                Is.EqualTo(volume).Within(slack),
                $"{prefix} 平移不改变体积");

            var (tmin, tmax) = GeometryAssert.Bounds(translated);
            var shiftSlack = tolerance * Math.Max(1.0, Math.Max(Math.Abs(min.X), Math.Abs(tx)));
            Assert.That(tmin.X, Is.EqualTo(min.X + tx).Within(shiftSlack), $"{prefix} 平移后 bbox.Min.X");
            Assert.That(tmax.Y, Is.EqualTo(max.Y + ty).Within(tolerance * Math.Max(1.0, Math.Abs(ty))), $"{prefix} 平移后 bbox.Max.Y");
            Assert.That(tmin.Z, Is.EqualTo(min.Z + tz).Within(tolerance * Math.Max(1.0, Math.Abs(tz))), $"{prefix} 平移后 bbox.Min.Z");
        }

        /// <summary>镜像两次回到原形（顶点、面积、体积、包围盒全部还原）。</summary>
        public static void AssertMirrorIsInvolution(Solid a, string? message = null)
        {
            var twice = a.Scale(-1, 1, 1).Scale(-1, 1, 1);

            AssertSameShape(a, twice, 0.0, Prefix(message) + " 镜像对合");
        }

        /// <summary>同形判定：多边形数、顶点数、总面积、有向体积、包围盒全部一致。</summary>
        public static void AssertSameShape(Solid expected, Solid actual, double tolerance, string? message = null)
        {
            var prefix = Prefix(message);
            var slack = Math.Max(tolerance, 0.0);

            var expectedSnapshot = IdentityAssert.Snapshot(expected);
            var actualSnapshot = IdentityAssert.Snapshot(actual);

            Assert.That(actualSnapshot.Polygons, Is.EqualTo(expectedSnapshot.Polygons),
                $"{prefix} 多边形数不同：{actualSnapshot.Polygons} vs {expectedSnapshot.Polygons}");
            Assert.That(actualSnapshot.Vertices, Is.EqualTo(expectedSnapshot.Vertices),
                $"{prefix} 顶点数不同：{actualSnapshot.Vertices} vs {expectedSnapshot.Vertices}");
            Assert.That(actualSnapshot.Area,
                Is.EqualTo(expectedSnapshot.Area).Within(Math.Max(slack, 1e-9 * Math.Max(1.0, expectedSnapshot.Area))),
                $"{prefix} 总面积不同");
            Assert.That(actualSnapshot.Volume,
                Is.EqualTo(expectedSnapshot.Volume).Within(Math.Max(slack, 1e-9 * Math.Max(1.0, Math.Abs(expectedSnapshot.Volume)))),
                $"{prefix} 有向体积不同");

            var (expMin, expMax) = GeometryAssert.Bounds(expected);
            var (actMin, actMax) = GeometryAssert.Bounds(actual);
            var boxSlack = Math.Max(slack, 1e-9 * Math.Max(1.0, Math.Max(Math.Abs(expMin.X), Math.Abs(expMax.X))));

            Assert.That(actMin.X, Is.EqualTo(expMin.X).Within(boxSlack), $"{prefix} bbox.Min.X");
            Assert.That(actMin.Y, Is.EqualTo(expMin.Y).Within(boxSlack), $"{prefix} bbox.Min.Y");
            Assert.That(actMin.Z, Is.EqualTo(expMin.Z).Within(boxSlack), $"{prefix} bbox.Min.Z");
            Assert.That(actMax.X, Is.EqualTo(expMax.X).Within(boxSlack), $"{prefix} bbox.Max.X");
            Assert.That(actMax.Y, Is.EqualTo(expMax.Y).Within(boxSlack), $"{prefix} bbox.Max.Y");
            Assert.That(actMax.Z, Is.EqualTo(expMax.Z).Within(boxSlack), $"{prefix} bbox.Max.Z");
        }

        /// <summary>多边形面积（按首顶点扇形剖分）。</summary>
        public static double PolygonArea(Polygon polygon)
        {
            var v = polygon.Vertices;
            if (v.Count < 3)
                return 0.0;

            var origin = v[0].Pos;
            var area = 0.0;
            for (var i = 1; i + 1 < v.Count; i++)
            {
                var u = v[i].Pos - origin;
                var w = v[i + 1].Pos - origin;
                area += u.Cross(w).Length / 2.0;
            }
            return area;
        }

        static string Prefix(string? message) => message == null ? "" : message + ":";
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

```bash
dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release --filter "FullyQualifiedName~Tier1TransformIdentityTest" 2>&1 | tail -30
```

预期：9 个测试方法全过（前 5 个各 64 个种子 = 320 用例，后 4 个各 1 用例）。

**失败时的判定要点：**
- **`TransformEquivariance` 的镜像分支失败（体积为负）** ⇒ 库的镜像绕序补偿有误。**但先排除一件事**：`Solid.Scale(-1,1,1)` 走的是 `Transform`，而 `Materialize` 里已经用过同样的镜像路径（Task 3 的 `Materialize_MirrorFlagsStillOutwardFacing` 已过）—— 如果那条过而这条不过，差异在于这里没有经过 `Materialize` 的轴序，说明问题与具体矩阵有关。登记缺陷时必须写清是哪一步。
- **`SerializationRoundTrip_PreservesGeometry` 失败** ⇒ `CsgSerialization` 的某个 `$type` 分支丢失或错读了几何。断言消息里带 JSON，那就是最小复现。`Profile2D` 现有 8 个分支（含 `Polygon`），`CsgNode` 现有 10 个 —— 用失败的 seed 定位到具体分支再登记。
- **`PlaneSplitPolygon_PartitionAreasMatch` 失败** ⇒ `Plane.SplitPolygon` 的面积不守恒，这是内核级严重问题（BSP 裁剪全靠它）。登记时给出 offset。
- **`VectorEquality_AgreesWithHashCode` 失败** ⇒ `Equals` 被重写而 `GetHashCode` 未同步。这是「相等但哈希不同」的真实契约违反。
- **`Vector3D_RejectsNaN` 失败** ⇒ 文档声称的 NaN 校验缺失。

均**不得**改期望值或去掉用例。

- [ ] **Step 5: 记录结果并加占位符守卫**

在 `TEST-REPORT` 追加 Tier 1（续）小节，列出六个 oracle 家族的用例数与通过数、以及发现的缺陷（如有）。然后：

```bash
grep -n "填实际" docs/testing/TEST-REPORT-2026-10-13.md
```

预期：无输出（Task 7 与 Task 8 的 `<填实际>` 都必须已替换）。

- [ ] **Step 6: 提交（已授权）**

```bash
git add tests/OpenCSG.NET.TestKit/Oracles/TransformAssert.cs tests/OpenCSG.NET.SystemTests tests/DEFECTS.md docs/testing
git commit -m "test: Tier 1 续——变换等价/包围盒/序列化往返/相等契约/平面分割属性测试"
```

---

### Task 9: Tier 2 —— ConvexHull + 解析解属性测试

**Files:**
- Create: `tests/OpenCSG.NET.TestKit/Oracles/ConvexHull.cs`
- Create: `tests/OpenCSG.NET.SystemTests/Property/Tier2AnalyticTest.cs`

**Interfaces:**
- Consumes: `ViewBridge`（Bridge/）、`MeshAssert`、`SolidGen`、`ViewSegGen`
- Produces: `public static class Csg.Test.ConvexHull`：
  - `MonotoneChain(IReadOnlyList<ViewPoint2> points) → IReadOnlyList<ViewPoint2>`
  - `Perimeter(IReadOnlyList<ViewPoint2> hull) → double`
  - `PerimeterOf(IReadOnlyList<ViewPoint2> points) → double`

- [ ] **Step 1: 写失败的测试**

`tests/OpenCSG.NET.SystemTests/Property/Tier2AnalyticTest.cs`：

```csharp
using System;
using System.Collections.Generic;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// Tier 2：与闭式答案比对。
    /// 最强的一条是「凸包周长守恒」—— 对凸实体，正交投影后可见线段总长
    /// 必须等于投影顶点的 2D 凸包周长。凸包由本工程独立实现（<see cref="ConvexHull"/>），
    /// 不调用被测代码，因此这是真正的外部 oracle。
    /// </summary>
    [TestFixture]
    public class Tier2AnalyticTest
    {
        const double Tol = 1e-6;

        [Test]
        public void ConvexHull_PerimeterOfUnitSquareIsFour()
        {
            var square = new[]
            {
                new ViewPoint2(0, 0), new ViewPoint2(1, 0),
                new ViewPoint2(1, 1), new ViewPoint2(0, 1),
                new ViewPoint2(0.5, 0.5),   // 内部点必须被排除
            };

            Assert.That(ConvexHull.PerimeterOf(square), Is.EqualTo(4.0).Within(1e-12));
        }

        [Test]
        public void ConvexHull_OfDiscreteCircleApproachesTwoPiR()
        {
            const double radius = 3.0;
            const int steps = 4096;

            var points = new List<ViewPoint2>(steps);
            for (var i = 0; i < steps; i++)
            {
                var angle = 2 * Math.PI * i / steps;
                points.Add(new ViewPoint2(radius * Math.Cos(angle), radius * Math.Sin(angle)));
            }

            var perimeter = ConvexHull.PerimeterOf(points);

            // 内接正多边形周长 = 2·n·R·sin(π/n) < 2πR，收敛阶 O(1/n²)
            var exact = 2 * Math.PI * radius;
            Assert.That(perimeter, Is.EqualTo(exact).Within(exact * 1e-6));
        }

        [Test]
        public void Cube_VisibleSegmentLengthEqualsConvexHullPerimeter()
        {
            var cube = Solids.Cube(10, center: true);
            var views = ViewBridge.ProjectViews(cube, ViewDirections.OrthographicThree);

            foreach (var view in views)
            {
                var projected = ProjectedVertices(cube, view.Direction);
                var expected = ConvexHull.PerimeterOf(projected);

                var actual = SumSegmentLength(view);

                Assert.That(actual, Is.EqualTo(expected).Within(expected * 1e-6),
                    $"{view.Direction.Name}: 可见线段总长 {actual:R} 应为投影凸包周长 {expected:R}");
            }
        }

        [Test]
        public void Box10x20x30_ThreeViewsHaveAnalyticExtents()
        {
            var box = Solids.Cube(new Vector3D(10, 20, 30), center: true);
            var views = ViewBridge.ProjectViews(box, ViewDirections.OrthographicThree);

            // 由 ViewProjectorContracts.cs 的取轴定义推导（Plan A 已两次独立验证）
            var expected = new (string Name, double Width, double Height)[]
            {
                ("主视", 30, 20), ("俯视", 30, 10), ("左视", 10, 20),
            };

            for (var i = 0; i < expected.Length; i++)
            {
                Assert.That(views[i].Direction.Name, Is.EqualTo(expected[i].Name));
                Assert.That(views[i].Bounds.Width, Is.EqualTo(expected[i].Width).Within(Tol),
                    $"{expected[i].Name} 宽");
                Assert.That(views[i].Bounds.Height, Is.EqualTo(expected[i].Height).Within(Tol),
                    $"{expected[i].Name} 高");
            }
        }

        [Test]
        public void Sphere_SilhouetteIsAClosedCircleOfRadiusR()
        {
            const double radius = 4.0;
            var sphere = Solids.Sphere(new SphereOptions
            {
                Center = new Vector3D(0, 0, 0), Radius = radius, Resolution = 48,
            });

            var views = ViewBridge.ProjectViews(sphere, ViewDirections.OrthographicThree);

            foreach (var view in views)
            {
                Assert.That(view.Arcs.Count, Is.GreaterThanOrEqualTo(1),
                    $"{view.Direction.Name}: 球面轮廓应被圆弧识别器还原为至少一条弧，实际 Arcs={view.Arcs.Count}");

                var best = view.Arcs[0];
                Assert.That(best.Radius, Is.EqualTo(radius).Within(radius * 0.02),
                    $"{view.Direction.Name}: 还原半径 {best.Radius:R} 应接近 {radius}");
                Assert.That(best.IsClosed, Is.True,
                    $"{view.Direction.Name}: 球面轮廓应被识别为整圆");
                Assert.That(best.SweepDegrees, Is.EqualTo(360.0).Within(1e-9));
            }
        }

        [Test]
        public void ArcSample_IsRecoveredAsAnArcWithKnownCenterAndRadius()
        {
            var center = new ViewPoint2(12.5, -7.25);
            const double radius = 9.0;
            const double startDeg = 20.0;
            const double sweepDeg = 110.0;

            var segments = ViewSegGen.ArcSample(center, radius, startDeg, sweepDeg, 24, visible: true);

            var curves = ViewArcFitter.Fit(segments);

            Assert.That(curves.Arcs.Count, Is.EqualTo(1),
                $"应恰好还原出 1 条弧，实际 {curves.Arcs.Count}；残余段 {curves.Segments.Count}");

            var arc = curves.Arcs[0];
            Assert.That(arc.Radius, Is.EqualTo(radius).Within(radius * 1e-3), "还原半径");
            Assert.That(arc.Center.X, Is.EqualTo(center.X).Within(0.05), "还原圆心 X");
            Assert.That(arc.Center.Y, Is.EqualTo(center.Y).Within(0.05), "还原圆心 Y");
            Assert.That(arc.SweepDegrees, Is.EqualTo(sweepDeg).Within(5.0), "还原扫掠角");
            Assert.That(arc.IsVisible, Is.True);
            Assert.That(curves.Segments.Count, Is.EqualTo(0), "全部输入应被弧吸收");
        }

        [Test]
        public void SegmentsFor_MatchesChordToleranceFormula()
        {
            using var scope = new TessellationScope(0.05);

            foreach (var radius in new[] { 1.0, 10.0, 100.0, 1000.0 })
            {
                // 解析解：单段圆心角 θ = 2·acos(1 − tol/r)，N = ceil(2π/θ)，夹在 [16,128]
                var half = Math.Acos(1.0 - 0.05 / radius);
                var analytic = (int)Math.Ceiling(Math.PI / half);
                var expected = Math.Min(Math.Max(analytic, Tessellation.MinSegments), Tessellation.MaxSegments);

                Assert.That(Tessellation.SegmentsFor(radius), Is.EqualTo(expected),
                    $"r={radius} 的分段数应等于解析值 {expected}");
            }
        }

        [Test]
        public void SegmentsFor_AdaptiveIsMonotoneInToleranceAndRadius()
        {
            using var scope = new TessellationScope(0.05);

            // 容差越松 ⇒ 分段越少（单调不增）
            var previous = int.MaxValue;
            foreach (var tolerance in new[] { 0.001, 0.01, 0.05, 0.2, 1.0 })
            {
                using var inner = new TessellationScope(tolerance);
                var count = Tessellation.SegmentsFor(50.0);

                Assert.That(count, Is.LessThanOrEqualTo(previous),
                    $"tol={tolerance} 时分段数 {count} 应 ≤ 上一档 {previous}");
                previous = count;
            }

            // 半径越大 ⇒ 需要越多分段（单调不减）
            using var fixedTolerance = new TessellationScope(0.05);
            previous = 0;
            foreach (var radius in new[] { 1.0, 5.0, 20.0, 100.0, 500.0 })
            {
                var count = Tessellation.SegmentsFor(radius);

                Assert.That(count, Is.GreaterThanOrEqualTo(previous),
                    $"r={radius} 时分段数 {count} 应 ≥ 上一档 {previous}");
                previous = count;
            }
        }

        /// <summary>
        /// 把实体全部顶点投到指定视图的 2D 平面。
        /// 刻意走网格坐标（<c>CsgMesh.TrianglesOf</c> 已完成 Y↔Z 交换）后用方向的
        /// Right/Up 取轴 —— 与 <see cref="ViewProjector"/> 用的是**同一套取轴定义**，
        /// 但**各自独立实现**（一个在库内，一个在测试侧手算）。这正是这条 oracle 的独立性所在：
        /// 若取轴口径被改错，两侧会同时错同向，但凸包周长与可见线段总长仍会不一致。
        /// </summary>
        static List<ViewPoint2> ProjectedVertices(Solid solid, ViewDirection direction)
        {
            var mesh = CsgMesh.TrianglesOf(solid);
            var points = new List<ViewPoint2>(mesh.Count * 3);

            foreach (var triangle in mesh)
                foreach (var p in new[] { triangle.A, triangle.B, triangle.C })
                {
                    var v = new ViewVector(p.X, p.Y, p.Z);
                    points.Add(new ViewPoint2(v.Dot(direction.Right), v.Dot(direction.Up)));
                }

            return points;
        }

        static double SumSegmentLength(ViewProjection view)
        {
            var total = 0.0;
            foreach (var segment in view.Segments)
                total += segment.Length;
            return total;
        }
    }
}
```

`ViewVector.Dot(ViewVector)` 是实例方法（`ViewGeometry.cs:65`），返回 `double`；`ViewVector` 的 ctor 是 `(double x, double y, double z)`。

- [ ] **Step 2: 跑测试确认失败**

```bash
dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release --filter "FullyQualifiedName~Tier2AnalyticTest" 2>&1 | tail -12
```

预期：编译失败 —— `CS0246: 未能找到类型或命名空间名"ConvexHull"`。

- [ ] **Step 3: 实现 ConvexHull**

`tests/OpenCSG.NET.TestKit/Oracles/ConvexHull.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Csg.Test
{
    /// <summary>
    /// 2D 凸包（Andrew 单调链）与周长。**独立于被测代码实现** ——
    /// 它是 Tier 2 的对照物，一旦复用了被测代码就不再是 oracle。
    /// </summary>
    public static class ConvexHull
    {
        /// <summary>返回逆时针凸包（不含重复的收尾点）。输入少于 3 个点时原样返回。</summary>
        public static IReadOnlyList<ViewPoint2> MonotoneChain(IReadOnlyList<ViewPoint2> points)
        {
            if (points.Count <= 2)
                return points.ToArray();

            var sorted = points
                .OrderBy(p => p.X)
                .ThenBy(p => p.Y)
                .ToArray();

            var hull = new List<ViewPoint2>(sorted.Length * 2);

            // 下凸壳
            foreach (var p in sorted)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }

            // 上凸壳
            var lower = hull.Count + 1;
            for (var i = sorted.Length - 2; i >= 0; i--)
            {
                var p = sorted[i];
                while (hull.Count >= lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }

            // 收尾点与首点重复，去掉
            hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        public static double Perimeter(IReadOnlyList<ViewPoint2> hull)
        {
            if (hull.Count < 2)
                return 0.0;

            var total = 0.0;
            for (var i = 0; i < hull.Count; i++)
            {
                var a = hull[i];
                var b = hull[(i + 1) % hull.Count];
                var dx = b.X - a.X;
                var dy = b.Y - a.Y;
                total += Math.Sqrt(dx * dx + dy * dy);
            }
            return total;
        }

        public static double PerimeterOf(IReadOnlyList<ViewPoint2> points) => Perimeter(MonotoneChain(points));

        /// <summary>叉积 (b−a)×(c−a)；&gt;0 表示左转（逆时针）。</summary>
        static double Cross(ViewPoint2 a, ViewPoint2 b, ViewPoint2 c)
            => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    }
}
```

**注意**：`ViewPoint2` **没有** `DistanceTo` 方法（那个方法是 `Vector2D` 的），所以 `ConvexHull.Perimeter` 里距离必须自己算 —— 上面的代码块已经这么写了，不要改成 `a.DistanceTo(b)`。

- [ ] **Step 4: 跑测试确认通过**

```bash
dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release --filter "FullyQualifiedName~Tier2AnalyticTest" 2>&1 | tail -30
```

预期：8 个用例全过。

**若 `Sphere_SilhouetteIsAClosedCircleOfRadiusR` 失败**：说明圆弧识别器没把球面轮廓还原成整圆，或还原的半径偏差大 —— 这是 `ViewArcFitter` 的**真实缺陷**（该子系统此前零测试）。登记 `DEFECT-nnn`，把该用例标 `[Explicit]`，并在报告里给出实际 `Arcs.Count` / `Radius` / `IsClosed`。

**若 `ArcSample_IsRecoveredAsAnArcWithKnownCenterAndRadius` 失败**：这是纯合成输入（从已知圆采样），失败只可能是拟合器的问题。同样登记缺陷。**注意**：`ViewArcFitter.MinRadius` = 0.5、`MinSweepDegrees` = 10 —— 用例参数（R=9、sweep=110°）刻意远离这些门限，所以失败不能归因于门限。

- [ ] **Step 5: 记录 Tier 2 结果**

在 `TEST-REPORT` 追加 Tier 2 小节，列出每条 oracle 的实际测量值（凸包周长 vs 可见线段总长、还原半径与扫掠角、各半径的分段数）。`<填实际>` 必须替换为真实数字。

- [ ] **Step 6: 提交（已授权）**

```bash
git add tests/OpenCSG.NET.TestKit/Oracles/ConvexHull.cs tests/OpenCSG.NET.SystemTests tests/DEFECTS.md docs/testing
git commit -m "test: Tier 2 解析解属性测试与独立凸包 oracle"
```

---

### Task 10: Tier 3 —— HlrAssert + 结构/单调/幂等属性测试 + 收口

**Files:**
- Create: `tests/OpenCSG.NET.TestKit/Oracles/HlrAssert.cs`
- Create: `tests/OpenCSG.NET.SystemTests/Property/Tier3StructureTest.cs`
- Modify: `docs/testing/TEST-REPORT-2026-10-13.md`

**Interfaces:**
- Consumes: `ViewSegGen`、`ViewBridge`、`MeshAssert`、`ConvexHull`
- Produces: `public static class Csg.Test.HlrAssert`：
  - `AssertBoundsCoverSegments(ViewProjection view, string? message = null)`
  - `AssertVisibilityAccounting(ViewProjection view, string? message = null)`
  - `AssertCleanPreservesCoverage(IReadOnlyList<ViewSegment> input, IReadOnlyList<ViewSegment> output, ViewBounds2 bounds, string? message = null)`
  - `AssertFitCoversInput(IReadOnlyList<ViewSegment> input, ViewCurves<ViewSegment> curves, double tolerance, string? message = null)`
  - `AssertTopologyIsClosedManifold(IReadOnlyList<ViewTriangle> triangles, string? message = null)`

- [ ] **Step 1: 写失败的测试**

`tests/OpenCSG.NET.SystemTests/Property/Tier3StructureTest.cs`：

```csharp
using System;
using System.Collections.Generic;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// Tier 3：结构不变量、单调性、幂等性。这些性质不给出「正确答案」，
    /// 但它们对任意输入成立，因此能廉价地覆盖大量输入空间。
    /// </summary>
    [TestFixture]
    public class Tier3StructureTest
    {
        static IEnumerable<TestCaseData> SegmentCases()
        {
            for (long seed = 0; seed < 32; seed++)
                yield return new TestCaseData(seed).SetName($"Seg_{seed:D2}");
        }

        [TestCaseSource(nameof(SegmentCases))]
        [Category("Smoke")]
        public void Bounds_CoverEverySegmentEndpoint(long seed)
        {
            var segments = ViewSegGen.Random(seed);
            var view = new ViewProjection(ViewDirections.Front, segments);

            HlrAssert.AssertBoundsCoverSegments(view, $"seed={seed}");
        }

        [TestCaseSource(nameof(SegmentCases))]
        [Category("Smoke")]
        public void VisibilityAccounting_AgreesWithPerSegmentFlags(long seed)
        {
            var segments = ViewSegGen.Random(seed);
            var view = new ViewProjection(ViewDirections.Front, segments);

            HlrAssert.AssertVisibilityAccounting(view, $"seed={seed}");
        }

        [TestCaseSource(nameof(SegmentCases))]
        [Category("Smoke")]
        public void Clean_PreservesCoverageAndVisibility(long seed)
        {
            var segments = ViewSegGen.Random(seed);
            var bounds = ViewSegGen.BoundsOf(segments);

            var cleaned = ViewSegmentCleanup.Clean(segments, bounds);

            HlrAssert.AssertCleanPreservesCoverage(segments, cleaned, bounds, $"seed={seed}");
        }

        [TestCaseSource(nameof(SegmentCases))]
        [Category("Smoke")]
        public void Clean_IsIdempotent(long seed)
        {
            var segments = ViewSegGen.Random(seed);
            var bounds = ViewSegGen.BoundsOf(segments);

            var once = ViewSegmentCleanup.Clean(segments, bounds);
            var twice = ViewSegmentCleanup.Clean(once, bounds);

            Assert.That(twice.Count, Is.EqualTo(once.Count),
                $"seed={seed}: 二次清理改变了线段数（{once.Count} → {twice.Count}）");
        }

        [Test]
        [Category("Smoke")]
        public void Clean_DoesNotMergeAcrossVisibilityLayers()
        {
            // `ViewSegGen.Random` 生成的线段集**每块只有一种可见性**，所以「跨可见性不合并」这条守卫
            // 在随机输入下不可观测 —— Task 5 实测 64 个种子里存在共线且相邻的异可见性对的数量为 **0**。
            // 单靠随机输入会让人误以为覆盖了这条守卫；这里手工构造一对同线、重叠、一可见一隐藏的线段。
            var bounds = new ViewBounds2(0, 0, 10, 10);
            var input = new[]
            {
                new ViewSegment(new ViewPoint2(0, 0), new ViewPoint2(6, 0), isVisible: true),
                new ViewSegment(new ViewPoint2(2, 0), new ViewPoint2(8, 0), isVisible: false),
            };

            var cleaned = ViewSegmentCleanup.Clean(input, bounds);

            var visible = 0;
            var hidden = 0;
            foreach (var segment in cleaned)
            {
                if (segment.IsVisible) visible++; else hidden++;
            }

            Assert.That(visible, Is.GreaterThan(0), "可见层不得被并入隐藏层");
            Assert.That(hidden, Is.GreaterThan(0), "隐藏层不得被并入可见层");
            Assert.That(cleaned.Count, Is.EqualTo(2),
                $"跨可见性的共线重叠段必须保持两条（Clean 的合并带同可见性守卫），实际 {cleaned.Count} 条");
        }

        [TestCaseSource(nameof(SegmentCases))]
        [Category("Smoke")]
        public void Fit_CoversInputWithArcsPlusResidues(long seed)
        {
            var segments = ViewSegGen.Random(seed);

            var curves = ViewArcFitter.Fit(segments);

            HlrAssert.AssertFitCoversInput(segments, curves, ViewArcFitter.DefaultTolerance, $"seed={seed}");
        }

        [TestCaseSource(nameof(SegmentCases))]
        [Category("Smoke")]
        public void Fit_DoesNotInventGeometry(long seed)
        {
            var segments = ViewSegGen.Random(seed);

            var curves = ViewArcFitter.Fit(segments);

            // 拟合不得凭空产出输入之外的总长度：
            // 弧长 + 残余段长 应接近输入总长（弧会略微缩短折线，故给 5% 余量）
            var inputLength = TotalLength(segments);
            var arcLength = 0.0;
            foreach (var arc in curves.Arcs)
                arcLength += arc.Radius * arc.SweepDegrees * Math.PI / 180.0;

            var outputLength = arcLength + TotalLength(curves.Segments);

            Assert.That(outputLength, Is.LessThanOrEqualTo(inputLength * 1.05 + 1e-6),
                $"seed={seed}: 拟合后总长 {outputLength:R} 超出输入总长 {inputLength:R} 的 5%");
        }

        [TestCaseSource(nameof(SegmentCases))]
        [Category("Smoke")]
        public void MergeConcyclic_IsIdempotent(long seed)
        {
            var segments = ViewSegGen.ArcSample(new ViewPoint2(0, 0), 10, 0, 360, 6, visible: true);

            var curves = ViewArcFitter.Fit(segments);
            var once = ViewArcFitter.MergeConcyclic(curves.Arcs);
            var twice = ViewArcFitter.MergeConcyclic(once);

            Assert.That(twice.Count, Is.EqualTo(once.Count),
                $"seed={seed}: MergeConcyclic 不幂等（{once.Count} → {twice.Count}）");
        }

        [Test]
        [Category("Smoke")]
        public void Topology_CubeIsAClosedManifoldWithEulerTwo()
        {
            var cube = Solids.Cube(10, center: true);
            var triangles = ViewBridge.ToViewTriangles(cube);

            HlrAssert.AssertTopologyIsClosedManifold(triangles, "cube");
        }

        [Test]
        [Category("Smoke")]
        public void Topology_WeldIsIdempotent()
        {
            var cube = Solids.Cube(10, center: true);
            var triangles = ViewBridge.ToViewTriangles(cube);

            var once = new ViewMeshTopology(triangles);
            var twice = new ViewMeshTopology(triangles);

            Assert.That(twice.Points.Count, Is.EqualTo(once.Points.Count));
            Assert.That(twice.Edges.Count, Is.EqualTo(once.Edges.Count));
        }

        [Test]
        [Category("Smoke")]
        public void Projection_ConvexSolidHasNoVisibleSelfOcclusion()
        {
            // 凸实体的可见线段必须构成一条闭合回路，且不存在被自身遮挡的可见段。
            // 判据：可见段数 > 0，且可见段总长仍等于投影凸包周长（凸体无遮挡 ⇒ 无隐藏段）。
            var cube = Solids.Cube(10, center: true);
            var views = ViewBridge.ProjectViews(cube, ViewDirections.OrthographicThree);

            foreach (var view in views)
            {
                Assert.That(view.VisibleCount, Is.GreaterThan(0), $"{view.Direction.Name}: 无可见段");
                Assert.That(SumVisibleLength(view),
                    Is.EqualTo(view.Bounds.Width * 2 + view.Bounds.Height * 2).Within(1e-6),
                    $"{view.Direction.Name}: 凸体可见段总长应等于视图矩形周长（4 条轮廓边）");
            }
        }

        static double TotalLength(IReadOnlyList<ViewSegment> segments)
        {
            var total = 0.0;
            foreach (var segment in segments)
                total += segment.Length;
            return total;
        }

        static double SumVisibleLength(ViewProjection view)
        {
            var total = 0.0;
            foreach (var segment in view.Segments)
                if (segment.IsVisible)
                    total += segment.Length;
            return total;
        }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

```bash
dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release --filter "FullyQualifiedName~Tier3StructureTest" 2>&1 | tail -12
```

预期：编译失败 —— `CS0246: 未能找到类型或命名空间名"HlrAssert"`。

- [ ] **Step 3: 实现 HlrAssert**

`tests/OpenCSG.NET.TestKit/Oracles/HlrAssert.cs`：

```csharp
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// Tier 3：HLR 前端的结构与守恒不变量。
    /// 刻意采用**覆盖度**而非「长度求和」来判定「不丢几何」：
    /// <see cref="ViewSegmentCleanup"/> 会塌缩被包含的重叠段，总长因此可能**减少**，
    /// 但被丢弃的只能是低于其阈值的残段 —— 覆盖度才是正确的不变量。
    /// </summary>
    public static class HlrAssert
    {
        /// <summary>点落在某线段上的容差（mm）。</summary>
        public const double OnSegmentTolerance = 1e-6;

        public static void AssertBoundsCoverSegments(ViewProjection view, string? message = null)
        {
            var prefix = Prefix(message);
            var bounds = view.Bounds;

            foreach (var segment in view.Segments)
                AssertEndpointInBounds(segment.Start, bounds, prefix, view.Direction.Name);
            foreach (var segment in view.Segments)
                AssertEndpointInBounds(segment.End, bounds, prefix, view.Direction.Name);
        }

        public static void AssertVisibilityAccounting(ViewProjection view, string? message = null)
        {
            var counted = 0;
            foreach (var segment in view.Segments)
                if (segment.IsVisible)
                    counted++;

            // VisibleCount 的权威定义是逐段 IsVisible 标记；
            // VisibleCount + HiddenCount == Segments.Count 是同义反复（HiddenCount 就是这么定义的），断言它等于没断言。
            Assert.That(view.VisibleCount, Is.EqualTo(counted),
                $"{Prefix(message)} {view.Direction.Name}: VisibleCount={view.VisibleCount} 与逐段标记计数 {counted} 不一致");
            Assert.That(view.HiddenCount, Is.EqualTo(view.Segments.Count - counted),
                $"{Prefix(message)} {view.Direction.Name}: HiddenCount 与逐段标记计数不一致");
        }

        /// <summary>
        /// 清理不得丢失「足够长」的输入几何，也不得跨可见性合并。
        /// 足够长 = 长于 Clean 的丢弃阈值（绝对 1e-3 与相对 1e-4×包络宽 取大者）。
        /// </summary>
        public static void AssertCleanPreservesCoverage(
            IReadOnlyList<ViewSegment> input,
            IReadOnlyList<ViewSegment> output,
            ViewBounds2 bounds,
            string? message = null)
        {
            var minKeep = Math.Max(ViewSegmentCleanup.MinAbsoluteLength,
                                   ViewSegmentCleanup.MinRelativeLength * Math.Max(bounds.Width, bounds.Height));

            foreach (var segment in input)
            {
                if (segment.Length <= minKeep)
                    continue;   // 低于阈值的残段本就会被丢弃

                foreach (var sample in SamplePoints(segment))
                {
                    var covering = FindCovering(output, sample, OnSegmentTolerance);

                    Assert.That(covering, Is.Not.Null,
                        $"{Prefix(message)} 清理丢失了覆盖：输入段 {segment.Start}→{segment.End} 上的点 "
                        + $"({sample.X:R},{sample.Y:R}) 不在任何输出段上");

                    Assert.That(covering!.Value.IsVisible, Is.EqualTo(segment.IsVisible),
                        $"{Prefix(message)} 清理跨越了可见性合并：输入段是 "
                        + $"{(segment.IsVisible ? "可见" : "隐藏")}，覆盖它的输出段却是 "
                        + $"{(covering.Value.IsVisible ? "可见" : "隐藏")}");
                }
            }
        }

        /// <summary>拟合结果（弧 + 残余段）必须覆盖全部输入线段。</summary>
        public static void AssertFitCoversInput(
            IReadOnlyList<ViewSegment> input,
            ViewCurves<ViewSegment> curves,
            double tolerance,
            string? message = null)
        {
            var slack = Math.Max(tolerance, OnSegmentTolerance);

            foreach (var segment in input)
            {
                foreach (var sample in SamplePoints(segment))
                {
                    if (FindCovering(curves.Segments, sample, slack) != null)
                        continue;

                    var onArc = false;
                    foreach (var arc in curves.Arcs)
                    {
                        var dx = sample.X - arc.Center.X;
                        var dy = sample.Y - arc.Center.Y;
                        var distance = Math.Sqrt(dx * dx + dy * dy);
                        if (Math.Abs(distance - arc.Radius) <= slack * 4)
                        {
                            onArc = true;
                            break;
                        }
                    }

                    Assert.That(onArc, Is.True,
                        $"{Prefix(message)} 拟合丢失了几何：输入段上的点 ({sample.X:R},{sample.Y:R}) "
                        + "既不在任何残余段上，也不在任何拟合弧上");
                }
            }
        }

        /// <summary>视图网格拓扑：顶点焊接后无边界边、无重复有向边，且欧拉特征为 2（亏格 0）。</summary>
        public static void AssertTopologyIsClosedManifold(
            IReadOnlyList<ViewTriangle> triangles, string? message = null)
        {
            var topology = new ViewMeshTopology(triangles);
            var prefix = Prefix(message);

            var boundary = 0;
            var directed = new Dictionary<long, int>();

            foreach (var edge in topology.Edges)
            {
                if (edge.TriB < 0)
                    boundary++;

                var key = ((long)edge.P0 << 32) | (uint)edge.P1;
                directed.TryGetValue(key, out var count);
                directed[key] = count + 1;
            }

            Assert.That(boundary, Is.EqualTo(0),
                $"{prefix} 有 {boundary} 条边界边（网格不封闭）");

            var duplicated = 0;
            foreach (var pair in directed)
                if (pair.Value != 1)
                    duplicated++;

            Assert.That(duplicated, Is.EqualTo(0),
                $"{prefix} 有 {duplicated} 条有向边重复（绕序不一致）");

            var chi = topology.Points.Count - topology.Edges.Count + topology.Triangles.Count;
            Assert.That(chi, Is.EqualTo(2),
                $"{prefix} 欧拉特征为 {chi}，亏格 0 的封闭网格应为 2"
                + $"（V={topology.Points.Count} E={topology.Edges.Count} F={topology.Triangles.Count}）");
        }

        static void AssertEndpointInBounds(ViewPoint2 point, ViewBounds2 bounds, string prefix, string viewName)
        {
            const double slack = 1e-9;
            Assert.That(point.X, Is.GreaterThanOrEqualTo(bounds.MinX - slack),
                $"{prefix} {viewName}: 端点 X={point.X:R} 小于包络下限 {bounds.MinX:R}");
            Assert.That(point.X, Is.LessThanOrEqualTo(bounds.MaxX + slack),
                $"{prefix} {viewName}: 端点 X={point.X:R} 超过包络上限 {bounds.MaxX:R}");
            Assert.That(point.Y, Is.GreaterThanOrEqualTo(bounds.MinY - slack),
                $"{prefix} {viewName}: 端点 Y={point.Y:R} 小于包络下限 {bounds.MinY:R}");
            Assert.That(point.Y, Is.LessThanOrEqualTo(bounds.MaxY + slack),
                $"{prefix} {viewName}: 端点 Y={point.Y:R} 超过包络上限 {bounds.MaxY:R}");
        }

        /// <summary>沿线段取 3 个内部采样点（1/4、1/2、3/4），避免端点因容差判定产生假阴性。</summary>
        static IEnumerable<ViewPoint2> SamplePoints(ViewSegment segment)
        {
            for (var i = 1; i <= 3; i++)
            {
                var t = i / 4.0;
                yield return new ViewPoint2(
                    segment.Start.X + (segment.End.X - segment.Start.X) * t,
                    segment.Start.Y + (segment.End.Y - segment.Start.Y) * t);
            }
        }

        static ViewSegment? FindCovering(
            IReadOnlyList<ViewSegment> segments, ViewPoint2 point, double tolerance)
        {
            foreach (var segment in segments)
                if (DistanceToSegment(point, segment) <= tolerance)
                    return segment;
            return null;
        }

        static double DistanceToSegment(ViewPoint2 point, ViewSegment segment)
        {
            var dx = segment.End.X - segment.Start.X;
            var dy = segment.End.Y - segment.Start.Y;
            var lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 0)
            {
                var px = point.X - segment.Start.X;
                var py = point.Y - segment.Start.Y;
                return Math.Sqrt(px * px + py * py);
            }

            var t = ((point.X - segment.Start.X) * dx + (point.Y - segment.Start.Y) * dy) / lengthSquared;
            if (t < 0) t = 0;
            if (t > 1) t = 1;

            var nearestX = segment.Start.X + dx * t;
            var nearestY = segment.Start.Y + dy * t;
            var ex = point.X - nearestX;
            var ey = point.Y - nearestY;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        static string Prefix(string? message) => message == null ? "" : message + ":";
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

```bash
dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release --filter "FullyQualifiedName~Tier3StructureTest" 2>&1 | tail -30
```

预期：9 个测试方法全过（前 7 个各 32 个种子 = 224 用例，后 2 个各 1 用例）。

**失败时的处置（按失败性质分三类）：**
- **`Clean` 覆盖度或可见性失败** ⇒ `ViewSegmentCleanup` 的真实缺陷（它此前零非平凡覆盖）。登记 `DEFECT-nnn`，用例标 `[Explicit]`。
- **`Fit` 覆盖度失败** ⇒ `ViewArcFitter` 的真实缺陷。同上。
- **`Topology` 欧拉特征不是 2** ⇒ `ViewMeshTopology` 的焊接或 `CsgMesh` 的三角扇化问题。同上。
- **`Projection_ConvexSolidHasNoVisibleSelfOcclusion` 失败** ⇒ 凸体出现了隐藏段，说明深度消隐对凸体误判。同上。
  **注意**：该用例假定立方体的可见段恰为 4 条矩形轮廓边、总长等于视图矩形周长。若失败而实际可见段包含面对角线，需先判断是「轮廓提取多算了边」还是「我的判据过严」—— 用 Plan A 已建立的诊断探针打出线段清单再判定，**不要直接放宽断言**。

- [ ] **Step 5: 收口 —— 分类与时间预算**

```bash
# 只跑 Smoke 档，量 PR 时长
dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release --filter "TestCategory=Smoke" 2>&1 | tail -6
```

预期：`Category=Smoke` 覆盖 Tier 1/2/3 的主体；记录通过数与墙钟耗时。

在 `TEST-REPORT` 追加：

```markdown
### Plan B1 · Task 10：Tier 3 结构不变量与时间预算

| 项 | 值 |
| --- | --- |
| Tier 3 用例数（含种子展开） | <填实际> |
| SystemTests 总用例数 | <填实际> |
| `--filter "TestCategory=Smoke"` 墙钟 | <填实际> |
| SystemTests 全套件墙钟 | <填实际> |

时间预算结论：<填实际：是否满足 spec §7 的「SystemTests ≤ 8 分钟」；若不满足，说明哪些用例应移入 nightly 并给出理由>
```

替换完 `<填实际>` 后验证没有残留：

```bash
grep -n "填实际" docs/testing/TEST-REPORT-2026-10-13.md
```

预期：无输出。**整个 TEST-REPORT 到这里必须零占位符残留**（Task 1/7/8/9/10 的模板都已替换）。

**若 Smoke 档超过 8 分钟**：把最贵的几个测试方法加 `[Category("Corpus")]`（即从 Smoke 移出，只在 nightly 跑），并在报告里写明移动了哪些与为什么。**不要**通过削减种子数来「达标」—— 覆盖广度是这个计划的价值所在。

- [ ] **Step 6: 全量验证**

```bash
dotnet build OpenCSG.NET.slnx -c Release 2>&1 | tail -6
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release 2>&1 | tail -3
dotnet test tests/OpenCSG.NET.SystemTests/OpenCSG.NET.SystemTests.csproj -c Release 2>&1 | tail -3
```

预期：构建 0 警告 0 错误；Tests 126 通过 / 0 失败 / 1 跳过；SystemTests 全绿且 0 失败。

- [ ] **Step 7: 提交（已授权）**

```bash
git add tests/OpenCSG.NET.TestKit/Oracles/HlrAssert.cs tests/OpenCSG.NET.SystemTests tests/DEFECTS.md docs/testing
git commit -m "test: Tier 3 结构不变量属性测试与 HLR oracle 收口"
```

---

## 完成定义（Plan B1）

- [ ] `dotnet build OpenCSG.NET.slnx -c Release` 成功，0 警告 0 错误（含新工程与 samples/perf）
- [ ] `dotnet test tests/OpenCSG.NET.Tests/...` = 126 通过 / 0 失败 / 1 跳过（`[Explicit]` 的 DEFECT-001）
- [ ] `dotnet test tests/OpenCSG.NET.SystemTests/...` 全绿、0 失败
- [ ] `tests/OpenCSG.NET.TestKit/Generation/corpus.json` 已提交，且重建**幂等**（两次生成逐字节相同）
- [ ] TestKit 目录已按 spec §4 落位（`Generation/` `Oracles/` `Bridge/` `Io/` `Infrastructure/`），`GeometryAssert` 等的命名空间仍为 `Csg.Test`，现有测试文件断言逻辑零改动
- [ ] `GoldenPaths.ResultsDirectory` 的标记文件名已参数化，`GoldenGuardTest` 增补的参数化用例通过
- [ ] `TessellationScope` 有 3 个用例（含异常路径复位）；`DEFECT-001` 已登记且其 `[Explicit]` 用例默认被跳过
- [ ] Tier 1（含 Tier 1 续的六族：变换等价/包围盒/序列化往返/相等契约/平面分割/NaN 校验）、Tier 2、Tier 3 的用例数与实测通过数已写入 `TEST-REPORT`，无占位符
- [ ] 时间预算结论已记录；若超预算，说明移动了哪些用例到 nightly
- [ ] 所有发现的缺陷已登记 `DEFECTS.md` 并给出最小复现；**`src/` 零改动**

## 后续计划（不在本计划范围）

| 计划 | 内容 | 依赖 B1 的 |
| --- | --- | --- |
| **Plan B2** | **Tier 5 遮挡 oracle（必做项）**：`RayCast` 独立射线投射 + `HlrAssert` 扩展 + 遮挡属性测试。spec §13 明确 Plan A 没有任何测试产生 `HiddenCount > 0`，`DepthRaster.Split` 的可见/隐藏拆分从未非平凡执行 —— 只有 Tier 5 能关闭它 | `SolidGen`、`Corpus`、`ViewBridge`、`ViewSegGen` |
| Plan C | L1/L2 单元与契约回填 + A1/A2/A4 最小修复 + `DefectRegistryTest` + `GeometryAssert` 空输入不对称 + 补强两个偏弱断言 | `Corpus`、`SolidGen`（作为固定输入源） |
| Plan D | L4 golden + L5 集成端到端（含 `CpurlinModel` 抽取、JSON golden 前先扩门禁） | `Corpus`、`ViewBridge` |
| Plan E | L6 perf/复杂度 + L7 稳健性/并行/跨运行时 | 全部 TestKit |
| Plan F | 覆盖率门禁 + CI 改造（`global.json`、windows 矩阵、nightly、ApiCompat 入 CI、不得过滤 `GoldenGuardTest`）+ 报告定稿 | `corpus.json`、`TEST-REPORT` |
