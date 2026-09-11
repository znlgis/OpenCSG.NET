using System;
using Csg;
using NUnit.Framework;
using static Csg.Solids;

namespace Csg.Test
{
    /// <summary>
    /// 图元与布尔结果的几何不变量测试（补 golden STL 比较的盲区）。
    /// 解析体积口径：正 n 边形（n = Solid.DefaultResolution3D = 12）面积 A(r) = 0.5·n·r²·sin(2π/n) = 3r²（r 为半径）。
    /// </summary>
    [TestFixture]
    public class GeometryInvariantTest : SolidTest
    {
        const double Tol = 1e-9;

        [Test]
        public void Cube_IsClosedOutwardSolid()
        {
            var unit = Cube();
            GeometryAssert.AssertSolidInvariants(unit, "Cube()");
            GeometryAssert.AssertVolume(unit, 1.0, Tol, "Cube()");
            GeometryAssert.AssertBounds(unit, new Vector3D(0, 0, 0), new Vector3D(1, 1, 1), Tol, "Cube()");

            var box = Cube(new Vector3D(2, 4, 6), new Vector3D(1, 2, 3));
            GeometryAssert.AssertSolidInvariants(box, "Cube(size, center)");
            GeometryAssert.AssertVolume(box, 48.0, Tol, "Cube(size, center)");
            GeometryAssert.AssertBounds(box, new Vector3D(0, 0, 0), new Vector3D(2, 4, 6), Tol, "Cube(size, center)");
        }

        [Test]
        public void Sphere_IsClosedOutwardSolid()
        {
            var sphere = Sphere(1);
            GeometryAssert.AssertSolidInvariants(sphere, "Sphere(1)");
            // 12 分辨率的多面体近似（qresolution=3，两极方向为 ±Z）；解析体积 4π/3 ≈ 4.18879。
            var volume = GeometryAssert.SignedVolume(sphere);
            Assert.That(volume, Is.GreaterThan(0), "球体外法线：净体积必须为正");
            Assert.That(volume, Is.EqualTo(4.0 * Math.PI / 3.0).Within(0.15 * 4.0 * Math.PI / 3.0), "球体多面体近似体积偏差过大");
        }

        [Test]
        public void Cylinder_IsClosedOutwardSolid()
        {
            var cylinder = Cylinder(1, 1, center: true);
            GeometryAssert.AssertSolidInvariants(cylinder, "Cylinder(1,1,center)");
            // 正 12 边形棱柱体积 = A(1)·h = 3·1
            GeometryAssert.AssertVolume(cylinder, 3.0, Tol, "Cylinder(1,1,center)");
        }

        [Test]
        public void Cone_IsClosedOutwardSolid()
        {
            // 正圆锥（顶半径 0）：棱锥体积 = A(1)·h/3 = 3·2/3 = 2
            var cone = CsgEvaluator.Evaluate(new ConeNode(new Vector3D(0, 0, 0), TopRadius: 0, BottomRadius: 1, Height: 2));
            GeometryAssert.AssertSolidInvariants(cone, "ConeNode(0,1,h=2)");
            GeometryAssert.AssertVolume(cone, 2.0, Tol, "ConeNode(0,1,h=2)");

            // 圆台：V = h/3·(A1+A2+√(A1A2)) = 2/3·(12+3+6) = 14
            var frustum = CsgEvaluator.Evaluate(new ConeNode(new Vector3D(0, 0, 0), TopRadius: 1, BottomRadius: 2, Height: 2));
            GeometryAssert.AssertSolidInvariants(frustum, "ConeNode(1,2,h=2)");
            GeometryAssert.AssertVolume(frustum, 14.0, Tol, "ConeNode(1,2,h=2)");
        }

        [Test]
        public void Extrude_IsClosedOutwardSolid()
        {
            var rect = CsgEvaluator.Evaluate(new ExtrudeNode(Profiles.Rectangle(4, 2), 1));
            GeometryAssert.AssertSolidInvariants(rect, "Extrude(Rectangle)");
            GeometryAssert.AssertVolume(rect, 8.0, Tol, "Extrude(Rectangle)");

            var lshape = CsgEvaluator.Evaluate(new ExtrudeNode(Profiles.LShape(3, 2, 0.5), 1));
            GeometryAssert.AssertSolidInvariants(lshape, "Extrude(LShape)");
            GeometryAssert.AssertVolume(lshape, 2.25, Tol, "Extrude(LShape)"); // 2×0.5 + 0.5×2.5

            var hbeam = CsgEvaluator.Evaluate(new ExtrudeNode(Profiles.HBeam(100, 80, 10, 12), 50));
            GeometryAssert.AssertSolidInvariants(hbeam, "Extrude(HBeam)");
            // 面积 = 2·80·12 + 100·10 = 2920；体积 = 面积·50
            GeometryAssert.AssertVolume(hbeam, 2920.0 * 50.0, 1e-6, "Extrude(HBeam)");
        }

