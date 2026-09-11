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

## Conventions

Primitive placement and orientation (enforced by tests in `PrimitiveConventionTest`):

| Primitive | Placement | Axis |
| --- | --- | --- |
| `Cube(size)` | spans `[0, size]³` — grows from the origin along the positive axes | — |
| `Cube(size, center: true)` / `Cube(size, centerPoint)` | centered on the origin / on `centerPoint` | — |
| `Sphere(r)` | centered on the origin by default (`center: true`); `center: false` spans `[0, 2r]³` | poles on ±Z |
| `Cylinder(r, h)` / `ConeNode` | `Center` is the midpoint of the axis | height along ±Y |
| `Cylinder(CylinderOptions)` | body spans `Start` → `End` | `End − Start` |
| `ExtrudeNode` | profile in the X-Y plane, extruded from Z = 0 to Z = `Height` | along +Z |
| `WedgeNode` | `Corner` is the **center** of the base rectangle, base at Z = `Corner.Z`, ridge at Y = `Corner.Y` | width X, depth Y, height Z |

`TransformNode` translates first, then rotates about the **global origin** in X→Y→Z order
(so `T · R`, not `R · T`). Negatively-scaled (mirroring) matrices are detected from the
determinant and flip vertex winding and plane normals, so mirrored solids stay outward-facing.

Numerics: boolean ops merge vertices/planes with a fixed **absolute** tolerance of `1e-5`;
they stay correct for coordinates up to at least `1e6` (survey-coordinate regression tests in
`LargeCoordinateBooleanTest`). Boolean results may contain T-vertex seams (there is no T-vertex
cleanup pass); the surface stays free of area holes — verified by the exact volume identity
`A ∪ B = A + B − A ∩ B` — but such a mesh is not a strictly closed 2-manifold.

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

## 约定

基本体的落位与轴向（由 `PrimitiveConventionTest` 固化）：

| 基本体 | 落位 | 轴向 |
| --- | --- | --- |
| `Cube(size)` | 占位 `[0, size]³` —— 自原点沿正轴生成 | — |
| `Cube(size, center: true)` / `Cube(size, centerPoint)` | 以原点 / 以 `centerPoint` 为中心 | — |
| `Sphere(r)` | 默认居中（`center: true`）；`center: false` 占位 `[0, 2r]³` | 两极沿 ±Z |
| `Cylinder(r, h)` / `ConeNode` | `Center` 为轴线中点 | 高度沿 ±Y |
| `Cylinder(CylinderOptions)` | 实体自 `Start` 到 `End` | `End − Start` |
| `ExtrudeNode` | 截面在 X-Y 平面，自 Z = 0 拉伸到 Z = `Height` | 沿 +Z |
| `WedgeNode` | `Corner` 是底面矩形的**中心**，底面位于 Z = `Corner.Z`，屋脊在 Y = `Corner.Y` | 宽 X、深 Y、高 Z |

`TransformNode` 先平移、再绕**全局原点**按 X→Y→Z 顺序旋转（即 `T · R`，不是 `R · T`）。
含镜像的矩阵（行列式为负）会被自动识别，顶点绕序与平面法线随之翻转，镜像实体法线仍朝外。

数值口径：布尔运算的顶点/平面合并容差是固定的**绝对** 1e-5；坐标到 1e6 量级仍正确
（回归用例见 `LargeCoordinateBooleanTest`）。布尔结果可能带 T 顶点接缝（库内没有 T 顶点
清理步骤）：表面没有面积缺失 —— 由体积恒等式 `A ∪ B = A + B − A ∩ B` 精确成立佐证 ——
但这种网格不是严格封闭的 2-流形。

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
