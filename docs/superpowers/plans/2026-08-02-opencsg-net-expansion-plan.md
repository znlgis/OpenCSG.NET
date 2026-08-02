# OpenCSG.NET 扩展实施计划

> **For agentic workers:** 使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 按任务逐步实施。步骤使用 checkbox (`- [ ]`) 语法跟踪进度。

**目标:** 为 OpenCSG.NET 新增 CsgNode 声明式树、Extrude/Wedge 图元、7 种参数化截面、JSON 配方序列化——零破坏现有 API。

**架构:** 路线 3（纯数据 CsgNode 树 + 独立求值器）。4 个新文件（CsgNode.cs / Profiles.cs / CsgEvaluator.cs / CsgSerialization.cs），全部使用 C# record 类型 + `namespace Csg`。求值器通过 `Solid.FromPolygons()` 将节点树转为现有 Solid 实体。

**技术栈:** C# 8.0, netstandard2.0, System.Text.Json 8.0.5, NUnit 3.13.3, 零其他依赖。

## 全局约束

- `namespace Csg`，与现有代码一致
- `LangVersion 8.0`，`Nullable enable`，`TreatWarningsAsErrors=true`
- 所有新类型使用 C# `record`（不可变、值相等）
- `Solid.FromPolygons(List<Polygon>)` 构造新几何体
- `Polygon(List<Vertex> vertices)` 构造多边形，Plane 自动计算
- `new Vertex(Vector3D pos, new Vector2D(0, 0))` 创建无纹理顶点
- 现有文件零修改（除非 .csproj 加依赖）
- 求值失败抛 `CsgEvaluationException`，不静默返回空 Solid
- 布尔运算二元（非可变参数），多级嵌套表达
- Rotation 使用欧拉角 `(rotX, rotY, rotZ)` 单位度

---

### Task 1: 添加 System.Text.Json 依赖 + 验证构建

**文件:**
- 修改: `src/OpenCSG.NET/OpenCSG.NET.csproj`

**接口:**
- 产出: 项目可引用 `System.Text.Json` 进行多态序列化

- [ ] **Step 1: 添加 NuGet 包引用**

在 `</PropertyGroup>` 之后、`<ItemGroup>` 之前插入：

```xml
  <ItemGroup>
    <PackageReference Include="System.Text.Json" Version="8.0.5" />
  </ItemGroup>
```

- [ ] **Step 2: 还原 + 构建验证**

运行: `dotnet restore src/OpenCSG.NET/OpenCSG.NET.csproj`
运行: `dotnet build src/OpenCSG.NET/OpenCSG.NET.csproj --no-restore`
预期: 构建成功，无警告无错误

- [ ] **Step 3: 确认现有测试仍然通过**

运行: `dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj`
预期: 所有 27 个现有测试 PASS

- [ ] **Step 4: Commit**

```bash
git add src/OpenCSG.NET/OpenCSG.NET.csproj
git commit -m "build: add System.Text.Json 8.0.5 dependency"
```

---

### Task 2: CsgNode.cs —— 定义所有节点 record 类型

**文件:**
- 创建: `src/OpenCSG.NET/CsgNode.cs`

**接口:**
- 产出: 10 个 CsgNode record 子类型 + CsgEvaluationException

- [ ] **Step 1: 创建文件，定义基类和所有节点类型**

```csharp
using System;
using System.Collections.Generic;

namespace Csg
{
    /// <summary>CSG 语法树节点基类。所有节点类型均继承此抽象 record。</summary>
    public abstract record CsgNode;

    // ---- 基本图元（叶节点） ----

    /// <param name="Center">长方体中心点</param>
    /// <param name="Size">长宽高（全尺寸，非半尺寸）</param>
    public record BoxNode(Vector3D Center, Vector3D Size) : CsgNode;

    /// <param name="Center">球心</param>
    /// <param name="Radius">半径</param>
    /// <param name="Resolution">经纬分段数，默认 12</param>
    public record SphereNode(Vector3D Center, double Radius, int Resolution = 12) : CsgNode;

    /// <param name="Center">圆柱底面中心</param>
    /// <param name="Radius">半径</param>
    /// <param name="Height">高度（沿 Z 轴）</param>
    /// <param name="Resolution">圆周分段数，默认 32</param>
    public record CylinderNode(Vector3D Center, double Radius, double Height, int Resolution = 32) : CsgNode;

    /// <param name="Center">锥台底面中心</param>
    /// <param name="RadiusBottom">底面半径</param>
    /// <param name="RadiusTop">顶面半径（可为 0 = 圆锥）</param>
    /// <param name="Height">高度（沿 Z 轴）</param>
    /// <param name="Resolution">圆周分段数，默认 32</param>
    public record ConeNode(Vector3D Center, double RadiusBottom, double RadiusTop, double Height, int Resolution = 32) : CsgNode;

    /// <param name="Profile">2D 截面形状</param>
    /// <param name="Height">拉伸高度（沿 Z 轴）</param>
    /// <param name="Center">截面所在平面中心（拉伸从 Center 向 +Z 方向）</param>
    public record ExtrudeNode(Profile2D Profile, double Height, Vector3D Center) : CsgNode;

    /// <param name="Center">楔形底面矩形中心</param>
    /// <param name="Size">(沿X长, 沿Y宽, 沿Z高)。楔形顶面退化为 Y 方向的一条边</param>
    public record WedgeNode(Vector3D Center, Vector3D Size) : CsgNode;

    // ---- 布尔运算（组合节点） ----

    public record UnionNode(CsgNode Left, CsgNode Right) : CsgNode;
    public record SubtractNode(CsgNode Left, CsgNode Right) : CsgNode;
    public record IntersectNode(CsgNode Left, CsgNode Right) : CsgNode;

    // ---- 变换包装 ----

    /// <param name="Child">被变换的子节点</param>
    /// <param name="Position">平移量 (x, y, z)</param>
    /// <param name="Rotation">旋转欧拉角 (rotX, rotY, rotZ)，单位度</param>
    public record TransformNode(CsgNode Child, Vector3D Position, Vector3D Rotation) : CsgNode;

    // ---- 异常类型 ----

    /// <summary>CsgNode 求值失败时抛出。</summary>
    public class CsgEvaluationException : Exception
    {
        public CsgNode FailedNode { get; }
        public CsgEvaluationException(string message, CsgNode node) : base(message)
            => FailedNode = node;
    }
}
```

