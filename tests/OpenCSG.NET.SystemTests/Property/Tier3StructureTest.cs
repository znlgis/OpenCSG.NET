using System;
using System.Collections.Generic;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// Tier 3：结构不变量、单调性、幂等性。这些性质不给出「正确答案」，
    /// 但它们对任意输入成立，因此能廉价地覆盖大量输入空间。
    /// </summary>
    [TestFixture]
    public class Tier3StructureTest
    {
        static IEnumerable<TestCaseData> SegmentCases()
        {
            for (long seed = 0; seed < 32; seed++)
                yield return new TestCaseData(seed).SetName($"Seg_{seed:D2}");
        }

        [TestCaseSource(nameof(SegmentCases))]
        [Category("Smoke")]
        public void Bounds_CoverEverySegmentEndpoint(long seed)
        {
            var segments = ViewSegGen.Random(seed);
            var view = new ViewProjection(ViewDirections.Front, segments);

            HlrAssert.AssertBoundsCoverSegments(view, $"seed={seed}");
        }

        [Test]
        [Category("Smoke")]
        public void VisibilityAccounting_MatchesHandBuiltFixture()
        {
            // 期望值必须来自调用方已知的地面真值。拿 VisibleCount 与「逐段重数一遍 IsVisible」对拍
            // 是恒真断言（VisibleCount 的实现就是那个循环），不能作为用例。
            var segments = new[]
            {
                new ViewSegment(new ViewPoint2(0, 0), new ViewPoint2(1, 0), true),
                new ViewSegment(new ViewPoint2(0, 1), new ViewPoint2(1, 1), false),
                new ViewSegment(new ViewPoint2(0, 2), new ViewPoint2(1, 2), true),
                new ViewSegment(new ViewPoint2(0, 3), new ViewPoint2(1, 3), false),
                new ViewSegment(new ViewPoint2(0, 4), new ViewPoint2(1, 4), true),
            };
            var view = new ViewProjection(ViewDirections.Front, segments);

            HlrAssert.AssertVisibilityAccounting(view, expectedVisible: 3, expectedHidden: 2);
        }

        [TestCaseSource(nameof(SegmentCases))]
        [Category("Smoke")]
        public void Clean_PreservesCoverageAndVisibility(long seed)
        {
            var segments = ViewSegGen.Random(seed);
            var bounds = ViewSegGen.BoundsOf(segments);

            var cleaned = ViewSegmentCleanup.Clean(segments, bounds);

            HlrAssert.AssertCleanPreservesCoverage(segments, cleaned, bounds, $"seed={seed}");
        }

        [TestCaseSource(nameof(SegmentCases))]
        [Category("Smoke")]
        public void Clean_IsIdempotent(long seed)
        {
            var segments = ViewSegGen.Random(seed);
            var bounds = ViewSegGen.BoundsOf(segments);

            var once = ViewSegmentCleanup.Clean(segments, bounds);
            var twice = ViewSegmentCleanup.Clean(once, bounds);

            Assert.That(twice.Count, Is.EqualTo(once.Count),
                $"seed={seed}: 二次清理改变了线段数（{once.Count} → {twice.Count}）");
        }

        [Test]
        [Category("Smoke")]
        public void Clean_DoesNotMergeAcrossVisibilityLayers()
        {
            // `ViewSegGen.Random` 生成的线段集**每块只有一种可见性**，所以「跨可见性不合并」这条守卫
            // 在随机输入下不可观测 —— Task 5 实测 64 个种子里存在共线且相邻的异可见性对的数量为 **0**。
            // 单靠随机输入会让人误以为覆盖了这条守卫；这里手工构造一对同线、重叠、一可见一隐藏的线段。
            var bounds = new ViewBounds2(0, 0, 10, 10);
            var input = new[]
            {
                new ViewSegment(new ViewPoint2(0, 0), new ViewPoint2(6, 0), isVisible: true),
                new ViewSegment(new ViewPoint2(2, 0), new ViewPoint2(8, 0), isVisible: false),
            };

            var cleaned = ViewSegmentCleanup.Clean(input, bounds);

            var visible = 0;
            var hidden = 0;
            foreach (var segment in cleaned)
            {
                if (segment.IsVisible) visible++; else hidden++;
            }

            Assert.That(visible, Is.GreaterThan(0), "可见层不得被并入隐藏层");
            Assert.That(hidden, Is.GreaterThan(0), "隐藏层不得被并入可见层");
            Assert.That(cleaned.Count, Is.EqualTo(2),
                $"跨可见性的共线重叠段必须保持两条（Clean 的合并带同可见性守卫），实际 {cleaned.Count} 条");
        }

        [Test]
        [Category("Smoke")]
        public void Clean_ChainedMergesStayWithinOracleTolerance()
        {
            // 反例钉：Clean 的合并是两级的（TryExtend 投影到累加基线，CollapseOverlaps 再投影到另一条基线），
            // 一个原始点最多经历两次投影 ⇒ oracle 覆盖容差必须是 2×joinTolerance = 8×minKeep。
            // 本夹具实测某点位移 0.0158 = 7.9×minKeep：4× 容差会在此误报，8× 必须通过 ——
            // 若有人把容差收紧回去，这条测试立即失败。
            var bounds = new ViewBounds2(0, -0.0158, 20, 0);
            var input = new[]
            {
                new ViewSegment(new ViewPoint2(0, 0), new ViewPoint2(10, 0), true),
                new ViewSegment(new ViewPoint2(10.02, -0.0079), new ViewPoint2(20, -0.0079), true),
                new ViewSegment(new ViewPoint2(9.9, -0.0158), new ViewPoint2(20, -0.0158), true),
            };

            var cleaned = ViewSegmentCleanup.Clean(input, bounds);

            Assert.That(cleaned.Count, Is.EqualTo(1),
                "夹具前提：该输入下 Clean 实测应合并为 1 条（若 Clean 行为变化，请复核本用例前提）");
            HlrAssert.AssertCleanPreservesCoverage(input, cleaned, bounds, "chained-merge fixture");
        }

        [TestCaseSource(nameof(SegmentCases))]
        [Category("Smoke")]
        public void Fit_CoversInputWithArcsPlusResidues(long seed)
        {
            var segments = ViewSegGen.Random(seed);

            var curves = ViewArcFitter.Fit(segments);

            HlrAssert.AssertFitCoversInput(segments, curves, ViewArcFitter.DefaultTolerance, $"seed={seed}");
        }

        [TestCaseSource(nameof(SegmentCases))]
        [Category("Smoke")]
        public void Fit_DoesNotInventGeometry(long seed)
        {
            var segments = ViewSegGen.Random(seed);

            var curves = ViewArcFitter.Fit(segments);

            // 拟合不得凭空产出输入之外的总长度：
            // 弧长 + 残余段长 应接近输入总长（弧会略微伸长折线，故给 5% 余量）
            var inputLength = TotalLength(segments);
            var arcLength = 0.0;
            foreach (var arc in curves.Arcs)
                arcLength += arc.Radius * arc.SweepDegrees * Math.PI / 180.0;

            var outputLength = arcLength + TotalLength(curves.Segments);

            Assert.That(outputLength, Is.LessThanOrEqualTo(inputLength * 1.05 + 1e-6),
                $"seed={seed}: 拟合后总长 {outputLength:R} 超出输入总长 {inputLength:R} 的 5%");
        }

        [TestCaseSource(nameof(SegmentCases))]
        [Category("Smoke")]
        public void MergeConcyclic_IsIdempotent(long seed)
        {
            // 输入必须是「同一圆上的多段弧」：旧的固定输入（R=10、扫掠 360°、6 段）实测 Fit 产出 0 条弧，
            // MergeConcyclic 在 arcs.Count ≤ 1 处短路，用例会 0 == 0 空洞通过。
            // 这里把圆按 N 块切分、块间留 gap：gap=0.5° 触发并接 + 补圆，较大 gap 保持分段，
            // 随种子覆盖三种行为且多数配置跨 0° 回绕（Unwrap 回绕路径）。
            var gaps = new[] { 0.5, 3.5, 6.5, 9.5, 15.5, 18.5, 21.5 };
            var count = 2 + (int)(seed % 3);
            var gap = gaps[(int)(seed % gaps.Length)];
            var offset = seed * 37.0 % 360.0;
            var sweep = (360.0 - count * gap) / count;
            var steps = Math.Max(3, (int)Math.Ceiling(sweep / 10.0));

            var segments = new List<ViewSegment>();
            for (var c = 0; c < count; c++)
                segments.AddRange(ViewSegGen.ArcSample(
                    new ViewPoint2(0, 0), 10, offset + c * (sweep + gap), sweep, steps, visible: seed % 2 == 0));

            var curves = ViewArcFitter.Fit(segments);
            var once = ViewArcFitter.MergeConcyclic(curves.Arcs);
            var twice = ViewArcFitter.MergeConcyclic(once);

            Assert.That(once, Is.Not.Empty,
                $"seed={seed}: 输入未产出任何弧（Fit arcs={curves.Arcs.Count}），用例将空洞通过");
            Assert.That(twice.Count, Is.EqualTo(once.Count),
                $"seed={seed}: MergeConcyclic 不幂等（{once.Count} → {twice.Count}）");
            Assert.That(SumSweepDegrees(twice), Is.EqualTo(SumSweepDegrees(once)).Within(1e-9),
                $"seed={seed}: 幂等合并改变了扫掠角总和（{SumSweepDegrees(once):R} → {SumSweepDegrees(twice):R}）");
        }

        [Test]
        [Category("Smoke")]
        public void MergeConcyclic_JoinsClosesAndKeepsAcrossThresholds()
        {
            // 种子驱动的用例只覆盖「Fit 内部已完成合并」后的稳定输入；这里用公共 ViewArc 构造器
            // 直接跨过两个阈值：并接阈值 ConcyclicMergeGapDegrees = 1°、近闭合补圆阈值 MaxClosingGapDegrees = 25°。
            var center = new ViewPoint2(3, -2);
            const double radius = 7.0;
            static double Deg(double degrees) => degrees * Math.PI / 180.0;

            // (a) 两段 179.5° 弧，两处 0.5° 空隙（≤ 1° 并接）且总缺口 1°（≤ 25° 补圆）⇒ 1 条整圆
            var joined = ViewArcFitter.MergeConcyclic(new[]
            {
                new ViewArc(center, radius, Deg(0), Deg(179.5), true, false),
                new ViewArc(center, radius, Deg(180), Deg(359.5), true, false),
            });
            Assert.That(joined.Count, Is.EqualTo(1), $"两段 0.5° 空隙的同圆弧应并成 1 条，实际 {joined.Count}");
            Assert.That(joined[0].IsClosed, Is.True, "总缺口 1° ≤ 25° 应补成整圆");
            Assert.That(joined[0].Radius, Is.EqualTo(radius).Within(1e-9));

            var joinedAgain = ViewArcFitter.MergeConcyclic(joined);
            Assert.That(joinedAgain.Count, Is.EqualTo(1));
            Assert.That(joinedAgain[0].IsClosed, Is.True);

            // (b) 缺口 20°（> 1° 不并接；总缺口 40° > 25° 不补圆）⇒ 保持 2 条
            var kept = ViewArcFitter.MergeConcyclic(new[]
            {
                new ViewArc(center, radius, Deg(0), Deg(160), true, false),
                new ViewArc(center, radius, Deg(180), Deg(340), true, false),
            });
            Assert.That(kept.Count, Is.EqualTo(2), "缺口 20° > 1° 且总缺口 40° > 25° ⇒ 不得并接或补圆");

            // (c) 并接但覆盖率不足（各 150°，缺口 0.5° 并接后总缺口 59.5° > 25°）⇒ 1 条开口弧，不补圆
            var unionOnly = ViewArcFitter.MergeConcyclic(new[]
            {
                new ViewArc(center, radius, Deg(0), Deg(150), true, false),
                new ViewArc(center, radius, Deg(150.5), Deg(300.5), true, false),
            });
            Assert.That(unionOnly.Count, Is.EqualTo(1), "0.5° 空隙应并接成 1 条");
            Assert.That(unionOnly[0].IsClosed, Is.False, "总缺口 59.5° > 25° ⇒ 不得补圆");

            // (d) 不同圆不得互相合并
            var foreign = ViewArcFitter.MergeConcyclic(new[]
            {
                new ViewArc(center, radius, Deg(0), Deg(90), true, false),
                new ViewArc(new ViewPoint2(center.X + 50, center.Y), radius * 2, Deg(0), Deg(90), true, false),
            });
            Assert.That(foreign.Count, Is.EqualTo(2), "不同圆心的弧不得合并");
        }

        [Test]
        [Category("Smoke")]
        public void Topology_CubeIsAClosedManifoldWithEulerTwo()
        {
            var cube = Solids.Cube(10, center: true);
            var triangles = ViewBridge.ToViewTriangles(cube);

            HlrAssert.AssertTopologyIsClosedManifold(triangles, "cube");
        }

        [Test]
        [Category("Smoke")]
        public void Topology_OracleRejectsFlippedWinding()
        {
            // 钉住 oracle 的判别力：翻转一个三角形的绕序后，拓扑断言必须失败。
            // （没有这条，绕序检查退化成死断言的回归不会被任何用例抓住。）
            var cube = Solids.Cube(10, center: true);
            var triangles = ViewBridge.ToViewTriangles(cube);
            var flipped = new List<ViewTriangle>(triangles);
            flipped[0] = new ViewTriangle(flipped[0][0], flipped[0][2], flipped[0][1]);

            Assert.That(() => HlrAssert.AssertTopologyIsClosedManifold(flipped, "flipped"),
                Throws.TypeOf<AssertionException>());
        }

        [Test]
        [Category("Smoke")]
        public void Topology_CubeWeldIsExactAndDeterministic()
        {
            // 立方体焊接后的解析真值：V=8、E=18（12 条棱 + 6 条面对角线）、F=12。
            // 同一输入二次构造必须逐点逐边一致（焊接不得依赖字典迭代顺序等不确定状态）。
            var cube = Solids.Cube(10, center: true);
            var triangles = ViewBridge.ToViewTriangles(cube);

            var topology = new ViewMeshTopology(triangles);

            Assert.That(topology.Points.Count, Is.EqualTo(8),
                $"立方体焊接顶点数应为 8，实际 {topology.Points.Count}");
            Assert.That(topology.Edges.Count, Is.EqualTo(18),
                $"立方体焊接边数应为 18，实际 {topology.Edges.Count}");
            Assert.That(topology.Triangles.Count, Is.EqualTo(12));

            var second = new ViewMeshTopology(triangles);
            Assert.That(second.Points.Count, Is.EqualTo(topology.Points.Count));
            Assert.That(second.Edges.Count, Is.EqualTo(topology.Edges.Count));
            for (var i = 0; i < topology.Points.Count; i++)
            {
                Assert.That(second.Points[i].X, Is.EqualTo(topology.Points[i].X));
                Assert.That(second.Points[i].Y, Is.EqualTo(topology.Points[i].Y));
                Assert.That(second.Points[i].Z, Is.EqualTo(topology.Points[i].Z));
            }
        }

        [Test]
        [Category("Smoke")]
        public void Projection_ConvexSolidHasNoVisibleSelfOcclusion()
        {
            // 凸实体的可见线段必须构成一条闭合回路，且不存在被自身遮挡的可见段。
            // 判据：可见段数 > 0，且可见段总长仍等于视图矩形周长（凸体无遮挡 ⇒ 无隐藏段）。
            var cube = Solids.Cube(10, center: true);
            var views = ViewBridge.ProjectViews(cube, ViewDirections.OrthographicThree);

            foreach (var view in views)
            {
                Assert.That(view.VisibleCount, Is.GreaterThan(0), $"{view.Direction.Name}: 无可见段");
                HlrAssert.AssertVisibilityAccounting(view, expectedVisible: 4, expectedHidden: 0);
                Assert.That(SumVisibleLength(view),
                    Is.EqualTo(view.Bounds.Width * 2 + view.Bounds.Height * 2).Within(1e-6),
                    $"{view.Direction.Name}: 凸体可见段总长应等于视图矩形周长（4 条轮廓边）");
            }
        }

        static double TotalLength(IReadOnlyList<ViewSegment> segments)
        {
            var total = 0.0;
            foreach (var segment in segments)
                total += segment.Length;
            return total;
        }

        static double SumVisibleLength(ViewProjection view)
        {
            var total = 0.0;
            foreach (var segment in view.Segments)
                if (segment.IsVisible)
                    total += segment.Length;
            return total;
        }

        static double SumSweepDegrees(IReadOnlyList<ViewArc> arcs)
        {
            var total = 0.0;
            foreach (var arc in arcs)
                total += arc.SweepDegrees;
            return total;
        }
    }
}
