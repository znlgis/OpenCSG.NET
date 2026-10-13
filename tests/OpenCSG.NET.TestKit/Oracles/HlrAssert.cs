using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// Tier 3：HLR 前端的结构与守恒不变量。
    /// 两处覆盖度判据的口径都与被测实现的**契约**对齐（不是随手放的容差，见各方法注释）：
    /// <list type="bullet">
    /// <item><see cref="AssertCleanPreservesCoverage"/> 的覆盖容差 = Clean 自己的接链容差
    /// （合并会把近共线成员投影到基准线，点的位移上界就是它）；</item>
    /// <item><see cref="AssertFitCoversInput"/> 按 <c>TryBuildArc</c> 的两级契约判定：
    /// 链顶点距圆 ≤ tolerance，弦中点距圆 ≤ max(tolerance, 5%·R)（矢高判据）。</item>
    /// </list>
    /// </summary>
    public static class HlrAssert
    {
        /// <summary>点落在线段上的绝对容差下限（mm）。</summary>
        public const double OnSegmentTolerance = 1e-6;

        public static void AssertBoundsCoverSegments(ViewProjection view, string? message = null)
        {
            var prefix = Prefix(message);
            var bounds = view.Bounds;

            foreach (var segment in view.Segments)
                AssertEndpointInBounds(segment.Start, bounds, prefix, view.Direction.Name);
            foreach (var segment in view.Segments)
                AssertEndpointInBounds(segment.End, bounds, prefix, view.Direction.Name);
        }

        /// <summary>
        /// 可见性记账与调用方给出的期望值一致。
        /// 注意不能拿 VisibleCount 与「逐段重数一遍 IsVisible」对拍 ——
        /// <see cref="ViewProjection.VisibleCount"/> 的实现就是那个循环，那样的断言恒真。
        /// 期望值必须来自调用方已知的地面真值。
        /// </summary>
        public static void AssertVisibilityAccounting(
            ViewProjection view,
            int expectedVisible,
            int expectedHidden,
            string? message = null)
        {
            var prefix = Prefix(message);

            Assert.That(view.VisibleCount, Is.EqualTo(expectedVisible),
                $"{prefix} {view.Direction.Name}: VisibleCount={view.VisibleCount}，期望 {expectedVisible}");
            Assert.That(view.HiddenCount, Is.EqualTo(expectedHidden),
                $"{prefix} {view.Direction.Name}: HiddenCount={view.HiddenCount}，期望 {expectedHidden}");
            Assert.That(view.Segments.Count, Is.EqualTo(expectedVisible + expectedHidden),
                $"{prefix} {view.Direction.Name}: 总段数 {view.Segments.Count}，期望 {expectedVisible + expectedHidden}");
        }

        /// <summary>
        /// 清理不得丢失「足够长」的输入几何，也不得跨可见性合并。
        /// <para>
        /// 足够长 = 长于 Clean 的丢弃阈值 <c>minLength = max(1e-3, 1e-4 × 视图对角线)</c>
        /// （与 <see cref="ViewSegmentCleanup.Clean"/> 的公式一致）。
        /// 覆盖容差取 Clean 自己的接链容差 <c>4 × minLength</c>：TryExtend / CollapseOverlaps
        /// 只把「两端点都落在基准线 joinTolerance 内」的近共线成员投影到基准线上，
        /// 成员点在合并中的位移上界就是这个容差；更紧的容差会把契约内的合法投影判成「丢几何」
        /// （控制器实测：32 个种子上 1e-6 有 72 处误报，4×minLength 为 0 处）。
        /// </para>
        /// </summary>
        public static void AssertCleanPreservesCoverage(
            IReadOnlyList<ViewSegment> input,
            IReadOnlyList<ViewSegment> output,
            ViewBounds2 bounds,
            string? message = null)
        {
            var diagonal = Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height);
            var minKeep = Math.Max(ViewSegmentCleanup.MinAbsoluteLength,
                                   ViewSegmentCleanup.MinRelativeLength * diagonal);
            var coverageTolerance = Math.Max(OnSegmentTolerance, 4.0 * minKeep);

            foreach (var segment in input)
            {
                if (segment.Length <= minKeep)
                    continue;   // 低于阈值的残段本就会被丢弃

                foreach (var sample in SamplePoints(segment))
                {
                    var covering = FindCovering(output, sample, coverageTolerance);

                    Assert.That(covering, Is.Not.Null,
                        $"{Prefix(message)} 清理丢失了覆盖：输入段 {segment.Start}→{segment.End} 上的点 "
                        + $"({sample.X:R},{sample.Y:R}) 不在任何输出段上");

                    Assert.That(covering!.Value.IsVisible, Is.EqualTo(segment.IsVisible),
                        $"{Prefix(message)} 清理跨越了可见性合并：输入段是 "
                        + $"{(segment.IsVisible ? "可见" : "隐藏")}，覆盖它的输出段却是 "
                        + $"{(covering.Value.IsVisible ? "可见" : "隐藏")}");
                }
            }
        }

        /// <summary>
        /// 拟合结果（弧 + 残余段）必须覆盖全部输入线段。
        /// 两级契约（与 <c>ViewArcFitter.TryBuildArc</c> 的校验一致）：
        /// <list type="number">
        /// <item>残余段原样保留未被消费的输入段 ⇒ 距离 0；</item>
        /// <item>被弧消费的段，其两端点必须距**同一条**弧的圆 ≤ <paramref name="tolerance"/>（全点校验）；</item>
        /// <item>弦中点距圆 ≤ max(tolerance, 5%·R)（矢高判据 —— 折线只以矢高意义逼近圆弧，
        /// 正多边形的顶点也共圆，但弦中点会大幅偏离）。</item>
        /// </list>
        /// </summary>
        public static void AssertFitCoversInput(
            IReadOnlyList<ViewSegment> input,
            ViewCurves<ViewSegment> curves,
            double tolerance,
            string? message = null)
        {
            var slack = Math.Max(tolerance, OnSegmentTolerance);
            var prefix = Prefix(message);

            foreach (var segment in input)
            {
                var onResidual = false;
                foreach (var residual in curves.Segments)
                {
                    if (DistanceToSegment(segment.Start, residual) <= slack
                        && DistanceToSegment(segment.End, residual) <= slack)
                    {
                        onResidual = true;
                        break;
                    }
                }

                if (!onResidual)
                {
                    Assert.That(HasArcThrough(curves.Arcs, segment.Start, segment.End, tolerance), Is.True,
                        $"{prefix} 拟合丢失了几何：输入段 {segment.Start}→{segment.End} 的两端点 "
                        + "既不在残余段上，也不在任何同一条拟合圆上");
                }

                var midpoint = new ViewPoint2(
                    (segment.Start.X + segment.End.X) / 2.0,
                    (segment.Start.Y + segment.End.Y) / 2.0);

                if (FindCovering(curves.Segments, midpoint, slack) != null)
                    continue;

                Assert.That(IsMidpointOnSomeArc(curves.Arcs, midpoint, tolerance), Is.True,
                    $"{prefix} 拟合丢失了几何：输入段 {segment.Start}→{segment.End} 的弦中点 "
                    + $"({midpoint.X:R},{midpoint.Y:R}) 距所有拟合圆过远");
            }
        }

        /// <summary>视图网格拓扑：顶点焊接后无边界边、无重复有向边，且欧拉特征为 2（亏格 0）。</summary>
        public static void AssertTopologyIsClosedManifold(
            IReadOnlyList<ViewTriangle> triangles, string? message = null)
        {
            var topology = new ViewMeshTopology(triangles);
            var prefix = Prefix(message);

            var boundary = 0;
            var directed = new Dictionary<long, int>();

            foreach (var edge in topology.Edges)
            {
                if (edge.TriB < 0)
                    boundary++;

                var key = ((long)edge.P0 << 32) | (uint)edge.P1;
                directed.TryGetValue(key, out var count);
                directed[key] = count + 1;
            }

            Assert.That(boundary, Is.EqualTo(0),
                $"{prefix} 有 {boundary} 条边界边（网格不封闭）");

            var duplicated = 0;
            foreach (var pair in directed)
                if (pair.Value != 1)
                    duplicated++;

            Assert.That(duplicated, Is.EqualTo(0),
                $"{prefix} 有 {duplicated} 条有向边重复（绕序不一致）");

            var chi = topology.Points.Count - topology.Edges.Count + topology.Triangles.Count;
            Assert.That(chi, Is.EqualTo(2),
                $"{prefix} 欧拉特征为 {chi}，亏格 0 的封闭网格应为 2"
                + $"（V={topology.Points.Count} E={topology.Edges.Count} F={topology.Triangles.Count}）");
        }

        static void AssertEndpointInBounds(ViewPoint2 point, ViewBounds2 bounds, string prefix, string viewName)
        {
            const double slack = 1e-9;
            Assert.That(point.X, Is.GreaterThanOrEqualTo(bounds.MinX - slack),
                $"{prefix} {viewName}: 端点 X={point.X:R} 小于包络下限 {bounds.MinX:R}");
            Assert.That(point.X, Is.LessThanOrEqualTo(bounds.MaxX + slack),
                $"{prefix} {viewName}: 端点 X={point.X:R} 超过包络上限 {bounds.MaxX:R}");
            Assert.That(point.Y, Is.GreaterThanOrEqualTo(bounds.MinY - slack),
                $"{prefix} {viewName}: 端点 Y={point.Y:R} 小于包络下限 {bounds.MinY:R}");
            Assert.That(point.Y, Is.LessThanOrEqualTo(bounds.MaxY + slack),
                $"{prefix} {viewName}: 端点 Y={point.Y:R} 超过包络上限 {bounds.MaxY:R}");
        }

        /// <summary>沿线段取 3 个内部采样点（1/4、1/2、3/4），避免端点因容差判定产生假阴性。</summary>
        static IEnumerable<ViewPoint2> SamplePoints(ViewSegment segment)
        {
            for (var i = 1; i <= 3; i++)
            {
                var t = i / 4.0;
                yield return new ViewPoint2(
                    segment.Start.X + (segment.End.X - segment.Start.X) * t,
                    segment.Start.Y + (segment.End.Y - segment.Start.Y) * t);
            }
        }

        static ViewSegment? FindCovering(
            IReadOnlyList<ViewSegment> segments, ViewPoint2 point, double tolerance)
        {
            foreach (var segment in segments)
                if (DistanceToSegment(point, segment) <= tolerance)
                    return segment;
            return null;
        }

        static bool HasArcThrough(IReadOnlyList<ViewArc> arcs, ViewPoint2 a, ViewPoint2 b, double tolerance)
        {
            foreach (var arc in arcs)
                if (DistanceToCircle(arc, a) <= tolerance && DistanceToCircle(arc, b) <= tolerance)
                    return true;
            return false;
        }

        static bool IsMidpointOnSomeArc(IReadOnlyList<ViewArc> arcs, ViewPoint2 point, double tolerance)
        {
            foreach (var arc in arcs)
                if (DistanceToCircle(arc, point) <= Math.Max(tolerance, 0.05 * arc.Radius))
                    return true;
            return false;
        }

        static double DistanceToCircle(ViewArc arc, ViewPoint2 point)
        {
            var dx = point.X - arc.Center.X;
            var dy = point.Y - arc.Center.Y;
            return Math.Abs(Math.Sqrt(dx * dx + dy * dy) - arc.Radius);
        }

        static double DistanceToSegment(ViewPoint2 point, ViewSegment segment)
        {
            var dx = segment.End.X - segment.Start.X;
            var dy = segment.End.Y - segment.Start.Y;
            var lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 0)
            {
                var px = point.X - segment.Start.X;
                var py = point.Y - segment.Start.Y;
                return Math.Sqrt(px * px + py * py);
            }

            var t = ((point.X - segment.Start.X) * dx + (point.Y - segment.Start.Y) * dy) / lengthSquared;
            if (t < 0) t = 0;
            if (t > 1) t = 1;

            var nearestX = segment.Start.X + dx * t;
            var nearestY = segment.Start.Y + dy * t;
            var ex = point.X - nearestX;
            var ey = point.Y - nearestY;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        static string Prefix(string? message) => message == null ? "" : message + ":";
    }
}
