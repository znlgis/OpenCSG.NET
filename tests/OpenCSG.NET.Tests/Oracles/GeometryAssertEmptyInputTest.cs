using System;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// GeometryAssert 空输入对称（Plan C / spec §13）：
    /// <see cref="MeshAssert.AssertClosedOrientedManifold"/> 已在 Plan A 拒绝空网格（TriangleCount == 0），
    /// 而 GeometryAssert 这一侧对空实体真空通过 —— 空实体的边统计全零，
    /// 四条闭合/定向断言全部成立，读起来像「合法的干净实体」。
    /// 这里钉住对称守卫：<see cref="GeometryAssert.ManifoldReport.PolygonCount"/> 与
    /// <see cref="GeometryAssert.AssertClosedOrientedManifold(Solid, string?)"/> 的 &gt;0 前置断言。
    /// </summary>
    [TestFixture]
    public class GeometryAssertEmptyInputTest
    {
        [Test]
        public void Analyze_EmptySolid_ReportsZeroPolygons_LooksCleanOtherwise()
        {
            var report = GeometryAssert.Analyze(new Solid());

            // 除 PolygonCount 外全零 —— 「空」与「干净」原本无从分辨。
            Assert.That(report.PolygonCount, Is.EqualTo(0), "空实体没有多边形");
            Assert.That(report.EdgeCount, Is.EqualTo(0));
            Assert.That(report.SingleUseEdges, Is.EqualTo(0));
            Assert.That(report.NonManifoldEdges, Is.EqualTo(0));
            Assert.That(report.DuplicatedEdges, Is.EqualTo(0));
            Assert.That(report.ZeroLengthEdges, Is.EqualTo(0));
            Assert.That(report.DegeneratePolygons, Is.EqualTo(0));
            Assert.That(report.IsClosedManifold, Is.True, "全零报告『看起来干净』正是要加 PolygonCount 的动机");

            // 并列口径（MeshAssert 侧）：空网格同样读出全零报告，两侧基线一致。
            var meshReport = MeshAssert.Analyze(Array.Empty<MeshTriangle>());
            Assert.That(meshReport.TriangleCount, Is.EqualTo(0));
            Assert.That(meshReport.NonManifoldEdges, Is.EqualTo(0));
        }

        [Test]
        public void Analyze_Cube_ReportsPolygonCount()
        {
            var cube = Solids.Cube(10, center: true);
            var report = GeometryAssert.Analyze(cube);

            Assert.That(report.PolygonCount, Is.EqualTo(cube.Polygons.Count));
            Assert.That(report.PolygonCount, Is.EqualTo(6), "立方体 6 个面（与 mesh 侧 12 个三角形的三角化口径并列）");
        }

        [Test]
        public void AssertClosedOrientedManifold_EmptySolid_Throws()
        {
            // 修复前：四条断言在零报告上全部成立 ⇒ 真空通过。失败消息口径对齐 MeshAssert 的空网格版本。
            var ex = Assert.Throws<AssertionException>(
                () => GeometryAssert.AssertClosedOrientedManifold(new Solid()));

            Assert.That(ex!.Message, Does.Contain("多边形数为 0"));
            Assert.That(ex.Message, Does.Contain("空实体不是合法实体（若无本断言，空输入会真空通过）"));
        }

        [Test]
        public void AssertClosedOrientedManifold_EmptyInput_RejectedByBothSides()
        {
            // 空输入对称：Solid 与 MeshTriangle 两侧都必须拒绝，不允许「空即通过」。
            Assert.Throws<AssertionException>(() => GeometryAssert.AssertClosedOrientedManifold(new Solid()));
            Assert.Throws<AssertionException>(
                () => MeshAssert.AssertClosedOrientedManifold(Array.Empty<MeshTriangle>()));
        }

        [Test]
        public void AssertClosedOrientedManifold_Cube_StillPasses()
        {
            // 守卫只影响空实体：正常实体的既有行为不变（B1/B2 oracle 路径回归由全量套件覆盖）。
            GeometryAssert.AssertClosedOrientedManifold(Solids.Cube(10, center: true), "立方体");
        }
    }
}
