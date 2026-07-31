# OpenCSG.NET Integration Design

**Date**: 2026-07-31
**Status**: Approved

## Overview

OpenCSG.NET 整合两个 CSG 库（Csg 和 DotNetCsg，同源自 praeclarum/Csg → hypar-io/Csg 分支链），取其各自改进，形成统一的新项目。目标：零依赖的 netstandard2.0 核心库 + 现代化测试/示例/CI。

## Source Projects

| Project | Path | NuGet | License | Last Commit | Key Differences |
|---------|------|-------|---------|-------------|-----------------|
| Csg | `D:\self\code\csg\Csg` | `SolidGeometry` v1.0.0 | MIT | 2026-05-26 | Union origin-centering fix, NaN validation, Xamarin apps |
| DotNetCsg | `D:\self\code\csg\DotNetCsg` | `DotNetCsg` v1.0.1 | MIT | 2023-06-03 | Binary STL, iterative BSP, RotateX/Y/Z, Runner apps, doc comments |

Both trace back to [praeclarum/Csg](https://github.com/praeclarum/Csg) (a manual C# port of OpenJsCad's csg.js).

## Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| License | MIT | Consistent with source projects; permissive for library distribution |
| NuGet Package ID | `OpenCSG.NET` | New brand matching repo name; independent identity |
| Target framework (lib) | `netstandard2.0` | Maximum compatibility (.NET Framework 4.6.1+ through modern .NET) |
| Target framework (tests/samples) | `net8.0` | Modern tooling, LTS |
| CI trigger | Git tag `v*` | Standard NuGet publish model; one tag = one release |
| README language | Chinese + English bilingual | OS locale + international audience |

## Project Structure

```
OpenCSG.NET/
├── OpenCSG.NET.sln
├── README.md                     # Bilingual (zh-CN + en)
├── LICENSE                       # MIT
├── .gitignore
├── .editorconfig                 # From Csg
├── .github/workflows/
│   └── publish.yml               # Tag-triggered NuGet publish
│
├── src/
│   └── OpenCSG.NET/
│       ├── OpenCSG.NET.csproj
│       ├── Solid.cs              # DotNetCsg base + Csg Union centering fix
│       ├── Tree.cs               # DotNetCsg (iterative BSP)
│       ├── Vector.cs             # Merge Csg NaN guards + DotNetCsg additions
│       ├── Solids.cs             # DotNetCsg (includes RotateX/Y/Z)
│       ├── Plane.cs              # Merge Csg plane validation + DotNetCsg
│       ├── Polygon.cs            # DotNetCsg
│       ├── Vertex.cs             # DotNetCsg
│       └── Formats.cs            # DotNetCsg (includes binary STL writer)
│
├── tests/
│   └── OpenCSG.NET.Tests/
│       ├── OpenCSG.NET.Tests.csproj
│       ├── SolidTest.cs
│       ├── CubeTest.cs
│       ├── SphereTest.cs
│       ├── CylinderTest.cs
│       ├── UnionTest.cs
│       ├── SubtractTest.cs
│       ├── IntersectTest.cs
│       ├── LargeCoordinateUnionTest.cs  # From Csg
│       ├── ExamplesTest.cs
│       └── Results/*.stl              # Golden-file baselines
│
├── samples/
│   ├── Runner.Examples/
│   │   ├── Runner.Examples.csproj
│   │   └── Program.cs
│   └── Runner.CPurlin/
│       ├── Runner.CPurlin.csproj
│       └── Program.cs
│
└── perf/
    └── OpenCSG.NET.PerfTest/
        ├── OpenCSG.NET.PerfTest.csproj
        ├── Program.cs
        └── PerfTest.cs

Excluded (obsolete / platform-specific):
  - Csg.Viewer.Mac (Xamarin.Mac, deprecated)
  - Csg.PerfTest.iOS (Xamarin.iOS, deprecated)
  - Csg.PerfTest.iOS (ditto)
```

## File Merge Strategy

### Solid.cs (core CSG logic)
- **Baseline**: DotNetCsg (992 lines, iterative BSP, RotateX/Y/Z, MayOverlap optimization)
- **Merge from Csg**: `UnionSubLocal` — center geometry at origin before BSP union, then translate back. This preserves floating-point precision for large-coordinate solids (Csg PR #2 from hypar-io).
- **Verification**: LargeCoordinateUnionTest from Csg must pass.

### Tree.cs (BSP tree)
- **Baseline**: DotNetCsg (iterative Invert/ClipPolygons using Queue/Stack, no recursion).
- **No Csg merge needed**: DotNetCsg version is strictly superior (avoids stack overflow).

### Solids.cs (primitive factories)
- **Baseline**: DotNetCsg (includes RotateX/Y/Z convenience methods).
- **No Csg merge needed**: DotNetCsg has richer API.

### Vector.cs (math primitives)
- **Baseline**: DotNetCsg.
- **Merge from Csg**: NaN validation in Vector3D constructor (`ArgumentOutOfRangeException`), plus any additional checks from Csg PR #1.

### Plane.cs (plane splitting)
- **Baseline**: DotNetCsg.
- **Merge from Csg**: Valid plane detection (`ensure we find a valid plane when making from points`), any robustness fixes.

### Formats.cs (STL export)
- **Baseline**: DotNetCsg (includes binary STL writer).
- **No Csg merge needed**: Csg only has text STL.

### Polygon.cs, Vertex.cs
- **Baseline**: DotNetCsg. Identical enough to need no merge.

## Test Strategy

- Baseline: DotNetCsg's golden-file STL comparison tests (27 STL files in `Results/`).
- Add: Csg's `LargeCoordinateUnionTest` verifying that union with large offset coordinates produces same polygon count as origin.
- Framework: NUnit 3.x on net8.0.
- Test runner: `dotnet test`.

## NuGet Publish CI (`publish.yml`)

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
      - run: dotnet build -c Release
      - run: dotnet test -c Release --no-build
      - run: dotnet pack src/OpenCSG.NET/OpenCSG.NET.csproj -c Release --no-build -o out
      - run: dotnet nuget push out/*.nupkg -k ${{ secrets.NUGET_API_KEY }} -s https://api.nuget.org/v3/index.json
```

Version is parsed from the Git tag (e.g., `v1.0.0` → package version `1.0.0`).

## Namespace

Keep namespace `Csg` for API compatibility with existing users migrating from either source project. The assembly/project name is `OpenCSG.NET` but the `using` stays `Csg`.

## README Outline (bilingual)

1. Project title + badges (NuGet, build)
2. English section:
   - What is OpenCSG.NET
   - Origin (derived from praeclarum/Csg, hypar-io/Csg, talanc/DotNetCsg)
   - Features
   - Quick start code snippet
   - API reference
3. Chinese section (等同上)
4. License
5. Acknowledgments

## Scope Boundaries

**In scope**:
- Core library merge and cleanup
- Test suite integration
- Sample runners
- Performance test
- NuGet publish CI
- Bilingual README

**Out of scope**:
- New features beyond existing code
- API redesign or breaking changes
- Xamarin platform support (obsolete)
- Documentation beyond README
- Benchmark regression testing in CI
