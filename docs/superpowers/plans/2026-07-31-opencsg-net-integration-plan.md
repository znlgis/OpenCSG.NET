# OpenCSG.NET Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Integrate Csg and DotNetCsg into OpenCSG.NET with merged improvements, modern project structure, bilingual README, and NuGet publish CI.

**Architecture:** Single netstandard2.0 core library (`src/OpenCSG.NET/`) with namespace `Csg`, net8.0 test project with golden-file STL validation, two net8.0 sample runners, and a net8.0 BenchmarkDotNet perf test. Source is DotNetCsg baseline + Csg's Union origin-centering fix in Solid.cs.

**Tech Stack:** C# 8.0 (lib), C# 12 (tests/samples), .NET Standard 2.0, .NET 8.0, NUnit 3.13.3, BenchmarkDotNet 0.13.5

## Global Constraints

- License: MIT (replace LGPL-2.1 in LICENSE file)
- NuGet PackageId: `OpenCSG.NET`
- Namespace: `Csg` (keep for API compatibility)
- Core lib: `netstandard2.0`, `LangVersion 8.0`, `Nullable: enable`, `TreatWarningsAsErrors: true`
- Tests/samples/perf: `net8.0`
- CI trigger: Git tag `v*` → build, test, pack, push to NuGet.org
- README: bilingual (Chinese + English)
- Exclude: Xamarin.Mac, Xamarin.iOS projects
- Keep: Runner.CPurlin sample

**Diff Summary:** Only ONE file needs cross-project merging (Solid.cs). All other files are identical or DotNetCsg is superior:
- **Solid.cs**: DotNetCsg baseline, ADD Csg's `Union()` origin-centering + `UnionSubLocal()`, `CombinedBounds()`, `CombinedBoundsAll()`, `TranslateBy()` helpers. Keep DotNetCsg's `RotateX/Y/Z`.
- **Vector.cs**: Identical between projects (both have NaN checks). Use DotNetCsg.
- **Plane.cs**: DotNetCsg already removed `unsafe`/`stackalloc` and already has `FromVector3Ds` with valid-plane detection. Use DotNetCsg.
- **Tree.cs, Solids.cs, Polygon.cs, Vertex.cs, Formats.cs**: DotNetCsg is better (iterative BSP, Rotate methods, binary STL). Use DotNetCsg.

---

### Task 1: Project scaffolding (directories, solution, csproj files)

**Files:**
- Create: `src/OpenCSG.NET/OpenCSG.NET.csproj`
- Create: `tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj`
- Create: `samples/Runner.Examples/Runner.Examples.csproj`
- Create: `samples/Runner.CPurlin/Runner.CPurlin.csproj`
- Create: `perf/OpenCSG.NET.PerfTest/OpenCSG.NET.PerfTest.csproj`
- Create: `OpenCSG.NET.sln`
- Create: `.editorconfig` (from `D:\self\code\csg\Csg\.editorconfig`)
- Modify: `LICENSE` (replace LGPL-2.1 with MIT from `D:\self\code\csg\Csg\LICENSE.txt`)

**Produced:**
- `OpenCSG.NET.sln` containing all 5 projects
- `src/OpenCSG.NET/OpenCSG.NET.csproj`: netstandard2.0, PackageId=OpenCSG.NET, version 1.0.0, authors `Csg contributors`, MIT license

- [ ] **Step 1: Create directory structure**

```powershell
New-Item -ItemType Directory -Path "src\OpenCSG.NET" -Force
New-Item -ItemType Directory -Path "tests\OpenCSG.NET.Tests\Results" -Force
New-Item -ItemType Directory -Path "samples\Runner.Examples" -Force
New-Item -ItemType Directory -Path "samples\Runner.CPurlin" -Force
New-Item -ItemType Directory -Path "perf\OpenCSG.NET.PerfTest" -Force
```

- [ ] **Step 2: Copy .editorconfig from Csg**

Copy `D:\self\code\csg\Csg\.editorconfig` to `D:\self\code\csg\OpenCSG.NET\.editorconfig`

- [ ] **Step 3: Replace LICENSE with MIT**

Copy `D:\self\code\csg\Csg\LICENSE.txt` content to replace `D:\self\code\csg\OpenCSG.NET\LICENSE` content.