- [ ] **Step 2: 构建验证**

运行: `dotnet build src/OpenCSG.NET/OpenCSG.NET.csproj --no-restore`
预期: 构建成功

- [ ] **Step 3: Commit**

```bash
git add src/OpenCSG.NET/CsgNode.cs
git commit -m "feat: add CsgNode record types and CsgEvaluationException"
```

---

### Task 3: Profiles.cs —— 参数化截面系统

**文件:**
- 创建: `src/OpenCSG.NET/Profiles.cs`

**接口:**
- 消耗: `Vector3D`, `Vector2D`（现有类型）
- 产出: `Profile2D` 抽象 record、7 个截面 record、`Profiles` 静态工厂

- [ ] **Step 1: 创建文件**

```csharp
namespace Csg
{
    /// <summary>2D 截面形状基类。每种截面存储参数配方，求值时展开为多边形顶点。</summary>
    public abstract record Profile2D;

    /// <param name="Width">X 方向宽度</param>
    /// <param name="Height">Y 方向高度</param>
    public record RectangleProfile(double Width, double Height) : Profile2D;

    /// <summary>H型钢截面（工字形），web 为竖直腹板，flange 为水平翼缘。</summary>
    /// <param name="WebHeight">腹板高度（总高减两倍翼缘厚）</param>
    /// <param name="FlangeWidth">翼缘宽度</param>
    /// <param name="WebThickness">腹板厚度</param>
    /// <param name="FlangeThickness">翼缘厚度</param>
    public record HBeamProfile(double WebHeight, double FlangeWidth,
                                double WebThickness, double FlangeThickness) : Profile2D;

    /// <summary>槽钢截面（C形），web 为竖直腹板，flange 为上下水平翼缘。</summary>
    /// <param name="WebHeight">腹板高度（总高减两倍翼缘厚）</param>
    /// <param name="FlangeWidth">翼缘宽度</param>
    /// <param name="WebThickness">腹板厚度</param>
    /// <param name="FlangeThickness">翼缘厚度</param>
    public record ChannelProfile(double WebHeight, double FlangeWidth,
                                  double WebThickness, double FlangeThickness) : Profile2D;

    /// <summary>方管截面（空心矩形）。</summary>
    /// <param name="Width">X 方向外宽</param>
    /// <param name="Height">Y 方向外高</param>
    /// <param name="Thickness">壁厚</param>
    public record SquareTubeProfile(double Width, double Height, double Thickness) : Profile2D;

    /// <summary>等腰梯形截面，顶边和底边平行于 X 轴。</summary>
    /// <param name="TopWidth">顶边宽度</param>
    /// <param name="BottomWidth">底边宽度</param>
    /// <param name="Height">Y 方向高度</param>
    public record TrapezoidProfile(double TopWidth, double BottomWidth, double Height) : Profile2D;

    /// <summary>胶囊形截面（矩形 + 两端半圆），长轴沿 X 方向。</summary>
    /// <param name="RectWidth">矩形部分宽度</param>
    /// <param name="Radius">两端半圆半径（也等于半高）</param>
    public record CapsuleProfile(double RectWidth, double Radius) : Profile2D;

    /// <summary>L形截面。竖边沿 Y 轴，水平边沿 X 轴。</summary>
    /// <param name="Vertical">竖边高度（Y 方向）</param>
    /// <param name="Horizontal">水平边宽度（X 方向）</param>
    /// <param name="Thickness">壁厚</param>
    public record LShapeProfile(double Vertical, double Horizontal, double Thickness) : Profile2D;

    /// <summary>参数化截面便捷工厂。</summary>
    public static class Profiles
    {
        public static RectangleProfile Rectangle(double width, double height)
            => new RectangleProfile(width, height);

        public static HBeamProfile HBeam(double webHeight, double flangeWidth,
                                          double webThickness, double flangeThickness)
            => new HBeamProfile(webHeight, flangeWidth, webThickness, flangeThickness);

        public static ChannelProfile Channel(double webHeight, double flangeWidth,
                                              double webThickness, double flangeThickness)
            => new ChannelProfile(webHeight, flangeWidth, webThickness, flangeThickness);

        public static SquareTubeProfile SquareTube(double width, double height, double thickness)
            => new SquareTubeProfile(width, height, thickness);

        public static TrapezoidProfile Trapezoid(double topWidth, double bottomWidth, double height)
            => new TrapezoidProfile(topWidth, bottomWidth, height);

        public static CapsuleProfile Capsule(double rectWidth, double radius)
            => new CapsuleProfile(rectWidth, radius);

        public static LShapeProfile LShape(double vertical, double horizontal, double thickness)
            => new LShapeProfile(vertical, horizontal, thickness);
    }
}
```

- [ ] **Step 2: 构建验证**

运行: `dotnet build src/OpenCSG.NET/OpenCSG.NET.csproj --no-restore`
预期: 构建成功

- [ ] **Step 3: Commit**

```bash
git add src/OpenCSG.NET/Profiles.cs
git commit -m "feat: add Profile2D types and Profiles static factory"
```

---

### Task 4: CsgEvaluator.cs —— 基础求值（基本图元 + 布尔运算 + 变换）

**文件:**
- 创建: `src/OpenCSG.NET/CsgEvaluator.cs`

**接口:**
- 消耗: `CsgNode`, `Solids`（Cube/Sphere/Cylinder）, `Solid.Union/Subtract/Intersect`
- 产出: `Evaluate(CsgNode) → Solid`，支持 Box/Sphere/Cylinder/Cone + 布尔 + Transform

- [ ] **Step 1: 创建文件，实现基础求值**

