using System;
using Csg;
using NUnit.Framework;
using static Csg.Solids;

namespace Csg.Test
{
    /// <summary>
    /// 基本体原点 / 轴向约定回归测试（审查项 1）。
    /// 约定口径（与 CsgNode/README/宿主 CadCsgEvaluator 的假设一致）：
    /// - Cube：默认自原点沿正轴生成 [0,size]³；center:true → 以原点为中心；center 点 → 以该点为中心。
    /// - Sphere：默认居中（center 默认 true）；不居中时最小角在 (r,r,r)。
    /// - Cylinder/Cone：轴向沿 ±Y，Center 是轴线中点，高度沿 Y。
    /// - Extrude：截面在 X-Y 平面，沿 +Z 从 0 拉到 Height。
    /// - Wedge：Corner 是底面矩形中心，底面位于 Z = Corner.Z，屋脊在 Y = Corner.Y、Z = Corner.Z + Height。
    /// </summary>
    [TestFixture]
    public class PrimitiveConventionTest
    {
        const double Tol = 1e-9;

        [Test]
        public void Cube_DefaultSpansFromOrigin()
        {
            GeometryAssert.AssertBounds(Cube(2), new Vector3D(0, 0, 0), new Vector3D(2, 2, 2), Tol, "Cube(2)");
            GeometryAssert.AssertBounds(Cube(), new Vector3D(0, 0, 0), new Vector3D(1, 1, 1), Tol, "Cube()");
        }

        [Test]
        public void Cube_CenterFlagCentersOnOrigin()
        {
            GeometryAssert.AssertBounds(Cube(2, center: true), new Vector3D(-1, -1, -1), new Vector3D(1, 1, 1), Tol, "Cube(2,center)");
        }

        [Test]
        public void Cube_CenterPointIsBoxCenter()
        {
            GeometryAssert.AssertBounds(Cube(2, new Vector3D(10, 20, 30)), new Vector3D(9, 19, 29), new Vector3D(11, 21, 31), Tol, "Cube(2,center)");
            GeometryAssert.AssertBounds(Cube(new Vector3D(2, 4, 6), new Vector3D(1, 2, 3)), new Vector3D(0, 0, 0), new Vector3D(2, 4, 6), Tol, "Cube(size,center)");
        }

        [Test]
        public void Cube_NegativeSizeIsTreatedAsAbsolute()
        {
            // Solids.Cube 对半径取绝对值（源码注释：negative radii make no sense）
            GeometryAssert.AssertBounds(Cube(new Vector3D(-2, -4, -6), new Vector3D(0, 0, 0)), new Vector3D(-1, -2, -3), new Vector3D(1, 2, 3), Tol, "Cube(-size)");
        }

        [Test]
        public void Cube_ZeroSizeYieldsEmptySolid()
        {
            Assert.That(Cube(0).Polygons.Count, Is.EqualTo(0));
        }

        [Test]
        public void Sphere_IsCenteredByDefault()
        {
            GeometryAssert.AssertBounds(Sphere(1), new Vector3D(-1, -1, -1), new Vector3D(1, 1, 1), Tol, "Sphere(1)");
            GeometryAssert.AssertBounds(Sphere(2, center: false), new Vector3D(0, 0, 0), new Vector3D(4, 4, 4), Tol, "Sphere(2,center:false)");
            GeometryAssert.AssertBounds(Sphere(1, new Vector3D(5, 0, 0)), new Vector3D(4, -1, -1), new Vector3D(6, 1, 1), Tol, "Sphere(1,center)");
        }

        [Test]
        public void Cylinder_AxisIsYAndCenterIsAxisMidpoint()
        {
            GeometryAssert.AssertBounds(Cylinder(1, 3), new Vector3D(-1, 0, -1), new Vector3D(1, 3, 1), Tol, "Cylinder(1,3)");
            GeometryAssert.AssertBounds(Cylinder(1, 3, center: true), new Vector3D(-1, -1.5, -1), new Vector3D(1, 1.5, 1), Tol, "Cylinder(1,3,center)");
        }

