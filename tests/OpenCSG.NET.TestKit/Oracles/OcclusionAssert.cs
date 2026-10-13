using System;
using System.Collections.Generic;
using System.Text;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>遮挡 oracle 的统计（供覆盖门与报告使用）。</summary>
    public readonly struct OcclusionCheckStats
    {
        public OcclusionCheckStats(int checkedSamples, int indeterminateSamples, int failedSamples)
        {
            CheckedSamples = checkedSamples;
            IndeterminateSamples = indeterminateSamples;
            FailedSamples = failedSamples;
        }

        /// <summary>双侧判定实际执行的采样点数。</summary>
        public int CheckedSamples { get; }

        /// <summary>稳健类集为空、被跳过的采样点数。</summary>
        public int IndeterminateSamples { get; }

        /// <summary>失败采样点数（&gt;0 时方法已抛）。</summary>
        public int FailedSamples { get; }
    }

    /// <summary>
    /// Tier 5：遮挡 oracle —— 以独立射线投射为基准，判定 <see cref="ViewProjection"/> 的
    /// 可见/隐藏拆分是否自洽（把「投影器把被遮挡的边画成实线」与「把可见的边画成虚线」都变成可复现的失败）。
    /// <para>
    /// <b>独立性声明</b>：本类不调用 <see cref="ViewProjector"/> / <c>DepthRaster</c> 的任何成员 ——
    /// 候选特征边从 <see cref="ViewMeshTopology"/> 的公开数据**独立重建**（规则与
    /// <c>ViewProjector.ProjectView</c> 的 ①② 逐条一致，但代码为独立实现，<see cref="ViewMeshTopology"/>
    /// 只作数据源），遮挡判定全部交给 <see cref="RayCast"/>；一旦复用被测链路，判定就不再是独立对照。
    /// bias / cell 两个标尺按被测实现的公开常量口径（1e-3 深度跨度、屏幕长边/900）独立复算。
    /// </para>
    /// <para>
    /// <b>双侧判定</b>：对每条输出线段的内部采样点 q（t=1/4、1/2、3/4，零长段跳过）：
    /// 在 q 附近找出 2D 重合的候选边（重合同一图元的可能有数条 —— 重叠轮廓、前后共线棱），
    /// 每条候选边在 q 的 3D 插值点做 9 探针（q 自身 + 8 邻域 ±cell）沿 −Look 的射线投射，
    /// 得到 robust 类集（某条边 9 探针全可见 / 全隐藏）与 possible 类集（出现过可见 / 出现过隐藏）；
    /// 再与投影类集（覆盖 q 的输出线段与弧的 IsVisible 集合）对拍：
    /// <list type="bullet">
    /// <item><b>missing</b>：robust 类未出现在投影类集中 —— 射线判定唯一确定的可见性被投影器画反或画丢；</item>
    /// <item><b>unjustified</b>：投影类集内的类在任何探针下都不可达 —— 投影器宣称的可见性无射线依据。</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>INDET（indeterminate）</b>：9 探针出现可见/隐藏混合时，该候选边不贡献 robust 类；若某采样点的
    /// 全部重合候选边都如此，则该点跳过双侧判定并计入
    /// <see cref="OcclusionCheckStats.IndeterminateSamples"/>。成因是<b>格量化敏感带</b>：
    /// ±cell 的探针跨越深度不连续（轮廓/台阶缘）时，同一 2D 位置的不同偏移可能落在遮挡台阶的两侧，
    /// 混合不是矛盾而是 oracle 的分辨率极限。
    /// </para>
    /// <para>
    /// <b>已知边界（不在覆盖范围）</b>：掠射轮廓 —— 视线与面相切（球轮廓、孔缘、侧视圆柱母线）时，
    /// 探针读数会在 <c>inf</c> 与有限值之间跳（射线沿面平面走、命中归属由浮点任意决定），
    /// 会系统性产生 INDET 或两侧都不成立的判定；这类形状的行为域由 DEFECT-004 机制②统一承接，
    /// 夹具应避免把掠射视角放进 oracle 覆盖。
    /// </para>
    /// <para>
    /// <b>已知边界（覆盖范围的构造性界限）</b>：
    /// <list type="bullet">
    /// <item><b>弧实体不产生采样点</b>：采样只看 <c>view.Segments</c>；被弧识别的几何已从 Segments 移出，
    /// 弧的 <c>IsVisible</c>（多数票）只在 0.06 径向容差内泄漏进邻近采样点的 proj 类集 ⇒
    /// 弧上的错误可见性只有在贴近其圆周处存活的线段上才会被间接检查。</item>
    /// <item><b>INDET 带恰是投影器的判定区</b>：±cell 探针把格量化敏感带标为 INDET ⇒
    /// <c>DepthRaster.Split</c> 的切分点放置错误与掠射族（DEFECT-004 机制②）在构造上不可由本 oracle 判别；
    /// 报告不得超过此界限宣称覆盖。</item>
    /// <item><b>possible 类集是并集</b>：重合边之间可以互换见证（A 的 robust V 可被 B 的输出 V 掩盖）——
    /// 这是 2D 图纸语义下的有意抽象。</item>
    /// </list>
    /// </para>
    /// </summary>
    public static class OcclusionAssert
    {
        /// <summary>2D 重合判据：采样点距候选边投影线段 ≤ 该值即视为同一图元（mm）。</summary>
        const double CoincidenceTolerance = 1e-6;

        /// <summary>弧重合的径向容差 = <c>ViewArcFitter.DefaultTolerance</c>(0.05) × 1.2。</summary>
        const double ArcRadialTolerance = 0.06;

        /// <summary>弧角域判据的容差（弧度）。</summary>
        const double ArcAngleTolerance = 1e-3;

        /// <summary>失败明细最多收集的条数（全部采样扫描完后一次性断言）。</summary>
        const int MaxFailureDetails = 10;

        /// <summary>与 <c>ViewProjector.RasterResolution</c> 同口径的屏幕长边分辨率（Tier5RayCastUnitTest 有常量漂移守卫）。</summary>
        public const int ScreenResolution = 900;

        /// <summary>与 <c>ViewProjector.DepthBiasFactor</c> 同口径的深度偏置系数（Tier5RayCastUnitTest 有常量漂移守卫）。</summary>
        public const double DepthBiasFactor = 1e-3;

        /// <summary>与 <c>ViewProjector.MinDepthGap</c> 同口径的偏置下限。</summary>
        const double MinDepthGap = 1e-9;

        /// <summary>低于该长度的输出线段视为零长段（其内部采样点全部退化）。</summary>
        const double ZeroLengthSegment = 1e-9;

        /// <summary>9 探针的格偏移方向（× cell）：q 自身 + 8 邻域。</summary>
        static readonly (double H, double V)[] ProbeDirections =
        {
            (0.0, 0.0),
            (1.0, 0.0), (-1.0, 0.0), (0.0, 1.0), (0.0, -1.0),
            (1.0, 1.0), (1.0, -1.0), (-1.0, 1.0), (-1.0, -1.0),
        };

        /// <summary>
        /// 对每个视图执行遮挡双侧判定，返回统计；存在任一失败采样点时抛出聚合断言。
        /// <para>
        /// 前置校验：至少有一个采样点通过判定（防「oracle 空洞」—— 若没有任何点被判定，
        /// 断言恒真、oracle 形同虚设）。
        /// </para>
        /// </summary>
        /// <param name="views">投影器输出（可见/隐藏拆分）；弧已从 <c>Segments</c> 移出，按角域单独计入。</param>
        /// <param name="triangles">投影器同源的视图三角形（射线投射的几何）。</param>
        /// <param name="message">失败消息前缀（如夹具名 / seed），便于定位。</param>
        public static OcclusionCheckStats AssertVisibilityMatchesRayCast(
            IReadOnlyList<ViewProjection> views,
            IReadOnlyList<ViewTriangle> triangles,
            string? message = null)
        {
            var prefix = message == null ? "" : message + "：";
            var mesh = new ViewMeshTopology(triangles);
            var fails = new List<string>(MaxFailureDetails);
            var checkedSamples = 0;
            var indeterminateSamples = 0;
            var failedSamples = 0;

            foreach (var view in views)
            {
                var look = view.Direction.Look;
                var right = view.Direction.Right;
                var up = view.Direction.Up;

                // ① 正对性 + 深度跨度（口径与 DepthRaster.Build 相同：只统计正对三角形）⇒ bias。
                var frontFacing = new bool[triangles.Count];
                double minD = double.MaxValue, maxD = double.MinValue;
                for (var i = 0; i < triangles.Count; i++)
                {
                    frontFacing[i] = mesh.Normals[i].Dot(look) < 0;
                    if (!frontFacing[i])
                        continue;

                    for (var v = 0; v < 3; v++)
                    {
                        var d = triangles[i][v].Dot(look);
                        if (d < minD) minD = d;
                        if (d > maxD) maxD = d;
                    }
                }

                // 与 ViewProjector.Build 同口径：深度跨度下限 1e-9（退化 —— 无正视三角形 —— 时避免 bias = −∞）。
                var range = Math.Max(maxD - minD, 1e-9);
                var bias = range * DepthBiasFactor + MinDepthGap;

                // ② 候选特征边（规则与 ViewProjector.ProjectView ①② 逐条一致，独立实现）：
                //    边界边 / 轮廓边（两侧正对性不同）/ 两侧正对时的锐边；两侧都背对 ⇒ 不画。
                var candidates = new List<ViewMeshTopology.ViewEdge>(mesh.Edges.Count / 4 + 16);
                foreach (var edge in mesh.Edges)
                {
                    if (edge.TriB < 0)
                    {
                        candidates.Add(edge);
                        continue;
                    }

                    var frontA = frontFacing[edge.TriA];
                    var frontB = frontFacing[edge.TriB];
                    if (frontA != frontB)
                    {
                        candidates.Add(edge);
                        continue;
                    }

                    if (!frontA && !frontB)
                        continue;

                    if (edge.IsSharp)
                        candidates.Add(edge);
                }

                // ③ cell：与 DepthRaster 同口径（屏幕长边/900），用于 9 探针的 ±1 格偏移。
                //    注意与 DepthRaster 的差异：那里只统计正对三角形，这里用全点集 —— 只影响 cell 尺度，可接受。
                double minH = double.MaxValue, maxH = double.MinValue;
                double minV = double.MaxValue, maxV = double.MinValue;
                foreach (var point in mesh.Points)
                {
                    var h = point.Dot(right);
                    var v = point.Dot(up);
                    if (h < minH) minH = h;
                    if (h > maxH) maxH = h;
                    if (v < minV) minV = v;
                    if (v > maxV) maxV = v;
                }

                var cell = Math.Max(Math.Max(maxH - minH, maxV - minV) / ScreenResolution, 1e-9);

                // ④ 候选边投影（2D 端点），供重合匹配与探针基点参数化复用。
                var projectedA = new ViewPoint2[candidates.Count];
                var projectedB = new ViewPoint2[candidates.Count];
                for (var i = 0; i < candidates.Count; i++)
                {
                    var a = mesh.Points[candidates[i].P0];
                    var b = mesh.Points[candidates[i].P1];
                    projectedA[i] = new ViewPoint2(a.Dot(right), a.Dot(up));
                    projectedB[i] = new ViewPoint2(b.Dot(right), b.Dot(up));
                }

                // ⑤ 逐输出线段取内部采样点，做双侧判定。
                //    采样密度：每段 3 个内部采样点（t=1/4、1/2、3/4）⇒ 检测分辨率 ≈ 段长的 25%，且 Clean 丢弃的短于 minLength 的碎段不被采样。
                foreach (var segment in view.Segments)
                {
                    if (segment.Length < ZeroLengthSegment)
                        continue;

                    for (var k = 1; k <= 3; k++)
                    {
                        var t = k / 4.0;
                        var q = new ViewPoint2(
                            segment.Start.X + (segment.End.X - segment.Start.X) * t,
                            segment.Start.Y + (segment.End.Y - segment.Start.Y) * t);

                        // 投影类集：覆盖 q 的输出线段；识别出的弧已从 Segments 移出，按「落在弧的圆上且角域内」计入。
                        var projVisible = false;
                        var projHidden = false;
                        foreach (var other in view.Segments)
                        {
                            if (DistanceToSegment2(q, other.Start, other.End) > CoincidenceTolerance)
                                continue;
                            if (other.IsVisible)
                                projVisible = true;
                            else
                                projHidden = true;
                        }

                        foreach (var arc in view.Arcs)
                        {
                            if (!IsOnArcDomain(arc, q))
                                continue;
                            if (arc.IsVisible)
                                projVisible = true;
                            else
                                projHidden = true;
                        }

                        // 9 探针：每条重合候选边独立分类 —— 全可见 ⇒ robust V；全隐藏 ⇒ robust H；
                        // 混合 ⇒ 该边不贡献 robust 类（格量化敏感带）。
                        var coincident = new List<int>(2);
                        var possibleVisible = false;
                        var possibleHidden = false;
                        var robustVisible = false;
                        var robustHidden = false;

                        for (var i = 0; i < candidates.Count; i++)
                        {
                            if (DistanceToSegment2(q, projectedA[i], projectedB[i]) > CoincidenceTolerance)
                                continue;

                            coincident.Add(i);

                            var allVisible = true;
                            var allHidden = true;
                            for (var p = 0; p < ProbeDirections.Length; p++)
                            {
                                var hitDistance = ProbeHitDistance(
                                    mesh, triangles, look, right, up, candidates[i], q,
                                    ProbeDirections[p].H * cell, ProbeDirections[p].V * cell);

                                // 深度 = p·Look 随远离观察者递增；命中距离 ≤ bias 视为「遮挡过浅、不计」
                                // （与 DepthRaster.IsVisibleAt 的 depth ≤ nearest + bias 同口径）。
                                var visible = double.IsPositiveInfinity(hitDistance) || hitDistance <= bias;
                                if (visible)
                                {
                                    allHidden = false;
                                    possibleVisible = true;
                                }
                                else
                                {
                                    allVisible = false;
                                    possibleHidden = true;
                                }
                            }

                            if (allVisible)
                                robustVisible = true;
                            else if (allHidden)
                                robustHidden = true;
                        }

                        // 没有重合候选边 / 投影类集为空 ⇒ 该采样点不参与判定（不计入 checked）。
                        if (coincident.Count == 0 || (!projVisible && !projHidden))
                            continue;

                        // robust 类集为空 ⇒ 只有格量化敏感带混合，INDET 跳过。
                        if (!robustVisible && !robustHidden)
                        {
                            indeterminateSamples++;
                            continue;
                        }

                        checkedSamples++;

                        // 双侧判定：robust ⊆ proj（missing）且 proj ⊆ possible（unjustified）。
                        var missingVisible = robustVisible && !projVisible;
                        var missingHidden = robustHidden && !projHidden;
                        var unjustifiedVisible = projVisible && !possibleVisible;
                        var unjustifiedHidden = projHidden && !possibleHidden;
                        if (!missingVisible && !missingHidden && !unjustifiedVisible && !unjustifiedHidden)
                            continue;

                        failedSamples++;
                        if (fails.Count < MaxFailureDetails)
                        {
                            fails.Add(DescribeFailure(
                                view, q, segment.IsVisible,
                                robustVisible, robustHidden, possibleVisible, possibleHidden,
                                projVisible, projHidden,
                                missingVisible, missingHidden, unjustifiedVisible, unjustifiedHidden,
                                mesh, candidates, coincident));
                        }
                    }
                }
            }

            var stats = new OcclusionCheckStats(checkedSamples, indeterminateSamples, failedSamples);

            // 防空洞：没有任何采样点被判定时断言恒真，oracle 形同虚设。
            Assert.That(stats.CheckedSamples, Is.GreaterThan(0),
                $"{prefix}oracle 空洞：没有任何采样点被双侧判定"
                + $"（indeterminate={stats.IndeterminateSamples}"
                + $" indet/checked={stats.IndeterminateSamples}/{stats.CheckedSamples}）");

            var aggregate = new StringBuilder();
            aggregate.Append(prefix).Append("遮挡 oracle 失败：")
                .Append(stats.FailedSamples).Append(" 个采样点不一致")
                .Append("（checked=").Append(stats.CheckedSamples)
                .Append(" indeterminate=").Append(stats.IndeterminateSamples)
                .Append(" failed=").Append(stats.FailedSamples)
                .Append(" indet/checked=").Append(stats.IndeterminateSamples)
                .Append('/').Append(stats.CheckedSamples).Append("）");
            foreach (var fail in fails)
                aggregate.Append(Environment.NewLine).Append("  - ").Append(fail);

            Assert.That(fails.Count, Is.EqualTo(0), aggregate.ToString());

            return stats;
        }

        /// <summary>
        /// 单探针：从「候选边在 2D 参数 t 处的 3D 插值点 + (dh,dv) 屏幕平移」沿 −Look 投射，
        /// 返回最近命中距离（跳过源边相邻面、过滤 t ≤ 1e-7 的自命中；无命中为 +∞）。
        /// <para>
        /// 用 2D 参数 t 反求 3D 基点（而非拿 q 直接当 3D 点）：同一条 2D 输出线可能是前后数条
        /// 候选边的投影叠加，q 甚至可能落在插值段之外；把 q 投影到该候选边自己的 2D 段上，
        /// 才能保证基点严格落在边内且与 q 的屏幕位置一致。
        /// </para>
        /// </summary>
        static double ProbeHitDistance(
            ViewMeshTopology mesh,
            IReadOnlyList<ViewTriangle> triangles,
            ViewVector look,
            ViewVector right,
            ViewVector up,
            ViewMeshTopology.ViewEdge edge,
            ViewPoint2 q,
            double dh,
            double dv)
        {
            var pa = mesh.Points[edge.P0];
            var pb = mesh.Points[edge.P1];
            var a2 = new ViewPoint2(pa.Dot(right), pa.Dot(up));
            var b2 = new ViewPoint2(pb.Dot(right), pb.Dot(up));
            var dx = b2.X - a2.X;
            var dy = b2.Y - a2.Y;
            var len2 = dx * dx + dy * dy;
            var t = len2 > 0 ? ((q.X - a2.X) * dx + (q.Y - a2.Y) * dy) / len2 : 0.0;
            t = Math.Clamp(t, 0, 1);

            var p3 = new ViewPoint(
                pa.X + (pb.X - pa.X) * t + dh * right.X + dv * up.X,
                pa.Y + (pb.Y - pa.Y) * t + dh * right.Y + dv * up.Y,
                pa.Z + (pb.Z - pa.Z) * t + dh * right.Z + dv * up.Z);

            var direction = new ViewVector(-look.X, -look.Y, -look.Z);
            return RayCast.NearestHitDistance(triangles, p3, direction, edge.TriA, edge.TriB);
        }

        /// <summary>q 是否落在弧上：径向 |dist−R| ≤ 0.06 且极角在弧的角域内（整圆恒真；角域容差 1e-3 rad）。</summary>
        static bool IsOnArcDomain(ViewArc arc, ViewPoint2 point)
        {
            var dx = point.X - arc.Center.X;
            var dy = point.Y - arc.Center.Y;
            if (Math.Abs(Math.Sqrt(dx * dx + dy * dy) - arc.Radius) > ArcRadialTolerance)
                return false;

            if (arc.IsClosed)
                return true;

            var sweep = arc.SweepDegrees * Math.PI / 180.0;
            var relative = Math.Atan2(dy, dx) - arc.StartAngle;
            while (relative < 0) relative += 2 * Math.PI;
            while (relative >= 2 * Math.PI) relative -= 2 * Math.PI;
            return relative <= sweep + ArcAngleTolerance;
        }

        /// <summary>点到 2D 线段的距离（端点外按端点计）。</summary>
        static double DistanceToSegment2(ViewPoint2 point, ViewPoint2 a, ViewPoint2 b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len2 = dx * dx + dy * dy;
            if (len2 <= 0)
            {
                var px = point.X - a.X;
                var py = point.Y - a.Y;
                return Math.Sqrt(px * px + py * py);
            }

            var t = ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / len2;
            if (t < 0) t = 0;
            if (t > 1) t = 1;

            var ex = point.X - (a.X + dx * t);
            var ey = point.Y - (a.Y + dy * t);
            return Math.Sqrt(ex * ex + ey * ey);
        }

        /// <summary>类集文本（V = 可见、H = 隐藏、V,H = 两类都有、∅ = 空）。</summary>
        static string ClassList(bool visible, bool hidden)
        {
            if (visible && hidden) return "V,H";
            if (visible) return "V";
            if (hidden) return "H";
            return "∅";
        }

        /// <summary>失败明细：视图名、q（R 格式）、robust/possible/proj 类集、missing/unjustified 归因、重合候选边的 3D 端点。</summary>
        static string DescribeFailure(
            ViewProjection view,
            ViewPoint2 q,
            bool sampleSegmentVisible,
            bool robustVisible,
            bool robustHidden,
            bool possibleVisible,
            bool possibleHidden,
            bool projVisible,
            bool projHidden,
            bool missingVisible,
            bool missingHidden,
            bool unjustifiedVisible,
            bool unjustifiedHidden,
            ViewMeshTopology mesh,
            List<ViewMeshTopology.ViewEdge> candidates,
            List<int> coincident)
        {
            var text = new StringBuilder();
            text.Append("视图 ").Append(view.Direction.Name)
                .Append(" q=(").Append(q.X.ToString("R")).Append(',').Append(q.Y.ToString("R")).Append(')')
                .Append(" 采样段=").Append(sampleSegmentVisible ? "V" : "H")
                .Append(" robust=[").Append(ClassList(robustVisible, robustHidden)).Append(']')
                .Append(" possible=[").Append(ClassList(possibleVisible, possibleHidden)).Append(']')
                .Append(" proj=[").Append(ClassList(projVisible, projHidden)).Append(']')
                .Append(" missing=[").Append(ClassList(missingVisible, missingHidden)).Append(']')
                .Append(" unjustified=[").Append(ClassList(unjustifiedVisible, unjustifiedHidden)).Append(']')
                .Append(" 重合候选边：");

            for (var i = 0; i < coincident.Count; i++)
            {
                if (i > 0)
                    text.Append("；");

                var edge = candidates[coincident[i]];
                text.Append('#').Append(edge.P0).Append('-').Append(edge.P1)
                    .Append(" 3D ").Append(mesh.Points[edge.P0]).Append("→").Append(mesh.Points[edge.P1])
                    .Append(" triA=").Append(edge.TriA).Append(" triB=").Append(edge.TriB);
            }

            return text.ToString();
        }
    }
}