```csharp
using System;
using System.Collections.Generic;

namespace Csg
{
    public static class CsgEvaluator
    {
        static Vertex V(double x, double y, double z)
            => new Vertex(new Vector3D(x, y, z), new Vector2D(0, 0));

        /// <summary>将 CsgNode 树递归求值为 Solid 实体。</summary>
        /// <exception cref="CsgEvaluationException">求值失败时抛出。</exception>
        public static Solid Evaluate(CsgNode node)
        {
            return node switch
            {
                BoxNode n          => EvaluateBox(n),
                SphereNode n       => EvaluateSphere(n),
                CylinderNode n     => EvaluateCylinder(n),
                ConeNode n         => EvaluateCone(n),
                ExtrudeNode n      => EvaluateExtrude(n),
                WedgeNode n        => EvaluateWedge(n),
                UnionNode n        => Evaluate(n.Left).Union(Evaluate(n.Right)),
                SubtractNode n     => Evaluate(n.Left).Subtract(Evaluate(n.Right)),
                IntersectNode n    => Evaluate(n.Left).Intersect(Evaluate(n.Right)),
                TransformNode n    => EvaluateTransformed(n),
                _                  => throw new CsgEvaluationException(
                    $"Unknown CsgNode type: {node.GetType().Name}", node)
            };
        }

        /// <summary>批量求值，每个节点独立。</summary>
        public static IReadOnlyList<Solid> EvaluateAll(IEnumerable<CsgNode> nodes)
        {
            var results = new List<Solid>();
            foreach (var n in nodes)
                results.Add(Evaluate(n));
            return results;
        }

        static Solid EvaluateBox(BoxNode n)
            => Solids.Cube(n.Size, n.Center);

        static Solid EvaluateSphere(SphereNode n)
            => Solids.Sphere(n.Radius, n.Center, new SphereOptions { Resolution = n.Resolution });

        static Solid EvaluateCylinder(CylinderNode n)
            => Solids.Cylinder(n.Radius, n.Height, n.Center,
                new CylinderOptions { Resolution = n.Resolution });

        static Solid EvaluateCone(ConeNode n)
            => Solids.Cylinder(n.RadiusBottom, n.RadiusTop, n.Height, n.Center,
                new CylinderOptions { Resolution = n.Resolution });

        static Solid EvaluateTransformed(TransformNode n)
        {
            var child = Evaluate(n.Child);
            var result = child.Translate(n.Position);
            if (n.Rotation.X != 0) result = result.RotateX(n.Rotation.X);
            if (n.Rotation.Y != 0) result = result.RotateY(n.Rotation.Y);
            if (n.Rotation.Z != 0) result = result.RotateZ(n.Rotation.Z);
            return result;
        }

        // Extrude / Wedge 占位——Task 5 和 Task 6 实现
        static Solid EvaluateExtrude(ExtrudeNode n)
            => throw new NotImplementedException("Extrude evaluation in next task");

        static Solid EvaluateWedge(WedgeNode n)
            => throw new NotImplementedException("Wedge evaluation in next task");
    }
}
```

- [ ] **Step 2: 构建验证**

运行: `dotnet build src/OpenCSG.NET/OpenCSG.NET.csproj --no-restore`
预期: 构建成功

- [ ] **Step 3: 编写并运行求值器基础测试**

创建 `tests/OpenCSG.NET.Tests/CsgEvaluatorTest.cs`:

```csharp
using Csg;
using NUnit.Framework;

namespace OpenCSG.NET.Tests
{
    [TestFixture]
    public class CsgEvaluatorTest : SolidTest
    {
        [Test]
        public void Box()
        {
            var node = new BoxNode(new Vector3D(0, 0, 0), new Vector3D(2, 2, 2));
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "CsgEvaluator_Box");
        }

        [Test]
        public void Sphere()
        {
            var node = new SphereNode(new Vector3D(0, 0, 0), 1, 12);
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "CsgEvaluator_Sphere");
        }

        [Test]
        public void Cylinder()
        {
            var node = new CylinderNode(new Vector3D(0, 0, 0), 1, 2, 32);
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "CsgEvaluator_Cylinder");
        }

        [Test]
        public void Union_TwoBoxes()
        {
            var left = new BoxNode(new Vector3D(0, 0, 0), new Vector3D(2, 2, 2));
            var right = new BoxNode(new Vector3D(1, 1, 1), new Vector3D(2, 2, 2));
            var node = new UnionNode(left, right);
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "CsgEvaluator_UnionTwoBoxes");
        }

        [Test]
        public void Subtract_BoxMinusSphere()
        {
            var box = new BoxNode(new Vector3D(0, 0, 0), new Vector3D(2, 2, 2));
            var sphere = new SphereNode(new Vector3D(1, 1, 1), 0.6, 12);
            var node = new SubtractNode(box, sphere);
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "CsgEvaluator_SubtractBoxSphere");
        }

        [Test]
        public void Intersect_TwoBoxes()
        {
            var left = new BoxNode(new Vector3D(0, 0, 0), new Vector3D(2, 2, 2));
            var right = new BoxNode(new Vector3D(0.5, 0.5, 0.5), new Vector3D(2, 2, 2));
            var node = new IntersectNode(left, right);
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "CsgEvaluator_IntersectTwoBoxes");
        }

        [Test]
        public void Transform_Translate()
        {
            var node = new TransformNode(
                new BoxNode(Vector3D.Zero, new Vector3D(2, 2, 2)),
                new Vector3D(5, 0, 0),
                Vector3D.Zero);
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "CsgEvaluator_TransformTranslate");
        }

        [Test]
        public void EvaluateAll_MultipleIndependent()
        {
            var nodes = new CsgNode[]
            {
                new BoxNode(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1)),
                new BoxNode(new Vector3D(2, 0, 0), new Vector3D(1, 1, 1)),
            };
            var solids = CsgEvaluator.EvaluateAll(nodes);
            Assert.That(solids.Count, Is.EqualTo(2));
        }
    }
}
```

运行: `dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj --filter "FullyQualifiedName~CsgEvaluatorTest"`
预期: Box/Sphere/Cylinder/布尔/Transform 测试首次运行时生成黄金 STL 文件（`*.stl_` 待验收），EvaluateAll 测试 PASS

- [ ] **Step 4: 验收黄金 STL 文件**

检查 `tests/OpenCSG.NET.Tests/Results/` 下新生成的 `*.stl_` 文件。确认无误后重命名为 `.stl`（去掉 `_` 后缀）：

```bash
# 验收通过后
Get-ChildItem tests/OpenCSG.NET.Tests/Results/CsgEvaluator_*.stl_ | ForEach-Object {
    $newName = $_.FullName -replace '\.stl_$', '.stl'
    Rename-Item $_.FullName $newName
}
```

- [ ] **Step 5: Commit**

```bash
git add src/OpenCSG.NET/CsgEvaluator.cs tests/OpenCSG.NET.Tests/CsgEvaluatorTest.cs tests/OpenCSG.NET.Tests/Results/CsgEvaluator_*.stl
git commit -m "feat: add CsgEvaluator basic evaluation (Box/Sphere/Cylinder/Cone/Boolean/Transform)"
```

---

