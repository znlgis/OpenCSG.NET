namespace Csg
{

using System;
using System.Collections.Generic;

/// <summary>
/// 正交视图消隐投影器（本模块的算法核心）：
/// <list type="number">
/// <item><b>特征边提取</b>——每个视图只保留三类边：轮廓边（相邻两面一正对相机、一背对）、
/// 锐边（二面角 &gt; <see cref="ViewMeshTopology.SharpAngleDegrees"/>）、边界边（只被一个三角形引用）。
/// 曲面镶嵌产生的近似共面缝被剔除，图面因此是工程轮廓而不是网格线团。
/// 边界边中属于**分解缝**（CSG 求值把大面按小特征切成多片后，T 接缝的单侧边）的子区间
/// 按内部边剔除（DEFECT-010 修复，见 <see cref="ClassifyBoundarySpans"/>）——否则薄板叠加
/// 小凸台这类构造会在视图里画出贯穿全长的幻线。</item>
/// <item><b>Z-buffer 消隐</b>——把正对相机的三角形光栅化到规则深度栅格，
/// 候选边沿长度采样，逐段与栅格深度比较切分为可见/隐藏。</item>
/// </list>
/// </summary>
/// <remarks>
/// 参考实现：znlgis/GStarCad.Net.Demo（MIT，MESHVIEWEXPORT）的 <c>OrthoProjector</c> + <c>DepthBuffer</c>；
/// 本移植保持其阈值口径（栅格 900、深度容差 = 深度跨度 ×1e-3、每边采样上限 4000），
/// 仅把输入从 STL 三角形改为调用方工程 CSG 实体三角化结果，并按视图独立构建栅格（更省内存、无跨视图状态）。
/// </remarks>
public sealed class ViewProjector
{
    /// <summary>深度栅格在屏幕长边方向的分辨率（越大越锐利，代价 ~O(分辨率²)）。</summary>
    public const int RasterResolution = 900;

    /// <summary>单条候选边的最大采样数（超出像素分辨率后再采样不提升切分质量）。</summary>
    public const int MaxEdgeSamples = 4000;

    /// <summary>深度比较容差系数：深度跨度的该比例作为偏置，避免自遮挡（Z-fighting）。</summary>
    public const double DepthBiasFactor = 1e-3;

    private const double MinDepthGap = 1e-9;

    /// <summary>分解缝判定的共线容差（mm）：与 <c>ViewMeshTopology</c> 的顶点焊接容差（1e-6）同量级。</summary>
    private const double CollinearTolerance = 1e-6;

    private readonly ViewMeshTopology _mesh;

    /// <summary>
    /// 逐边界边的**保留子区间**（t ∈ [0,1] 沿边，扁平存放 [t0,t1,t0,t1,…]）：
    /// <c>null</c> = 整边保留（真实开放边界或未识别为分解缝）；空数组 = 整边剔除；
    /// 其余为按分解缝覆盖切分后的保留段。仅边界边（<c>TriB &lt; 0</c>）非 null，其余下标恒为 null。
    /// </summary>
    private readonly double[]?[] _boundaryKeepSpans;

    public ViewProjector(IReadOnlyList<ViewTriangle> triangles)
    {
        _mesh = new ViewMeshTopology(triangles);
        _boundaryKeepSpans = ClassifyBoundarySpans(_mesh);
    }

    /// <summary>网格规模（供进度/日志用）。</summary>
    public int TriangleCount => _mesh.Triangles.Count;

    /// <summary>按给定方向列表投影（默认国标第一角三视图）。</summary>
    public IReadOnlyList<ViewProjection> Project(IReadOnlyList<ViewDirection>? directions = null)
    {
        var list = directions ?? ViewDirections.OrthographicThree;
        var results = new List<ViewProjection>(list.Count);
        foreach (var direction in list)
            results.Add(ProjectView(direction));
        return results;
    }

    /// <summary>单视图投影。</summary>
    public ViewProjection ProjectView(ViewDirection view)
    {
        if (view == null) throw new ArgumentNullException(nameof(view));

        var triangles = _mesh.Triangles;
        var look = view.Look;

        // ① 正对相机判定：外法向与视线方向相反即为朝向观察者的面。
        var frontFacing = new bool[triangles.Count];
        for (var i = 0; i < triangles.Count; i++)
            frontFacing[i] = _mesh.Normals[i].Dot(look) < 0;

        // ② 候选特征边筛选（轮廓 / 锐 / 边界；边界边按分解缝识别取保留子区间 —— DEFECT-010 修复）。
        var candidates = new List<(ViewPoint A, ViewPoint B)>(_mesh.Edges.Count / 4 + 16);
        for (var i = 0; i < _mesh.Edges.Count; i++)
        {
            var edge = _mesh.Edges[i];
            var a = _mesh.Points[edge.P0];
            var b = _mesh.Points[edge.P1];

            if (edge.TriB < 0)
            {
                var keep = _boundaryKeepSpans[i];
                if (keep == null)
                {
                    candidates.Add((a, b));
                }
                else
                {
                    for (var s = 0; s + 1 < keep.Length; s += 2)
                        candidates.Add((Lerp(a, b, keep[s]), Lerp(a, b, keep[s + 1])));
                }

                continue;
            }

            var frontA = frontFacing[edge.TriA];
            var frontB = frontFacing[edge.TriB];
            if (frontA != frontB)
            {
                candidates.Add((a, b)); // 轮廓边
                continue;
            }

            if (!frontA && !frontB)
                continue; // 两侧都背对相机 ⇒ 该边不可能被看见

            if (edge.IsSharp)
                candidates.Add((a, b));
        }

        // ③ 深度栅格 + 逐边切分。
        var buffer = DepthRaster.Build(_mesh, frontFacing, view);
        var segments = new List<ViewSegment>(candidates.Count * 2);

        foreach (var (a, b) in candidates)
        {
            var a2 = new ViewPoint2(a.Dot(view.Right), a.Dot(view.Up));
            var b2 = new ViewPoint2(b.Dot(view.Right), b.Dot(view.Up));
            // 深度 = p·Look：相机在 −Look 侧 ⇒ 该值随「远离观察者」递增，
            // 与 DepthRaster 的「每格存最小深度 + IsVisibleAt 判 depth ≤ nearest + bias」配套。
            buffer.Split(a2, b2, a.Dot(view.Look), b.Dot(view.Look), segments);
        }

        // ④ 圆弧识别 —— **必须在碎片清理之前**：清理的"同可见性共线合并"会把圆弧的短折线压成
        //    一根根直线段，弧就再也认不出来了（用户实测 202某分图0-09：三维里的圆弧落到二维全成折线）。
        //    识别时不分可见性成链（贴着自身面的轮廓常被判成 V-H 交替），弧的可见性由多数票决定。
        var curves = ViewArcFitter.Fit(segments);

        // ⑤ 清理逐格采样产生的抖动碎片（否则图面是虚线密集的毛边），再交给排版。
        var cleaned = curves.Segments.Count == 0
            ? curves.Segments
            : ViewSegmentCleanup.Clean(curves.Segments, ViewBounds2.Of(EnumeratePoints(curves.Segments)));

        return new ViewProjection(view, cleaned, curves.Arcs);
    }

    private static ViewPoint Lerp(ViewPoint a, ViewPoint b, double t)
        => new ViewPoint(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    /// <summary>
    /// **分解缝识别**（DEFECT-010 修复）：CSG 求值把大面按小特征（凸台/孔/槽的 BSP 平面）切成多片后，
    /// 求交线两侧的三角化不匹配（T 接缝）⇒ 该线在三角汤里成为**单侧（边界）边**，投影器按边界边
    /// 放行就会画出贯穿大面的幻线。识别口径与「匹配网格」对齐：若其一段子区间被**共面三角形**的
    /// 共线边覆盖（两侧面法向夹角 ≤ <see cref="ViewMeshTopology.SharpAngleDegrees"/> ⇒ 是同一片
    /// 曲面的内部分割，匹配网格里该处本就不存在特征边），该子区间按内部边剔除；其余区间
    /// （真实开放边界、锐特征线——其覆盖方与自身不共面）原样保留。
    /// <para>
    /// 只剔除**共面**覆盖是有意的：真实棱线（如板顶角线）的 T 接缝形态与幻线结构相同
    /// （长边 + 多段覆盖），唯一区别就是覆盖方与自身共面与否 —— 探针实测区分见
    /// <c>BoundaryProjectionTest</c>（板顶角线 |Δn|=90° 保留、分解缝 |Δn|=0° 剔除）。
    /// 覆盖检索按方向量化分桶（同一直线上的边必落同桶），单次构建 O(边界边 × 桶规模)。
    /// </para>
    /// </summary>
    static double[]?[] ClassifyBoundarySpans(ViewMeshTopology mesh)
    {
        var edges = mesh.Edges;
        var result = new double[]?[edges.Count];

        var boundaryCount = 0;
        for (var i = 0; i < edges.Count; i++)
            if (edges[i].TriB < 0)
                boundaryCount++;
        if (boundaryCount == 0)
            return result;

        // 方向桶：量化单位方向（符号规范化）⇒ 共线边必同桶；桶内再做精确共线/覆盖判定。
        var buckets = new Dictionary<(long X, long Y, long Z), List<int>>(edges.Count);
        for (var i = 0; i < edges.Count; i++)
        {
            if (DirectionBucket(mesh, edges[i], out var key))
            {
                if (!buckets.TryGetValue(key, out var list))
                {
                    list = new List<int>(4);
                    buckets[key] = list;
                }

                list.Add(i);
            }
        }

        var cosSharp = Math.Cos(ViewMeshTopology.SharpAngleDegrees * Math.PI / 180.0);

        for (var i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            if (edge.TriB >= 0)
                continue;

            var pa = mesh.Points[edge.P0];
            var pb = mesh.Points[edge.P1];
            var d = new ViewVector(pb.X - pa.X, pb.Y - pa.Y, pb.Z - pa.Z);
            var length = Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
            if (length <= CollinearTolerance)
                continue; // 退化边

            var u = new ViewVector(d.X / length, d.Y / length, d.Z / length);
            if (!DirectionBucket(mesh, edge, out var bucketKey) || !buckets.TryGetValue(bucketKey, out var bucket))
                continue;

            List<(double Lo, double Hi)>? suppressed = null;
            foreach (var f in bucket)
            {
                if (f == i)
                    continue;

                var other = edges[f];
                var qa = mesh.Points[other.P0];
                var qb = mesh.Points[other.P1];

                // 共线：两端点到本边直线的垂直距离 ≤ 容差。
                if (PerpendicularDistance(qa, pa, u) > CollinearTolerance
                    || PerpendicularDistance(qb, pa, u) > CollinearTolerance)
                    continue;

                var ta = (double)((qa.X - pa.X) * u.X + (qa.Y - pa.Y) * u.Y + (qa.Z - pa.Z) * u.Z) / length;
                var tb = (double)((qb.X - pa.X) * u.X + (qb.Y - pa.Y) * u.Y + (qb.Z - pa.Z) * u.Z) / length;
                var lo = Math.Max(0.0, Math.Min(ta, tb));
                var hi = Math.Min(1.0, Math.Max(ta, tb));
                if ((hi - lo) * length <= CollinearTolerance)
                    continue; // 仅端点相接，无实质覆盖

                // 共面判定：覆盖边所在三角形与本边三角形法向夹角 ≤ 锐边阈值 ⇒ 同一曲面的内部分割。
                var smooth = mesh.Normals[edge.TriA].Dot(mesh.Normals[other.TriA]) >= cosSharp;
                if (!smooth && other.TriB >= 0)
                    smooth = mesh.Normals[edge.TriA].Dot(mesh.Normals[other.TriB]) >= cosSharp;
                if (!smooth)
                    continue; // 覆盖方与自身不共面 ⇒ 真实棱线（保留），如板顶角线

                (suppressed ??= new List<(double, double)>(2)).Add((lo, hi));
            }

            if (suppressed == null)
                continue; // 无共面覆盖 ⇒ 真实开放边界，整边保留

            result[i] = Complement(suppressed);
        }

        return result;
    }

    /// <summary>互补区间（[0,1] 减去合并后的抑制区间）；无保留段时返回空数组（整边剔除）。</summary>
    static double[] Complement(List<(double Lo, double Hi)> suppressed)
    {
        suppressed.Sort((x, y) => x.Lo.CompareTo(y.Lo));
        var keep = new List<double>(suppressed.Count * 2 + 2);
        var cursor = 0.0;

        foreach (var (lo, hi) in suppressed)
        {
            if (lo > cursor)
                keep.Add(cursor);
            if (lo > cursor)
                keep.Add(lo);
            if (hi > cursor)
                cursor = hi;
        }

        if (cursor < 1.0)
        {
            keep.Add(cursor);
            keep.Add(1.0);
        }

        return keep.ToArray();
    }

    /// <summary>端点方向量化分桶（符号规范化：首个非零分量取正）。</summary>
    static bool DirectionBucket(ViewMeshTopology mesh, ViewMeshTopology.ViewEdge edge, out (long X, long Y, long Z) key)
    {
        key = default;
        var a = mesh.Points[edge.P0];
        var b = mesh.Points[edge.P1];
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var dz = b.Z - a.Z;
        var length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (length <= CollinearTolerance)
            return false;

        dx /= length;
        dy /= length;
        dz /= length;

        if (dx > CollinearTolerance || (Math.Abs(dx) <= CollinearTolerance && dy > CollinearTolerance)
            || (Math.Abs(dx) <= CollinearTolerance && Math.Abs(dy) <= CollinearTolerance && dz < 0))
        {
            dx = -dx;
            dy = -dy;
            dz = -dz;
        }

        const double inv = 1e6;
        key = ((long)Math.Round(dx * inv), (long)Math.Round(dy * inv), (long)Math.Round(dz * inv));
        return true;
    }

    static double PerpendicularDistance(ViewPoint p, ViewPoint origin, ViewVector u)
    {
        var vx = p.X - origin.X;
        var vy = p.Y - origin.Y;
        var vz = p.Z - origin.Z;
        var t = vx * u.X + vy * u.Y + vz * u.Z;
        var rx = vx - t * u.X;
        var ry = vy - t * u.Y;
        var rz = vz - t * u.Z;
        return Math.Sqrt(rx * rx + ry * ry + rz * rz);
    }

    private static IEnumerable<ViewPoint2> EnumeratePoints(IReadOnlyList<ViewSegment> segments)
    {
        foreach (var segment in segments)
        {
            yield return segment.Start;
            yield return segment.End;
        }
    }

    /// <summary>
    /// 正交投影深度栅格（消隐用）：正对相机的三角形按屏幕空间重心坐标光栅化，
    /// 每格保留最近深度。正交投影下深度在屏幕空间是仿射的 ⇒ 线性插值精确。
    /// </summary>
    private sealed class DepthRaster
    {
        private readonly double _bias;
        private readonly double _cell;
        private readonly double[] _depth;

        /// <summary>
        /// 逐格**格内深度误差上界**（DEFECT-005 修复）：最小深度写入者（该格 nearest 的来源三角形）
        /// 在一块格子内的深度变化量 = <c>cell × (|∂d/∂x| + |∂d/∂y|)</c>（正交投影下 d 在屏幕空间仿射，
        /// 梯度由重心坐标系数闭式给出）。旧口径只比 <c>depth ≤ 格心深度 + bias</c>：斜掠面在同一格内
        /// 格心采样深度可比边点插值深度近 <c>cell·tanφ</c>，当该量超过 bias（= 深度跨度×1e-3）时
        /// 几何可见的边被误判 H（实测 59/64 语料种子、长至 91.7 格的虚线）。把误差上界随 nearest 一起
        /// 记账后，比较对象变成「nearest 写入面在该格内的深度区间」上界，格内量化误差不再进判定。
        /// （备选「写入面平面外推」模型实测更差：写入三角形不覆盖采样点所在格区时外推无意义，
        /// 30/64 种子出现 111 样本/视图的伪 H —— 见 fix 轮报告。）
        /// </summary>
        private readonly double[] _slack;
        private readonly bool _empty;
        private readonly int _height;
        private readonly double _minH;
        private readonly double _minV;
        private readonly int _width;

        private DepthRaster(bool empty)
        {
            _empty = empty;
            _depth = Array.Empty<double>();
            _slack = Array.Empty<double>();
        }

        private DepthRaster(
            double[] depth, double[] slack, int width, int height, double minH, double minV, double cell, double bias)
        {
            _depth = depth;
            _slack = slack;
            _width = width;
            _height = height;
            _minH = minH;
            _minV = minV;
            _cell = cell;
            _bias = bias;
        }

        public static DepthRaster Build(
            ViewMeshTopology mesh, bool[] frontFacing, ViewDirection view)
        {
            double minH = double.MaxValue, minV = double.MaxValue;
            double maxH = double.MinValue, maxV = double.MinValue;
            double minD = double.MaxValue, maxD = double.MinValue;
            var any = false;

            for (var i = 0; i < mesh.Triangles.Count; i++)
            {
                if (!frontFacing[i])
                    continue;

                var triangle = mesh.Triangles[i];
                for (var v = 0; v < 3; v++)
                {
                    var p = triangle[v];
                    var h = p.Dot(view.Right);
                    var vv = p.Dot(view.Up);
                    var d = p.Dot(view.Look);
                    if (h < minH) minH = h;
                    if (h > maxH) maxH = h;
                    if (vv < minV) minV = vv;
                    if (vv > maxV) maxV = vv;
                    if (d < minD) minD = d;
                    if (d > maxD) maxD = d;
                    any = true;
                }
            }

            if (!any)
                return new DepthRaster(true);

            var sizeH = Math.Max(maxH - minH, 1e-9);
            var sizeV = Math.Max(maxV - minV, 1e-9);
            var cell = Math.Max(sizeH, sizeV) / RasterResolution;
            if (cell <= 0)
                cell = 1e-6;

            var width = (int)Math.Ceiling(sizeH / cell) + 2;
            var height = (int)Math.Ceiling(sizeV / cell) + 2;
            var depth = new double[width * height];
            for (var i = 0; i < depth.Length; i++)
                depth[i] = double.PositiveInfinity;

            var range = Math.Max(maxD - minD, 1e-9);
            var raster = new DepthRaster(
                depth, new double[width * height], width, height, minH, minV, cell, range * DepthBiasFactor + MinDepthGap);

            for (var i = 0; i < mesh.Triangles.Count; i++)
            {
                if (!frontFacing[i])
                    continue;

                var triangle = mesh.Triangles[i];
                raster.Rasterize(
                    ToScreen(triangle.A, view),
                    ToScreen(triangle.B, view),
                    ToScreen(triangle.C, view));
            }

            return raster;
        }

        /// <summary>沿线段采样，按深度比较把可见/隐藏段追加到 <paramref name="output"/>。</summary>
        public void Split(
            ViewPoint2 a, ViewPoint2 b, double depthA, double depthB, List<ViewSegment> output)
        {
            if (_empty)
            {
                output.Add(new ViewSegment(a, b, true));
                return;
            }

            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            var pixelLength = Math.Sqrt(dx * dx + dy * dy) / _cell;
            var samples = (int)Math.Ceiling(pixelLength) + 1;
            if (samples < 2)
                samples = 2;
            if (samples > MaxEdgeSamples)
                samples = MaxEdgeSamples;

            var startT = 0.0;
            var previous = IsVisibleAt(a, b, depthA, depthB, 0.0);

            for (var s = 1; s <= samples; s++)
            {
                var t = (double)s / samples;
                var current = IsVisibleAt(a, b, depthA, depthB, t);
                if (current != previous)
                {
                    Emit(a, b, startT, t, previous, output);
                    startT = t;
                    previous = current;
                }
            }

            Emit(a, b, startT, 1.0, previous, output);
        }

        private bool IsVisibleAt(ViewPoint2 a, ViewPoint2 b, double depthA, double depthB, double t)
        {
            var px = (int)((a.X + (b.X - a.X) * t - _minH) / _cell);
            var py = (int)((a.Y + (b.Y - a.Y) * t - _minV) / _cell);
            if (px < 0 || py < 0 || px >= _width || py >= _height)
                return true; // 栅格外的采样点不构成遮挡

            var nearest = _depth[py * _width + px];
            if (double.IsPositiveInfinity(nearest))
                return true;

            var depth = depthA + (depthB - depthA) * t;
            return depth <= nearest + _bias + _slack[py * _width + px];
        }

        private static void Emit(
            ViewPoint2 a, ViewPoint2 b, double t0, double t1, bool visible, List<ViewSegment> output)
        {
            if (t1 - t0 < 1e-9)
                return;

            output.Add(new ViewSegment(
                new ViewPoint2(a.X + (b.X - a.X) * t0, a.Y + (b.Y - a.Y) * t0),
                new ViewPoint2(a.X + (b.X - a.X) * t1, a.Y + (b.Y - a.Y) * t1),
                visible));
        }

        private static (double H, double V, double D) ToScreen(ViewPoint p, ViewDirection view)
            => (p.Dot(view.Right), p.Dot(view.Up), p.Dot(view.Look));

        private void Rasterize(
            (double H, double V, double D) a,
            (double H, double V, double D) b,
            (double H, double V, double D) c)
        {
            var ax = (a.H - _minH) / _cell;
            var ay = (a.V - _minV) / _cell;
            var bx = (b.H - _minH) / _cell;
            var by = (b.V - _minV) / _cell;
            var cx = (c.H - _minH) / _cell;
            var cy = (c.V - _minV) / _cell;

            var x0 = (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx)));
            var x1 = (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx)));
            var y0 = (int)Math.Floor(Math.Min(ay, Math.Min(by, cy)));
            var y1 = (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy)));

            if (x0 < 0) x0 = 0;
            if (y0 < 0) y0 = 0;
            if (x1 > _width - 1) x1 = _width - 1;
            if (y1 > _height - 1) y1 = _height - 1;

            var denom = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
            if (Math.Abs(denom) < 1e-12)
                return; // 屏幕空间退化三角形

            var invDenom = 1.0 / denom;

            // 屏幕空间深度梯度（格坐标为单位的偏导；∂d/∂sx = invDenom·[(by−cy)(a.D−c.D) + (cy−ay)(b.D−c.D)]）,
            // 格内深度变化上界 = 一格步长 × 分量绝对值之和（DEFECT-005 修复）。
            var slopePerCell = Math.Abs((by - cy) * (a.D - c.D) + (cy - ay) * (b.D - c.D)) * invDenom
                             + Math.Abs((cx - bx) * (a.D - c.D) + (ax - cx) * (b.D - c.D)) * invDenom;

            for (var py = y0; py <= y1; py++)
            {
                var sy = py + 0.5;
                for (var px = x0; px <= x1; px++)
                {
                    var sx = px + 0.5;
                    var l0 = ((by - cy) * (sx - cx) + (cx - bx) * (sy - cy)) * invDenom;
                    var l1 = ((cy - ay) * (sx - cx) + (ax - cx) * (sy - cy)) * invDenom;
                    var l2 = 1.0 - l0 - l1;
                    if (l0 < -1e-6 || l1 < -1e-6 || l2 < -1e-6)
                        continue;

                    var d = l0 * a.D + l1 * b.D + l2 * c.D;
                    var index = py * _width + px;
                    if (d < _depth[index])
                    {
                        _depth[index] = d;
                        _slack[index] = slopePerCell;
                    }
                }
            }
        }
    }
}
}


