namespace Csg
{

using System;
using System.Collections.Generic;

/// <summary>
/// 图线清理（消隐后处理）：把 Z-buffer 逐格采样产生的**微小抖动碎片**收敛为工程图应有的连续线段。
/// <para>
/// <b>为什么必须做</b>：消隐按栅格逐点比较深度，一条恰好贴着面的棱会在若干格上被判为「略被遮挡」，
/// 于是同一条棱被切成 V-H-V-H 的碎段（实测 5% 的段长度为零、大量 10mm 级碎段）。
/// 直接把这些段写进 DWG，图面会变成虚线密集的「毛边」，无法用作工程图。
/// </para>
/// <para>
/// 做法（保持可见/隐藏语义不变，只做收敛）：
/// <list type="number">
/// <item>丢弃长度低于阈值的碎段（阈值 = max(绝对下限, 视图对角线 × 相对下限)，按视图尺度自适应）；</item>
/// <item>把方向相近、端点相接的**同可见性**碎段合并成一条（共线延长，取投影最远端点）；</item>
/// <item>合并后仍过短的段丢弃。</item>
/// </list>
/// <b>不跨可见性合并</b> —— 虚线不能与实线接成一条；合并**只延长不缩短**。
/// </para>
/// </summary>
public static class ViewSegmentCleanup
{
    /// <summary>绝对下限（模型单位 mm）：低于此长度的图线在图面上无意义。</summary>
    public const double MinAbsoluteLength = 1e-3;

    /// <summary>相对下限：视图对角线长度的该比例（大件允许更长的碎段被判为无意义）。</summary>
    public const double MinRelativeLength = 1e-4;

    /// <summary>端点相接容差 = 长度下限 × 该倍数。</summary>
    private const double JoinToleranceFactor = 4.0;

    /// <summary>方向相近判据：单位向量点积 ≥ 该值（≈ 夹角 ≤ 8°）才允许合并。</summary>
    private const double DirectionCosineThreshold = 0.99;

    /// <summary>清理一套线段（按给定包络自适应阈值）；返回新列表，不修改入参。</summary>
    public static IReadOnlyList<ViewSegment> Clean(IReadOnlyList<ViewSegment> segments, ViewBounds2 bounds)
    {
        if (segments == null) throw new ArgumentNullException(nameof(segments));
        if (segments.Count == 0)
            return Array.Empty<ViewSegment>();

        var diagonal = Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height);
        var minLength = Math.Max(MinAbsoluteLength, diagonal * MinRelativeLength);
        var joinTolerance = minLength * JoinToleranceFactor;

        // ① 丢弃过短碎段。
        var kept = new List<ViewSegment>(segments.Count);
        foreach (var segment in segments)
        {
            if (segment.Length >= minLength)
                kept.Add(segment);
        }

        // ② 合并同可见性、方向相近、端点相接的段。
        var merged = new List<ViewSegment>(kept.Count);
        foreach (var segment in kept)
        {
            var absorbed = false;
            for (var i = 0; i < merged.Count; i++)
            {
                if (merged[i].IsVisible != segment.IsVisible)
                    continue;
                if (!TryExtend(merged[i], segment, joinTolerance, out var extended))
                    continue;

                merged[i] = extended;
                absorbed = true;
                break;
            }

            if (!absorbed)
                merged.Add(segment);
        }

        // ③ 合并后仍过短的段丢弃。
        var pruned = new List<ViewSegment>(merged.Count);
        foreach (var segment in merged)
        {
            if (segment.Length >= minLength)
                pruned.Add(segment);
        }

        // ④ 折叠**同可见性**的重复/重叠段：同一条模型棱可能被两组三角形各投影一次（重叠区被画两遍），
        //    折叠后每段只保留一份，图线数与 DWG 实体数随之下降。
        return CollapseOverlaps(pruned, joinTolerance);
    }

    /// <summary>按（可见性 + 共线方向 + 区间）折叠重叠段；返回每组的并区间。</summary>
    private static List<ViewSegment> CollapseOverlaps(List<ViewSegment> segments, double tolerance)
    {
        var result = new List<ViewSegment>(segments.Count);
        var consumed = new bool[segments.Count];

        for (var i = 0; i < segments.Count; i++)
        {
            if (consumed[i])
                continue;

            var group = new List<ViewSegment> { segments[i] };
            consumed[i] = true;
            var baseSegment = segments[i];
            if (!TryDirection(baseSegment, out var direction))
            {
                result.Add(baseSegment);
                continue;
            }

            var origin = baseSegment.Start;
            var low = Math.Min(Project(baseSegment.Start, origin, direction), Project(baseSegment.End, origin, direction));
            var high = Math.Max(Project(baseSegment.Start, origin, direction), Project(baseSegment.End, origin, direction));

            for (var j = i + 1; j < segments.Count; j++)
            {
                if (consumed[j] || segments[j].IsVisible != baseSegment.IsVisible)
                    continue;
                if (!SameDirection(direction, segments[j]))
                    continue;

                // 共线判据必须包含"到基准直线的垂距"：只比方向与投影区间不够
                // —— 竖直段在水平基准上的投影会退化为一点，若此时基准水平长度也接近 0，
                //    区间比较会误判为"重叠"而把互相垂直的两条棱合并成一条错线。
                if (!IsOnSameLine(baseSegment, segments[j], direction, tolerance))
                    continue;

                var t0 = Project(segments[j].Start, origin, direction);
                var t1 = Project(segments[j].End, origin, direction);
                var lowJ = Math.Min(t0, t1);
                var highJ = Math.Max(t0, t1);

                // 只在「重叠或相接」时归并（不合并同一线上相隔很远的段）。
                if (lowJ > high + tolerance || highJ < low - tolerance)
                    continue;

                consumed[j] = true;
                group.Add(segments[j]);
                low = Math.Min(low, lowJ);
                high = Math.Max(high, highJ);
            }

            var from = new ViewPoint2(origin.X + direction.X * low, origin.Y + direction.Y * low);
            var to = new ViewPoint2(origin.X + direction.X * high, origin.Y + direction.Y * high);
            result.Add(new ViewSegment(from, to, baseSegment.IsVisible));
        }

        return result;
    }