### Task 5: CsgEvaluator.cs —— Extrude 求值（截面展开 + 三角化 + 拉伸）

**文件:**
- 修改: `src/OpenCSG.NET/CsgEvaluator.cs`（替换 Extrude / Wedge 占位，追加截面展开方法）

**接口:**
- 消耗: `Profile2D`（Task 3）, `Solid.FromPolygons()`, `Polygon(List<Vertex>)`
- 产出: `EvaluateExtrude(ExtrudeNode) → Solid`

- [ ] **Step 1: 实现截面顶点展开**

在 `CsgEvaluator` 类中追加（替换占位的 `EvaluateExtrude`）：

```csharp
static List<Vector2D> ExpandProfile(Profile2D profile)
{
    switch (profile)
    {
        case RectangleProfile p:
            var hw = p.Width / 2;
            var hh = p.Height / 2;
            return new List<Vector2D> {
                new Vector2D(-hw, -hh), new Vector2D( hw, -hh),
                new Vector2D( hw,  hh), new Vector2D(-hw,  hh)
            };

        case HBeamProfile p:
        {
            var hw = p.FlangeWidth / 2;
            var hh = (p.WebHeight + 2 * p.FlangeThickness) / 2;
            var iw = p.WebThickness / 2;
            var ih = p.WebHeight / 2;
            return new List<Vector2D> {
                new Vector2D(-hw, -hh), new Vector2D( hw, -hh),
                new Vector2D( hw, -ih), new Vector2D( iw, -ih),
                new Vector2D( iw,  ih), new Vector2D( hw,  ih),
                new Vector2D( hw,  hh), new Vector2D(-hw,  hh),
                new Vector2D(-hw,  ih), new Vector2D(-iw,  ih),
                new Vector2D(-iw, -ih), new Vector2D(-hw, -ih)
            };
        }

        case ChannelProfile p:
        {
            var hw = p.FlangeWidth;
            var hh = (p.WebHeight + 2 * p.FlangeThickness) / 2;
            var ft = p.FlangeThickness;
            var wt = p.WebThickness;
            // 开口朝 +X 方向
            return new List<Vector2D> {
                new Vector2D(0,    -hh),       new Vector2D(hw, -hh),
                new Vector2D(hw,   -hh + ft),  new Vector2D(wt, -hh + ft),
                new Vector2D(wt,   -hh + ft + p.WebHeight),
                new Vector2D(hw,   -hh + ft + p.WebHeight),
                new Vector2D(hw,    hh),       new Vector2D(0,   hh)
            };
        }

        case SquareTubeProfile p:
        {
            // 仅返回外轮廓。空心效果通过 CsgNode 层用两个 Extrude + SubtractNode 实现：
            // Subtract(Extrude(外矩形), Extrude(内矩形))
            var hw = p.Width / 2;
            var hh = p.Height / 2;
            return new List<Vector2D> {
                new Vector2D(-hw, -hh), new Vector2D( hw, -hh),
                new Vector2D( hw,  hh), new Vector2D(-hw,  hh)
            };
        }

        case TrapezoidProfile p:
        {
            var hwTop = p.TopWidth / 2;
            var hwBot = p.BottomWidth / 2;
            var h = p.Height;
            return new List<Vector2D> {
                new Vector2D(-hwBot, 0), new Vector2D( hwBot, 0),
                new Vector2D( hwTop, h), new Vector2D(-hwTop, h)
            };
        }

        case CapsuleProfile p:
        {
            // 多段线近似半圆 + 矩形边
            var pts = new List<Vector2D>();
            int segs = 16;
            double r = p.Radius;
            double halfW = p.RectWidth / 2;
            // 右半圆 (从 -90° 到 +90°)
            for (int i = 0; i <= segs; i++)
            {
                double angle = -Math.PI / 2 + Math.PI * i / segs;
                pts.Add(new Vector2D(halfW + r * Math.Cos(angle), r * Math.Sin(angle)));
            }
            // 左半圆 (从 +90° 到 +270°)
            for (int i = 0; i <= segs; i++)
            {
                double angle = Math.PI / 2 + Math.PI * i / segs;
                pts.Add(new Vector2D(-halfW + r * Math.Cos(angle), r * Math.Sin(angle)));
            }
            return pts;
        }

        case LShapeProfile p:
        {
            return new List<Vector2D> {
                new Vector2D(0, 0), new Vector2D(p.Horizontal, 0),
                new Vector2D(p.Horizontal, p.Thickness),
                new Vector2D(p.Thickness, p.Thickness),
                new Vector2D(p.Thickness, p.Vertical),
                new Vector2D(0, p.Vertical)
            };
        }

        default:
            throw new InvalidOperationException(
                $"Unknown Profile2D type: {profile.GetType().Name}");
    }
}
```

- [ ] **Step 2: 实现 Ear Clipping 三角化**

在 `CsgEvaluator` 类中追加：

```csharp
/// <summary>对简单多边形进行 ear-clipping 三角化。顶点按逆时针排列，返回三角形索引三元组。</summary>
static List<(int, int, int)> Triangulate(List<Vector2D> polygon)
{
    var indices = new List<int>();
    for (int i = 0; i < polygon.Count; i++) indices.Add(i);
    var tris = new List<(int, int, int)>();
    int safety = polygon.Count * 3;

    while (indices.Count > 3 && safety-- > 0)
    {
        bool earFound = false;
        for (int i = 0; i < indices.Count; i++)
        {
            int prev = indices[(i - 1 + indices.Count) % indices.Count];
            int curr = indices[i];
            int next = indices[(i + 1) % indices.Count];

            if (IsConvex(polygon[prev], polygon[curr], polygon[next]) &&
                !HasPointInside(polygon, indices, prev, curr, next))
            {
                tris.Add((prev, curr, next));
                indices.RemoveAt(i);
                earFound = true;
                break;
            }
        }
        if (!earFound) break;
    }
    // 最后三个顶点构成三角形
    if (indices.Count == 3)
        tris.Add((indices[0], indices[1], indices[2]));

    return tris;
}

static bool IsConvex(Vector2D a, Vector2D b, Vector2D c)
    => Cross2D(b - a, c - b) >= 0;

static double Cross2D(Vector2D a, Vector2D b) => a.X * b.Y - a.Y * b.X;

static bool HasPointInside(List<Vector2D> poly, List<int> indices, int prev, int curr, int next)
{
    var a = poly[prev];
    var b = poly[curr];
    var c = poly[next];
    foreach (var i in indices)
    {
        if (i == prev || i == curr || i == next) continue;
        if (PointInTriangle(poly[i], a, b, c)) return true;
    }
    return false;
}

static bool PointInTriangle(Vector2D p, Vector2D a, Vector2D b, Vector2D c)
{
    double d1 = Sign2D(p, a, b);
    double d2 = Sign2D(p, b, c);
    double d3 = Sign2D(p, c, a);
    bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
    bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
    return !(hasNeg && hasPos);
}

static double Sign2D(Vector2D p1, Vector2D p2, Vector2D p3)
    => (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y);
```

