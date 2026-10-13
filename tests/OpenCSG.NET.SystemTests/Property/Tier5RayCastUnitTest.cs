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

            // 非单位方向：t 按传入向量长度计 —— |direction| = 2 时命中点仍在 z=0，t = 5/2 = 2.5。
            Assert.That(RayCast.Intersect(Triangle(0), origin, new ViewVector(0, 0, 2), out var scaledT), Is.True);
            Assert.That(scaledT, Is.EqualTo(2.5).Within(1e-12));
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
            // 三个边界点都必须命中，各踩一条重心判据的等号侧：
            // 顶点 C（u=0、v=1）踩 u+v=1 上界（判据 u+v > 1+1e-9 的等号侧）。
            Assert.That(RayCast.Intersect(Triangle(0), new ViewPoint(0, 1, -1), new ViewVector(0, 0, 1), out _), Is.True);
            // 边 AB 中点（u=0.75、v=0）踩 v=0 下界（判据 v < -1e-9 的等号侧）。
            var edgeMid = new ViewPoint(0.5, -1, -1);
            Assert.That(RayCast.Intersect(Triangle(0), edgeMid, new ViewVector(0, 0, 1), out _), Is.True);
            // 顶点 A（u=0、v=0）同时踩 u=0 与 v=0 两条下界。
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

            // skipB 与 skipA 同权：skipA=0 跳过远板（z=5）、skipB=1 跳过近板（z=0）⇒ 两面全被忽略、无其余三角形 ⇒ +∞。
            // （若 skipB 子句被删除，近板 t=1 会漏出，此处立即变红。）
            Assert.That(double.IsPositiveInfinity(
                RayCast.NearestHitDistance(triangles, origin, direction, skipA: 0, skipB: 1)), Is.True);

            // 起点正好在近板上：近板由 skipA=1 跳过 ⇒ 命中远板 t=5（自命中由 skip 处理，此处不经过 minDistance）。
            var onPlate = new ViewPoint(0, 0, 0);
            Assert.That(RayCast.NearestHitDistance(triangles, onPlate, direction, skipA: 1),
                Is.EqualTo(5.0).Within(1e-12));

            // 不跳过任何三角形时：近板上求得的 t 为 −0.0，在 Intersect 的 t>0 判据处即被拒绝（未到 minDistance 过滤），
            // 结果仍是远板 t=5；严格为正、低于 minDistance 的自命中过滤另由 FiltersSubThresholdSelfHits 覆盖。
            Assert.That(RayCast.NearestHitDistance(triangles, onPlate, direction),
                Is.EqualTo(5.0).Within(1e-12));
        }

        [Test]
        public void NearestHitDistance_FiltersSubThresholdSelfHits()
        {
            // 起点距近板 1e-8：命中参数严格为正（近板 t=1e-8），但不超过默认 minDistance=1e-7 ——
            // 这是唯一真正走 `t <= minDistance` 过滤分支的场景（t=0 的自命中在 Intersect 的 t>0 判据处已被拒绝）。
            var triangles = new List<ViewTriangle> { Triangle(0), Triangle(5) };
            var origin = new ViewPoint(0, 0, -1e-8);
            var direction = new ViewVector(0, 0, 1);

            // 默认 minDistance=1e-7：近板 t=1e-8 被过滤 ⇒ 命中远板 t = 5 + 1e-8。
            Assert.That(RayCast.NearestHitDistance(triangles, origin, direction),
                Is.EqualTo(5.0 + 1e-8).Within(1e-12));

            // 放宽到 minDistance=1e-9：近板 t=1e-8 > 1e-9 得以释放 ⇒ 最近命中即近板 t=1e-8。
            Assert.That(RayCast.NearestHitDistance(triangles, origin, direction, minDistance: 1e-9),
                Is.EqualTo(1e-8).Within(1e-12));
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
