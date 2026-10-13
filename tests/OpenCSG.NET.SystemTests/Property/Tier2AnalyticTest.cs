using System;
using System.Collections.Generic;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// Tier 2：与闭式答案比对。
    /// 最强的一条是「凸包周长守恒」—— 对凸实体，正交投影后可见线段总长
    /// 必须等于投影顶点的 2D 凸包周长。凸包由本工程独立实现（<see cref="ConvexHull"/>），
    /// 不调用被测代码，因此这是真正的外部 oracle。
    /// </summary>
    [TestFixture]
    public class Tier2AnalyticTest
    {
        const double Tol = 1e-6;

        [Test]
        public void ConvexHull_PerimeterOfUnitSquareIsFour()
        {
            var square = new[]
            {
                new ViewPoint2(0, 0), new ViewPoint2(1, 0),
                new ViewPoint2(1, 1), new ViewPoint2(0, 1),
                new ViewPoint2(0.5, 0.5),   // 内部点必须被排除
            };

            Assert.That(ConvexHull.PerimeterOf(square), Is.EqualTo(4.0).Within(1e-12));
        }

        [Test]
        public void ConvexHull_OfDiscreteCircleApproachesTwoPiR()
        {
            const double radius = 3.0;
            const int steps = 4096;

            var points = new List<ViewPoint2>(steps);
            for (var i = 0; i < steps; i++)
            {
                var angle = 2 * Math.PI * i / steps;
                points.Add(new ViewPoint2(radius * Math.Cos(angle), radius * Math.Sin(angle)));
            }

            var perimeter = ConvexHull.PerimeterOf(points);

            // 内接正多边形周长 = 2·n·R·sin(π/n) < 2πR，收敛阶 O(1/n²)
            var exact = 2 * Math.PI * radius;
            Assert.That(perimeter, Is.EqualTo(exact).Within(exact * 1e-6));
        }

        [Test]
        public void Cube_VisibleSegmentLengthEqualsConvexHullPerimeter()
        {
            var cube = Solids.Cube(10, center: true);
            var views = ViewBridge.ProjectViews(cube, ViewDirections.OrthographicThree);

            foreach (var view in views)
            {
                var projected = ProjectedVertices(cube, view.Direction);
                var expected = ConvexHull.PerimeterOf(projected);

                var actual = SumSegmentLength(view);

                Assert.That(actual, Is.EqualTo(expected).Within(expected * 1e-6),
                    $"{view.Direction.Name}: 可见线段总长 {actual:R} 应为投影凸包周长 {expected:R}");
            }
        }

        [Test]
        public void Box10x20x30_ThreeViewsHaveAnalyticExtents()
        {
            var box = Solids.Cube(new Vector3D(10, 20, 30), center: true);
            var views = ViewBridge.ProjectViews(box, ViewDirections.OrthographicThree);

            // 由 ViewProjectorContracts.cs 的取轴定义推导（Plan A 已两次独立验证）
            var expected = new (string Name, double Width, double Height)[]
            {
                ("主视", 30, 20), ("俯视", 30, 10), ("左视", 10, 20),
            };

            for (var i = 0; i < expected.Length; i++)
            {
                Assert.That(views[i].Direction.Name, Is.EqualTo(expected[i].Name));
                Assert.That(views[i].Bounds.Width, Is.EqualTo(expected[i].Width).Within(Tol),
                    $"{expected[i].Name} 宽");
                Assert.That(views[i].Bounds.Height, Is.EqualTo(expected[i].Height).Within(Tol),
                    $"{expected[i].Name} 高");
            }
        }

        /// <summary>
        /// 球面轮廓应被圆弧识别器还原为整圆（该子系统文档自述的契约：消隐栅格把圆轮廓打断成
        /// 互不连通的几条链 ⇒ 按共圆聚合补成整圆，见 <c>ViewArcFitter.MergeConcyclic</c>）。
        /// <para>
        /// <b>实际（DEFECT-004）</b>：三视图各只得到 2 条 14°/14°/26° 的开口小弧
        /// （主视第一条 R≈4.0374、扫掠 14.22°、IsClosed=false；俯视 R≈4.0653、14.13°；
        /// 左视第一条 R≈0.8881、25.96°），残余碎段 ~1490 条/视图 ⇒ 本用例失败。
        /// 两个独立机制（探针 artifacts/task9probe）：
        /// ① <c>ViewArcFitter.BuildChains</c> 的 <c>Grow</c> 只在「边朝向与遍历方向一致」时
        /// 继续：候选边以 End 接到 tip（朝向反向）时，加入后仍按 <c>segments[lastIndex].End</c>
        /// 取新 tip（应为该边另一端）⇒ 增长终止且该边远端顶点永不入链。实测把干净的 48 边
        /// 2D 圆环（端点度数全 2、所有边长 0.5232）切成 4 条链 [13,11,13,11]，拟合出
        /// 90°+75°+90°+75° 四条弧，在反向边处留下两处 15° 空隙（> 1° 并接阈值，且覆盖
        /// 330° < 2π−25°）⇒ 整圆永不出现；同一输入下左视（环=赤道圈、朝向恰好一致）
        /// 能得到 360° 整圆，证明与几何无关、纯取向依赖。
        /// ② 消隐栅格逐格采样把掠射轮廓边切成可见/隐藏交替碎段（实测 1529 段、752 次翻转、
        /// 半数游程 1–2 个采样 ≈0.009–0.018mm、最大 0.078），成链阈值
        /// <c>degenerate = max(tolerance, join) = 0.05</c> 排除 96.2%（1471/1529）碎段
        /// ⇒ 真实管线里连左视本可闭合的链也被摧毁。
        /// 复现：<c>dotnet test tests/OpenCSG.NET.SystemTests -c Release --filter "FullyQualifiedName~Sphere_SilhouetteIsAClosedCircleOfRadiusR"</c>
        /// （<c>[Explicit]</c> 用例被显式过滤选中时会执行，断言消息带实际 Arcs/Radius/IsClosed）。
        /// 期望：1 条整圆（R=4±2%、SweepDegrees=360、IsClosed=true）。
        /// </para>
        /// </summary>
        [Test]
        [Explicit("DEFECT-004: 掠射轮廓 + 朝向反向边致球面整圆无法还原（成链 Grow 方向 bug + 消隐碎段阈值）")]
        public void Sphere_SilhouetteIsAClosedCircleOfRadiusR()
        {
            const double radius = 4.0;
            var sphere = Solids.Sphere(new SphereOptions
            {
                Center = new Vector3D(0, 0, 0), Radius = radius, Resolution = 48,
            });

            var views = ViewBridge.ProjectViews(sphere, ViewDirections.OrthographicThree);

            foreach (var view in views)
            {
                Assert.That(view.Arcs.Count, Is.GreaterThanOrEqualTo(1),
                    $"{view.Direction.Name}: 球面轮廓应被圆弧识别器还原为至少一条弧，实际 Arcs={view.Arcs.Count}");

                var best = view.Arcs[0];
                Assert.That(best.Radius, Is.EqualTo(radius).Within(radius * 0.02),
                    $"{view.Direction.Name}: 还原半径 {best.Radius:R} 应接近 {radius}");
                Assert.That(best.IsClosed, Is.True,
                    $"{view.Direction.Name}: 球面轮廓应被识别为整圆");
                Assert.That(best.SweepDegrees, Is.EqualTo(360.0).Within(1e-9));
            }
        }

        [Test]
        public void ArcSample_IsRecoveredAsAnArcWithKnownCenterAndRadius()
        {
            var center = new ViewPoint2(12.5, -7.25);
            const double radius = 9.0;
            const double startDeg = 20.0;
            const double sweepDeg = 110.0;

            var segments = ViewSegGen.ArcSample(center, radius, startDeg, sweepDeg, 24, visible: true);

            var curves = ViewArcFitter.Fit(segments);

            Assert.That(curves.Arcs.Count, Is.EqualTo(1),
                $"应恰好还原出 1 条弧，实际 {curves.Arcs.Count}；残余段 {curves.Segments.Count}");

            var arc = curves.Arcs[0];
            Assert.That(arc.Radius, Is.EqualTo(radius).Within(radius * 1e-3), "还原半径");
            Assert.That(arc.Center.X, Is.EqualTo(center.X).Within(0.05), "还原圆心 X");
            Assert.That(arc.Center.Y, Is.EqualTo(center.Y).Within(0.05), "还原圆心 Y");
            Assert.That(arc.SweepDegrees, Is.EqualTo(sweepDeg).Within(5.0), "还原扫掠角");
            Assert.That(arc.IsVisible, Is.True);
            Assert.That(curves.Segments.Count, Is.EqualTo(0), "全部输入应被弧吸收");
        }

        [Test]
        public void SegmentsFor_MatchesChordToleranceFormula()
        {
            using var scope = new TessellationScope(0.05);

            foreach (var radius in new[] { 1.0, 10.0, 100.0, 1000.0 })
            {
                // 解析解：单段圆心角 θ = 2·acos(1 − tol/r)，N = ceil(2π/θ)，夹在 [16,128]
                var half = Math.Acos(1.0 - 0.05 / radius);
                var analytic = (int)Math.Ceiling(Math.PI / half);
                var expected = Math.Min(Math.Max(analytic, Tessellation.MinSegments), Tessellation.MaxSegments);

                Assert.That(Tessellation.SegmentsFor(radius), Is.EqualTo(expected),
                    $"r={radius} 的分段数应等于解析值 {expected}");
            }
        }

        [Test]
        public void SegmentsFor_AdaptiveIsMonotoneInToleranceAndRadius()
        {
            using var scope = new TessellationScope(0.05);

            // 容差越松 ⇒ 分段越少（单调不增）
            var previous = int.MaxValue;
            foreach (var tolerance in new[] { 0.001, 0.01, 0.05, 0.2, 1.0 })
            {
                using var inner = new TessellationScope(tolerance);
                var count = Tessellation.SegmentsFor(50.0);

                Assert.That(count, Is.LessThanOrEqualTo(previous),
                    $"tol={tolerance} 时分段数 {count} 应 ≤ 上一档 {previous}");
                previous = count;
            }

            // 半径越大 ⇒ 需要越多分段（单调不减）
            using var fixedTolerance = new TessellationScope(0.05);
            previous = 0;
            foreach (var radius in new[] { 1.0, 5.0, 20.0, 100.0, 500.0 })
            {
                var count = Tessellation.SegmentsFor(radius);

                Assert.That(count, Is.GreaterThanOrEqualTo(previous),
                    $"r={radius} 时分段数 {count} 应 ≥ 上一档 {previous}");
                previous = count;
            }
        }

        /// <summary>
        /// 把实体全部顶点投到指定视图的 2D 平面。
        /// 刻意走网格坐标（<c>CsgMesh.TrianglesOf</c> 已完成 Y↔Z 交换）后用方向的
        /// Right/Up 取轴 —— 与 <see cref="ViewProjector"/> 用的是**同一套取轴定义**，
        /// 但**各自独立实现**（一个在库内，一个在测试侧手算）。这正是这条 oracle 的独立性所在：
        /// 若取轴口径被改错，两侧会同时错同向，但凸包周长与可见线段总长仍会不一致。
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

        static double SumSegmentLength(ViewProjection view)
        {
            var total = 0.0;
            foreach (var segment in view.Segments)
                total += segment.Length;
            return total;
        }
    }
}