- [ ] **Step 3: 实现 Extrude 求值主逻辑**

替换占位的 `EvaluateExtrude` 为：

```csharp
static Solid EvaluateExtrude(ExtrudeNode n)
{
    var pts2D = ExpandProfile(n.Profile);

    // 处理带孔截面（如方管）：外轮廓在前，孔洞在后
    // 简化处理：只取第一个闭合环作为外轮廓，孔洞忽略（上层用 SubtractNode 开孔）
    // 对单个多边形做三角化
    var tris = Triangulate(pts2D);
    if (tris.Count == 0)
        throw new CsgEvaluationException("Extrude: triangulation produced no triangles", n);

    var polygons = new List<Polygon>();
    double zBottom = 0;
    double zTop = n.Height;

    // 底面三角形（法线朝 -Z）
    foreach (var (i0, i1, i2) in tris)
    {
        var v0 = V(pts2D[i0].X, pts2D[i0].Y, zBottom);
        var v1 = V(pts2D[i1].X, pts2D[i1].Y, zBottom);
        var v2 = V(pts2D[i2].X, pts2D[i2].Y, zBottom);
        polygons.Add(new Polygon(new List<Vertex> { v2, v1, v0 }));
    }

    // 顶面三角形（法线朝 +Z）
    foreach (var (i0, i1, i2) in tris)
    {
        var v0 = V(pts2D[i0].X, pts2D[i0].Y, zTop);
        var v1 = V(pts2D[i1].X, pts2D[i1].Y, zTop);
        var v2 = V(pts2D[i2].X, pts2D[i2].Y, zTop);
        polygons.Add(new Polygon(new List<Vertex> { v0, v1, v2 }));
    }

    // 侧面（沿外轮廓每条边生成两个三角形）
    int count = pts2D.Count;
    for (int i = 0; i < count; i++)
    {
        int j = (i + 1) % count;
        var b0 = V(pts2D[i].X, pts2D[i].Y, zBottom);
        var b1 = V(pts2D[j].X, pts2D[j].Y, zBottom);
        var t0 = V(pts2D[i].X, pts2D[i].Y, zTop);
        var t1 = V(pts2D[j].X, pts2D[j].Y, zTop);
        polygons.Add(new Polygon(new List<Vertex> { b0, b1, t1, t0 }));
    }

    var solid = Solid.FromPolygons(polygons);
    // 平移至 Center
    if (n.Center.X != 0 || n.Center.Y != 0 || n.Center.Z != 0)
        solid = solid.Translate(n.Center);
    return solid;
}
```

- [ ] **Step 4: 构建验证**

运行: `dotnet build src/OpenCSG.NET/OpenCSG.NET.csproj --no-restore`
预期: 构建成功

- [ ] **Step 5: 编写 Extrude 测试**

创建 `tests/OpenCSG.NET.Tests/ExtrudeTest.cs`:

```csharp
using Csg;
using NUnit.Framework;

namespace OpenCSG.NET.Tests
{
    [TestFixture]
    public class ExtrudeTest : SolidTest
    {
        [Test]
        public void Rectangle_Simple()
        {
            var node = new ExtrudeNode(
                Profiles.Rectangle(4, 2), 1, new Vector3D(0, 0, 0));
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "Extrude_Rectangle");
        }

        [Test]
        public void Trapezoid()
        {
            var node = new ExtrudeNode(
                Profiles.Trapezoid(2, 3, 2), 1, new Vector3D(0, 0, 0));
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "Extrude_Trapezoid");
        }

        [Test]
        public void LShape()
        {
            var node = new ExtrudeNode(
                Profiles.LShape(3, 2, 0.5), 1, new Vector3D(0, 0, 0));
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "Extrude_LShape");
        }

        [Test]
        public void Capsule()
        {
            var node = new ExtrudeNode(
                Profiles.Capsule(3, 1), 1, new Vector3D(0, 0, 0));
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "Extrude_Capsule");
        }

        [Test]
        public void HBeam()
        {
            var node = new ExtrudeNode(
                Profiles.HBeam(100, 80, 10, 12), 50, new Vector3D(0, 0, 0));
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "Extrude_HBeam");
        }

        [Test]
        public void Channel()
        {
            var node = new ExtrudeNode(
                Profiles.Channel(100, 60, 8, 10), 50, new Vector3D(0, 0, 0));
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "Extrude_Channel");
        }

        [Test]
        public void SquareTube()
        {
            var node = new ExtrudeNode(
                Profiles.SquareTube(80, 80, 5), 50, new Vector3D(0, 0, 0));
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "Extrude_SquareTube");
        }

        [Test]
        public void WithCenterOffset()
        {
            var node = new ExtrudeNode(
                Profiles.Rectangle(2, 2), 3, new Vector3D(10, 20, 5));
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "Extrude_CenterOffset");
        }
    }
}
```

运行: `dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj --filter "FullyQualifiedName~ExtrudeTest"`
预期: 首次运行生成黄金 STL `*.stl_` 文件

- [ ] **Step 6: 验收黄金 STL + Commit**

验收 Extrude 相关的 `*.stl_` 文件，重命名为 `.stl`。

```bash
git add src/OpenCSG.NET/CsgEvaluator.cs tests/OpenCSG.NET.Tests/ExtrudeTest.cs tests/OpenCSG.NET.Tests/Results/Extrude_*.stl
git commit -m "feat: add Extrude evaluation with profile expansion and ear-clipping triangulation"
```

---

### Task 6: CsgEvaluator.cs —— Wedge 求值

**文件:**
- 修改: `src/OpenCSG.NET/CsgEvaluator.cs`（替换 Wedge 占位）

**接口:**
- 消耗: `Solid.FromPolygons()`
- 产出: `EvaluateWedge(WedgeNode) → Solid`

- [ ] **Step 1: 实现 Wedge 求值**

