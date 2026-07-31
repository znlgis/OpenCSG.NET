# OpenCSG.NET

[![NuGet](https://img.shields.io/nuget/v/OpenCSG.NET)](https://www.nuget.org/packages/OpenCSG.NET/)

**English** | [中文](#中文)

OpenCSG.NET is a Constructive Solid Geometry (CSG) library for .NET — solid primitives (Cube, Sphere, Cylinder), boolean operations (Union, Subtract, Intersect), and STL export. Zero dependencies, `netstandard2.0`, MIT licensed.

A manual C# port of [OpenJsCad](https://github.com/joostn/OpenJsCad)'s `csg.js`, merged from two forks of [praeclarum/Csg](https://github.com/praeclarum/Csg) (via the hypar-io/Csg branch chain).

## Quick Start

```
dotnet add package OpenCSG.NET
```

```csharp
using Csg;
using static Csg.Solids;

// Primitives
var cube = Cube(size: 2, center: true);
var sphere = Sphere(r: 1, center: true);
var cylinder = Cylinder(r: 0.5, h: 3, center: true);

// Boolean operations
var union = Union(cube, sphere);
var difference = cube.Subtract(sphere);
var intersection = cube.Intersect(sphere);

// Transformations
var moved = cube.Translate(x: 5, y: 0, z: 0)
                .RotateZ(45)
                .Scale(0.5);

// STL export (ASCII)
using (var fs = File.Create("output.stl"))
using (var wr = new StreamWriter(fs))
{
    union.WriteStl("union", wr);
}

// STL export (binary)
using (var fs = File.Create("output.stl"))
using (var wr = new BinaryWriter(fs))
{
    union.WriteStl("union", wr);
}
```

## Build

```bash
dotnet build OpenCSG.NET.slnx    # requires .NET 9+ SDK
dotnet test tests/OpenCSG.NET.Tests/
```

To build the core library with .NET 8 SDK:

```bash
dotnet build src/OpenCSG.NET/OpenCSG.NET.csproj -c Release
dotnet test tests/OpenCSG.NET.Tests/
```

Targets: library = `netstandard2.0`, tests/samples/perf = `net8.0`.

## Upstream

```
OpenJsCad csg.js (JavaScript)
  └── praeclarum/Csg (manual C# port)
        └── hypar-io/Csg
              ├── Csg (origin-centering Union fix, NaN validation)
              └── DotNetCsg (binary STL, iterative BSP, RotateX/Y/Z)
                    └── OpenCSG.NET (this project — merged improvements)
```

OpenCSG.NET integrates [Csg](https://github.com/znlgis/Csg) and [DotNetCsg](https://github.com/znlgis/DotNetCsg), taking the best from each: the origin-centering `Union()` fix from Csg, and binary STL output, iterative BSP tree, rotation helpers, and samples from DotNetCsg.

## License

MIT. See [LICENSE](LICENSE) for the full text.

---

## <a id="中文">中文</a>

OpenCSG.NET 是一个面向 .NET 的构造实体几何（CSG）建模库。提供基础形体（立方体、球体、圆柱体）、布尔运算（并集、差集、交集）以及 STL 文件导出。零依赖，`netstandard2.0`，MIT 授权。

本库是 [OpenJsCad](https://github.com/joostn/OpenJsCad) `csg.js` 的手工 C# 移植，合并了 [praeclarum/Csg](https://github.com/praeclarum/Csg)（经 hypar-io/Csg 分支链）两个 fork 的改进。

## 快速开始

```
dotnet add package OpenCSG.NET
```

```csharp
using Csg;
using static Csg.Solids;

// 基础形体
var cube = Cube(size: 2, center: true);
var sphere = Sphere(r: 1, center: true);
var cylinder = Cylinder(r: 0.5, h: 3, center: true);

// 布尔运算
var union = Union(cube, sphere);           // 并集
var difference = cube.Subtract(sphere);    // 差集
var intersection = cube.Intersect(sphere); // 交集

// 变换
var moved = cube.Translate(x: 5, y: 0, z: 0)
                .RotateZ(45)
                .Scale(0.5);

// STL 导出 (ASCII)
using (var fs = File.Create("output.stl"))
using (var wr = new StreamWriter(fs))
{
    union.WriteStl("union", wr);
}

// STL 导出 (二进制)
using (var fs = File.Create("output.stl"))
using (var wr = new BinaryWriter(fs))
{
    union.WriteStl("union", wr);
}
```

## 构建

```bash
dotnet build OpenCSG.NET.slnx    # 需要 .NET 9+ SDK
dotnet test tests/OpenCSG.NET.Tests/
```

使用 .NET 8 SDK 构建核心库：

```bash
dotnet build src/OpenCSG.NET/OpenCSG.NET.csproj -c Release
dotnet test tests/OpenCSG.NET.Tests/
```

目标框架：核心库 = `netstandard2.0`，测试/示例/性能 = `net8.0`。

## 上游关系

```
OpenJsCad csg.js (JavaScript)
  └── praeclarum/Csg (手工 C# 移植)
        └── hypar-io/Csg
              ├── Csg (Union 原点居中修复、NaN 校验)
              └── DotNetCsg (二进制 STL、迭代 BSP、RotateX/Y/Z)
                    └── OpenCSG.NET (本项目 — 合并两者改进)
```

OpenCSG.NET 整合了 [Csg](https://github.com/znlgis/Csg) 和 [DotNetCsg](https://github.com/znlgis/DotNetCsg)，取其各自优势：来自 Csg 的 Union 原点居中修复，来自 DotNetCsg 的二进制 STL 输出、迭代 BSP 树、旋转变换辅助方法及示例。

## 许可证

MIT。详见 [LICENSE](LICENSE)。
