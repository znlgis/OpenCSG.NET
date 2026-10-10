namespace Csg
{

using System;
using System.Collections.Generic;

/// <summary>
/// 三角汤的焊接拓扑：把共享角点合并到统一顶点表，并记录每条无向边两侧的三角形与二面角锐边标志。
/// <para>
/// <b>为什么需要它</b>：三角网格里每个三角形独立存三个顶点，若按原始坐标逐三角形取边，
/// 一个立方体面会被镶嵌对角线切开、圆柱面会留下大量镶嵌缝；只有先焊接、再按二面角筛「特征边」，
/// 三视图才会是干净的工程图轮廓而不是网格线团。
/// </para>
/// <para>
/// 参考实现：znlgis/GStarCad.Net.Demo（MIT，MESHVIEWEXPORT）的 <c>MeshTopology</c>；
/// 本移植把顶点表改为「量化格 + 邻域精确比较」，避免格哈希碰撞把不同点误判为同一点。
/// </para>
/// </summary>
public sealed class ViewMeshTopology
{
    /// <summary>锐边判据：相邻面二面角超过该值即视为真实特征边（否则视为曲面镶嵌的近似共面缝）。</summary>
    public const double SharpAngleDegrees = 25.0;

    private const double WeldTolerance = 1e-6;

    private readonly int[][] _trianglePoints;

    public ViewMeshTopology(IReadOnlyList<ViewTriangle> triangles)
    {
        Triangles = triangles ?? throw new ArgumentNullException(nameof(triangles));

        var points = new List<ViewPoint>(triangles.Count * 3);
        var normals = new List<ViewVector>(triangles.Count);
        _trianglePoints = new int[triangles.Count][];

        // 量化格 → 候选顶点下标（格内再做精确比较，故不会因哈希碰撞而误合并）。
        var grid = new Dictionary<(long X, long Y, long Z), List<int>>(triangles.Count * 3);
        var inv = 1.0 / WeldTolerance;

        for (var i = 0; i < triangles.Count; i++)
        {
            var triangle = triangles[i];
            var ids = new int[3];
            for (var v = 0; v < 3; v++)
                ids[v] = Weld(triangle[v], inv, grid, points);

            _trianglePoints[i] = ids;

            // 法向一律由**绕序**重算（右手法则）。退化三角形（面积≈0）返回零向量 ⇒ 其相邻边不被判为锐边，
            // 避免"法向朝向不可靠"造出假特征线（参考实现此处会回退到文件里的面法向，方向可信度更低）。
            normals.Add(triangle.Normal.Normalized());
        }

        Points = points;
        Normals = normals;
        Edges = BuildEdges();
    }

    /// <summary>原始三角形（顺序与 <see cref="Normals"/> 一致）。</summary>
    public IReadOnlyList<ViewTriangle> Triangles { get; }

    /// <summary>焊接后的顶点表。</summary>
    public IReadOnlyList<ViewPoint> Points { get; }

    /// <summary>逐三角形法向（已归一化）。</summary>
    public IReadOnlyList<ViewVector> Normals { get; }

    /// <summary>无向边表（含相邻三角形与锐边标志）。</summary>
    public IReadOnlyList<ViewEdge> Edges { get; }

    /// <summary>一条无向边：两端点下标（<see cref="P0"/> &lt; <see cref="P1"/>）+ 相邻三角形 + 锐边标志。</summary>
    public readonly struct ViewEdge
    {
        public ViewEdge(int p0, int p1, int triA, int triB, bool isSharp)
        {
            P0 = p0;
            P1 = p1;
            TriA = triA;
            TriB = triB;
            IsSharp = isSharp;
        }

        public int P0 { get; }
        public int P1 { get; }

        /// <summary>首个相邻三角形下标。</summary>
        public int TriA { get; }

        /// <summary>第二个相邻三角形下标；<c>-1</c> 表示边界边（只被一个三角形引用）。</summary>
        public int TriB { get; }

        /// <summary>两面夹角超过 <see cref="SharpAngleDegrees"/>（真实折边，而非曲面镶嵌缝）。</summary>
        public bool IsSharp { get; }
    }

    private static int Weld(
        ViewPoint point, double inv, Dictionary<(long, long, long), List<int>> grid, List<ViewPoint> points)
    {
        var key = (
            (long)Math.Round(point.X * inv),
            (long)Math.Round(point.Y * inv),
            (long)Math.Round(point.Z * inv));

        if (grid.TryGetValue(key, out var candidates))
        {
            foreach (var index in candidates)
            {
                var existing = points[index];
                if (Math.Abs(existing.X - point.X) <= WeldTolerance
                    && Math.Abs(existing.Y - point.Y) <= WeldTolerance
                    && Math.Abs(existing.Z - point.Z) <= WeldTolerance)
                {
                    return index;
                }
            }
        }
        else
        {
            candidates = new List<int>(2);
            grid[key] = candidates;
        }

        var id = points.Count;
        points.Add(point);
        candidates.Add(id);
        return id;
    }

    private List<ViewEdge> BuildEdges()
    {
        var edges = new List<ViewEdge>(Triangles.Count * 3 / 2);
        var edgeIndex = new Dictionary<long, int>(Triangles.Count * 3);
        var cosSharp = Math.Cos(SharpAngleDegrees * Math.PI / 180.0);

        for (var t = 0; t < _trianglePoints.Length; t++)
        {
            var ids = _trianglePoints[t];
            for (var e = 0; e < 3; e++)
            {
                var a = ids[e];
                var b = ids[(e + 1) % 3];
                if (a == b)
                    continue; // 退化边（零长度）

                var p0 = Math.Min(a, b);
                var p1 = Math.Max(a, b);
                var key = ((long)p0 << 32) | (uint)p1;

                if (edgeIndex.TryGetValue(key, out var existing))
                {
                    var edge = edges[existing];
                    if (edge.TriB < 0)
                    {
                        // 第二条相邻面到位：据此判定二面角是否超过锐边阈值。
                        var dot = Normals[edge.TriA].Dot(Normals[t]);
                        edges[existing] = new ViewEdge(p0, p1, edge.TriA, t, dot < cosSharp);
                    }
                }
                else
                {
                    edgeIndex[key] = edges.Count;
                    edges.Add(new ViewEdge(p0, p1, t, -1, false));
                }
            }
        }

        return edges;
    }
}
}