替换占位的 `EvaluateWedge` 为：

```csharp
static Solid EvaluateWedge(WedgeNode n)
{
    double hx = n.Size.X / 2;  // X 半长
    double hy = n.Size.Y / 2;  // Y 半宽
    double h = n.Size.Z;       // Z 高度

    // 底面矩形 (z=0):  (-hx,-hy,0) (hx,-hy,0) (hx,hy,0) (-hx,hy,0)
    var b0 = V(-hx, -hy, 0);
    var b1 = V( hx, -hy, 0);
    var b2 = V( hx,  hy, 0);
    var b3 = V(-hx,  hy, 0);

    // 顶面退化为 Y 方向一条边 (z=h):  (-hx,0,h) (hx,0,h)
    var t0 = V(-hx, 0, h);
    var t1 = V( hx, 0, h);

    var polygons = new List<Polygon>();

    // 底面（法线朝 -Z）
    polygons.Add(new Polygon(new List<Vertex> { b3, b2, b1, b0 }));

    // 前斜面 (b0-b1-t1-t0)
    polygons.Add(new Polygon(new List<Vertex> { b0, b1, t1, t0 }));

    // 后面（竖直矩形）(b2-b3-t0-t1)
    polygons.Add(new Polygon(new List<Vertex> { b2, b3, t0, t1 }));

    // 左侧三角形 (b3-b0-t0)
    polygons.Add(new Polygon(new List<Vertex> { b3, b0, t0 }));

    // 右侧三角形 (b1-b2-t1)
    polygons.Add(new Polygon(new List<Vertex> { b1, b2, t1 }));

    // 顶面三角形 (t0-t1-... 实际退化边)
    polygons.Add(new Polygon(new List<Vertex> { t0, t1, V(0, 0, h) }));

    var solid = Solid.FromPolygons(polygons);
    if (n.Center.X != 0 || n.Center.Y != 0 || n.Center.Z != 0)
        solid = solid.Translate(n.Center);
    return solid;
}
```

- [ ] **Step 2: 构建验证**

运行: `dotnet build src/OpenCSG.NET/OpenCSG.NET.csproj --no-restore`
预期: 构建成功

- [ ] **Step 3: 编写 Wedge 测试**

创建 `tests/OpenCSG.NET.Tests/WedgeTest.cs`:

```csharp
using Csg;
using NUnit.Framework;

namespace OpenCSG.NET.Tests
{
    [TestFixture]
    public class WedgeTest : SolidTest
    {
        [Test]
        public void Basic()
        {
            var node = new WedgeNode(new Vector3D(0, 0, 0), new Vector3D(2, 2, 2));
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "Wedge_Basic");
        }

        [Test]
        public void WithCenterOffset()
        {
            var node = new WedgeNode(new Vector3D(5, 5, 0), new Vector3D(3, 1, 2));
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "Wedge_Offset");
        }

        [Test]
        public void ThinWedge()
        {
            var node = new WedgeNode(new Vector3D(0, 0, 0), new Vector3D(4, 0.5, 1));
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "Wedge_Thin");
        }
    }
}
```

运行: `dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj --filter "FullyQualifiedName~WedgeTest"`
预期: 首次运行生成黄金 STL `*.stl_` 文件

- [ ] **Step 4: 验收黄金 STL + Commit**

```bash
git add src/OpenCSG.NET/CsgEvaluator.cs tests/OpenCSG.NET.Tests/WedgeTest.cs tests/OpenCSG.NET.Tests/Results/Wedge_*.stl
git commit -m "feat: add Wedge evaluation"
```

---

### Task 7: CsgSerialization.cs —— JSON 序列化

**文件:**
- 创建: `src/OpenCSG.NET/CsgSerialization.cs`

**接口:**
- 消耗: `CsgNode`, `Profile2D`, `System.Text.Json`, `Vector3D`/`Vector2D`
- 产出: `ToJson(CsgNode)` / `FromJson(string)` / `ToJson(IEnumerable)` / `FromJsonArray(string)`

