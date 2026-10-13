using System;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// 退化输入健全性（Plan C / Task 7 Step 2）：1e-5 容差临界对、1e12 尺度、±0 扇区角、
    /// 默认 360 与显式 360 的等价性。全部按实现里的真实容差常量与分支构造，不再猜。
    /// </summary>
    [TestFixture]
    public class DegenerateInputsTest
    {
        /// <summary>Plane.SplitPolygon 的共面容差常量（读实现：<c>Plane.EPSILON = 1e-5</c>）。</summary>
        const double PlaneEpsilon = 1e-5;

        [Test]
        public void PlaneSplitPolygon_EpsilonBoundary_BothSides()
        {
            // 读实现：|t| ≤ EPSILON 判共面（Type 0/1），t > EPSILON 判前侧（Type 2），t < −EPSILON 判后侧（Type 3）。
            // 每侧各取「门限内半步」与「门限外一倍」两组。
            Assert.That(SplitType(PlaneEpsilon / 2), Is.EqualTo(0), "+EPS/2 仍在共面容差内 ⇒ 共面（Type 0）");
            Assert.That(SplitType(2 * PlaneEpsilon), Is.EqualTo(2), "+2·EPS 超出容差 ⇒ 整体在前侧（Type 2）");
            Assert.That(SplitType(-PlaneEpsilon / 2), Is.EqualTo(0), "−EPS/2 仍在共面容差内 ⇒ 共面（Type 0）");
            Assert.That(SplitType(-2 * PlaneEpsilon), Is.EqualTo(3), "−2·EPS 超出容差 ⇒ 整体在后侧（Type 3）");
        }

        static int SplitType(double z)
        {
            var polygon = new Polygon(
                new Vertex(new Vector3D(0, 0, z), new Vector2D(0, 0)),
                new Vertex(new Vector3D(1, 0, z), new Vector2D(0, 0)),
                new Vertex(new Vector3D(0, 1, z), new Vector2D(0, 0)));
            var plane = new Plane(new Vector3D(0, 0, 1), 0);
            plane.SplitPolygon(polygon, out var result);
            return result.Type;
        }

        [Test]
        public void CubeAt1e12_VolumeAndBoundsStayConsistent()
        {
            // 1e12 边长：坐标本身精确（1e12/2 = 5e11 可精确表示），体积按**相对容差**口径核对。
            var cube = Solids.Cube(1e12, center: true);
            Assert.That(cube.Polygons.Count, Is.EqualTo(6));

            GeometryAssert.AssertBounds(cube,
                new Vector3D(-5e11, -5e11, -5e11), new Vector3D(5e11, 5e11, 5e11), 0.0, "Cube(1e12)");

            const double expected = 1e36;   // 1e12³
            GeometryAssert.AssertVolume(cube, expected, expected * 1e-9, "Cube(1e12) 相对容差 1e-9");
        }

        [Test]
        public void CylinderSectorAngle_MinusZero_IsEmptyLikeZero()
        {
            // A2 契约：SectorAngle == 0 ⇒ 空实体；-0.0 == 0（IEEE）⇒ 与 0 同判：不抛、不产面。
            var solid = Solids.Cylinder(new CylinderOptions
            {
                Start = new Vector3D(0, 0, 0),
                End = new Vector3D(0, 0, 20),
                RadiusStart = 10,
                RadiusEnd = 10,
                SectorAngle = -0.0,
            });

            Assert.That(solid.Polygons.Count, Is.EqualTo(0), "SectorAngle = -0.0 应与非负零同判（空实体）");
        }

        [Test]
        public void CylinderSectorAngle_DefaultEqualsExplicit360()
        {
            var def = Solids.Cylinder(new CylinderOptions
            {
                Start = new Vector3D(0, 0, 0),
                End = new Vector3D(0, 0, 20),
                RadiusStart = 10,
                RadiusEnd = 10,
            });
            var explicit360 = Solids.Cylinder(new CylinderOptions
            {
                Start = new Vector3D(0, 0, 0),
                End = new Vector3D(0, 0, 20),
                RadiusStart = 10,
                RadiusEnd = 10,
                SectorAngle = 360,
            });

            // 非空前提：两个实体都必须真的产出多边形，防「0 == 0」的空实体等价性假通过。
            Assert.That(def.Polygons.Count, Is.GreaterThan(0), "前提：默认 360 应是非空实体");
            Assert.That(explicit360.Polygons.Count, Is.GreaterThan(0), "前提：显式 360 应是非空实体");
            Assert.That(def.Polygons.Count, Is.EqualTo(explicit360.Polygons.Count),
                "默认值（360）与显式 360 必须走同一分支、产出同形几何");
            Assert.That(GeometryAssert.SignedVolume(def), Is.EqualTo(GeometryAssert.SignedVolume(explicit360)),
                "同一构造路径 ⇒ 有向体积逐位一致");
        }
    }
}
