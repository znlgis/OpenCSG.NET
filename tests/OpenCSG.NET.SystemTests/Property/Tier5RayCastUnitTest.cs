using System;
using System.Collections.Generic;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// Tier 5：RayCast 射线-三角形求交的单元测试（几何基元级，手工构造）。
    /// </summary>
    [TestFixture]
    public class Tier5RayCastUnitTest
    {
        static ViewTriangle Triangle(double z)
            => new ViewTriangle(
                new ViewPoint(-1, -1, z),
                new ViewPoint(1, -1, z),
                new ViewPoint(0, 1, z));

        [Test]
        public void Intersect_HitsTriangleFromBehindAtExactDistance()
        {
            var origin = new ViewPoint(0, 0, -5);
            var direction = new ViewVector(0, 0, 1);

            var hit = RayCast.Intersect(Triangle(0), origin, direction, out var t);

            Assert.That(hit, Is.True);
            Assert.That(t, Is.EqualTo(5.0).Within(1e-12));
        }

        [Test]
        public void Intersect_MissesWhenRayPointsAway()
        {
            var origin = new ViewPoint(0, 0, -5);
            var direction = new ViewVector(0, 0, -1);

            Assert.That(RayCast.Intersect(Triangle(0), origin, direction, out _), Is.False);
        }

        [Test]
        public void Intersect_MissesWhenRayIsParallelToPlane()
        {
            var origin = new ViewPoint(0, 0, 0);
            var direction = new ViewVector(1, 0, 0);

            Assert.That(RayCast.Intersect(Triangle(0), origin, direction, out _), Is.False);
        }

        [Test]
        public void Intersect_CountsBoundaryHits()
        {
            // 顶点上（u=0 分支）与边上（u+v=1 分支）都必须命中。
            Assert.That(RayCast.Intersect(Triangle(0), new ViewPoint(0, 1, -1), new ViewVector(0, 0, 1), out _), Is.True);
            var edgeMid = new ViewPoint(0.5, -1, -1);
            Assert.That(RayCast.Intersect(Triangle(0), edgeMid, new ViewVector(0, 0, 1), out _), Is.True);
            var sideMid = new ViewPoint(-1, -1, -1);
            Assert.That(RayCast.Intersect(Triangle(0), sideMid, new ViewVector(0, 0, 1), out _), Is.True);
        }

        [Test]
        public void Intersect_DegenerateTriangleNeverHits()
        {
            var degenerate = new ViewTriangle(
                new ViewPoint(0, 0, 0), new ViewPoint(1, 0, 0), new ViewPoint(2, 0, 0));
            Assert.That(RayCast.Intersect(degenerate, new ViewPoint(1, 1, 0), new ViewVector(0, -1, 0), out _), Is.False);
        }

        [Test]
        public void NearestHitDistance_ReturnsNearestOfTwoParallelPlates()
        {
            var triangles = new List<ViewTriangle> { Triangle(5), Triangle(0) };
            var origin = new ViewPoint(0, 0, -1);
            var direction = new ViewVector(0, 0, 1);

            var t = RayCast.NearestHitDistance(triangles, origin, direction);

            Assert.That(t, Is.EqualTo(1.0).Within(1e-12));
        }

        [Test]
        public void NearestHitDistance_SkipsIgnoredTrianglesAndSelfHits()
        {
            var triangles = new List<ViewTriangle> { Triangle(5), Triangle(0) };
            var origin = new ViewPoint(0, 0, -1);
            var direction = new ViewVector(0, 0, 1);

            // 跳过近板（下标 1，z=0）后应命中远板（z=5）：t = 6。
            var t = RayCast.NearestHitDistance(triangles, origin, direction, skipA: 1);

            Assert.That(t, Is.EqualTo(6.0).Within(1e-12));

            // 起点正好在近板上：t=0 的自命中被 minDistance 过滤掉；近板被跳过 ⇒ 命中远板 t=5。
            var onPlate = new ViewPoint(0, 0, 0);
            Assert.That(RayCast.NearestHitDistance(triangles, onPlate, direction, skipA: 1),
                Is.EqualTo(5.0).Within(1e-12));

            // 不跳过任何三角形时，t=0 自命中仍被 minDistance 过滤，返回远板 t=5。
            Assert.That(RayCast.NearestHitDistance(triangles, onPlate, direction),
                Is.EqualTo(5.0).Within(1e-12));
        }

        [Test]
        public void NearestHitDistance_NoHitReturnsInfinity()
        {
            var triangles = new List<ViewTriangle> { Triangle(0) };
            var t = RayCast.NearestHitDistance(triangles, new ViewPoint(0, 0, -1), new ViewVector(0, 1, 0));
            Assert.That(double.IsPositiveInfinity(t), Is.True);
        }
    }
}
