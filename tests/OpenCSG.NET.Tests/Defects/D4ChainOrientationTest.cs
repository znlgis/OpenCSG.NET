using System;
using System.Collections.Generic;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// DEFECT-004 机制① 回归（成链取向）：<see cref="ViewArcFitter"/> 的成链在候选边**反向接入**
    /// （以其 End 接到链尖端）时，只把边加入链、却仍按「链端片段的 End」推新尖端 ⇒ 尖端停在原地、
    /// 链当场截断，反向边远端的顶点永不入链（记录在案：干净 48 边圆环在 ±X 极值两处反向边被切成
    /// 4 条链 [13,11,13,11]、拟合出 90°+75°+90°+75° 四条开口弧）。
    /// <para>
    /// 本用例 = 最小复刻：12 边圆环、两条反向边；修复后应 1 条链、1 条整圆（R 精确、全部段被吸收）。
    /// 整圆还原还依赖消隐侧不把可见轮廓判成 H 碎段（DEFECT-004 机制②/005 的栅格域），本用例
    /// 只喂**纯几何**输入、绕开消隐，单独钉住成链取向这一根因。
    /// </para>
    /// </summary>
    [TestFixture]
    public class D4ChainOrientationTest
    {
        [Test]
        public void MixedOrientationRing_IsChainedAcrossReversedEdges()
        {
            const double radius = 4.0;
            const int n = 12;
            var segments = new List<ViewSegment>(n);

            for (var i = 0; i < n; i++)
            {
                var a0 = 2 * Math.PI * i / n;
                var a1 = 2 * Math.PI * (i + 1) / n;
                var p0 = new ViewPoint2(radius * Math.Cos(a0), radius * Math.Sin(a0));
                var p1 = new ViewPoint2(radius * Math.Cos(a1), radius * Math.Sin(a1));

                // 两条边反向（首尾对调）：复刻记录中「±X 极值处反向边」的最小形态。
                segments.Add(i == 2 || i == 8
                    ? new ViewSegment(p1, p0, true)
                    : new ViewSegment(p0, p1, true));
            }

            var curves = ViewArcFitter.Fit(segments);

            Assert.That(curves.Arcs.Count, Is.EqualTo(1),
                $"12 边圆环（2 条反向）应整体成链并识别为 1 条弧，实际 {curves.Arcs.Count}："
                + string.Join("；", System.Linq.Enumerable.Select(curves.Arcs,
                    a => $"R={a.Radius:F4} sweep={a.SweepDegrees:F2}° closed={a.IsClosed}")));
            Assert.That(curves.Arcs[0].IsClosed, Is.True, "圆环应闭合为整圆");
            Assert.That(curves.Arcs[0].Radius, Is.EqualTo(radius).Within(1e-9), "整圆半径精确");
            Assert.That(curves.Segments, Is.Empty, "全部输入段应被弧吸收");
        }

        [Test]
        public void UniformOrientationRing_IsChainedAsSingleCircle()
        {
            // 对照（当前即正确）：朝向一致的圆环不依赖上述修复路径。
            const double radius = 4.0;
            const int n = 12;
            var segments = new List<ViewSegment>(n);

            for (var i = 0; i < n; i++)
            {
                var a0 = 2 * Math.PI * i / n;
                var a1 = 2 * Math.PI * (i + 1) / n;
                segments.Add(new ViewSegment(
                    new ViewPoint2(radius * Math.Cos(a0), radius * Math.Sin(a0)),
                    new ViewPoint2(radius * Math.Cos(a1), radius * Math.Sin(a1)),
                    true));
            }

            var curves = ViewArcFitter.Fit(segments);

            Assert.That(curves.Arcs.Count, Is.EqualTo(1));
            Assert.That(curves.Arcs[0].IsClosed, Is.True);
            Assert.That(curves.Arcs[0].Radius, Is.EqualTo(radius).Within(1e-9));
            Assert.That(curves.Segments, Is.Empty);
        }
    }
}