        [Test]
        public void Cylinder_OptionsSpanStartToEnd()
        {
            var solid = Cylinder(new CylinderOptions
            {
                Start = new Vector3D(0, 0, 0),
                End = new Vector3D(0, 0, 4),
                RadiusStart = 1,
                RadiusEnd = 1,
            });
            GeometryAssert.AssertBounds(solid, new Vector3D(-1, -1, 0), new Vector3D(1, 1, 4), Tol, "Cylinder(Z 轴)");
        }

        [Test]
        public void Cone_BottomAtMinusHalfHeight_ApexAtPlusHalfHeight()
        {
            var cone = CsgEvaluator.Evaluate(new ConeNode(new Vector3D(0, 0, 0), TopRadius: 0, BottomRadius: 1, Height: 2));
            GeometryAssert.AssertBounds(cone, new Vector3D(-1, -1, -1), new Vector3D(1, 1, 1), Tol, "ConeNode");

            // 顶点（唯一 y = +Height/2 处的点）必须落在轴线上：顶半径 0 → y=+1 处所有顶点 x=z=0
            foreach (var polygon in cone.Polygons)
            {
                foreach (var vertex in polygon.Vertices)
                {
                    if (Math.Abs(vertex.Pos.Y - 1.0) < Tol)
                    {
                        Assert.That(vertex.Pos.X, Is.EqualTo(0).Within(Tol), "圆锥顶点应在轴线上");
                        Assert.That(vertex.Pos.Z, Is.EqualTo(0).Within(Tol), "圆锥顶点应在轴线上");
                    }
                }
            }
        }

        [Test]
        public void Cone_CenterPointIsAxisMidpoint()
        {
            var cone = CsgEvaluator.Evaluate(new ConeNode(new Vector3D(10, 20, 30), TopRadius: 0, BottomRadius: 1, Height: 2));
            GeometryAssert.AssertBounds(cone, new Vector3D(9, 19, 29), new Vector3D(11, 21, 31), Tol, "ConeNode(center)");
        }

        [Test]
        public void Extrude_ProfileInXyPlaneExtrudedAlongPlusZFromZero()
        {
            var solid = CsgEvaluator.Evaluate(new ExtrudeNode(Profiles.Rectangle(4, 2), 1));
            GeometryAssert.AssertBounds(solid, new Vector3D(-2, -1, 0), new Vector3D(2, 1, 1), Tol, "Extrude(Rectangle)");
        }

        [Test]
        public void Wedge_CornerIsBaseCenterAndRidgeIsAtHalfDepth()
        {
            var solid = CsgEvaluator.Evaluate(new WedgeNode(new Vector3D(5, 5, 0), 3, 1, 2));
            GeometryAssert.AssertBounds(solid, new Vector3D(3.5, 4.5, 0), new Vector3D(6.5, 5.5, 2), Tol, "WedgeNode");

            // 屋脊（z = Corner.Z + Height）只出现在 y = Corner.Y 处
            foreach (var polygon in solid.Polygons)
            {
                foreach (var vertex in polygon.Vertices)
                {
                    if (Math.Abs(vertex.Pos.Z - 2.0) < Tol)
                        Assert.That(vertex.Pos.Y, Is.EqualTo(5.0).Within(Tol), "楔形体屋脊应在 Y = Corner.Y（底面中心深度）");
                }
            }
        }

        [Test]
        public void Wedge_BasePlaneIsCornerZ()
        {
            var solid = CsgEvaluator.Evaluate(new WedgeNode(new Vector3D(0, 0, 7), 2, 2, 3));
            GeometryAssert.AssertBounds(solid, new Vector3D(-1, -1, 7), new Vector3D(1, 1, 10), Tol, "WedgeNode(base Z)");
        }
    }
}
