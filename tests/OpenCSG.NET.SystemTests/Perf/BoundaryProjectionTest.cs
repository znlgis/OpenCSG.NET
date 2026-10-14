using System;
using System.Collections.Generic;
using System.Linq;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// L6 边界（Plan E / Task 1.4 前两条）：薄板 z-fighting 行为钉、MaxEdgeSamples=4000 长棱边界。
    /// <para>
    /// 全部读数为**实测钉**（2026-10-14，探针 <c>artifacts/planE/probe</c>，同机同输入逐段 dump 后取数）；
    /// 预算 = 实测 × 5（协议与基准写在每条断言消息里，见 <see cref="BudgetClock"/>）。
    /// 行为钉的含义：读数由当前实现确定，任何变化都要求人工重钉并在报告里说明 —— 不得直接改数字。
    /// </para>
    /// </summary>
    [TestFixture]
    [Category("Perf")]
    public class BoundaryProjectionTest
    {
        // ── 用例 1：薄板 + 0.01mm 凸台 ───────────────────────────────────────

        /// <summary>薄板夹具的三视图预算（ms）：实测 36.3ms（三视图 ×10 轮，采样 min，5 次采样） × 5。</summary>
        const double ThinPlateBudgetMs = 181.5;

        [Test]
        public void ThinPlate_ThinBoss_ProjectionBehaviorPinned_AndBudget()
        {
            var solid = PerfWorkloads.ThinPlateWithBoss();

            // 前置：凸台拓扑在 Union 后保留（实测 14 个多边形）。若凸台构造退化（完全没入板内），
            // Union 会把它整体吞掉只剩 6 个多边形 —— 那样测的就不是投影行为，直接失败重钉。
            Assert.That(solid.Polygons.Count, Is.EqualTo(14),
                $"Union 后面数实测 {solid.Polygons.Count}（钉值 14）—— 夹具/布尔路径已变，须重钉");

            var views = ViewBridge.ProjectViews(solid, ViewDirections.OrthographicThree);
            var front = Named(views, "主视");
            var top = Named(views, "俯视");
            var left = Named(views, "左视");

            // ① 主视/俯视：0.01mm 凸台**无任何痕迹**（丢边）。
            // 实测：主视 5 段（4V+1H，hiddenLen 998.891626）；俯视 5 段（4V+1H，hiddenLen 1000.000000）。
            // 凸台的外露轮廓本应在 h=−10.01 处留下 v∈[−50,50] 的 100mm 线，但被
            // ViewSegmentCleanup 的长度阈值（对角 1414mm×1e-4 = 0.1414mm）先丢弃、凸台上表面的
            // 轮廓线与板轮廓（h=−10）只差 0.01mm ≪ 共线合并容差（阈值×4 = 0.566mm）而被并入板轮廓
            // —— 与 DEFECT-004/005 族同源：亚像素/亚阈值特征在栅格与清理量化下整体消失（此处是清理端）。
            AssertViewPin(front, viewName: "主视", expectedVisible: 4, expectedHidden: 1, expectedHiddenLength: 998.8916);
            AssertViewPin(top, viewName: "俯视", expectedVisible: 4, expectedHidden: 1, expectedHiddenLength: 1000.0);
            AssertOnOutlineOnly(front, viewName: "主视", outlineH: ThinPlateThicknessHalf);
            AssertOnOutlineOnly(top, viewName: "俯视", outlineH: ThinPlateThicknessHalf);

            // ② 左视（沿板厚方向看）：凸台可见（足迹 ±50 的两条 100mm 线），但 Union 的分解缝
            //    在 ±50 处留下**贯穿整板的 1000mm 线**（实测 2 条）—— 这是网格拓扑伪影（CSG 求值把
            //    板的大面按凸台足迹切成多片、T 接缝在三角汤里成为单侧边 ⇒ 投影器按「边界边」放行），
            //    与 DEFECT-004/005 的栅格量化无关，单独在此钉出，防被当成同族问题漏掉。
            //    实测：10 段（8V+2H），hiddenLen 198.790111。
            AssertViewPin(left, viewName: "左视", expectedVisible: 8, expectedHidden: 2, expectedHiddenLength: 198.7901);

            var seams = left.Segments.Count(s => s.IsVisible && s.Length > 999.9
                && Math.Abs(Math.Abs(s.Start.Y) - 50.0) < 0.05);
            Assert.That(seams, Is.EqualTo(2),
                $"左视 ±50 处贯穿整板（1000mm）的分解缝线实测 {seams} 条（钉值 2）");

            var footprint = left.Segments.Count(s => s.IsVisible && Math.Abs(s.Length - 100.0) < 0.05);
            Assert.That(footprint, Is.EqualTo(2),
                $"左视凸台足迹（100mm 竖线）实测 {footprint} 条（钉值 2）");

            // ③ 不挂起预算：三视图 ×10 轮（实测 36.3ms，min of 5 采样）。
            var measured = BudgetClock.MinMs(3, 10,
                () => ViewBridge.ProjectViews(solid, ViewDirections.OrthographicThree));
            TestContext.Progress.WriteLine($"[Perf] 薄板凸台三视图 ×10：实测 {measured:F1}ms / 预算 {ThinPlateBudgetMs:F1}ms");
            BudgetClock.AssertWithinBudget("薄板凸台三视图 ×10", measured, ThinPlateBudgetMs,
                "3 采样 × 每采样 10 轮三视图，取 min", "36.3ms（探针同协议实测）");
        }

        // ── 用例 2：1e6 长圆柱的 MaxEdgeSamples 边界 ─────────────────────────

        /// <summary>1e6 长圆柱的三视图预算（ms）：实测 33.9ms（三视图 ×10 轮，采样 min，5 次采样） × 5。</summary>
        const double LongCylinderBudgetMs = 169.5;

        /// <summary>薄板半厚（mm）：主视/俯视里板轮廓的 |h|。</summary>
        const double ThinPlateThicknessHalf = PerfWorkloads.ThinPlateThickness / 2;

        [Test]
        public void LongCylinder_MaxEdgeSamplesBoundary_Pinned_AndBudget()
        {
            var solid = PerfWorkloads.LongCylinder();
            var triangles = ViewBridge.ToViewTriangles(solid);

            Assert.That(solid.Polygons.Count, Is.EqualTo(36), "1e6 圆柱面数实测 36（钉值）");
            Assert.That(triangles.Count, Is.EqualTo(48), "三角化后 48 个三角形（钉值）");

            // ① 采样数分析（复刻口径见 CandidateSampleStats）：1e6 长棱在三个视图里的采样数
            //    实测 901 / 234 / 901，**均低于 4000** —— MaxEdgeSamples 上限对闭合实体是不可达的
            //    结构结论：候选边（轮廓/锐/边界）的端点必在「正面三角形」的屏面包络内
            //    （轮廓边必属一个正面三角形、锐边两侧都正面），故采样数 ≤ √2 × 900 ≈ 1273；
            //    1e6 长棱之所以只采 901 个点，是因为栅格单元 cell = 屏面最大跨度/900 与棱长同步放大。
            //    4000 上限只在「开放网格的边界边投影远大于正面包络」时才咬合（本例不触发）。
            AssertSamplePin(triangles, ViewDirections.Front, viewName: "主视", expectedCandidates: 19, expectedMaxSamples: 901);
            AssertSamplePin(triangles, ViewDirections.Top, viewName: "俯视", expectedCandidates: 12, expectedMaxSamples: 234);
            AssertSamplePin(triangles, ViewDirections.Left, viewName: "左视", expectedCandidates: 19, expectedMaxSamples: 901);

            // ② 实际投影：不挂起 + 拓扑计数实测钉。
            //    主视/左视 6 段（3V+3H，hiddenLen 2000866.025 —— 两条 1e6 轴向棱的远侧全藏 + 端盖弦）；
            //    俯视（轴向视图）0 段并识别出 1 条 330° 开口弧（R=500，非整圆）。
            var projector = new ViewProjector(triangles);

            var front = projector.ProjectView(ViewDirections.Front);
            AssertViewPin(front, viewName: "主视", expectedVisible: 3, expectedHidden: 3, expectedHiddenLength: 2000866.025);

            var top = projector.ProjectView(ViewDirections.Top);
            Assert.That(top.Segments.Count, Is.EqualTo(0), "俯视（轴向）实测 0 段（钉值）");
            Assert.That(top.Arcs.Count, Is.EqualTo(1), "俯视识别出 1 条弧（钉值）");
            Assert.That(top.Arcs[0].Radius, Is.EqualTo(500.0).Within(0.01), "弧半径 = 圆柱半径（钉值 500）");
            Assert.That(top.Arcs[0].SweepDegrees, Is.EqualTo(330.0).Within(0.5),
                "弧扫掠实测 330°（12 边形端盖弦只补到 330°，钉值）");
            Assert.That(top.Arcs[0].IsClosed, Is.False, "非整圆（钉值；DEFECT-004 族现象）");
            Assert.That(top.Bounds.Width, Is.EqualTo(0.0).Within(1e-9),
                "俯视包络读 (0,0,0,0) ——「全弧视图」包络退化哨兵，即 DEFECT-009（只报告）；"
                + "若该缺陷被修复，此钉必须重钉为 ≈(−500,−500)–(500,500)");

            var left = projector.ProjectView(ViewDirections.Left);
            AssertViewPin(left, viewName: "左视", expectedVisible: 3, expectedHidden: 3, expectedHiddenLength: 2000866.025);

            // ③ 不挂起预算：三视图 ×10 轮（实测 33.9ms，min of 5 采样）。
            var measured = BudgetClock.MinMs(3, 10,
                () => ViewBridge.ProjectViews(solid, ViewDirections.OrthographicThree));
            TestContext.Progress.WriteLine($"[Perf] 1e6 长圆柱三视图 ×10：实测 {measured:F1}ms / 预算 {LongCylinderBudgetMs:F1}ms");
            BudgetClock.AssertWithinBudget("1e6 长圆柱三视图 ×10", measured, LongCylinderBudgetMs,
                "3 采样 × 每采样 10 轮三视图，取 min", "33.9ms（探针同协议实测）");
        }

        // ── 公共断言/复刻 ────────────────────────────────────────────────────

        static ViewProjection Named(IReadOnlyList<ViewProjection> views, string name)
        {
            foreach (var view in views)
                if (view.Direction.Name == name)
                    return view;

            Assert.Fail($"视图 {name} 不存在（三视图只有 主视/俯视/左视）");
            return null!;
        }

        static void AssertViewPin(
            ViewProjection view, string viewName, int expectedVisible, int expectedHidden,
            double expectedHiddenLength)
        {
            Assert.That(view.Segments.Count, Is.EqualTo(expectedVisible + expectedHidden),
                $"{viewName} 段数实测 {view.Segments.Count}（钉值 V+H = {expectedVisible}+{expectedHidden}）");
            Assert.That(view.VisibleCount, Is.EqualTo(expectedVisible), $"{viewName} 可见段实测 {view.VisibleCount}");
            Assert.That(view.HiddenCount, Is.EqualTo(expectedHidden), $"{viewName} 隐藏段实测 {view.HiddenCount}");

            var hiddenLength = view.Segments.Where(s => !s.IsVisible).Sum(s => s.Length);
            Assert.That(hiddenLength, Is.EqualTo(expectedHiddenLength).Within(0.05),
                $"{viewName} 隐藏总长实测 {hiddenLength:F6}（钉值 {expectedHiddenLength:F6} ±0.05）");
        }

        /// <summary>
        /// 断言该视图的**全部**线段端点都落在板轮廓 |h| = 半厚 上（±1e-6）——「0.01mm 凸台无痕迹」的
        /// 精确表述：任何内部/偏置线（如凸台轮廓 h=−10.01）都会立刻违反。
        /// </summary>
        static void AssertOnOutlineOnly(ViewProjection view, string viewName, double outlineH)
        {
            foreach (var segment in view.Segments)
            {
                Assert.That(Math.Abs(Math.Abs(segment.Start.X) - outlineH), Is.LessThan(1e-6),
                    $"{viewName}：段 ({segment.Start.X:F4},{segment.Start.Y:F4})→({segment.End.X:F4},{segment.End.Y:F4}) "
                    + $"的起点偏离板轮廓 |h|={outlineH}（凸台本应在 |h|={outlineH + PerfWorkloads.ThinBossProtrusion} "
                    + "处留下 100mm 轮廓线 —— 实测被清理阈值/共线合并吞掉，属丢边行为钉）");
                Assert.That(Math.Abs(Math.Abs(segment.End.X) - outlineH), Is.LessThan(1e-6),
                    $"{viewName}：段 ({segment.Start.X:F4},{segment.Start.Y:F4})→({segment.End.X:F4},{segment.End.Y:F4}) "
                    + $"的终点偏离板轮廓 |h|={outlineH}");
            }
        }

        static void AssertSamplePin(
            IReadOnlyList<ViewTriangle> triangles, ViewDirection direction, string viewName,
            int expectedCandidates, int expectedMaxSamples)
        {
            var (candidates, maxSamples) = CandidateSampleStats(triangles, direction);
            Assert.That(candidates, Is.EqualTo(expectedCandidates),
                $"{viewName} 候选边数实测 {candidates}（钉值 {expectedCandidates}）");
            Assert.That(maxSamples, Is.EqualTo(expectedMaxSamples),
                $"{viewName} 候选边最大采样数实测 {maxSamples}（钉值 {expectedMaxSamples}）");
            Assert.That(maxSamples, Is.LessThan(ViewProjector.MaxEdgeSamples),
                $"{viewName}：实测最大采样数 {maxSamples} 应低于 MaxEdgeSamples={ViewProjector.MaxEdgeSamples}"
                + "（闭合实体的结构上限 ≈ √2×900 ≈ 1273）");
        }

        /// <summary>
        /// 复刻 <see cref="ViewProjector"/> 的**候选边筛选 + 采样数**口径（只用公开 API：
        /// <see cref="ViewMeshTopology"/> 的边/法向 + <see cref="ViewProjector.RasterResolution"/> /
        /// <see cref="ViewProjector.MaxEdgeSamples"/>）；<c>DepthRaster.Build</c> 是私有实现，无法直接调用。
        /// 复刻给边界用例提供「1e6 长棱被采多少个点」的可断言读数；若实现口径变化，本复刻与投影器
        /// 一起漂移会立刻失败（钉的价值所在）。
        /// </summary>
        static (int Candidates, int MaxSamples) CandidateSampleStats(
            IReadOnlyList<ViewTriangle> triangles, ViewDirection view)
        {
            var mesh = new ViewMeshTopology(triangles);

            var frontFacing = new bool[mesh.Triangles.Count];
            for (var i = 0; i < mesh.Triangles.Count; i++)
                frontFacing[i] = mesh.Normals[i].Dot(view.Look) < 0;

            double minH = double.MaxValue, maxH = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
            var any = false;
            for (var i = 0; i < mesh.Triangles.Count; i++)
            {
                if (!frontFacing[i]) continue;
                var triangle = mesh.Triangles[i];
                for (var v = 0; v < 3; v++)
                {
                    var point = triangle[v];
                    var h = point.Dot(view.Right);
                    var vv = point.Dot(view.Up);
                    if (h < minH) minH = h;
                    if (h > maxH) maxH = h;
                    if (vv < minV) minV = vv;
                    if (vv > maxV) maxV = vv;
                    any = true;
                }
            }

            Assert.That(any, Is.True, "该视图没有正面三角形 —— 深度栅格为空，不构成消隐路径");

            var sizeH = Math.Max(maxH - minH, 1e-9);
            var sizeV = Math.Max(maxV - minV, 1e-9);
            var cell = Math.Max(sizeH, sizeV) / ViewProjector.RasterResolution;
            if (cell <= 0) cell = 1e-6;

            var candidates = 0;
            var maxSamples = 0;
            foreach (var edge in mesh.Edges)
            {
                var candidate = false;
                if (edge.TriB < 0)
                    candidate = true;   // 边界边（单侧引用）：投影器无条件放行
                else
                {
                    var frontA = frontFacing[edge.TriA];
                    var frontB = frontFacing[edge.TriB];
                    if (frontA != frontB) candidate = true;              // 轮廓边
                    else if (!frontA && !frontB) candidate = false;      // 两侧都背面 ⇒ 不可见
                    else candidate = edge.IsSharp;                        // 两侧正面 ⇒ 锐边
                }
                if (!candidate) continue;
                candidates++;

                var a = mesh.Points[edge.P0];
                var b = mesh.Points[edge.P1];
                var dx = a.Dot(view.Right) - b.Dot(view.Right);
                var dy = a.Dot(view.Up) - b.Dot(view.Up);
                var samples = (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy) / cell) + 1;
                if (samples < 2) samples = 2;
                if (samples > ViewProjector.MaxEdgeSamples) samples = ViewProjector.MaxEdgeSamples;
                if (samples > maxSamples) maxSamples = samples;
            }

            return (candidates, maxSamples);
        }
    }
}