- [ ] **Step 1: 创建序列化文件**

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Csg
{
    public static class CsgSerialization
    {
        static readonly JsonSerializerOptions s_options = CreateOptions();

        static JsonSerializerOptions CreateOptions()
        {
            var opts = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            opts.Converters.Add(new Vector3DConverter());
            opts.Converters.Add(new Vector2DConverter());
            opts.Converters.Add(new Profile2DConverter());
            return opts;
        }

        /// <summary>CsgNode 树 → JSON 字符串</summary>
        public static string ToJson(CsgNode node, bool indented = true)
        {
            var opts = indented ? s_options : new JsonSerializerOptions(s_options) { WriteIndented = false };
            return JsonSerializer.Serialize(node, node.GetType(), opts);
        }

        /// <summary>JSON 字符串 → CsgNode 树</summary>
        public static CsgNode FromJson(string json)
        {
            var node = JsonSerializer.Deserialize<CsgNode>(json, s_options);
            if (node is null)
                throw new JsonException("Deserialization returned null");
            return node;
        }

        /// <summary>CsgNode 集合 → JSON 数组字符串</summary>
        public static string ToJson(IEnumerable<CsgNode> nodes, bool indented = true)
        {
            var opts = indented ? s_options : new JsonSerializerOptions(s_options) { WriteIndented = false };
            return JsonSerializer.Serialize(nodes, opts);
        }

        /// <summary>JSON 数组字符串 → CsgNode 集合</summary>
        public static IReadOnlyList<CsgNode> FromJsonArray(string json)
        {
            var nodes = JsonSerializer.Deserialize<List<CsgNode>>(json, s_options);
            if (nodes is null)
                throw new JsonException("Deserialization returned null");
            return nodes;
        }
    }

    // ---- JsonConverters ----

    sealed class Vector3DConverter : JsonConverter<Vector3D>
    {
        public override Vector3D Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            double x = 0, y = 0, z = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var prop = reader.GetString();
                    reader.Read();
                    switch (prop)
                    {
                        case "x": x = reader.GetDouble(); break;
                        case "y": y = reader.GetDouble(); break;
                        case "z": z = reader.GetDouble(); break;
                    }
                }
            }
            return new Vector3D(x, y, z);
        }

        public override void Write(Utf8JsonWriter writer, Vector3D value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("x", value.X);
            writer.WriteNumber("y", value.Y);
            writer.WriteNumber("z", value.Z);
            writer.WriteEndObject();
        }
    }

    sealed class Vector2DConverter : JsonConverter<Vector2D>
    {
        public override Vector2D Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            double x = 0, y = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var prop = reader.GetString();
                    reader.Read();
                    switch (prop)
                    {
                        case "x": x = reader.GetDouble(); break;
                        case "y": y = reader.GetDouble(); break;
                    }
                }
            }
            return new Vector2D(x, y);
        }

        public override void Write(Utf8JsonWriter writer, Vector2D value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("x", value.X);
            writer.WriteNumber("y", value.Y);
            writer.WriteEndObject();
        }
    }

    sealed class Profile2DConverter : JsonConverter<Profile2D>
    {
        public override Profile2D? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            var typeName = root.GetProperty("$type").GetString();
            var json = root.GetRawText();

            return typeName switch
            {
                "Rectangle"    => JsonSerializer.Deserialize<RectangleProfile>(json, options),
                "HBeam"        => JsonSerializer.Deserialize<HBeamProfile>(json, options),
                "Channel"      => JsonSerializer.Deserialize<ChannelProfile>(json, options),
                "SquareTube"   => JsonSerializer.Deserialize<SquareTubeProfile>(json, options),
                "Trapezoid"    => JsonSerializer.Deserialize<TrapezoidProfile>(json, options),
                "Capsule"      => JsonSerializer.Deserialize<CapsuleProfile>(json, options),
                "LShape"       => JsonSerializer.Deserialize<LShapeProfile>(json, options),
                _ => throw new JsonException($"Unknown Profile2D $type: {typeName}")
            };
        }

        public override void Write(Utf8JsonWriter writer, Profile2D value, JsonSerializerOptions options)
        {
            var typeName = value switch
            {
                RectangleProfile   => "Rectangle",
                HBeamProfile       => "HBeam",
                ChannelProfile     => "Channel",
                SquareTubeProfile  => "SquareTube",
                TrapezoidProfile   => "Trapezoid",
                CapsuleProfile     => "Capsule",
                LShapeProfile      => "LShape",
                _ => throw new JsonException($"Unknown Profile2D type: {value.GetType().Name}")
            };

            var json = JsonSerializer.Serialize(value, value.GetType(), options);
            using var doc = JsonDocument.Parse(json);
            writer.WriteStartObject();
            writer.WriteString("$type", typeName);
            foreach (var prop in doc.RootElement.EnumerateObject())
                prop.WriteTo(writer);
            writer.WriteEndObject();
        }
    }
}
```

- [ ] **Step 2: 给 CsgNode 添加多态序列化支持**

修改 `src/OpenCSG.NET/CsgNode.cs`，在基类上方添加 `[JsonDerivedType]` 注册：

在文件顶部 `using System.Collections.Generic;` 后添加：
```csharp
using System.Text.Json.Serialization;
```

在 `public abstract record CsgNode;` 上方添加多态注册：

```csharp
[JsonDerivedType(typeof(BoxNode), "Box")]
[JsonDerivedType(typeof(SphereNode), "Sphere")]
[JsonDerivedType(typeof(CylinderNode), "Cylinder")]
[JsonDerivedType(typeof(ConeNode), "Cone")]
[JsonDerivedType(typeof(ExtrudeNode), "Extrude")]
[JsonDerivedType(typeof(WedgeNode), "Wedge")]
[JsonDerivedType(typeof(UnionNode), "Union")]
[JsonDerivedType(typeof(SubtractNode), "Subtract")]
[JsonDerivedType(typeof(IntersectNode), "Intersect")]
[JsonDerivedType(typeof(TransformNode), "Transform")]
public abstract record CsgNode;
```

(注意：`[JsonDerivedType]` 有时需要额外添加 `[JsonPolymorphic]`——如果编译报错则添加：
```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(BoxNode), "Box")]
...
public abstract record CsgNode;
```
)

- [ ] **Step 3: 构建验证**

运行: `dotnet build src/OpenCSG.NET/OpenCSG.NET.csproj --no-restore`
预期: 构建成功。如因 `[JsonDerivedType]` 在 netstandard2.0 下不可用而报错，尝试改用自定义多态 converter 方案（见本任务末尾备选方案）

- [ ] **Step 4: 编写序列化往返测试**

创建 `tests/OpenCSG.NET.Tests/CsgSerializationTest.cs`:

```csharp
using Csg;
using NUnit.Framework;
using System.Text.Json;

namespace OpenCSG.NET.Tests
{
    [TestFixture]
    public class CsgSerializationTest
    {
        [Test]
        public void BoxNode_RoundTrip()
        {
            var node = new BoxNode(new Vector3D(1, 2, 3), new Vector3D(4, 5, 6));
            var json = CsgSerialization.ToJson(node);
            var restored = CsgSerialization.FromJson(json);

            Assert.That(restored, Is.TypeOf<BoxNode>());
            var box = (BoxNode)restored;
            Assert.That(box.Center.X, Is.EqualTo(1));
            Assert.That(box.Center.Y, Is.EqualTo(2));
            Assert.That(box.Center.Z, Is.EqualTo(3));
            Assert.That(box.Size.X, Is.EqualTo(4));
            Assert.That(box.Size.Y, Is.EqualTo(5));
            Assert.That(box.Size.Z, Is.EqualTo(6));
        }

        [Test]
        public void SphereNode_RoundTrip()
        {
            var node = new SphereNode(new Vector3D(0, 0, 0), 2.5, 24);
            var json = CsgSerialization.ToJson(node);
            var restored = CsgSerialization.FromJson(json);

            Assert.That(restored, Is.TypeOf<SphereNode>());
            var s = (SphereNode)restored;
            Assert.That(s.Radius, Is.EqualTo(2.5));
            Assert.That(s.Resolution, Is.EqualTo(24));
        }

        [Test]
        public void NestedBoolean_RoundTrip()
        {
            var node = new SubtractNode(
                new BoxNode(Vector3D.Zero, new Vector3D(10, 10, 10)),
                new UnionNode(
                    new SphereNode(new Vector3D(2, 2, 2), 2, 12),
                    new CylinderNode(new Vector3D(5, 5, 0), 1, 8, 32)
                ));

            var json = CsgSerialization.ToJson(node);
            var restored = CsgSerialization.FromJson(json);

            Assert.That(restored, Is.TypeOf<SubtractNode>());
            var sub = (SubtractNode)restored;
            Assert.That(sub.Left, Is.TypeOf<BoxNode>());
            Assert.That(sub.Right, Is.TypeOf<UnionNode>());
        }

