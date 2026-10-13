using System;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// DEFECT-009 最小复现（登记于 tests/DEFECTS.md 未修复表；程序规则：只报告不改 src/）。
    /// <para>
    /// <b><c>ViewProjection.Bounds</c> 仅由 <c>Segments</c> 导出、完全忽略 <c>Arcs</c></b>：
    /// ctor 只把线段端点喂 <see cref="ViewBounds2.Of"/>（<c>src/OpenCSG.NET/ViewProjectorContracts.cs:161-167</c>），
    /// 而管线在弧识别成功后把被弧吸收的折线从 <c>Segments</c> 移除并换成 <c>Arcs</c>
    /// （<c>src/OpenCSG.NET/ViewProjector.cs:112-119</c>）⇒ <b>全弧视图</b>（轴向视图的整圈边缘被
    /// <see cref="ViewArcFitter"/> 整体拟合为一条弧，如圆柱俯视）的包络退化为 <c>(0,0,0,0)</c>
    /// ——即 <see cref="ViewBounds2.Empty"/> 哨兵，与「无几何」不可区分。
    /// </para>
    /// <para>
    /// 期望：ctor 内把 arc 极值并入包络（<c>IsClosed</c> ⇒ 全圆外接盒；开口弧取保守全圆盒）
    /// ⇒ 本组构造的期望包络 ≈(−8,−8,8,8)。
    /// </para>
    /// <para>
    /// 与 D6/D8 同风格：断言钉住**当前（错误）读数**，期望值写在注释与断言消息里；
    /// <c>[Explicit]</c> 使套件保持绿色、缺陷可一行复现（规则 2）：
    /// <c>dotnet test tests/OpenCSG.NET.Tests -c Release --filter "FullyQualifiedName~D9BoundsIgnoreArcsTest"</c>。
    /// 修复后按规则 5 摘标注迁移，并重基线化受影响的 JSON golden（<c>Cylinder_ThreeViews</c> 等，
    /// 经 promote 脚本人工确认）。
    /// </para>
    /// </summary>
    [TestFixture]
    public class D9BoundsIgnoreArcsTest
    {
        /// <summary>
        /// 库级构造（不依赖投影管线）：零线段 + 1 条 R=8 整圆（圆心原点）——真实几何跨度
        /// x、y ∈ [−8, 8]，期望包络 ≈(−8,−8,8,8)；当前读数 (0,0,0,0)。
        /// </summary>
        [Test]
        [Explicit("DEFECT-009: ViewProjection.Bounds 忽略 Arcs——全弧视图包络退化为 (0,0,0,0)（= ViewBounds2.Empty 哨兵）")]
        public void LibraryOnly_AllArcView_BoundsDegradeToEmptySentinel()
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

            // 实际（当前，错误读数）：包络 = (0,0,0,0)。
            // 期望：≈(−8,−8,8,8)（R=8 整圆的外接盒——修法方向：ctor 内把 arc 极值并入包络点集）。
            var bounds = view.Bounds;
            Assert.That(bounds.MinX, Is.EqualTo(0.0), "DEFECT-009 实际读数：MinX = 0（期望 −8）");
            Assert.That(bounds.MinY, Is.EqualTo(0.0), "DEFECT-009 实际读数：MinY = 0（期望 −8）");
            Assert.That(bounds.MaxX, Is.EqualTo(0.0), "DEFECT-009 实际读数：MaxX = 0（期望 +8）");
            Assert.That(bounds.MaxY, Is.EqualTo(0.0), "DEFECT-009 实际读数：MaxY = 0（期望 +8）");
            Assert.That(bounds.Width, Is.EqualTo(0.0), "DEFECT-009 实际读数：Width = 0（期望 16）");
            Assert.That(bounds.Height, Is.EqualTo(0.0), "DEFECT-009 实际读数：Height = 0（期望 16）");

            // 危害的精确形态：包络读数与 Empty 哨兵逐位相同 ⇒ 「有真实几何的视图」与「无几何」不可区分。
            Assert.That(bounds, Is.EqualTo(ViewBounds2.Empty),
                "DEFECT-009：包络 = ViewBounds2.Empty 哨兵（不可区分正是本缺陷的核心危害）");
        }

        /// <summary>
        /// 端到端（真实管线）：圆柱 r=8、高 24（轴沿 CSG Y）、默认 12 段——俯视为轴向视图，
        /// 12 边形顶点的最小二乘圆精确成立 ⇒ 整圈边缘被 <see cref="ViewArcFitter"/> 整体吸收为
        /// 1 条 330° 开口弧（缺 30° &gt; MaxClosingGapDegrees=25° 未补整圆）、Segments=0
        /// ⇒ 包络同样退化为 (0,0,0,0)（期望 ≈(−8,−8,8,8)：弧圆心≈原点、R=8）。
        /// </summary>
        [Test]
        [Explicit("DEFECT-009: ViewProjection.Bounds 忽略 Arcs——圆柱俯视（全弧视图）包络退化 (0,0,0,0)")]
        public void CylinderTop_EndToEnd_BoundsDegradeToEmptySentinel()
        {
            var view = ViewBridge.ProjectViews(
                Solids.Cylinder(8, 24, center: true), new[] { ViewDirections.Top })[0];

            Assert.That(view.Direction.Name, Is.EqualTo(ViewDirections.Top.Name));
            Assert.That(view.Segments.Count, Is.EqualTo(0),
                "DEFECT-009 实际读数：俯视 Segments=0（折线段全被弧吸收）");
            Assert.That(view.Arcs.Count, Is.EqualTo(1), "DEFECT-009 实际读数：俯视 Arcs=1");

            var arc = view.Arcs[0];
            Assert.That(arc.Radius, Is.EqualTo(8.0), "DEFECT-009 实际读数：弧半径 R=8");
            Assert.That(Math.Abs(arc.Center.X), Is.LessThan(1e-12), "前置：弧圆心 x ≈ 0");
            Assert.That(Math.Abs(arc.Center.Y), Is.LessThan(1e-12), "前置：弧圆心 y ≈ 0");
            Assert.That(arc.IsClosed, Is.False, "DEFECT-009 实际读数：330° 开口弧（未补整圆）");
            Assert.That(arc.SweepDegrees, Is.EqualTo(330.0).Within(1e-9),
                "DEFECT-009 实际读数：sweep=330°（缺 30° > MaxClosingGapDegrees=25°）");

            // 实际（当前，错误读数）：(0,0,0,0)（= ViewBounds2.Empty 哨兵）。
            // 期望：≈(−8,−8,8,8)——该 330° 弧覆盖 ±X/±Y 四个极值点，修复后应得整圆外接盒同款读数。
            var bounds = view.Bounds;
            Assert.That(bounds, Is.EqualTo(ViewBounds2.Empty),
                "DEFECT-009 实际读数：俯视包络 = (0,0,0,0)（期望 ≈(−8,−8,8,8)：真实几何跨 16×16）");
            Assert.That(bounds.Width, Is.EqualTo(0.0), "DEFECT-009 实际读数：Width=0（期望 16）");
            Assert.That(bounds.Height, Is.EqualTo(0.0), "DEFECT-009 实际读数：Height=0（期望 16）");
        }
    }
}
