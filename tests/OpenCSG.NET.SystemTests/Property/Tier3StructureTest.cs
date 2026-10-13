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
        public void Topology_CubeIsAClosedManifoldWithEulerTwo()
        {
            var cube = Solids.Cube(10, center: true);
            var triangles = ViewBridge.ToViewTriangles(cube);

            HlrAssert.AssertTopologyIsClosedManifold(triangles, "cube");
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