        [Test]
        public void ExtrudeNode_WithProfile_RoundTrip()
        {
            var node = new ExtrudeNode(
                Profiles.HBeam(100, 80, 10, 12), 50, new Vector3D(1, 2, 3));
            var json = CsgSerialization.ToJson(node);
            var restored = CsgSerialization.FromJson(json);

            Assert.That(restored, Is.TypeOf<ExtrudeNode>());
            var ext = (ExtrudeNode)restored;
            Assert.That(ext.Profile, Is.TypeOf<HBeamProfile>());
            Assert.That(ext.Height, Is.EqualTo(50));
        }

        [Test]
        public void JsonContainsTypeDiscriminator()
        {
            var node = new BoxNode(Vector3D.Zero, new Vector3D(1, 1, 1));
            var json = CsgSerialization.ToJson(node);

            Assert.That(json, Does.Contain("\"$type\""));
            Assert.That(json, Does.Contain("Box"));
        }

        [Test]
        public void UnknownType_ThrowsJsonException()
        {
            var json = "{\"$type\":\"UnknownType\"}";
            Assert.Throws<JsonException>(() => CsgSerialization.FromJson(json));
        }

        [Test]
        public void ToJsonArray_RoundTrip()
        {
            var nodes = new CsgNode[]
            {
                new BoxNode(Vector3D.Zero, new Vector3D(1, 1, 1)),
                new SphereNode(Vector3D.Zero, 2, 12),
            };
            var json = CsgSerialization.ToJson(nodes);
            var restored = CsgSerialization.FromJsonArray(json);

            Assert.That(restored.Count, Is.EqualTo(2));
            Assert.That(restored[0], Is.TypeOf<BoxNode>());
            Assert.That(restored[1], Is.TypeOf<SphereNode>());
        }

        [Test]
        public void Idempotent_DoubleSerialize()
        {
            var node = new ExtrudeNode(
                Profiles.Capsule(3, 1), 5, Vector3D.Zero);

            var json1 = CsgSerialization.ToJson(node);
            var restored = CsgSerialization.FromJson(json1);
            var json2 = CsgSerialization.ToJson(restored);

            Assert.That(json1, Is.EqualTo(json2));
        }
    }
}
```

- [ ] **Step 5: 运行序列化测试**

运行: `dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj --filter "FullyQualifiedName~CsgSerializationTest"`
预期: 全部 PASS

- [ ] **Step 6: Commit**

```bash
git add src/OpenCSG.NET/CsgNode.cs src/OpenCSG.NET/CsgSerialization.cs tests/OpenCSG.NET.Tests/CsgSerializationTest.cs
git commit -m "feat: add CsgNode JSON serialization with System.Text.Json polymorphism"
```

- [ ] **备选方案：如果 JsonDerivedType 在 netstandard2.0 下不可用**

`JsonDerivedTypeAttribute` 是 .NET 7+ 的 API。在 netstandard2.0 下不可用时采用**自定义多态 Converter** 方案：

在 `CsgNode.cs` 中移除 `[JsonDerivedType]` attribute，改为在 `CsgSerialization.cs` 中添加 `CsgNodeConverter`：

```csharp
sealed class CsgNodeConverter : JsonConverter<CsgNode>
{
    public override CsgNode? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var typeName = root.GetProperty("$type").GetString();
        var json = root.GetRawText();

        return typeName switch
        {
            "Box"        => JsonSerializer.Deserialize<BoxNode>(json, options),
            "Sphere"     => JsonSerializer.Deserialize<SphereNode>(json, options),
            "Cylinder"   => JsonSerializer.Deserialize<CylinderNode>(json, options),
            "Cone"       => JsonSerializer.Deserialize<ConeNode>(json, options),
            "Extrude"    => JsonSerializer.Deserialize<ExtrudeNode>(json, options),
            "Wedge"      => JsonSerializer.Deserialize<WedgeNode>(json, options),
            "Union"      => JsonSerializer.Deserialize<UnionNode>(json, options),
            "Subtract"   => JsonSerializer.Deserialize<SubtractNode>(json, options),
            "Intersect"  => JsonSerializer.Deserialize<IntersectNode>(json, options),
            "Transform"  => JsonSerializer.Deserialize<TransformNode>(json, options),
            _ => throw new JsonException($"Unknown CsgNode $type: {typeName}")
        };
    }

    public override void Write(Utf8JsonWriter writer, CsgNode value, JsonSerializerOptions options)
    {
        var typeName = value switch
        {
            BoxNode         => "Box",
            SphereNode      => "Sphere",
            CylinderNode    => "Cylinder",
            ConeNode        => "Cone",
            ExtrudeNode     => "Extrude",
            WedgeNode       => "Wedge",
            UnionNode       => "Union",
            SubtractNode    => "Subtract",
            IntersectNode   => "Intersect",
            TransformNode   => "Transform",
            _ => throw new JsonException($"Unknown CsgNode type: {value.GetType().Name}")
        };

        var json = JsonSerializer.Serialize(value, value.GetType(), options);
        using var doc = JsonDocument.Parse(json);
        writer.WriteStartObject();
        writer.WriteString("$type", typeName);
        foreach (var prop in doc.RootElement.EnumerateObject())
            prop.WriteTo(writer);
        writer.WriteEndObject();
    }
}
```

并在 `CreateOptions()` 中注册：`opts.Converters.Add(new CsgNodeConverter());`

---

### Task 8: 最终集成验证

**文件:**
- 运行: 全部测试套件

- [ ] **Step 1: 运行全部测试**

```bash
dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj
```

预期: 全部现有测试（27 个） + 全部新增测试 PASS，无回归

- [ ] **Step 2: 运行全部构建**

```bash
dotnet build OpenCSG.NET.slnx
```

预期: 5 个项目全部构建成功，零警告

- [ ] **Step 3: 检查命名空间一致性**

```bash
rg "^namespace Csg" src/OpenCSG.NET/CsgNode.cs src/OpenCSG.NET/CsgEvaluator.cs src/OpenCSG.NET/CsgSerialization.cs src/OpenCSG.NET/Profiles.cs
```

预期: 四个文件均输出 `namespace Csg`

- [ ] **Step 4: Commit**

```bash
git add -A
git status
# 确认无意外文件后
git commit -m "test: final integration verification - all tests passing"
```
