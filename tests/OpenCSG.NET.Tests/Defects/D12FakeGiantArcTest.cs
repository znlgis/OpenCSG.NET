using System;
using System.Collections.Generic;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// DEFECT-012 回归（2026-10-14 修复）：<b>圆角簇 + 长直弦</b>的折线链不得被 <see cref="ViewArcFitter"/>
    /// 拟合成巨半径伪弧。
    /// <para>
    /// 原缺陷：<c>TryBuildArc</c> 的弦中点（矢高）判据 <c>≤ max(tolerance, 5%·R)</c> 随 R 线性放宽
    /// —— R=278.56 时放行 13.9mm，长弦中点偏差 9.2mm 照样通过（CPurlin 实锤：R≈279 弧在 142mm
    /// 直边中段外拱 9.2mm，弧象限极点越出 CSG 包络、左视 Bounds 由 64×152 外扩到 ≈73.96×156.60）。
    /// 「单条长弦 + 两端内弯短链」与「粗采样真弧」在单弦几何上不可分（弦矢比同为 L/8R）。
    /// </para>
    /// <para>
    /// 修复：<b>角步分布均匀性守卫</b>（<c>TryBuildArc</c>）—— 顶点按**弧序**逐顶角步统计
    /// （从最大间隙后的首顶点起、跨 0° 回绕累进），要求 <c>最大角步 ≤ 1.6 × 平均角步</c>：
    /// 真弧链按镶嵌角步均匀采样（12/24/48 边形比值 = 1；15°/30° 混采样 ≈1.33），伪链的长弦
    /// 一步跨过大幅张角（合成复刻 ≈4.3、CPurlin 实锤 ≈9）。本文件 = 合成最小复刻：6 顶点链
    /// （±17° / ±15.5° / ±asin(0.25) 全在 R=200 圆上、中间一条 100mm 直弦）修复前被整体吸收为
    /// 1 条 R=200、扫掠 34° 的弧（弧 0° 极点 x=50.0 对折线最右点 x≈43.649 外拱 6.35mm）。
    /// </para>
    /// </summary>
    [TestFixture]
    public class D12FakeGiantArcTest
    {
        /// <summary>
        /// 对照（当前即正确）：纯共线顶点链不会被拟合 —— Kåsa 行列式奇异/巨 R 由曲率显著性判据卡掉。
        /// 伪弧不是「直线被当成弧」，而是**圆角簇把长弦链"垫弯"**：短段端簇参与穿接，长弦中段失守。
        /// </summary>
        [Test]
        public void StraightChain_IsKeptAsSegments()
        {
            var segments = new List<ViewSegment>();
            for (var y = -50.0; y < 50.0; y += 25.0)
                segments.Add(new ViewSegment(new ViewPoint2(0, y), new ViewPoint2(0, y + 25.0), true));

            var curves = ViewArcFitter.Fit(segments);

            Assert.That(curves.Arcs, Is.Empty, "共线链不得被拟合为弧（曲率显著性判据 / Kåsa 奇异的既有防线）");
            Assert.That(curves.Segments.Count, Is.EqualTo(segments.Count), "共线链全部保留为线段");
        }

        /// <summary>
        /// 回归（修复后行为）：6 顶点 / 5 段链 —— 两簇端部短段（等效 CPurlin 的 R5 圆角簇）
        /// + 一条 100mm 直弦，全部顶点精确落在一个 R=200 的圆上（构造即「粗采样假想弧」，
        /// 与真弧链在单弦几何上不可分）。修复后：整链拒拟合（角步不均匀：长弦步 28.96° 对
        /// 平均 ~6.8°），5 段原样保留为折线（「宁缺勿滥」）。
        /// <para>
        /// 构造数字：圆心 (−150,0)、R=200，顶点角 ±17° / ±15.5° / ±asin(0.25)
        ///（= 弦端点 y=±50 ⇒ 直弦长 100，宽 2.39，对角线 ≈117）。
        /// </para>
        /// </summary>
        [Test]
        public void CornerClustersPlusLongChord_NotFittedAsArc()
        {
            const double cx = -150.0;
            const double radius = 200.0;
            var halfChordAngle = Math.Asin(0.25);                 // 弦端点 y=±50

            static ViewPoint2 OnCircle(double cx, double radius, double angle)
                => new ViewPoint2(cx + radius * Math.Cos(angle), radius * Math.Sin(angle));

            var segments = new List<ViewSegment>
            {
                new ViewSegment(OnCircle(cx, radius, 17.0 * Math.PI / 180), OnCircle(cx, radius, 15.5 * Math.PI / 180), true),
                new ViewSegment(OnCircle(cx, radius, 15.5 * Math.PI / 180), OnCircle(cx, radius, halfChordAngle), true),
                new ViewSegment(OnCircle(cx, radius, halfChordAngle), OnCircle(cx, radius, -halfChordAngle), true),
                new ViewSegment(OnCircle(cx, radius, -halfChordAngle), OnCircle(cx, radius, -15.5 * Math.PI / 180), true),
                new ViewSegment(OnCircle(cx, radius, -15.5 * Math.PI / 180), OnCircle(cx, radius, -17.0 * Math.PI / 180), true),
            };

            var curves = ViewArcFitter.Fit(segments);

            Assert.That(curves.Arcs, Is.Empty,
                "修复后：角步不均匀（长弦步 28.96° vs 平均 6.8°，比值 4.3 > 1.6）的链不得拟合成弧");
            Assert.That(curves.Segments.Count, Is.EqualTo(5), "5 段全部保留为折线");

            // 反证（修复前此链被吸收为 1 条 R=200、扫掠 34° 的弧，弧 0° 极点 x = 50.0 越出折线
            // 最右点 x ≈ 43.649 达 6.35mm）：修复后不得再有任何弧越出折线包络。
            double polylineMaxX = double.MinValue, polylineMinX = double.MaxValue;
            foreach (var s in curves.Segments)
            {
                polylineMaxX = Math.Max(polylineMaxX, Math.Max(s.Start.X, s.End.X));
                polylineMinX = Math.Min(polylineMinX, Math.Min(s.Start.X, s.End.X));
            }

            foreach (var arc in curves.Arcs)
                Assert.That(arc.Center.X + arc.Radius, Is.LessThanOrEqualTo(polylineMaxX));
            Assert.That(polylineMaxX, Is.EqualTo(43.64916731037087).Within(1e-9), "折线最右点不变（几何未被替换）");
            Assert.That(polylineMinX, Is.EqualTo(41.260951192607).Within(1e-9), "折线最左点不变");
        }
    }
}
