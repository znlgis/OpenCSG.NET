using System.Collections.Generic;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// Tier 5：遮挡 oracle 在手工夹具上的验证（独立射线投射 vs 投影器的可见/隐藏拆分）。
    /// <para>
    /// 三个夹具全部轴对齐：oracle 的 9 探针在深度不连续处可能混合（= 格量化敏感带），
    /// 但不应出现掠射（探针读数在 <c>inf</c> 与有限值之间跳）导致的系统性失败；
    /// 带孔板只覆盖主视/俯视 —— 左视孔缘掠射属 DEFECT-004 机制②的行为域，见下。
    /// </para>
    /// </summary>
    [TestFixture]
    public class Tier5OcclusionOracleTest
    {
        [Test]
        public void BoxesShifted_VisibilityMatchesRayCast()
        {
            var solid = OcclusionFixtures.BoxesShifted();
            var triangles = ViewBridge.ToViewTriangles(solid);
            var views = ViewBridge.ProjectViews(solid, ViewDirections.OrthographicThree);

            // 逐视图调用（而非聚合一次）：防空洞守卫按调用判定，聚合调用无法发现单个视图零判定。
            foreach (var view in views)
                OcclusionAssert.AssertVisibilityMatchesRayCast(new[] { view }, triangles,
                    $"boxes-shifted/{view.Direction.Name}");

            Assert.That(HiddenCountOf(views, "左视"), Is.GreaterThan(0),
                "前块应遮住后块的一部分（DEFECT-004 之外的首个 H>0 夹具）");
        }

        [Test]
        public void LShape_VisibilityMatchesRayCast()
        {
            var solid = OcclusionFixtures.LShape();
            var triangles = ViewBridge.ToViewTriangles(solid);
            var views = ViewBridge.ProjectViews(solid, ViewDirections.OrthographicThree);

            // 逐视图调用（而非聚合一次）：防空洞守卫按调用判定，聚合调用无法发现单个视图零判定。
            foreach (var view in views)
                OcclusionAssert.AssertVisibilityMatchesRayCast(new[] { view }, triangles,
                    $"lshape/{view.Direction.Name}");

            Assert.That(HiddenCountOf(views, "俯视"), Is.GreaterThan(0), "阶梯远侧棱边应被阶梯遮挡");
            Assert.That(HiddenCountOf(views, "左视"), Is.GreaterThan(0), "阶梯远侧棱边应被阶梯遮挡");
        }

        [Test]
        public void DrilledPlate_AxisViewsMatchRayCast()
        {
            var solid = OcclusionFixtures.DrilledPlate();
            var triangles = ViewBridge.ToViewTriangles(solid);
            // 左视不含在内：孔缘掠射（视线与孔壁近乎平行）时射线沿面平面走、投影器按格判定，
            // 属 DEFECT-004 机制②（消隐栅格把掠射轮廓切成 V/H 交替碎段）的行为域，由该缺陷统一承接。
            var views = ViewBridge.ProjectViews(solid, new[] { ViewDirections.Front, ViewDirections.Top });

            // 逐视图调用（而非聚合一次）：防空洞守卫按调用判定，聚合调用无法发现单个视图零判定。
            foreach (var view in views)
                OcclusionAssert.AssertVisibilityMatchesRayCast(new[] { view }, triangles,
                    $"plate-drilled/{view.Direction.Name}");

            Assert.That(HiddenCountOf(views, "主视"), Is.GreaterThan(0), "空腔远侧轮廓应被遮挡");
            Assert.That(HiddenCountOf(views, "俯视"), Is.GreaterThan(0), "空腔远侧轮廓应被遮挡");
        }

        static int HiddenCountOf(IReadOnlyList<ViewProjection> views, string name)
        {
            foreach (var view in views)
                if (view.Direction.Name == name)
                    return view.HiddenCount;

            Assert.Fail($"视图 {name} 不存在");
            return 0;
        }
    }
}
