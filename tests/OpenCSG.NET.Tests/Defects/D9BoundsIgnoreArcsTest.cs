using System;
using System.Linq;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// DEFECT-009 回归（2026-10-14 修复；登记于 tests/DEFECTS.md 已修复表）。
    /// <para>
    /// 原缺陷：<c>ViewProjection.Bounds</c> 仅由 <c>Segments</c> 导出、忽略 <c>Arcs</c> ——
    /// 管线把被弧吸收的折线从 <c>Segments</c> 移除并改放 <c>Arcs</c> 后，<b>全弧视图</b>
    /// （如圆柱俯视：整圈边缘被 <see cref="ViewArcFitter"/> 整体拟合为一条弧）的包络退化为
    /// <c>(0,0,0,0)</c>（= <see cref="ViewBounds2.Empty"/> 哨兵，与「无几何」不可区分）。
    /// </para>
    /// <para>
    /// 修复：ctor 内把每条弧的极值并入包络点集 —— 整圆取全圆外接盒（<c>Center ± Radius</c>），
    /// 开口弧取**精确弧盒**（两端点 + 扫掠范围内的 0/90/180/270° 象限极值点；不用保守全圆盒，
    /// 巨半径浅弧下全圆盒会破坏落图定位，依据见实现处注释）。受影响的 JSON golden 已按修复后
    /// 输出重基线化。
    /// </para>
    /// </summary>
    [TestFixture]
    public class D9BoundsIgnoreArcsTest
    {
        /// <summary>
        /// 库级构造（不依赖投影管线）：零线段 + 1 条 R=8 整圆（圆心原点）——真实几何跨度
        /// x、y ∈ [−8, 8]，期望包络 = (−8,−8,8,8)。
        /// </summary>
        [Test]
        public void LibraryOnly_AllArcView_BoundsIncludeArcExtent()
        {
            var view = new ViewProjection(
                ViewDirections.Top,
                Array.Empty<ViewSegment>(),
                new[] { new ViewArc(new ViewPoint2(0, 0), 8, 0, 2 * Math.PI, isVisible: true, isClosed: true) });

            // 前置：确为「全弧视图」（零线段 + 1 条整圆 R=8）。
            Assert.That(view.Segments.Count, Is.EqualTo(0));
            Assert.That(view.Arcs.Count, Is.EqualTo(1));
            Assert.That(view.Arcs[0].IsClosed, Is.True);
            Assert.That(view.Arcs[0].Radius, Is.EqualTo(8.0));

            var bounds = view.Bounds;
            Assert.That(bounds.MinX, Is.EqualTo(-8.0).Within(1e-12), "修复后：包络涵盖弧几何（MinX = cx − R）");
            Assert.That(bounds.MinY, Is.EqualTo(-8.0).Within(1e-12), "修复后：包络涵盖弧几何（MinY = cy − R）");
            Assert.That(bounds.MaxX, Is.EqualTo(8.0).Within(1e-12), "修复后：包络涵盖弧几何（MaxX = cx + R）");
            Assert.That(bounds.MaxY, Is.EqualTo(8.0).Within(1e-12), "修复后：包络涵盖弧几何（MaxY = cy + R）");
            Assert.That(bounds.Width, Is.EqualTo(16.0).Within(1e-12));
            Assert.That(bounds.Height, Is.EqualTo(16.0).Within(1e-12));

            // 危害的精确形态已消除：包络不再等于 Empty 哨兵（「有真实几何的视图」与「无几何」可区分）。
            Assert.That(bounds, Is.Not.EqualTo(ViewBounds2.Empty),
                "修复后：全弧视图包络 ≠ ViewBounds2.Empty 哨兵");
        }

        /// <summary>
        /// 端到端（真实管线）：圆柱 r=8、高 24（轴沿 CSG Y）、默认 12 段——俯视为轴向视图，
        /// 整圈边缘被拟合为 1 条弧（Segments=0）；DEFECT-009 修复后包络 = 弧盒 ≈(−8,−8,8,8)
        /// （弧圆心≈原点、R=8）。**DEFECT-004 机制①修复后重钉**：该弧由 330° 开口弧变为
        /// 整圆 360°/closed=true（成链取向携带）——全圆盒口径不变。
        /// </summary>
        [Test]
        public void CylinderTop_EndToEnd_BoundsIncludeConservativeArcBox()
        {
            var view = ViewBridge.ProjectViews(
                Solids.Cylinder(8, 24, center: true), new[] { ViewDirections.Top })[0];

            Assert.That(view.Direction.Name, Is.EqualTo(ViewDirections.Top.Name));
            Assert.That(view.Segments.Count, Is.EqualTo(0), "前置：俯视折线段全被弧吸收");
            Assert.That(view.Arcs.Count, Is.EqualTo(1), "前置：俯视 Arcs=1");

            var arc = view.Arcs[0];
            Assert.That(arc.Radius, Is.EqualTo(8.0), "前置：弧半径 R=8");
            Assert.That(Math.Abs(arc.Center.X), Is.LessThan(1e-12), "前置：弧圆心 x ≈ 0");
            Assert.That(Math.Abs(arc.Center.Y), Is.LessThan(1e-12), "前置：弧圆心 y ≈ 0");
            Assert.That(arc.IsClosed, Is.True, "前置：整圆（DEFECT-004 机制①修复后；修复前为 330° 开口弧）");
            Assert.That(arc.SweepDegrees, Is.EqualTo(360.0).Within(1e-9), "前置：sweep=360°");


            // 修复后：包络 = 弧盒（真实几何跨 16×16）。
            var bounds = view.Bounds;
            Assert.That(bounds.MinX, Is.EqualTo(-8.0).Within(1e-9), "修复后：俯视包络 MinX = cx − R");
            Assert.That(bounds.MinY, Is.EqualTo(-8.0).Within(1e-9), "修复后：俯视包络 MinY = cy − R");
            Assert.That(bounds.MaxX, Is.EqualTo(8.0).Within(1e-9), "修复后：俯视包络 MaxX = cx + R");
            Assert.That(bounds.MaxY, Is.EqualTo(8.0).Within(1e-9), "修复后：俯视包络 MaxY = cy + R");
            Assert.That(bounds.Width, Is.EqualTo(16.0).Within(1e-9));
            Assert.That(bounds.Height, Is.EqualTo(16.0).Within(1e-9));
            Assert.That(bounds, Is.Not.EqualTo(ViewBounds2.Empty), "修复后：包络 ≠ Empty 哨兵");
        }

        /// <summary>
        /// 混合几何对照：线段 + 弧都要进包络，且开口弧用**精确弧盒**（端点 + 扫掠内轴极值点）。
        /// 取从 0° 到 180° 的半圆（C=(−5,−5)、R=3）：弧上点 y ∈ [−5, −2]、x ∈ [−8, −2]
        /// （0°/180° 端点在 y=−5，90° 极值点在 (−5,−2)）。
        /// </summary>
        [Test]
        public void MixedSegmentsAndArcs_BoundsCoverBoth()
        {
            var view = new ViewProjection(
                ViewDirections.Front,
                new[] { new ViewSegment(new ViewPoint2(10, 10), new ViewPoint2(20, 15), true) },
                new[] { new ViewArc(new ViewPoint2(-5, -5), 3, 0, Math.PI, isVisible: true, isClosed: false) });

            var bounds = view.Bounds;
            Assert.That(bounds.MinX, Is.EqualTo(-8.0).Within(1e-12), "弧 180° 端点 x = −5 − 3");
            Assert.That(bounds.MinY, Is.EqualTo(-5.0).Within(1e-12), "弧两端点 y = −5（精确弧盒不取全圆 −8）");
            Assert.That(bounds.MaxX, Is.EqualTo(20.0).Within(1e-12), "线段端点 20 > 弧盒 MaxX = −2");
            Assert.That(bounds.MaxY, Is.EqualTo(15.0).Within(1e-12), "线段端点 15 > 弧盒 MaxY = −2");
        }
    }
}
