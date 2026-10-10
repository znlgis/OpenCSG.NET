namespace Csg
{

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>一段圆弧或整圆（模型/图纸坐标 mm；角度弧度制、以 +X 为 0、逆时针为正）。</summary>
public readonly struct ViewArc
{
    public ViewArc(ViewPoint2 center, double radius, double startAngle, double endAngle, bool isVisible, bool isClosed)
    {
        Center = center;
        Radius = radius;
        StartAngle = startAngle;
        EndAngle = endAngle;
        IsVisible = isVisible;
        IsClosed = isClosed;
    }

    /// <summary>圆心。</summary>
    public ViewPoint2 Center { get; }

    /// <summary>半径（mm）。</summary>
    public double Radius { get; }

    /// <summary>起始角（弧度）。</summary>
    public double StartAngle { get; }

    /// <summary>终止角（弧度）。</summary>
    public double EndAngle { get; }

    /// <summary>该弧是否可见（false = 虚线层）。</summary>
    public bool IsVisible { get; }

    /// <summary>是否整圆（<see cref="StartAngle"/> 与 <see cref="EndAngle"/> 相差 2π）。</summary>
    public bool IsClosed { get; }

    /// <summary>扫掠角（度，0~360）。</summary>
    public double SweepDegrees
    {
        get
        {
            if (IsClosed) return 360.0;

            var sweep = EndAngle - StartAngle;
            while (sweep < 0) sweep += 2 * Math.PI;
            while (sweep >= 2 * Math.PI) sweep -= 2 * Math.PI;
            return sweep * 180.0 / Math.PI;
        }
    }

    public override string ToString()
        => $"圆心({Center.X:F2},{Center.Y:F2}) R{Radius:F2} {SweepDegrees:F1}° {(IsVisible ? "可见" : "虚线")}";
}

/// <summary>圆弧识别结果：识别出的圆弧 + 未被识别的折线段（两者共同覆盖输入，无重叠）。</summary>
public sealed class ViewCurves<T>
    where T : IProjectedSegment
{
    public ViewCurves(IReadOnlyList<ViewArc> arcs, IReadOnlyList<T> segments)
    {
        Arcs = arcs;
        Segments = segments;
    }

    /// <summary>识别出的圆弧/整圆（按输入顺序）。</summary>
    public IReadOnlyList<ViewArc> Arcs { get; }

    /// <summary>保留为直线的折线段。</summary>
    public IReadOnlyList<T> Segments { get; }
}

/// <summary>
/// **圆弧识别**（纯函数，图纸坐标）：把消隐后由三角面片离散出来的**短折线**还原成圆弧/整圆，
/// 使落图实体与甲方图纸一致（源图 6-1 实测有 97 ARC + 13 CIRCLE，全部以真曲线表达；我们此前只能落折线）。
/// <para>
/// 判据（全部满足才合并，宁缺勿滥）：
/// ① 折线段首尾相接成链（端点容差 <see cref="MinJoinTolerance"/>，实际按视图尺度自适应）；
/// ② 链上连续 ≥ <c>minSegments</c> 段、≥ 4 个顶点的**代数最小二乘圆拟合**（Kåsa）有效；
/// ③ 所有参与点偏离拟合圆 ≤ <c>tolerance</c>（默认 0.05 mm）；
/// ④ 半径 ∈ [<see cref="MinRadius"/>, <see cref="MaxRadius"/>]、扫掠角 ≥ <see cref="MinSweepDegrees"/>。
/// </para>
/// <para>
/// 可见段与隐藏段**各自独立**成链与拟合（同一几何的可见弧与虚线段不得互相连接）。
/// 识别失败一律回退为原折线 ⇒ 本类只做"更接近源图的实体类型"，不会丢线。
/// </para>
/// </summary>
public static class ViewArcFitter
{
    /// <summary>端点相接容差下限（mm）；实际值按视图尺度自适应（<see cref="Fit"/> 的 joinTolerance）。</summary>
    public const double MinJoinTolerance = 1e-3;

    /// <summary>最小可识别半径下限（mm）——实际下限取 max(此值, 视图对角线 × <see cref="MinRadiusFraction"/>)。</summary>
    public const double MinRadius = 0.5;

    /// <summary>
    /// 最小可识别半径 / 视图对角线：碎片段会拟合成 R≈0.1mm 的"小圈"（图上不可见，纯噪声），
    /// 1e-3 相当于"图面上小于 0.5mm 的圆不落 ARC/CIRCLE 实体"（10m 视图 ⇒ R &lt; 10mm 判为碎片）。
    /// </summary>
    public const double MinRadiusFraction = 1e-3;

    /// <summary>最大可识别半径（mm）——接近直线的拟合按折线保留。</summary>
    public const double MaxRadius = 1e6;

    /// <summary>
    /// 半径 / 链包围盒对角线 的上限（**曲率显著性判据**）：直线链会被拟合成 R≈∞ 的巨圆
    /// （矢高≈0、容差判据全都通过），必须卡掉。12 倍等价于要求扫掠角 ≳ 5°（与
    /// <see cref="MinSweepDegrees"/> 同口径）：弦 = 2R·sin(θ/2) ⇒ R ≤ 12·弦 ⇔ sin(θ/2) ≥ 1/24。
    /// </summary>
    public const double MaxRadiusToExtent = 5.0;

    /// <summary>
    /// 最小扫掠角（度）——太短的弧不值得换实体类型（10° 的弦矢高只有弦长的 0.4%，图上与直线无异）。
    /// </summary>
    public const double MinSweepDegrees = 10.0;

    /// <summary>
    /// 近闭合补圆的最大缺角（度）：消隐栅格会丢掉个别棱（实测圆柱轴向视图缺一条 15° 弦 ⇒ 只能得到
    /// 345° 的开口弧），而模型里的孔/圆环本身是闭合的 ⇒ 缺角不超过该阈值时补成整圆
    /// （孔径符号在图上才是完整的圆；不补的话轮廓上会留一道缺口）。
    /// </summary>
    public const double MaxClosingGapDegrees = 25.0;

    /// <summary>默认拟合容差（mm）。</summary>
    public const double DefaultTolerance = 0.05;

    /// <summary>打开链诊断（探针/测试用；默认关，避免热路径拼字符串）。</summary>
    public static bool DiagnosticsEnabled { get; set; }

    /// <summary>最近一次 <see cref="Fit"/> 的链诊断（<see cref="DiagnosticsEnabled"/> 打开时有效）。</summary>
    public static string LastDiagnostics { get; private set; } = string.Empty;

    /// <summary>默认最少段数（3 段 = 4 个顶点才有拟合意义）。</summary>
    public const int DefaultMinSegments = 3;

    /// <summary>把 <paramref name="segments"/> 中的圆弧段识别出来；返回圆弧 + 剩余折线。</summary>
    /// <param name="segments">投影线（消隐后、**未经碎片清理**的原始段最好 —— 清理会把弧压成直线）。</param>
    /// <param name="tolerance">拟合容差（mm，点到圆的最大偏差）。</param>
    /// <param name="minSegments">最少段数（3 段 = 4 点才有拟合意义）。</param>
    /// <param name="joinTolerance">端点相接容差（mm）；null ⇒ 按视图对角线自适应 max(1e-3, 对角线×1e-4)。</param>
    /// <remarks>
    /// <b>成链不分可见性</b>：贴着自身面的轮廓会被消隐判成 V-H 交替，按可见性分组成链会把一条完整的弧
    /// 切成一段段认不出来的碎链（用户实测 2026-10-09：三维里的圆弧落到二维全成折线）。整段弧的可见性
    /// 改由所辖线段的**多数票**决定 —— 真实被遮挡的弧（如孔的另一侧）仍然整体落虚线。
    /// </remarks>
    public static ViewCurves<T> Fit<T>(
        IReadOnlyList<T> segments,
        double tolerance = DefaultTolerance,
        int minSegments = DefaultMinSegments,
        double? joinTolerance = null)
        where T : IProjectedSegment
    {
        if (segments == null) throw new ArgumentNullException(nameof(segments));
        if (tolerance <= 0) tolerance = DefaultTolerance;
        if (minSegments < 3) minSegments = 3;

        var bounds = ViewBounds2.Of(EnumeratePoints(segments));
        var diagonal = Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height);

        var join = joinTolerance.GetValueOrDefault(0);
        if (join <= 0)
            join = Math.Max(MinJoinTolerance, diagonal * 1e-4);

        var minRadius = Math.Max(MinRadius, diagonal * MinRadiusFraction);

        var arcs = new List<ViewArc>();
        var kept = new List<T>(segments.Count);
        var consumed = new bool[segments.Count];

        // 退化段（长度 ≤ max(容差, 接链容差)）：平行于视线的棱在 2D 里投影成一个点 —— 它们没有方向信息，
        // 却会把"前缘圆"和"后缘圆"在同一个投影点处串成一条乱链（圆柱轴向视图实测：整条链跨两圈 ⇒ 认不出圆）。
        // 直接排除出成链图，它们仍留在 kept 里交给后续碎片清理丢弃。
        var degenerate = Math.Max(tolerance, join);

        // 几何重复段折叠：投影会把"前后两条重合的棱"压成同一段（圆柱轴向视图：前缘圆与后缘圆逐点重合，
        // 各棱还被深度栅格切成上百个 0.1mm 小段）⇒ 不折叠的话链会在两条重合棱之间来回跳，永远闭不上口
        // （实测：整圈折线被拟合成 345° 的开口弧、且一条不剩地吞掉所有线段）。
        var group = BuildDuplicateGroups(segments, join);
        var effectiveVisible = new bool[segments.Count];
        for (var i = 0; i < segments.Count; i++)
            effectiveVisible[group[i]] |= segments[i].IsVisible;

        var chains = BuildChains(segments, group, consumed, join, degenerate);

        if (DiagnosticsEnabled)
        {
            var sizes = chains.Select(c => c.Count).OrderByDescending(n => n).Take(6).ToList();
            var representatives = 0;
            for (var i = 0; i < group.Length; i++)
                if (group[i] == i) representatives++;

            var detail = new System.Text.StringBuilder();
            detail.Append($"输入 {segments.Count} 段（代表 {representatives}）")
                  .Append($"，退化段阈值 {degenerate:F4}，接链容差 {join:F4}，最小半径 {minRadius:F3}；")
                  .Append($"链 {chains.Count} 条，最大几条：[{string.Join(", ", sizes)}]");
            foreach (var chain in chains.OrderByDescending(c => c.Count).Take(2))
            {
                var head = segments[chain[0]].Start;
                var tail = segments[chain[chain.Count - 1]].End;
                var gap = Math.Sqrt((head.X - tail.X) * (head.X - tail.X) + (head.Y - tail.Y) * (head.Y - tail.Y));
                detail.Append($"; 链{chain.Count}: 首({head.X:F2},{head.Y:F2}) 末({tail.X:F2},{tail.Y:F2}) 首末距 {gap:F3}");
            }

            LastDiagnostics = detail.ToString();
        }

        foreach (var chain in chains)
            ScanChain(segments, effectiveVisible, chain, consumed, group, arcs, tolerance, minSegments, join, minRadius);

        for (var i = 0; i < segments.Count; i++)
            if (!consumed[group[i]])
                kept.Add(segments[i]);

        return new ViewCurves<T>(MergeConcyclic(arcs), kept);
    }

    /// <summary>共圆聚合时判定"同一个圆"的半径/圆心相对容差。</summary>
    public const double SameCircleRelativeTolerance = 1e-3;

    /// <summary>同一条圆上两段弧相接的最大缝隙（度）：缝隙小于它就并成一段。</summary>
    public const double ConcyclicMergeGapDegrees = 1.0;

    /// <summary>
    /// **共圆聚合**：把同圆心、同半径的多段弧并成一条。
    /// <para>
    /// 孔/圆环的轮廓常被消隐栅格打断成**互不连通**的几条链（不止"缺一条弦"），单条链永远闭不上口 ⇒
    /// 整圆永远出不来。这里按"同圆心同半径"把所有碎片弧归组，取角度并集：
    /// 并集覆盖整圈（缺口 ≤ <see cref="MaxClosingGapDegrees"/>）⇒ 补成**整圆**；
    /// 否则输出合并后的连续弧段（同一条圆上的实体数大幅下降）。可见性按所辖扫掠角多数票。
    /// </para>
    /// </summary>
    public static IReadOnlyList<ViewArc> MergeConcyclic(IReadOnlyList<ViewArc> arcs)
    {
        if (arcs == null) throw new ArgumentNullException(nameof(arcs));
        if (arcs.Count <= 1) return arcs;

        var groups = new List<List<ViewArc>>();
        foreach (var arc in arcs)
        {
            List<ViewArc>? target = null;
            foreach (var candidate in groups)
            {
                if (IsSameCircle(candidate[0], arc))
                {
                    target = candidate;
                    break;
                }
            }

            if (target == null)
            {
                target = new List<ViewArc>();
                groups.Add(target);
            }

            target.Add(arc);
        }

        var merged = new List<ViewArc>(arcs.Count);
        foreach (var group in groups)
        {
            if (group.Count == 1)
            {
                merged.Add(group[0]);
                continue;
            }

            merged.AddRange(MergeGroup(group));
        }

        return merged;
    }

    private static bool IsSameCircle(ViewArc a, ViewArc b)
    {
        var scale = Math.Max(Math.Max(a.Radius, b.Radius), 1.0);
        var relative = scale * SameCircleRelativeTolerance;

        if (Math.Abs(a.Radius - b.Radius) > Math.Max(DefaultTolerance, relative)) return false;

        var dx = a.Center.X - b.Center.X;
        var dy = a.Center.Y - b.Center.Y;
        return Math.Sqrt(dx * dx + dy * dy) <= Math.Max(DefaultTolerance, relative);
    }

    /// <summary>把同一圆上的多段弧按角度并集合并（含跨 0° 回绕）；覆盖整圈时补成整圆。</summary>
    private static List<ViewArc> MergeGroup(IReadOnlyList<ViewArc> group)
    {
        var head = group[0];
        var mergeGap = ConcyclicMergeGapDegrees * Math.PI / 180.0;
        var closeGap = MaxClosingGapDegrees * Math.PI / 180.0;

        var spans = new List<(double Start, double End, double VisibleSweep, double Sweep)>(group.Count);
        foreach (var arc in group)
        {
            var sweep = arc.IsClosed ? 2 * Math.PI : Unwrap(arc.EndAngle - arc.StartAngle);
            var start = Unwrap(arc.StartAngle);
            spans.Add((start, start + sweep, arc.IsVisible ? sweep : 0, sweep));
        }

        spans.Sort((x, y) => x.Start.CompareTo(y.Start));

        var merged = new List<(double Start, double End, double VisibleSweep, double Sweep)>(spans.Count);
        foreach (var span in spans)
        {
            if (merged.Count == 0)
            {
                merged.Add(span);
                continue;
            }

            var last = merged[merged.Count - 1];
            if (span.Start <= last.End + mergeGap)
            {
                merged[merged.Count - 1] = (
                    last.Start,
                    Math.Max(last.End, span.End),
                    last.VisibleSweep + span.VisibleSweep,
                    last.Sweep + span.Sweep);
                continue;
            }

            merged.Add(span);
        }

        // 回绕：末段跨过 0° 与首段相接时并成一段（角度可超过 2π，落图时宿主自行归一）。
        while (merged.Count > 1
            && merged[0].Start + 2 * Math.PI <= merged[merged.Count - 1].End + mergeGap)
        {
            var first = merged[0];
            var last = merged[merged.Count - 1];
            merged.RemoveAt(merged.Count - 1);
            merged.RemoveAt(0);
            merged.Insert(0, (
                last.Start,
                first.End + 2 * Math.PI,
                first.VisibleSweep + last.VisibleSweep,
                first.Sweep + last.Sweep));
        }

        var result = new List<ViewArc>(merged.Count);
        foreach (var span in merged)
        {
            var sweep = span.End - span.Start;
            var visible = span.Sweep <= 0 || span.VisibleSweep * 2 >= span.Sweep;

            if (sweep >= 2 * Math.PI - closeGap)
            {
                // 并集覆盖整圈 ⇒ 补成整圆（起点取缺口之后的那个角）。
                result.Add(new ViewArc(head.Center, head.Radius, span.Start, span.Start + 2 * Math.PI, visible, true));
                continue;
            }

            if (sweep * 180.0 / Math.PI < MinSweepDegrees) continue;

            result.Add(new ViewArc(head.Center, head.Radius, span.Start, span.Start + sweep, visible, false));
        }

        return result;
    }

    private static IEnumerable<ViewPoint2> EnumeratePoints<T>(IReadOnlyList<T> segments)
        where T : IProjectedSegment
    {
        foreach (var segment in segments)
        {
            yield return segment.Start;
            yield return segment.End;
        }
    }

    // —— ① 成链 ——

    /// <summary>几何重复段的代表元：<c>group[i]</c> = i 所在重复组的代表下标（自己就是代表时为 i）。</summary>
    private static int[] BuildDuplicateGroups<T>(IReadOnlyList<T> segments, double joinTolerance)
        where T : IProjectedSegment
    {
        var map = new Dictionary<(long, long, long, long), int>(segments.Count);
        var group = new int[segments.Count];

        for (var i = 0; i < segments.Count; i++)
        {
            group[i] = i;

            var (ax, ay) = Key(segments[i].Start, joinTolerance);
            var (bx, by) = Key(segments[i].End, joinTolerance);

            if (map.TryGetValue((ax, ay, bx, by), out var forward))
            {
                group[i] = group[forward];
                continue;
            }

            if (map.TryGetValue((bx, by, ax, ay), out var backward))
            {
                group[i] = group[backward];
                continue;
            }

            map[(ax, ay, bx, by)] = i;
        }

        return group;
    }

    private static List<List<int>> BuildChains<T>(
        IReadOnlyList<T> segments,
        int[] group,
        bool[] consumed,
        double joinTolerance,
        double degenerateLength)
        where T : IProjectedSegment
    {
        var buckets = new Dictionary<(long, long), List<int>>();
        void Add(ViewPoint2 p, int index)
        {
            var key = Key(p, joinTolerance);
            if (!buckets.TryGetValue(key, out var list))
            {
                list = new List<int>(2);
                buckets[key] = list;
            }
            list.Add(index);
        }

        for (var i = 0; i < segments.Count; i++)
        {
            if (consumed[i] || group[i] != i || Length(segments[i]) <= degenerateLength) continue;
            Add(segments[i].Start, i);
            Add(segments[i].End, i);
        }

        var chains = new List<List<int>>();
        var used = new HashSet<int>();

        for (var seed = 0; seed < segments.Count; seed++)
        {
            if (consumed[seed] || group[seed] != seed || used.Contains(seed)
                || Length(segments[seed]) <= degenerateLength) continue;

            var chain = new List<int> { seed };
            used.Add(seed);

            // 向两端延伸（靠端点索引找下一条未用线段）。
            Grow(chain, forward: true);
            Grow(chain, forward: false);

            chains.Add(chain);
        }

        return chains;

        void Grow(List<int> chain, bool forward)
        {
            while (true)
            {
                var lastIndex = forward ? chain[chain.Count - 1] : chain[0];
                var tip = forward ? segments[lastIndex].End : segments[lastIndex].Start;
                var next = FindNext(tip, lastIndex, forward);
                if (next < 0) return;

                used.Add(next);
                if (forward) chain.Add(next); else chain.Insert(0, next);
            }
        }

        int FindNext(ViewPoint2 tip, int source, bool forward)
        {
            if (!buckets.TryGetValue(Key(tip, joinTolerance), out var candidates)) return -1;

            var best = -1;
            var bestScore = double.MaxValue;
            foreach (var candidate in candidates)
            {
                if (candidate == source || used.Contains(candidate) || consumed[candidate]) continue;
                if (group[candidate] != candidate || Length(segments[candidate]) <= degenerateLength) continue;

                // 端点相接（容差随视图尺度自适应，用来过滤哈希网格的近似命中）。
                var start = segments[candidate].Start;
                var end = segments[candidate].End;
                var connects = Distance(start, tip) <= joinTolerance || Distance(end, tip) <= joinTolerance;
                if (!connects) continue;

                var other = Distance(start, tip) <= joinTolerance ? end : start;
                var incoming = Subtract(tip, forward ? segments[source].Start : segments[source].End);
                var outgoing = Subtract(other, tip);

                // 取"转角最小"的候选：链优先沿平滑方向延续。
                var score = AngleBetween(incoming, outgoing);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }
    }

    // —— ②③④ 逐链扫描圆弧段 ——

    private static void ScanChain<T>(
        IReadOnlyList<T> segments,
        bool[] effectiveVisible,
        List<int> chain,
        bool[] consumed,
        int[] group,
        List<ViewArc> arcs,
        double tolerance,
        int minSegments,
        double joinTolerance,
        double minRadius)
        where T : IProjectedSegment
    {
        if (chain.Count < minSegments) return;

        var vertices = new List<ViewPoint2>(chain.Count + 1);
        var first = segments[chain[0]];
        vertices.Add(first.Start);
        foreach (var index in chain)
            vertices.Add(segments[index].End);

        // ① 先试「整条链就是一段圆弧/整圆」——这是最常见的情形（一条投影边链 = 一段弧），
        //    且全局最小二乘对逐点抖动的抑制最好，不会被局部窗口切成碎片。
        if (TryFitCircle(vertices, 0, vertices.Count, tolerance, minRadius, out var whole)
            && TryBuildArc(vertices, 0, vertices.Count - 1, whole,
                MajorityVisible(effectiveVisible, chain, 0, chain.Count), tolerance, joinTolerance, out var wholeArc))
        {
            arcs.Add(wholeArc);
            foreach (var index in chain)
                consumed[index] = true;
            return;
        }

        // ② 退化为滑动窗口扫描：链上混有直线段（如"圆弧 + 直边"的轮廓）时逐段找出圆弧。
        var i = 0;

        while (i + minSegments < vertices.Count)
        {
            if (!TryFitCircle(vertices, i, minSegments + 1, tolerance, minRadius, out var circle))
            {
                i++;
                continue;
            }

            // 尽量向后延伸：逐点校验（种子圆或最近一次重拟合圆任一命中即接受），
            // 周期性重拟合以抑制漂移 —— 单点抖动不得把一条长弧切成碎片（"先接受、后整体校验"）。
            var accepted = i + minSegments;
            var end = accepted;
            var refreshed = circle;
            var sinceRefit = 0;
            while (end + 1 < vertices.Count)
            {
                var candidate = end + 1;
                var next = vertices[candidate];
                var onSeed = DistanceToCircle(circle, next) <= tolerance;
                var onRefreshed = DistanceToCircle(refreshed, next) <= tolerance;
                if (!onSeed && !onRefreshed) break;

                end = candidate;
                accepted = candidate;

                if (++sinceRefit >= 8
                    && TryFitCircle(vertices, i, end - i + 1, tolerance, minRadius, out var fresh))
                {
                    refreshed = fresh;
                    sinceRefit = 0;
                }
            }

            var span = accepted - i;          // 段数
            if (span < minSegments)
            {
                i++;
                continue;
            }

            if (!TryBuildArc(vertices, i, accepted, circle,
                    MajorityVisible(effectiveVisible, chain, i, accepted), tolerance, joinTolerance, out var arc))
            {
                i++;
                continue;
            }

            arcs.Add(arc);
            for (var k = i; k < accepted; k++)
                consumed[chain[k]] = true;

            i = accepted;
        }
    }

    /// <summary>所辖线段的多数票可见性（弧跨 V-H 交替时按多数落实线/虚线）。</summary>
    private static bool MajorityVisible(
        bool[] effectiveVisible, List<int> chain, int from, int toExclusive)
    {
        var visible = 0;
        var total = 0;
        for (var k = from; k < toExclusive && k < chain.Count; k++)
        {
            total++;
            if (effectiveVisible[chain[k]]) visible++;
        }

        return total == 0 || visible * 2 >= total;
    }

    private static bool TryBuildArc(
        List<ViewPoint2> vertices,
        int from,
        int to,
        Circle circle,
        bool visible,
        double tolerance,
        double joinTolerance,
        out ViewArc arc)
    {
        arc = default;

        // —— 弧的起止角：取"包含全部点、扫掠最小"的那段弧（等价于补上最大角间隙）——
        // 早前用"首末点角度 + 中间点定方向"的写法在末点角度恰好落在起点**之前**时会算出
        // 349° 的假弧（2026-10-09 用户实测：图上出现凭空的大圈），故改为按角间隙求解。
        var angles = new List<double>(to - from + 1);
        for (var k = from; k <= to; k++)
            angles.Add(Unwrap(Angle(circle.Center, vertices[k])));
        angles.Sort();

        var gapIndex = 0;
        var largestGap = 2 * Math.PI - (angles[angles.Count - 1] - angles[0]);
        for (var k = 1; k < angles.Count; k++)
        {
            var gap = angles[k] - angles[k - 1];
            if (gap > largestGap)
            {
                largestGap = gap;
                gapIndex = k;
            }
        }

        var sweep = 2 * Math.PI - largestGap;
        var start = angles[gapIndex % angles.Count];

        // 所有点都必须贴在拟合圆上（防链上混入直线段）。
        for (var k = from; k <= to; k++)
            if (DistanceToCircle(circle, vertices[k]) > tolerance) return false;

        // **曲率显著性**：半径必须与链自身尺度相称（直线链拟出的 R≈∞ 巨圆在此被卡掉）。
        var extent = DiagonalOf(vertices, from, to);
        if (circle.Radius > MaxRadiusToExtent * Math.Max(extent, 1e-9)) return false;

        // **弦中点（矢高）判据**：正多边形的顶点也共圆，但其弦中点会大幅偏离圆 ——
        // 只有"折线本身就在逼近这段圆弧"（矢高 ≤ max(容差, 5%R)）才允许换成圆弧实体。
        var sagittaAllowance = Math.Max(tolerance, 0.05 * circle.Radius);
        for (var k = from; k < to; k++)
        {
            var midpoint = new ViewPoint2(
                (vertices[k].X + vertices[k + 1].X) / 2.0,
                (vertices[k].Y + vertices[k + 1].Y) / 2.0);
            if (DistanceToCircle(circle, midpoint) > sagittaAllowance) return false;
        }

        // 整圆判定：链首尾相接 **且** 角度绕满一圈（最大角间隙不超过 120°）。
        var closed = to - from >= 3
            && Distance(vertices[from], vertices[to]) <= joinTolerance
            && largestGap <= 2 * Math.PI / 3;

        if (closed)
        {
            sweep = 2 * Math.PI;
        }
        else if (largestGap <= MaxClosingGapDegrees * Math.PI / 180.0)
        {
            // 近闭合补圆：模型里的孔/圆环本身闭合，投影栅格却会丢掉个别棱（实测缺 15° 弦）。
            closed = true;
            sweep = 2 * Math.PI;
        }

        if (sweep * 180.0 / Math.PI < MinSweepDegrees) return false;

        arc = new ViewArc(circle.Center, circle.Radius, start, start + sweep, visible, closed);
        return true;
    }

    private static bool TryFitCircle(
        List<ViewPoint2> vertices, int from, int count, double tolerance, double minRadius, out Circle circle)
    {
        circle = default;
        if (count < 3 || from + count > vertices.Count) return false;

        double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0, sxz = 0, syz = 0, sz = 0;

        for (var k = from; k < from + count; k++)
        {
            var x = vertices[k].X;
            var y = vertices[k].Y;
            var z = x * x + y * y;

            sx += x; sy += y; sz += z;
            sxx += x * x; syy += y * y; sxy += x * y;
            sxz += x * z; syz += y * z;
        }

        var n = count;
        var det = Determinant(
            sxx, sxy, sx,
            sxy, syy, sy,
            sx, sy, n);

        if (Math.Abs(det) < 1e-12) return false;

        var detA = Determinant(
            sxz, sxy, sx,
            syz, syy, sy,
            sz, sy, n);
        var detB = Determinant(
            sxx, sxz, sx,
            sxy, syz, sy,
            sx, sz, n);
        var detC = Determinant(
            sxx, sxy, sxz,
            sxy, syy, syz,
            sx, sy, sz);

        var a = detA / det;
        var b = detB / det;
        var c = detC / det;

        var cx = a / 2.0;
        var cy = b / 2.0;
        var radiusSquared = c + cx * cx + cy * cy;
        if (radiusSquared <= 0) return false;

        var radius = Math.Sqrt(radiusSquared);
        if (!IsFinite(radius) || radius < minRadius || radius > MaxRadius) return false;

        circle = new Circle(new ViewPoint2(cx, cy), radius);

        // 拟合质量：参与点必须全部贴合（否则链上混了直线段）。
        for (var k = from; k < from + count; k++)
            if (DistanceToCircle(circle, vertices[k]) > tolerance)
                return false;

        return true;
    }

    // —— 工具 ——

    private readonly struct Circle
    {
        public Circle(ViewPoint2 center, double radius)
        {
            Center = center;
            Radius = radius;
        }

        public ViewPoint2 Center { get; }
        public double Radius { get; }
    }

    private static double Determinant(
        double a11, double a12, double a13,
        double a21, double a22, double a23,
        double a31, double a32, double a33)
        => a11 * (a22 * a33 - a23 * a32)
         - a12 * (a21 * a33 - a23 * a31)
         + a13 * (a21 * a32 - a22 * a31);

    private static (long, long) Key(ViewPoint2 p, double joinTolerance)
        => ((long)Math.Round(p.X / joinTolerance), (long)Math.Round(p.Y / joinTolerance));

    private static double Length<T>(T segment)
        where T : IProjectedSegment
        => Distance(segment.Start, segment.End);

    private static double Distance(ViewPoint2 a, ViewPoint2 b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static ViewPoint2 Subtract(ViewPoint2 a, ViewPoint2 b) => new(a.X - b.X, a.Y - b.Y);

    private static double Angle(ViewPoint2 center, ViewPoint2 p) => Math.Atan2(p.Y - center.Y, p.X - center.X);

    private static double DistanceToCircle(Circle circle, ViewPoint2 p)
        => Math.Abs(Distance(circle.Center, p) - circle.Radius);

    /// <summary>顶点区间 [from, to] 的包围盒对角线（曲率显著性判据的尺度基准）。</summary>
    private static double DiagonalOf(List<ViewPoint2> vertices, int from, int to)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        for (var k = from; k <= to && k < vertices.Count; k++)
        {
            var p = vertices[k];
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }

        var width = maxX - minX;
        var height = maxY - minY;
        return Math.Sqrt(width * width + height * height);
    }

    private static double Unwrap(double angle)
    {
        while (angle < 0) angle += 2 * Math.PI;
        while (angle >= 2 * Math.PI) angle -= 2 * Math.PI;
        return angle;
    }

    private static double AngleBetween(ViewPoint2 incoming, ViewPoint2 outgoing)
    {
        var li = Math.Sqrt(incoming.X * incoming.X + incoming.Y * incoming.Y);
        var lo = Math.Sqrt(outgoing.X * outgoing.X + outgoing.Y * outgoing.Y);
        if (li < 1e-12 || lo < 1e-12) return Math.PI;

        var dot = (incoming.X * outgoing.X + incoming.Y * outgoing.Y) / (li * lo);
        if (dot > 1) dot = 1;
        if (dot < -1) dot = -1;
        return Math.Acos(dot);
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
}