        [Test]
        public void Wedge_IsClosedOutwardSolid()
        {
            var wedge = CsgEvaluator.Evaluate(new WedgeNode(new Vector3D(0, 0, 0), 2, 2, 2));
            GeometryAssert.AssertSolidInvariants(wedge, "WedgeNode(2,2,2)");
            // 三角柱：截面积 1/2·2·2 = 2，宽 2 → 体积 4
            GeometryAssert.AssertVolume(wedge, 4.0, Tol, "WedgeNode(2,2,2)");
        }

        [Test]
        public void BooleanResults_AreClosedOutwardSolids()
        {
            var cube = Cube(3, center: true);
            var sphere = Sphere(2, center: true);

            // 球面参与布尔后存在 T 顶点接缝（BSP 布尔的已知产物，库内无 T 顶点清理）：
            // 立方体棱处两侧面片几何贴合、面积不缺失，但顶点分割不同 → 网格不是封闭 2-流形。
            // 这里不做封闭性硬断言，改以「定向一致 + 平面自洽 + 体积恒等式」把关（漏洞会立刻破坏体积恒等式）。
            var union = cube.Union(sphere);
            GeometryAssert.AssertOrientationConsistent(union, "Cube ∪ Sphere");
            GeometryAssert.AssertPlanesMatchWinding(union, "Cube ∪ Sphere");
            Assert.That(GeometryAssert.SignedVolume(union), Is.GreaterThan(0), "并集体积必须为正（法线朝外）");

            var difference = cube.Subtract(sphere);
            GeometryAssert.AssertOrientationConsistent(difference, "Cube − Sphere");
            GeometryAssert.AssertPlanesMatchWinding(difference, "Cube − Sphere");
            Assert.That(GeometryAssert.SignedVolume(difference), Is.GreaterThan(0), "差集体积必须为正（法线朝外）");
            Assert.That(GeometryAssert.SignedVolume(difference), Is.LessThan(36.0), "差集体积应小于立方体");

            var intersection = cube.Intersect(sphere);
            GeometryAssert.AssertSolidInvariants(intersection, "Cube ∩ Sphere");
            Assert.That(GeometryAssert.SignedVolume(intersection), Is.GreaterThan(0), "交集体积必须为正");
            Assert.That(GeometryAssert.SignedVolume(intersection), Is.LessThan(36.0), "交集体积应小于立方体");

            // 体积守恒恒等式：A ∪ B = A + B − A ∩ B（同时验证无真实裂缝导致的面缺失）
            Assert.That(GeometryAssert.SignedVolume(union),
                Is.EqualTo(GeometryAssert.SignedVolume(cube) + GeometryAssert.SignedVolume(sphere) - GeometryAssert.SignedVolume(intersection))
                  .Within(1e-9),
                "并集体积应等于 A + B − A∩B");
            Assert.That(GeometryAssert.SignedVolume(difference),
                Is.EqualTo(GeometryAssert.SignedVolume(cube) - GeometryAssert.SignedVolume(intersection)).Within(1e-9),
                "差集体积应等于 A − A∩B");
        }

        [Test]
        public void CoplanarTouchUnion_IsClosedOutwardSolid()
        {
            // 两面贴合的共面布尔：两 4³ 立方体在 x=0 面相接，并集 4×4×8 = 128
            var union = Cube(4, new Vector3D(-2, 0, 0)).Union(Cube(4, new Vector3D(2, 0, 0)));
            GeometryAssert.AssertSolidInvariants(union, "共面并集");
            GeometryAssert.AssertVolume(union, 128.0, 1e-9, "共面并集");
        }

        [Test]
        public void EmptyIntersection_ProducesNoPolygons()
        {
            var empty = Cube(4, new Vector3D(-2, 0, 0)).Intersect(Cube(4, new Vector3D(2, 0, 0)));
            Assert.That(empty.Polygons.Count, Is.EqualTo(0));
            GeometryAssert.AssertSolidInvariants(empty, "空交集");
            GeometryAssert.AssertVolume(empty, 0.0, 0.0, "空交集");
        }
    }
}