- [ ] **Step 4: Create src/OpenCSG.NET/OpenCSG.NET.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <AssemblyName>OpenCSG.NET</AssemblyName>

    <PackageId>OpenCSG.NET</PackageId>
    <Version>1.0.0</Version>
    <Authors>Csg contributors</Authors>
    <Description>Constructive Solid Geometry (CSG) library for .NET. Solid primitives (Cube, Sphere, Cylinder), boolean operations (Union, Subtract, Intersect), and STL output. Ported from OpenJsCad csg.js.</Description>
    <PackageTags>Mesh;3D;Geometry;CSG;Model;Graphics</PackageTags>
    <PackageProjectUrl>https://github.com/znlgis/OpenCSG.NET</PackageProjectUrl>
    <PackageLicenseFile>LICENSE</PackageLicenseFile>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <AllowedOutputExtensionsInPackageBuildOutputFolder>$(AllowedOutputExtensionsInPackageBuildOutputFolder);.pdb</AllowedOutputExtensionsInPackageBuildOutputFolder>

    <LangVersion>8.0</LangVersion>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <NoWarn>1701;1702;1591</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <None Include="..\..\LICENSE" Pack="true" PackagePath="$(PackageLicenseFile)" />
    <None Include="..\..\README.md" Pack="true" PackagePath="$(PackageReadmeFile)" />
  </ItemGroup>

</Project>
```

- [ ] **Step 5: Create tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj**

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
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\OpenCSG.NET\OpenCSG.NET.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 6: Create samples/Runner.Examples/Runner.Examples.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\OpenCSG.NET\OpenCSG.NET.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 7: Create samples/Runner.CPurlin/Runner.CPurlin.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\OpenCSG.NET\OpenCSG.NET.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 8: Create perf/OpenCSG.NET.PerfTest/OpenCSG.NET.PerfTest.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\OpenCSG.NET\OpenCSG.NET.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.13.5" />
  </ItemGroup>

</Project>
```

- [ ] **Step 9: Create solution file + add all projects**

```powershell
dotnet new sln -n OpenCSG.NET
dotnet sln add src\OpenCSG.NET\OpenCSG.NET.csproj
dotnet sln add tests\OpenCSG.NET.Tests\OpenCSG.NET.Tests.csproj
dotnet sln add samples\Runner.Examples\Runner.Examples.csproj
dotnet sln add samples\Runner.CPurlin\Runner.CPurlin.csproj
dotnet sln add perf\OpenCSG.NET.PerfTest\OpenCSG.NET.PerfTest.csproj
```

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "scaffold: create solution structure and project files"
```

---

### Task 2: Copy core library source files from DotNetCsg

**Files:**
- Create: `src/OpenCSG.NET/Vertex.cs` (from `D:\self\code\csg\DotNetCsg\Csg\Vertex.cs`)
- Create: `src/OpenCSG.NET/Polygon.cs` (from `D:\self\code\csg\DotNetCsg\Csg\Polygon.cs`)
- Create: `src/OpenCSG.NET/Plane.cs` (from `D:\self\code\csg\DotNetCsg\Csg\Plane.cs`)
- Create: `src/OpenCSG.NET/Vector.cs` (from `D:\self\code\csg\DotNetCsg\Csg\Vector.cs`)
- Create: `src/OpenCSG.NET/Tree.cs` (from `D:\self\code\csg\DotNetCsg\Csg\Tree.cs`)
- Create: `src/OpenCSG.NET/Formats.cs` (from `D:\self\code\csg\DotNetCsg\Csg\Formats.cs`)
- Create: `src/OpenCSG.NET/Solids.cs` (from `D:\self\code\csg\DotNetCsg\Csg\Solids.cs`)

- [ ] **Step 1: Copy all 7 files verbatim**

Copy each file from `D:\self\code\csg\DotNetCsg\Csg\` to `src\OpenCSG.NET\` (preserve namespace `Csg`, no edits needed).

- [ ] **Step 2: Create Solid.cs — DotNetCsg baseline with Csg Union fix**

The file is `src/OpenCSG.NET/Solid.cs`. Copy `D:\self\code\csg\DotNetCsg\Csg\Solid.cs` first, then apply these edits:

Replace the `Union` method (lines 37-48) with Csg's version:

```csharp
public Solid Union (params Solid[] others)
{
    if (others.Length == 0) {
        return this.Retesselated ().Canonicalized ();
    }

    var center = CombinedBoundsAll (this, others).Center;
    var result = TranslateBy (this, center.Negated);
    for (var i = 0; i < others.Length; i++) {
        result = result.UnionSubLocal (TranslateBy (others[i], center.Negated));
    }
    result = result.Retesselated ().Canonicalized ();
    return TranslateBy (result, center);
}
```

Replace the `UnionSub` method (lines 50-74) with Csg's version:

```csharp
Solid UnionSub (Solid csg, bool retesselate, bool canonicalize)
{
    if (!MayOverlap (csg)) {
        return UnionForNonIntersecting (csg);
    }

    var center = CombinedBounds (this, csg).Center;
    var a = TranslateBy (this, center.Negated);
    var b = TranslateBy (csg, center.Negated);
    var result = a.UnionSubLocal (b);
    if (retesselate)
        result = result.Retesselated ();
    if (canonicalize)
        result = result.Canonicalized ();
    return TranslateBy (result, center);
}
```

Add the new `UnionSubLocal` method right after `UnionSub`:

```csharp
Solid UnionSubLocal (Solid csg)
{
    if (!MayOverlap (csg)) {
        return UnionForNonIntersecting (csg);
    }

    var treeA = new Tree (Bounds, Polygons);
    var treeB = new Tree (csg.Bounds, csg.Polygons);

    treeA.ClipTo (treeB, false);
    treeB.ClipTo (treeA);
    treeB.Invert ();
    treeB.ClipTo (treeA);
    treeB.Invert ();

    var newpolygons = new List<Polygon> (treeA.AllPolygons ());
    newpolygons.AddRange (treeB.AllPolygons ());
    return Solid.FromPolygons (newpolygons);
}
```

Add helper methods `CombinedBounds`, `CombinedBoundsAll`, `TranslateBy` before the `Bounds` property. The exact insertion point is right after the `PolygonsPerPlaneKeyComparer` class (after line ~320 in the Csg version / after the `GetHashCode` method block). Insert:

```csharp
static BoundingBox CombinedBounds (Solid a, Solid b)
{
    if (a.Polygons.Count == 0) {
        return b.Bounds;
    }
    if (b.Polygons.Count == 0) {
        return a.Bounds;
    }
    var min = a.Bounds.Min.Min (b.Bounds.Min);
    var max = a.Bounds.Max.Max (b.Bounds.Max);
    return new BoundingBox (min, max);
}

