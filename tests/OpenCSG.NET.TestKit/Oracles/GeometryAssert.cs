using System;
using System.Collections.Generic;
using System.Globalization;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// 几何不变量断言工具。
    /// 背景：既有 golden STL 比较只校验「三角形数 / 总面积 / 包围盒」，
    /// 对绕序反转（法线朝内）、镜像、退化面完全无感（镜像体与原体面积、包围盒相同）。
    /// 这里补上三类强不变量：
    /// 1) 定向封闭流形：每条无向边恰好出现两次、每条有向边恰好一次、无零长边；
    /// 2) 散度定理净体积：法线一致朝外时为正，镜像后必须仍为正；
    /// 3) 平面自洽：每个多边形的 Plane 法线与其顶点绕序一致、且平面过顶点。
    /// 另外把「只被一个面使用的边」细分为 T 顶点（有顶点落在该边内部）与真实裂缝（洞），
    /// 前者是 BSP 布尔的已知产物（库内无 T 顶点清理），后者一定是缺陷。
    /// </summary>
    public static class GeometryAssert
    {
        /// <summary>顶点在同一位置合并的容差：远大于接缝处 1e-16 浮点噪声、远小于 CSG 的 1e-5 容差。</summary>
        /// <remarks>聚簇实现共享于 <see cref="VertexCluster"/>：两侧口径必须一致，结论才可交叉比对。</remarks>
        public const double MergeTolerance = VertexCluster.Tolerance;

        public sealed class ManifoldReport
        {
            public int PolygonCount;          // 多边形总数（0 = 空实体：全零统计看起来「干净」，实为无输入）
            public int EdgeCount;
            public int NonManifoldEdges;      // 被 != 2 个面使用的无向边
            public int DuplicatedEdges;       // 同向重复出现的有向边（绕序不一致）
            public int ZeroLengthEdges;
            public int DegeneratePolygons;
            public int SingleUseEdges;        // 只被一个面使用的边（单面边）
            public int TJunctions;            // 单面边中：被更长共线边分割，或自身覆盖其它边的（T 顶点结构）
            public int SeamEdges;             // 其余单面边：两侧网格分割不一致形成的接缝

            public bool IsClosedManifold => NonManifoldEdges == 0;

            public override string ToString()
                => $"edges={EdgeCount} singleUse={SingleUseEdges} nonManifold={NonManifoldEdges} duplicated={DuplicatedEdges} "
                 + $"zeroLength={ZeroLengthEdges} degeneratePolys={DegeneratePolygons} "
                 + $"T-junctions={TJunctions} seamEdges={SeamEdges}";
        }

        /// <summary>
        /// 散度定理净体积 V = Σ v0·(v1×v2)/6（多边形按首顶点扇形剖分）。
        /// **参考点取包围盒中心**，而不是全局原点：对闭合曲面该值与参考点无关，
        /// 但用原点做参考时，离原点很远的实体（测绘坐标 ±1e6）每项量级 ~1e18、
        /// 真实体积仅 ~1e1，双精度在 1e18 处的 ULP≈128 会直接把体积吞掉
        /// （实测：r=1 球平移 1e6 后读到 780.4，平移 (1e6,−2e6,3e6) 后读到 −6089.98）。
        /// </summary>
        public static double SignedVolume(Solid solid)
        {
            if (solid.Polygons.Count == 0)
                return 0.0;

            var (boundMin, boundMax) = Bounds(solid);
            var reference = (boundMin + boundMax) * 0.5;

            double volume = 0;
            foreach (var polygon in solid.Polygons)
            {
                var v = polygon.Vertices;
                if (v.Count < 3)
                    continue;
                var v0 = v[0].Pos - reference;
                for (int i = 1; i < v.Count - 1; i++)
                    volume += Dot(v0, Cross(v[i].Pos - reference, v[i + 1].Pos - reference));
            }
            return volume / 6.0;
        }

        /// <summary>统计封闭性/定向性/T 顶点。</summary>
        public static ManifoldReport Analyze(Solid solid)
        {
            var report = new ManifoldReport();
            report.PolygonCount = solid.Polygons.Count;
            var positions = new List<Vector3D>();
            foreach (var polygon in solid.Polygons)
            {
                if (polygon.Vertices.Count < 3)
                {
                    report.DegeneratePolygons++;
                    continue;
                }
                foreach (var vertex in polygon.Vertices)
                    positions.Add(vertex.Pos);
            }

            var clusterOf = VertexCluster.Cluster(positions, p => (p.X, p.Y, p.Z));
            var clusterCenters = new List<Vector3D>();
            var centerByCluster = new Dictionary<int, Vector3D>();
            for (int i = 0; i < positions.Count; i++)
            {
                if (!centerByCluster.ContainsKey(clusterOf[i]))
                    centerByCluster[clusterOf[i]] = positions[i];
            }
            foreach (var kv in centerByCluster)
                clusterCenters.Add(kv.Value);

            var directed = new Dictionary<long, int>();
            var undirected = new Dictionary<long, int>();
            var undirectedEnds = new Dictionary<long, (Vector3D A, Vector3D B)>();

            int offset = 0;
            foreach (var polygon in solid.Polygons)
            {
                var v = polygon.Vertices;
                if (v.Count < 3)
                    continue;
                for (int i = 0; i < v.Count; i++)
                {
                    int next = i + 1 == v.Count ? 0 : i + 1;
                    int a = clusterOf[offset + i];
                    int b = clusterOf[offset + next];
                    if (a == b)
                    {
                        report.ZeroLengthEdges++;
                        continue;
                    }
                    long key = a < b ? VertexCluster.Pack(a, b) : VertexCluster.Pack(b, a);
                    undirected.TryGetValue(key, out var uc);
                    undirected[key] = uc + 1;
                    if (!undirectedEnds.ContainsKey(key))
                        undirectedEnds[key] = (v[i].Pos, v[next].Pos);
                    long dkey = VertexCluster.Pack(a, b);
                    directed.TryGetValue(dkey, out var dc);
                    directed[dkey] = dc + 1;
                }
                offset += v.Count;
            }

            var singleUseEdges = new List<(Vector3D A, Vector3D B)>();
            var allEdges = new List<(Vector3D A, Vector3D B)>();
            foreach (var kv in undirected)
            {
                report.EdgeCount++;
                allEdges.Add(undirectedEnds[kv.Key]);
                if (kv.Value == 2)
                    continue;
                report.NonManifoldEdges++;
                if (kv.Value == 1)
                    singleUseEdges.Add(undirectedEnds[kv.Key]);
            }

            // 分类单面边：T 顶点结构（长边内部有顶点/被短边分割，或短边落在更长边内部，
            // 覆盖边可以是被两个面共享的正常边）vs 接缝（两侧网格分割不同但几何贴合）。
            report.SingleUseEdges = singleUseEdges.Count;
            foreach (var edge in singleUseEdges)
            {
                var containsOther = HasVertexStrictlyInside(clusterCenters, edge.A, edge.B)
                    || allEdges.Exists(other => IsStrictlyInside(edge.A, edge.B, other.A, other.B));
                var coveredByOther = allEdges.Exists(other => IsStrictlyInside(other.A, other.B, edge.A, edge.B));
                if (containsOther || coveredByOther)
                    report.TJunctions++;
                else
                    report.SeamEdges++;
            }

            foreach (var kv in directed)
                if (kv.Value != 1)
                    report.DuplicatedEdges++;

            return report;
        }

        /// <summary>
        /// 严格封闭定向流形断言（图元与多数布尔结果应满足）。
        /// 空实体（PolygonCount == 0）直接拒绝 —— 其全零报告会让四条断言全部真空成立，
        /// 口径与 <see cref="MeshAssert.AssertClosedOrientedManifold(IReadOnlyList{MeshTriangle}, string?)"/> 的空网格守卫对称。
        /// </summary>
        public static void AssertClosedOrientedManifold(Solid solid, string? message = null)
        {
            var report = Analyze(solid);
            Assert.That(report.PolygonCount, Is.GreaterThan(0),
                $"{Describe(message)}: 多边形数为 0 —— 空实体不是合法实体（若无本断言，空输入会真空通过）| {report}");
            Assert.That(report.NonManifoldEdges, Is.EqualTo(0),
                $"{Describe(message)}: 有 {report.NonManifoldEdges} 条边不是恰好被两个面共享（T 顶点/裂缝）| {report}");
            Assert.That(report.DuplicatedEdges, Is.EqualTo(0),
                $"{Describe(message)}: 有 {report.DuplicatedEdges} 条有向边重复（绕序不一致）| {report}");
            Assert.That(report.ZeroLengthEdges, Is.EqualTo(0),
                $"{Describe(message)}: 有 {report.ZeroLengthEdges} 条零长边（退化多边形）| {report}");
            Assert.That(report.DegeneratePolygons, Is.EqualTo(0),
                $"{Describe(message)}: 有 {report.DegeneratePolygons} 个顶点数 < 3 的退化多边形 | {report}");
        }

        /// <summary>
        /// 允许 T 顶点接缝（BSP 布尔的已知产物，见 Analyze），但要求：
        /// 无同向重复边（绕序一致）、无零长边、无退化多边形。
        /// 注意本断言不检测「面积缺失型漏洞」——那由体积恒等式（A∪B = A+B−A∩B）在用例中把关。
        /// </summary>
        public static void AssertOrientationConsistent(Solid solid, string? message = null)
        {
            var report = Analyze(solid);
            Assert.That(report.DuplicatedEdges, Is.EqualTo(0),
                $"{Describe(message)}: 有 {report.DuplicatedEdges} 条有向边重复（绕序不一致）| {report}");
            Assert.That(report.ZeroLengthEdges, Is.EqualTo(0),
                $"{Describe(message)}: 有 {report.ZeroLengthEdges} 条零长边（退化多边形）| {report}");
            Assert.That(report.DegeneratePolygons, Is.EqualTo(0),
                $"{Describe(message)}: 有 {report.DegeneratePolygons} 个顶点数 < 3 的退化多边形 | {report}");
        }

        /// <summary>每个多边形的 Plane 必须与顶点绕序同向、且平面过其顶点。</summary>
        public static void AssertPlanesMatchWinding(Solid solid, string? message = null)
        {
            var scale = Math.Max(1.0, MaxAbsCoordinate(solid));
            foreach (var polygon in solid.Polygons)
            {
                var v = polygon.Vertices;
                if (v.Count < 3)
                    continue;
                var a = v[0].Pos;
                var normal = new Vector3D(0, 0, 0);
                for (int i = 1; i + 1 < v.Count; i++)
                {
                    normal = Cross(v[i].Pos - a, v[i + 1].Pos - a);
                    if (normal.Length > 1e-9 * scale)
                        break;
                }
                if (!(normal.Length > 1e-9 * scale))
                    continue; // 整片退化（面积为零），由流形检查另行覆盖

                var dot = normal.Unit.Dot(polygon.Plane.Normal);
                Assert.That(dot, Is.GreaterThan(0.99),
                    $"{Describe(message)}: 多边形法线 {polygon.Plane.Normal} 与顶点绕序反向（dot={dot:R}）");

                var offset = polygon.Plane.Normal.Dot(a) - polygon.Plane.W;
                Assert.That(Math.Abs(offset), Is.LessThan(1e-6 * scale),
                    $"{Describe(message)}: 顶点 {a} 不在其多边形平面上（距离 {offset:R}）");
            }
        }

        public static void AssertVolume(Solid solid, double expected, double tolerance, string? message = null)
        {
            var actual = SignedVolume(solid);
            Assert.That(actual, Is.EqualTo(expected).Within(tolerance),
                $"{Describe(message)}: 净体积 {actual:R} != 期望 {expected:R}（法线朝外时为正；负值说明整体绕序反转）");
        }

        public static void AssertBounds(Solid solid, Vector3D expectedMin, Vector3D expectedMax, double tolerance, string? message = null)
        {
            var (min, max) = Bounds(solid);
            Assert.That(min.X, Is.EqualTo(expectedMin.X).Within(tolerance), $"{Describe(message)}: bbox.Min.X");
            Assert.That(min.Y, Is.EqualTo(expectedMin.Y).Within(tolerance), $"{Describe(message)}: bbox.Min.Y");
            Assert.That(min.Z, Is.EqualTo(expectedMin.Z).Within(tolerance), $"{Describe(message)}: bbox.Min.Z");
            Assert.That(max.X, Is.EqualTo(expectedMax.X).Within(tolerance), $"{Describe(message)}: bbox.Max.X");
            Assert.That(max.Y, Is.EqualTo(expectedMax.Y).Within(tolerance), $"{Describe(message)}: bbox.Max.Y");
            Assert.That(max.Z, Is.EqualTo(expectedMax.Z).Within(tolerance), $"{Describe(message)}: bbox.Max.Z");
        }

        public static (Vector3D Min, Vector3D Max) Bounds(Solid solid)
        {
            if (solid.Polygons.Count == 0)
                return (new Vector3D(0, 0, 0), new Vector3D(0, 0, 0));

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (var polygon in solid.Polygons)
            {
                foreach (var vertex in polygon.Vertices)
                {
                    var p = vertex.Pos;
                    minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); minZ = Math.Min(minZ, p.Z);
                    maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); maxZ = Math.Max(maxZ, p.Z);
                }
            }
            return (new Vector3D(minX, minY, minZ), new Vector3D(maxX, maxY, maxZ));
        }

        /// <summary>整个实体的不变量组合断言（封闭 + 定向 + 平面自洽）。</summary>
        public static void AssertSolidInvariants(Solid solid, string? message = null)
        {
            AssertClosedOrientedManifold(solid, message);
            AssertPlanesMatchWinding(solid, message);
        }

        /// <summary>线段 [c,d] 是否共线地严格落在 [a,b] 内部（容差 MergeTolerance）。</summary>
        static bool IsStrictlyInside(Vector3D a, Vector3D b, Vector3D c, Vector3D d)
        {
            var ab = b - a;
            var lengthSquared = Dot(ab, ab);
            if (lengthSquared <= 0)
                return false;
            if (DistanceToLine(c, a, ab, lengthSquared) > MergeTolerance
                || DistanceToLine(d, a, ab, lengthSquared) > MergeTolerance)
                return false;
            var tc = Dot(c - a, ab) / lengthSquared;
            var td = Dot(d - a, ab) / lengthSquared;
            if (tc > td)
            {
                var swap = tc;
                tc = td;
                td = swap;
            }
            return tc > 1e-9 && td < 1 - 1e-9;
        }

        static double DistanceToLine(Vector3D p, Vector3D origin, Vector3D direction, double directionLengthSquared)
        {
            var t = Dot(p - origin, direction) / directionLengthSquared;
            return (p - (origin + direction * t)).Length;
        }

        static bool HasVertexStrictlyInside(List<Vector3D> points, Vector3D a, Vector3D b)
        {
            var ab = b - a;
            var lengthSquared = Dot(ab, ab);
            if (lengthSquared <= 0)
                return false;
            foreach (var p in points)
            {
                var ap = p - a;
                var t = Dot(ap, ab) / lengthSquared;
                if (t <= 1e-6 || t >= 1 - 1e-6)
                    continue;
                var projected = a + ab * t;
                var d = p - projected;
                if (d.Length <= MergeTolerance)
                    return true;
            }
            return false;
        }

        static double MaxAbsCoordinate(Solid solid)
        {
            double max = 0;
            foreach (var polygon in solid.Polygons)
                foreach (var vertex in polygon.Vertices)
                    max = Math.Max(max, Math.Max(Math.Abs(vertex.Pos.X), Math.Max(Math.Abs(vertex.Pos.Y), Math.Abs(vertex.Pos.Z))));
            return max;
        }

        static string Describe(string? message) => message ?? "solid";

        static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        static Vector3D Cross(Vector3D a, Vector3D b)
            => new Vector3D(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    }
}