    /// <summary>把 <paramref name="add"/> 并到 <paramref name="baseSegment"/> 上（仅当共线且端点相接）；合并只延长不缩短。</summary>
    private static bool TryExtend(
        ViewSegment baseSegment, ViewSegment add, double tolerance, out ViewSegment extended)
    {
        extended = baseSegment;

        if (!TryDirection(baseSegment, out var direction))
            return false;
        if (!SameDirection(direction, add))
            return false;

        // 共线判据：两端点到基准直线的垂距必须在容差内。
        // **不能只比方向与投影区间** —— 基准段在自身方向上的投影可能退化（例如长度定义方向的边界情形），
        // 此时"区间重叠"会把互相垂直的棱误判为可合并，把 4 条轮廓边并成 2 条。
        var nx = -direction.Y;
        var ny = direction.X;
        if (PerpendicularDistance(add.Start, baseSegment.Start, nx, ny) > tolerance
            || PerpendicularDistance(add.End, baseSegment.Start, nx, ny) > tolerance)
        {
            return false;
        }

        var p0 = baseSegment.Start;
        var p1 = baseSegment.End;
        var t0 = Project(p0, p0, direction);
        var t1 = Project(p1, p0, direction);

        // 以 base 的两个端点定区间，按 t 归一化到 [0,1]。
        var low = Math.Min(t0, t1);
        var high = Math.Max(t0, t1);
        var span = high - low;
        if (span <= 0)
            return false;

        var add0 = Project(add.Start, p0, direction);
        var add1 = Project(add.End, p0, direction);
        var addLow = Math.Min(add0, add1);
        var addHigh = Math.Max(add0, add1);

        // 端点相接：两段区间在容差内相邻或重叠。
        var touching = addLow <= high + tolerance && addHigh >= low - tolerance;
        if (!touching)
            return false;

        var newLow = Math.Min(low, addLow);
        var newHigh = Math.Max(high, addHigh);
        if (newHigh - newLow < span - 1e-12)
            return false; // 只允许延长

        var u0 = (newLow - low) / span;
        var u1 = (newHigh - low) / span;
        var from = new ViewPoint2(p0.X + direction.X * u0 * span, p0.Y + direction.Y * u0 * span);
        var to = new ViewPoint2(p0.X + direction.X * u1 * span, p0.Y + direction.Y * u1 * span);

        extended = new ViewSegment(from, to, baseSegment.IsVisible);
        return true;
    }

    /// <summary>
    /// 两段是否落在同一条直线上：方向相近 + **两端点到基准直线的垂距都在容差内**。
    /// （仅靠"方向 + 投影区间"会在基准段投影退化时把垂直段误并进来。）
    /// </summary>
    private static bool IsOnSameLine(
        ViewSegment baseSegment, ViewSegment other, (double X, double Y) direction, double tolerance)
    {
        // 直线的法向（direction 已归一化）。
        var nx = -direction.Y;
        var ny = direction.X;
        var origin = baseSegment.Start;

        // 基准段自身两端点必须都贴在这条线上（防止 base 因退化而定义出任意方向）。
        if (PerpendicularDistance(baseSegment.Start, origin, nx, ny) > tolerance
            || PerpendicularDistance(baseSegment.End, origin, nx, ny) > tolerance)
        {
            return false;
        }

        return PerpendicularDistance(other.Start, origin, nx, ny) <= tolerance
            && PerpendicularDistance(other.End, origin, nx, ny) <= tolerance;
    }

    private static double PerpendicularDistance(
        ViewPoint2 point, ViewPoint2 origin, double nx, double ny)
        => Math.Abs((point.X - origin.X) * nx + (point.Y - origin.Y) * ny);

    private static bool SameDirection((double X, double Y) direction, ViewSegment other)
    {
        if (!TryDirection(other, out var otherDirection))
            return false;

        var dot = direction.X * otherDirection.X + direction.Y * otherDirection.Y;
        return Math.Abs(dot) >= DirectionCosineThreshold;
    }

    private static bool TryDirection(ViewSegment segment, out (double X, double Y) direction)
    {
        var dx = segment.End.X - segment.Start.X;
        var dy = segment.End.Y - segment.Start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0)
        {
            direction = (0, 0);
            return false;
        }

        direction = (dx / length, dy / length);
        return true;
    }

    private static double Project(ViewPoint2 point, ViewPoint2 origin, (double X, double Y) direction)
        => (point.X - origin.X) * direction.X + (point.Y - origin.Y) * direction.Y;
}
}