static BoundingBox CombinedBoundsAll (Solid initial, Solid[] others)
{
    var hasBounds = false;
    var min = new Vector3D (0, 0, 0);
    var max = new Vector3D (0, 0, 0);
    void Include (Solid solid)
    {
        if (solid.Polygons.Count == 0) {
            return;
        }
        if (!hasBounds) {
            min = solid.Bounds.Min;
            max = solid.Bounds.Max;
            hasBounds = true;
        }
        else {
            min = min.Min (solid.Bounds.Min);
            max = max.Max (solid.Bounds.Max);
        }
    }

    Include (initial);
    foreach (var other in others) {
        Include (other);
    }
    return hasBounds ? new BoundingBox (min, max) : new BoundingBox (new Vector3D (0, 0, 0), new Vector3D (0, 0, 0));
}

static Solid TranslateBy (Solid solid, Vector3D offset)
{
    if (offset.X == 0.0 && offset.Y == 0.0 && offset.Z == 0.0) {
        return solid;
    }
    return solid.Translate (offset);
}
```

- [ ] **Step 3: Verify RotateX/Y/Z are preserved**

Confirm lines exist in Solid.cs:
```csharp
public Solid RotateX(double degrees) => Transform(Matrix4x4.RotationX(degrees));
public Solid RotateY(double degrees) => Transform(Matrix4x4.RotationY(degrees));
public Solid RotateZ(double degrees) => Transform(Matrix4x4.RotationZ(degrees));
```

- [ ] **Step 4: Build core library**

```powershell
dotnet build src/OpenCSG.NET/OpenCSG.NET.csproj -c Release
```
Expected: Build succeeds with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/OpenCSG.NET/
git commit -m "feat: add core library with merged Union precision fix"
```

---

### Task 3: Set up test project

