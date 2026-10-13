using System;
using System.Collections.Generic;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// CsgHelpers 的 L2 契约（Plan C / Task 7 Step 1）：<see cref="CsgHelpers.RevolvedStack"/> 的
    /// 4 条 throw 路径 + 成体路径，<see cref="HalfPlaneCut.Build"/>/<see cref="HalfPlaneCut.BuildXY"/>
    /// 的零方向 throw。异常类型（ArgumentException + nameof(stations/dirY/dirX)）逐条读实现确认。
    /// </summary>
    [TestFixture]
    public class CsgHelpersContractTest
    {
        [Test]
        public void RevolvedStack_NullEmptyOrSingleStation_Throws()
        {
            var nullEx = Assert.Throws<ArgumentException>(() => CsgHelpers.RevolvedStack(null, 0, 0, 0));
            Assert.That(nullEx.ParamName, Is.EqualTo("stations"));

            var emptyEx = Assert.Throws<ArgumentException>(
                () => CsgHelpers.RevolvedStack(Array.Empty<(double Radius, double Y)>(), 0, 0, 0));
            Assert.That(emptyEx.ParamName, Is.EqualTo("stations"));

            var singleEx = Assert.Throws<ArgumentException>(
                () => CsgHelpers.RevolvedStack(new[] { (Radius: 1.0, Y: 0.0) }, 0, 0, 0));
            Assert.That(singleEx.Message, Does.Contain("两个站点"));
        }

        [Test]
        public void RevolvedStack_NonAscendingHeights_Throws()
        {
            var descending = Assert.Throws<ArgumentException>(() => CsgHelpers.RevolvedStack(
                new[] { (Radius: 1.0, Y: 1.0), (Radius: 1.0, Y: 0.0) }, 0, 0, 0));
            Assert.That(descending.Message, Does.Contain("严格升序"));

            // 等高（h = 0）同样不是严格升序。
            var equal = Assert.Throws<ArgumentException>(() => CsgHelpers.RevolvedStack(
                new[] { (Radius: 1.0, Y: 0.0), (Radius: 2.0, Y: 0.0) }, 0, 0, 0));
            Assert.That(equal.Message, Does.Contain("严格升序"));
        }

        [Test]
        public void RevolvedStack_NegativeRadius_Throws()
        {
            var ex = Assert.Throws<ArgumentException>(() => CsgHelpers.RevolvedStack(
                new[] { (Radius: -1.0, Y: 0.0), (Radius: 1.0, Y: 1.0) }, 0, 0, 0));
            Assert.That(ex.Message, Does.Contain("不得为负"));
        }

        [Test]
        public void RevolvedStack_BothRadiiZero_Throws()
        {
            var ex = Assert.Throws<ArgumentException>(() => CsgHelpers.RevolvedStack(
                new[] { (Radius: 0.0, Y: 0.0), (Radius: 0.0, Y: 1.0) }, 0, 0, 0));
            Assert.That(ex.Message, Does.Contain("均为 0"));
        }

        [Test]
        public void RevolvedStack_EqualRadii_BuildsCylinder()
        {
            var node = CsgHelpers.RevolvedStack(
                new[] { (Radius: 1.0, Y: 0.0), (Radius: 1.0, Y: 2.0) }, centerX: 0, baseY: 0, centerZ: 0);

            Assert.That(node, Is.TypeOf<CylinderNode>(), "等半径段应走圆柱分支（|Δr| < 1e-9）");
            var solid = CsgEvaluator.Evaluate(node);
            Assert.That(solid.Polygons.Count, Is.GreaterThan(0));
            GeometryAssert.AssertSolidInvariants(solid, "RevolvedStack 两站圆柱");
        }

        [Test]
        public void RevolvedStack_VaryingRadii_BuildsConeOrUnion()
        {
            var cone = CsgHelpers.RevolvedStack(
                new[] { (Radius: 1.0, Y: 0.0), (Radius: 2.0, Y: 1.0) }, centerX: 0, baseY: 0, centerZ: 0);
            Assert.That(cone, Is.TypeOf<ConeNode>(), "变半径两站段应走圆台（Cone）分支");

            var stack = CsgHelpers.RevolvedStack(
                new[] { (Radius: 1.0, Y: 0.0), (Radius: 2.0, Y: 1.0), (Radius: 0.5, Y: 3.0) }, centerX: 0, baseY: 0, centerZ: 0);
            Assert.That(stack, Is.TypeOf<UnionNode>(), "多段应合并为并集");

            var solid = CsgEvaluator.Evaluate(stack);
            Assert.That(solid.Polygons.Count, Is.GreaterThan(0));
            GeometryAssert.AssertSolidInvariants(solid, "RevolvedStack 三段并集");
        }

        [Test]
        public void HalfPlaneCut_ZeroDirection_Throws()
        {
            // 切割方向 (0,0) ⇒ 无法定义法线：Build 报 dirY、BuildXY 报 dirX（读实现的 ParamName）。
            var build = Assert.Throws<ArgumentException>(
                () => HalfPlaneCut.Build(centerX: 0, refY: 0, refZ: 0, dirY: 0, dirZ: 0,
                    halfExtent: 10, overshoot: 5, sideSign: 1));
            Assert.That(build.ParamName, Is.EqualTo("dirY"));
            Assert.That(build.Message, Does.Contain("为零"));

            var buildXY = Assert.Throws<ArgumentException>(
                () => HalfPlaneCut.BuildXY(refX: 0, refY: 0, dirX: 0, dirY: 0,
                    halfExtent: 10, overshoot: 5, sideSign: 1, centerZ: 0));
            Assert.That(buildXY.ParamName, Is.EqualTo("dirX"));
            Assert.That(buildXY.Message, Does.Contain("为零"));
        }

        [Test]
        public void HalfPlaneCut_NonZeroDirection_ReturnsRotatedBox()
        {
            var node = HalfPlaneCut.Build(centerX: 0, refY: 0, refZ: 0, dirY: 0, dirZ: 1,
                halfExtent: 10, overshoot: 5, sideSign: 1);
            Assert.That(node, Is.TypeOf<TransformNode>(), "切割体 = 绕轴旋转的盒（RotateAboutCenter 产物）");

            var nodeXY = HalfPlaneCut.BuildXY(refX: 0, refY: 0, dirX: 1, dirY: 0,
                halfExtent: 10, overshoot: 5, sideSign: 1, centerZ: 0);
            Assert.That(nodeXY, Is.TypeOf<TransformNode>());
        }
    }
}
