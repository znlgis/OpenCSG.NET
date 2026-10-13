using System;
using System.Collections.Generic;
using System.Linq;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// L5 集成：凸包周长 oracle 在**真实管线输出**上的成立表（口径与 <c>Tier2AnalyticTest</c> 一致，
    /// 本文件把对象扩展到凸多边形柱与圆柱并把「成立/不成立」逐格实测后写死）。
    /// <para>
    /// <b>oracle 与前置条件</b>：对凸实体，可见段总长 == 投影顶点的 2D 凸包周长
    /// （<see cref="ConvexHull"/> 为独立实现），当且仅当<b>没有可见特征边投影到轮廓内部</b>。
    /// 轴向视图（观察方向与轮廓面平行/垂直关系成立）满足；凸多边形柱的横向视图天然不满足
    /// （前向侧面的相邻棱边投影落在轮廓内部）—— 这不是缺陷，是 oracle 的几何适用域边界。
    /// </para>
    /// <para>
    /// <b>成立表（探针 <c>artifacts/planD/d5probe</c> 实测，ratio = 可见段总长 / 凸包周长）</b>：
    /// <list type="bullet">
    /// <item>Cube10：主/俯/左 40/40/40 = 1（三视全成立）；Box10x20x30：100/80/60 = 1（三视全成立，本文件作对照）；</item>
    /// <item>PrismXY 正六棱柱（R=10、旋转 0.3 rad、拉伸 6）：左视 60.0 = 1（轴向视图：轮廓 = 正六边形，
    /// 6×10=60 恰为解析周长，可见轮廓边 = 6 条六边形边）；主视 ratio=1.2109228659164657、
    /// 俯视 1.0986599115617357（横向视图，见下）；</item>
    /// <item>PrismXY 凸五边形棱柱（(0,0),(30,4),(22,18),(6,20),(-6,9)、拉伸 6）：左视 89.60999731652899 = 1；
    /// 主视 1.1145316377171215、俯视 1.036823085420279（横向视图）；</item>
    /// <item>Solids.Cylinder 轴向视图（俯视）：<b>不成立且非几何原因</b> —— 实测 Segments=0、Arcs=1
    /// （R=8、330° 开口弧）⇒ 可见段总长 0 ≠ 凸包周长 49.69325665968398。
    /// 即 DEFECT-004 机制①（成链 Grow 在朝向反向边处截断、330°&lt;360° 无法补整圆）的行为域，
    /// 由 <see cref="CylinderAxialView_RimAbsorbedIntoArcs_OracleNotApplicable"/> 钉住当前读数；
    /// DEFECT-009 已记录同一弧导致 Bounds 退化。横向视图 ratio=1.532759951958693（母线轮廓 +
    /// 可见缘弦段，几何原因不成立）。</item>
    /// <item>Solids.Sphere：视线切向视图（球轮廓为掠射）ratio≈2.51/2.52/2.13（碎段化，DEFECT-004 机制②域），
    /// 按 brief 口径整类排除，不设断言（数值见报告）。</item>
    /// </list>
    /// </para>
    /// </summary>
    [TestFixture]
    public class ConvexHullPipelineTest
    {
        /// <summary>可见段总长 vs 凸包周长的相对容差（brief 口径：expected × 1e-6；实测 ratio 逐位 = 1）。</summary>
        const double RelativeTolerance = 1e-6;

        static Solid BuildEntity(string entity) => entity switch
        {
            "Cube10" => Solids.Cube(10, center: true),
            "Box10x20x30" => Solids.Cube(new Vector3D(10, 20, 30), center: true),
            "PrismHex" => CsgEvaluator.Evaluate(CsgHelpers.PrismXY(RegularPolygon(6, 10.0), 0, 6)),
            "PrismPent" => CsgEvaluator.Evaluate(CsgHelpers.PrismXY(
                new[] { (0.0, 0.0), (30.0, 4.0), (22.0, 18.0), (6.0, 20.0), (-6.0, 9.0) }, 0, 6)),
            _ => throw new ArgumentException($"未知实体 {entity}", nameof(entity)),
        };

        static IEnumerable<TestCaseData> ValidCases()
        {
            foreach (var entity in new[] { "Cube10", "Box10x20x30" })
                foreach (var view in new[] { "主视", "俯视", "左视" })
                    yield return new TestCaseData(entity, view).SetName($"Hull_{entity}_{view}");

            // 轴向视图（观察方向 = 拉伸轴）：轮廓即断面多边形自身。
            yield return new TestCaseData("PrismHex", "左视").SetName("Hull_PrismHex_左视_轴向");
            yield return new TestCaseData("PrismPent", "左视").SetName("Hull_PrismPent_左视_轴向");
        }

        [TestCaseSource(nameof(ValidCases))]
        public void VisibleSegmentLength_EqualsProjectedHullPerimeter(string entity, string viewName)
        {
            var solid = BuildEntity(entity);
            var view = ViewOf(solid, viewName);

            var expected = ConvexHull.PerimeterOf(ProjectedVertices(solid, view.Direction));
            Assert.That(expected, Is.GreaterThan(0.0), $"{entity}/{viewName}：投影凸包周长必须为正（防空网格真空通过）");

            // 读数口径前提：本表全部组合的轮廓未被弧吸收（实测 ArcCount=0）——否则「可见段总长」不含弧长。
            Assert.That(view.ArcCount, Is.EqualTo(0),
                $"{entity}/{viewName}：实测 ArcCount=0；若轮廓被弧吸收，本表读数口径需重审");
            Assert.That(view.Segments.Count, Is.GreaterThan(0));

            var actual = SumVisibleSegmentLength(view);
            Assert.That(actual, Is.EqualTo(expected).Within(expected * RelativeTolerance),
                $"{entity}/{viewName}：可见段总长 {actual:R} 应为投影凸包周长 {expected:R}"
                + $"（实测 ratio=1；仅轴向/正交视角满足 oracle 前置条件）");
        }

        static IEnumerable<TestCaseData> TransversalCases()
        {
            yield return new TestCaseData("PrismHex", "主视").SetName("HullPrecondition_PrismHex_主视");
            yield return new TestCaseData("PrismHex", "俯视").SetName("HullPrecondition_PrismHex_俯视");
            yield return new TestCaseData("PrismPent", "主视").SetName("HullPrecondition_PrismPent_主视");
            yield return new TestCaseData("PrismPent", "俯视").SetName("HullPrecondition_PrismPent_俯视");
        }

        /// <summary>
        /// 凸多边形柱的横向视图：oracle 前置条件（无可见特征边投影到轮廓内部）不满足 ⇒
        /// 可见段总长 **超过** 凸包周长。实测 ratios：PrismHex 主视 1.2109228659164657、俯视 1.0986599115617357；
        /// PrismPent 主视 1.1145316377171215、俯视 1.036823085420279。
        /// 本用例把「不成立」写成显式读数钉（防止未来被静默当作通过），不是缺陷复现
        /// （凸多面体横向视图的可见内棱是几何事实，见类注释）。
        /// </summary>
        [TestCaseSource(nameof(TransversalCases))]
        public void Prism_TransversalViews_ExceedHullPerimeter_RecordedPrecondition(string entity, string viewName)
        {
            var solid = BuildEntity(entity);
            var view = ViewOf(solid, viewName);

            var hull = ConvexHull.PerimeterOf(ProjectedVertices(solid, view.Direction));
            Assert.That(hull, Is.GreaterThan(0.0));

            var actual = SumVisibleSegmentLength(view);
            var ratio = actual / hull;

            Assert.That(ratio, Is.GreaterThan(1.0 + 1e-3),
                $"{entity}/{viewName}：实测 ratio={ratio:R} 应显著 > 1（可见内棱投影在轮廓内部 ⇒ oracle 不适用）");
            Assert.That(ratio, Is.LessThan(2.0),
                $"{entity}/{viewName}：实测 ratio={ratio:R} 超出该几何的量级（1.04–1.22），疑似其他变化");
        }

        /// <summary>
        /// 圆柱轴向视图（<c>Solids.Cylinder(8, 24, center: true)</c> 的俯视）：
        /// 实测 Segments=0、Arcs=1（R=8、330° 开口弧）⇒ 可见段总长 0 ≠ 凸包周长 49.69325665968398。
        /// 即 <b>DEFECT-004</b> 机制①的行为域（成链在朝向反向边处截断 ⇒ 缺 30° 无法补整圆；
        /// 与 <c>D9BoundsIgnoreArcsTest</c> 钉住的同一读数一致），故 oracle 在此不适用；
        /// 本用例只钉当前读数 + 记录不适用性。修复 DEFECT-004 后本断言应重基线化
        /// （原文：330° 开口弧 ⇒ 本表改引修复后的整圆口径）。
        /// </summary>
        [Test]
        public void CylinderAxialView_RimAbsorbedIntoArcs_OracleNotApplicable()
        {
            var solid = Solids.Cylinder(8, 24, center: true);
            var views = ViewBridge.ProjectViews(solid, ViewDirections.OrthographicThree);
            var view = views.Single(v => v.Direction.Name == "俯视");

            Assert.That(view.Segments.Count, Is.EqualTo(0),
                "DEFECT-004 当前读数：轴向视图折线段全被弧吸收（Segments=0）");
            Assert.That(view.Arcs.Count, Is.EqualTo(1), "DEFECT-004 当前读数：Arcs=1");
            var arc = view.Arcs[0];
            Assert.That(arc.Radius, Is.EqualTo(8.0).Within(1e-9), "DEFECT-004 当前读数：R=8");
            Assert.That(arc.SweepDegrees, Is.EqualTo(330.0).Within(1e-6),
                "DEFECT-004 当前读数：330° 开口弧（缺 30° > MaxClosingGapDegrees=25° ⇒ 未补整圆）");
            Assert.That(Math.Abs(arc.Center.X), Is.LessThan(1e-12));
            Assert.That(Math.Abs(arc.Center.Y), Is.LessThan(1e-12));

            // oracle 的不适用性：可见段总长 = 0，凸包周长 = 内接 12 边形周长（实测 49.69325665968398）。
            var hull = ConvexHull.PerimeterOf(ProjectedVertices(solid, view.Direction));
            var visible = SumVisibleSegmentLength(view);
            Assert.That(visible, Is.EqualTo(0.0), "DEFECT-004 域：可见段总长为 0（几何全在弧表里）");
            Assert.That(hull, Is.GreaterThan(0.0), "凸包周长（真实几何投影）应为正");
            Assert.That(hull, Is.EqualTo(49.69325665968398).Within(1e-9), "实测内接 12 边形周长钉");
        }

        static ViewProjection ViewOf(Solid solid, string viewName)
        {
            var views = ViewBridge.ProjectViews(solid, ViewDirections.OrthographicThree);
            var view = views.SingleOrDefault(v => v.Direction.Name == viewName);
            Assert.That(view, Is.Not.Null, $"视图 {viewName} 不存在（三视图只有 主视/俯视/左视）");
            return view!;
        }

        static double SumVisibleSegmentLength(ViewProjection view)
        {
            var total = 0.0;
            foreach (var segment in view.Segments)
                if (segment.IsVisible)
                    total += segment.Length;
            return total;
        }

        /// <summary>
        /// 把实体全部顶点投到指定视图的 2D 平面（与 <c>Tier2AnalyticTest</c> 相同口径：
        /// 走网格坐标后用方向的 Right/Up 取轴，测试侧独立手算 —— oracle 的独立性所在）。
        /// </summary>
        static List<ViewPoint2> ProjectedVertices(Solid solid, ViewDirection direction)
        {
            var mesh = CsgMesh.TrianglesOf(solid);
            var points = new List<ViewPoint2>(mesh.Count * 3);

            foreach (var triangle in mesh)
                foreach (var p in new[] { triangle.A, triangle.B, triangle.C })
                {
                    var v = new ViewVector(p.X, p.Y, p.Z);
                    points.Add(new ViewPoint2(v.Dot(direction.Right), v.Dot(direction.Up)));
                }

            return points;
        }

        static (double X, double Y)[] RegularPolygon(int sides, double radius)
        {
            var points = new (double X, double Y)[sides];
            for (var i = 0; i < sides; i++)
            {
                var angle = 2 * Math.PI * i / sides + 0.3;
                points[i] = (radius * Math.Cos(angle), radius * Math.Sin(angle));
            }
            return points;
        }
    }
}