**Files:**
- Create: `tests/OpenCSG.NET.Tests/SolidTest.cs` (from DotNetCsg)
- Create: `tests/OpenCSG.NET.Tests/CubeTest.cs`, `SphereTest.cs`, `CylinderTest.cs`, `UnionTest.cs`, `SubtractTest.cs`, `IntersectTest.cs`, `ExamplesTest.cs` (from DotNetCsg)
- Create: `tests/OpenCSG.NET.Tests/LargeCoordinateUnionTest.cs` (from Csg)
- Create: `tests/OpenCSG.NET.Tests/Results/*.stl` (27 golden files from DotNetCsg)

**Interfaces:**
- Consumes: `OpenCSG.NET.csproj` project reference
- Produces: `dotnet test` passes all tests

- [ ] **Step 1: Copy test C# files from DotNetCsg**

```powershell
Copy-Item "D:\self\code\csg\DotNetCsg\Csg.Test\SolidTest.cs" "tests\OpenCSG.NET.Tests\"
Copy-Item "D:\self\code\csg\DotNetCsg\Csg.Test\CubeTest.cs" "tests\OpenCSG.NET.Tests\"
Copy-Item "D:\self\code\csg\DotNetCsg\Csg.Test\SphereTest.cs" "tests\OpenCSG.NET.Tests\"
Copy-Item "D:\self\code\csg\DotNetCsg\Csg.Test\CylinderTest.cs" "tests\OpenCSG.NET.Tests\"
Copy-Item "D:\self\code\csg\DotNetCsg\Csg.Test\UnionTest.cs" "tests\OpenCSG.NET.Tests\"
Copy-Item "D:\self\code\csg\DotNetCsg\Csg.Test\SubtractTest.cs" "tests\OpenCSG.NET.Tests\"
Copy-Item "D:\self\code\csg\DotNetCsg\Csg.Test\IntersectTest.cs" "tests\OpenCSG.NET.Tests\"
Copy-Item "D:\self\code\csg\DotNetCsg\Csg.Test\ExamplesTest.cs" "tests\OpenCSG.NET.Tests\"
```

- [ ] **Step 2: Copy golden STL files**

```powershell
Copy-Item "D:\self\code\csg\DotNetCsg\Csg.Test\Results\*.stl" "tests\OpenCSG.NET.Tests\Results\"
```

- [ ] **Step 3: Add LargeCoordinateUnionTest from Csg**

Copy `D:\self\code\csg\Csg\Csg.Test\LargeCoordinateUnionTest.cs` to `tests\OpenCSG.NET.Tests\LargeCoordinateUnionTest.cs`

- [ ] **Step 4: Build and run tests**

```powershell
dotnet build tests/OpenCSG.NET.Tests/ -c Release
dotnet test tests/OpenCSG.NET.Tests/ -c Release
```
Expected: All tests pass. The LargeCoordinateUnionTest verifies the merged Union centering fix works.

- [ ] **Step 5: Commit**

```bash
git add tests/
git commit -m "test: add unit tests with golden-file STL validation"
```

---

### Task 4: Set up sample runners

**Files:**
- Create: `samples/Runner.Examples/Program.cs` (from `D:\self\code\csg\DotNetCsg\Runner.Examples\Program.cs`)
- Create: `samples/Runner.CPurlin/Program.cs` (from `D:\self\code\csg\DotNetCsg\Runner.CPurlin\Program.cs`)

**Interfaces:**
- Consumes: `OpenCSG.NET.csproj` project reference
- Produces: `dotnet run` in each sample folder generates STL files

- [ ] **Step 1: Copy Runner.Examples Program.cs**

```powershell
Copy-Item "D:\self\code\csg\DotNetCsg\Runner.Examples\Program.cs" "samples\Runner.Examples\"
```

- [ ] **Step 2: Copy Runner.CPurlin Program.cs**

```powershell
Copy-Item "D:\self\code\csg\DotNetCsg\Runner.CPurlin\Program.cs" "samples\Runner.CPurlin\"
```

- [ ] **Step 3: Build both runners**

```powershell
dotnet build samples/Runner.Examples/ -c Release
dotnet build samples/Runner.CPurlin/ -c Release
```
Expected: Both build with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add samples/
git commit -m "feat: add sample runners (Examples and CPurlin)"
```

---

### Task 5: Set up performance test

**Files:**
- Create: `perf/OpenCSG.NET.PerfTest/Program.cs` (from `D:\self\code\csg\DotNetCsg\Csg.PerfTest\Program.cs`)
- Create: `perf/OpenCSG.NET.PerfTest/PerfTest.cs` (from `D:\self\code\csg\DotNetCsg\Csg.PerfTest\PerfTest.cs`)

**Interfaces:**
- Consumes: `OpenCSG.NET.csproj` project reference
- Produces: `dotnet run -c Release` runs benchmark

- [ ] **Step 1: Copy PerfTest files**

```powershell
Copy-Item "D:\self\code\csg\DotNetCsg\Csg.PerfTest\Program.cs" "perf\OpenCSG.NET.PerfTest\"
Copy-Item "D:\self\code\csg\DotNetCsg\Csg.PerfTest\PerfTest.cs" "perf\OpenCSG.NET.PerfTest\"
```

- [ ] **Step 2: Build**

```powershell
dotnet build perf/OpenCSG.NET.PerfTest/ -c Release
```
Expected: Build with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add perf/
git commit -m "feat: add BenchmarkDotNet performance test"
```

---

### Task 6: Write bilingual README

**Files:**
- Modify: `README.md`

**Interfaces:**
- Produces: Complete README in Chinese + English

- [ ] **Step 1: Write README.md**

Content (replace current 2-line file):

```markdown
# OpenCSG.NET

[![NuGet](https://img.shields.io/nuget/v/OpenCSG.NET.svg)](https://www.nuget.org/packages/OpenCSG.NET/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

OpenCSG.NET is a .NET library for **Constructive Solid Geometry (CSG)** — create 3D solid primitives and combine them using boolean operations (union, subtract, intersect), then export to STL.

This project integrates improvements from multiple forks of [praeclarum/Csg](https://github.com/praeclarum/Csg), a manual C# port of [OpenJsCad csg.js](https://github.com/joostn/OpenJsCad).

---

## English

### Features

- **3D Primitives**: Cube, Sphere, Cylinder (with configurable resolution, sector angle, and tapered ends)
- **Boolean Operations**: Union, Subtract, Intersect — implemented via BSP tree (iterative, no recursion)
- **Transformations**: Translate, Scale, Rotate (X/Y/Z), arbitrary Matrix4x4
- **STL Export**: Both ASCII and binary STL output
- **Zero Dependencies**: Targets .NET Standard 2.0 — works on .NET Framework 4.6.1+, .NET Core, .NET 6/7/8/9+
- **Precision Fix**: Large-coordinate Union operations automatically center geometry at origin to preserve BSP floating-point accuracy

### Quick Start

```csharp
using static Csg.Solids;

// Create a cube and a sphere
var cube = Cube(size: 2);
var sphere = Sphere(radius: 1.2, center: true);

// Subtract sphere from cube
var result = cube.Subtract(sphere);

// Export to STL
using var writer = new StreamWriter("output.stl");
result.WriteStl("my-model", writer);
```

### Installation

```bash
dotnet add package OpenCSG.NET
```

### API Reference

All types are in namespace `Csg`.

**Solids (static factory):**
- `Cube(double size = 1, ...)` — axis-aligned box
- `Sphere(double radius = 1, ...)` — latitude/longitude sphere
- `Cylinder(double radius = 1, double height = 1, ...)` — cylinder or frustum
- `Union(params Solid[])`, `Difference(params Solid[])`, `Intersection(params Solid[])`

**Solid (instance methods):**
- `.Union(params Solid[])` — boolean union
- `.Subtract(params Solid[])` — boolean difference
- `.Intersect(params Solid[])` — boolean intersection
- `.Translate(x, y, z)` / `.Scale(x, y, z)` / `.RotateX/Y/Z(degrees)`
- `.Transform(Matrix4x4)` — arbitrary 4x4 transform
- `.WriteStl(name, writer)` — ASCII STL output
- `.WriteStl(name, binaryWriter)` — binary STL output

### Samples

| Project | Description |
|---------|-------------|
| `samples/Runner.Examples` | Generates STL files for basic primitives and boolean operations |
| `samples/Runner.CPurlin` | Real-world example: C-section steel purlin with bolted holes |

### Development

- **.NET SDK 8.0+** required to build
- `dotnet build` — build all projects
- `dotnet test` — run golden-file STL comparison tests
- `dotnet run -c Release --project perf/OpenCSG.NET.PerfTest` — run benchmarks

---

## 中文

### 特性

- **3D 图元**：立方体、球体、圆柱体（支持自定义分辨率、扇形角、锥台）
- **布尔运算**：并集、差集、交集 — 基于 BSP 树实现（迭代式，无递归）
- **变换**：平移、缩放、旋转 (X/Y/Z)、任意 4x4 矩阵变换
- **STL 导出**：支持 ASCII 和二进制两种格式
- **零依赖**：目标框架 .NET Standard 2.0 — 兼容 .NET Framework 4.6.1+ 及现代 .NET
- **精度修复**：大坐标并集操作自动将几何体居中到原点，保持 BSP 浮点精度

### 快速开始

```csharp
using static Csg.Solids;

// 创建立方体和球体
var cube = Cube(size: 2);
var sphere = Sphere(radius: 1.2, center: true);

// 差集运算
var result = cube.Subtract(sphere);

// 导出 STL
using var writer = new StreamWriter("output.stl");
result.WriteStl("my-model", writer);
```

### 安装

```bash
dotnet add package OpenCSG.NET
```

### 开发

- 需要 **.NET SDK 8.0+**
- `dotnet build` — 构建全部项目
- `dotnet test` — 运行 STL 快照对比测试
- `dotnet run -c Release --project perf/OpenCSG.NET.PerfTest` — 运行性能测试

---

## Acknowledgments / 致谢

This project builds on the work of:

- [praeclarum/Csg](https://github.com/praeclarum/Csg) — original C# port by Frank Krueger
- [hypar-io/Csg](https://github.com/hypar-io/Csg) — BSP precision improvements
- [talanc/DotNetCsg](https://github.com/talanc/DotNetCsg) — binary STL, iterative BSP, documentation
- [OpenJsCad csg.js](https://github.com/joostn/OpenJsCad) — original JavaScript library

## License

MIT — see [LICENSE](LICENSE) for details.
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: add bilingual README"
```

---

### Task 7: Add NuGet publish CI workflow

**Files:**
- Create: `.github/workflows/publish.yml`

**Interfaces:**
- Trigger: Git tag `v*` push
- Produces: NuGet package published to nuget.org

- [ ] **Step 1: Create workflow directory**

```powershell
New-Item -ItemType Directory -Path ".github\workflows" -Force
```

- [ ] **Step 2: Write publish.yml**

```yaml
name: Publish to NuGet

on:
  push:
    tags: ['v*']

jobs:
  publish:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build -c Release --no-restore

      - name: Test
        run: dotnet test tests/OpenCSG.NET.Tests/ -c Release --no-build

      - name: Pack
        run: dotnet pack src/OpenCSG.NET/OpenCSG.NET.csproj -c Release --no-build -o out

      - name: Push to NuGet
        run: dotnet nuget push out/*.nupkg -k ${{ secrets.NUGET_API_KEY }} -s https://api.nuget.org/v3/index.json --skip-duplicate
```

- [ ] **Step 3: Commit**

```bash
git add .github/
git commit -m "ci: add NuGet publish workflow (tag trigger)"
```

---

### Task 8: Final verification

**Files:** (none — this is a verification step only)

- [ ] **Step 1: Full solution build**

```powershell
dotnet build -c Release
```
Expected: All 5 projects build with 0 errors.

- [ ] **Step 2: Run all tests**

```powershell
dotnet test -c Release
```
Expected: All tests pass, including LargeCoordinateUnionTest.

- [ ] **Step 3: Verify git status is clean**

```powershell
git status
```
Expected: All files committed, working tree clean.

- [ ] **Step 4: Review final file tree**

```powershell
Get-ChildItem -Recurse -File | ForEach-Object { $_.FullName.Replace("$pwd\", "") } | Sort-Object
```
Expected: No leftover .stl_ rejected files, no unexpected files.

- [ ] **Step 5: Commit (if any final fixes)**

```bash
git add -A
git commit -m "chore: final integration verification"
```

---

## Self-Review Checklist

- [x] Spec coverage: Each spec requirement maps to a task
  - Core library merge → Task 2
  - Test suite → Task 3
  - Sample runners → Task 4
  - Performance test → Task 5
  - README → Task 6
  - NuGet CI → Task 7
- [x] No placeholders: All steps have actual commands/code
- [x] Type consistency: Namespace `Csg`, PackageId `OpenCSG.NET`, all project references match
