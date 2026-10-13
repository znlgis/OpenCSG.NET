using System;
using System.Collections.Generic;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// 三角网格（MeshTriangle 序列）上的不变量断言。
    /// GeometryAssert 只能吃 Solid（读 Polygon.Vertices）；STL 写出/读入、CsgMesh、
    /// ViewMeshTopology 这条链上的对象是扁平三角面，需要独立的分析口径。
    /// 顶点聚簇容差与 ViewMeshTopology 的 WeldTolerance (1e-6) 取齐，使结论可交叉验证。
    /// </summary>
    public static class MeshAssert
    {
        public const double MergeTolerance = 1e-6;

        /// <summary>欧拉特征与边统计。</summary>
        public readonly struct EdgeReport
        {
            public EdgeReport(int vertexCount, int edgeCount, int triangleCount,
                              int boundaryEdges, int nonManifoldEdges, int duplicatedEdges,
                              int eulerCharacteristic)
            {
                VertexCount = vertexCount;
                EdgeCount = edgeCount;
                TriangleCount = triangleCount;
                BoundaryEdges = boundaryEdges;
                NonManifoldEdges = nonManifoldEdges;
                DuplicatedEdges = duplicatedEdges;
                EulerCharacteristic = eulerCharacteristic;
            }

            public int VertexCount { get; }
            public int EdgeCount { get; }
            public int TriangleCount { get; }
            public int BoundaryEdges { get; }      // 只被一个三角面使用的无向边
            public int NonManifoldEdges { get; }   // 被 != 2 个三角面使用的无向边
            public int DuplicatedEdges { get; }    // 同向重复出现的有向边（绕序不一致）
            public int EulerCharacteristic { get; } // V − E + F

            public override string ToString()
                => $"V={VertexCount} E={EdgeCount} F={TriangleCount} "
                 + $"boundary={BoundaryEdges} nonManifold={NonManifoldEdges} "
                 + $"duplicated={DuplicatedEdges} chi={EulerCharacteristic}";
        }

        /// <summary>散度定理有向体积（右手绕序 ⇒ 法线朝外 ⇒ 正值）。</summary>
        public static double SignedVolume(IReadOnlyList<MeshTriangle> triangles)
        {
            double volume = 0;
            for (int i = 0; i < triangles.Count; i++)
            {
                var a = triangles[i].A;
                var b = triangles[i].B;
                var c = triangles[i].C;
                volume += a.X * (b.Y * c.Z - b.Z * c.Y)
                        - a.Y * (b.X * c.Z - b.Z * c.X)
                        + a.Z * (b.X * c.Y - b.Y * c.X);
            }
            return volume / 6.0;
        }

        /// <summary>三角面积之和（镜像不改变面积，故可与 Solid 侧交叉校验）。</summary>
        public static double TotalArea(IReadOnlyList<MeshTriangle> triangles)
        {
            double area = 0;
            for (int i = 0; i < triangles.Count; i++)
            {
                var a = triangles[i].A;
                var b = triangles[i].B;
                var c = triangles[i].C;
                var ux = b.X - a.X; var uy = b.Y - a.Y; var uz = b.Z - a.Z;
                var vx = c.X - a.X; var vy = c.Y - a.Y; var vz = c.Z - a.Z;
                var nx = uy * vz - uz * vy;
                var ny = uz * vx - ux * vz;
                var nz = ux * vy - uy * vx;
                area += Math.Sqrt(nx * nx + ny * ny + nz * nz) / 2.0;
            }
            return area;
        }

        public static (MeshPoint Min, MeshPoint Max) Bounds(IReadOnlyList<MeshTriangle> triangles)
        {
            if (triangles.Count == 0)
                return (new MeshPoint(0, 0, 0), new MeshPoint(0, 0, 0));

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            for (int i = 0; i < triangles.Count; i++)
            {
                for (int k = 0; k < 3; k++)
                {
                    var p = k == 0 ? triangles[i].A : k == 1 ? triangles[i].B : triangles[i].C;
                    if (p.X < minX) minX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Z < minZ) minZ = p.Z;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y > maxY) maxY = p.Y;
                    if (p.Z > maxZ) maxZ = p.Z;
                }
            }
            return (new MeshPoint(minX, minY, minZ), new MeshPoint(maxX, maxY, maxZ));
        }

        /// <summary>顶点按容差聚簇后统计边使用次数与欧拉特征。</summary>
        public static EdgeReport Analyze(IReadOnlyList<MeshTriangle> triangles)
        {
            var points = new List<MeshPoint>(triangles.Count * 3);
            for (int i = 0; i < triangles.Count; i++)
            {
                points.Add(triangles[i].A);
                points.Add(triangles[i].B);
                points.Add(triangles[i].C);
            }

            var cluster = Cluster(points);
            int vertexCount = 0;
            foreach (var c in cluster)
                if (c + 1 > vertexCount)
                    vertexCount = c + 1;

            // 无向边使用计数 + 有向边计数
            var undirected = new Dictionary<long, int>();
            var directed = new Dictionary<long, int>();
            for (int i = 0; i < triangles.Count; i++)
            {
                int b0 = i * 3;
                for (int k = 0; k < 3; k++)
                {
                    int a = cluster[b0 + k];
                    int b = cluster[b0 + (k + 1) % 3];
                    if (a == b)
                        continue;
                    long ukey = a < b ? Pack(a, b) : Pack(b, a);
                    undirected.TryGetValue(ukey, out var uc);
                    undirected[ukey] = uc + 1;
                    long dkey = Pack(a, b);
                    directed.TryGetValue(dkey, out var dc);
                    directed[dkey] = dc + 1;
                }
            }

            int boundary = 0, nonManifold = 0, duplicated = 0;
            foreach (var kv in undirected)
            {
                if (kv.Value == 1) boundary++;
                if (kv.Value != 2) nonManifold++;
            }
            foreach (var kv in directed)
                if (kv.Value != 1)
                    duplicated++;

            return new EdgeReport(
                vertexCount,
                undirected.Count,
                triangles.Count,
                boundary,
                nonManifold,
                duplicated,
                vertexCount - undirected.Count + triangles.Count);
        }

        /// <summary>封闭定向流形：每条无向边恰好两个面、每条有向边恰好一次。</summary>
        public static void AssertClosedOrientedManifold(IReadOnlyList<MeshTriangle> triangles, string? message = null)
        {
            var report = Analyze(triangles);
            var prefix = message ?? "mesh";

            Assert.That(report.BoundaryEdges, Is.EqualTo(0),
                $"{prefix}: 有 {report.BoundaryEdges} 条边界边（网格不封闭）| {report}");
            Assert.That(report.NonManifoldEdges, Is.EqualTo(0),
                $"{prefix}: 有 {report.NonManifoldEdges} 条边不是恰好被两个面共享 | {report}");
            Assert.That(report.DuplicatedEdges, Is.EqualTo(0),
                $"{prefix}: 有 {report.DuplicatedEdges} 条有向边重复（绕序不一致）| {report}");
        }

        /// <summary>网格哈希 + 27 邻域精确比较的容差聚簇（与 GeometryAssert.Cluster 同口径）。</summary>
        static int[] Cluster(List<MeshPoint> points)
        {
            var cell = MergeTolerance;
            var buckets = new Dictionary<(long, long, long), List<int>>();
            var centers = new List<MeshPoint>();
            var result = new int[points.Count];

            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                long cx = (long)Math.Floor(p.X / cell), cy = (long)Math.Floor(p.Y / cell), cz = (long)Math.Floor(p.Z / cell);
                int found = -1;
                for (long dx = -1; dx <= 1 && found < 0; dx++)
                for (long dy = -1; dy <= 1 && found < 0; dy++)
                for (long dz = -1; dz <= 1 && found < 0; dz++)
                {
                    if (!buckets.TryGetValue((cx + dx, cy + dy, cz + dz), out var list))
                        continue;
                    foreach (var candidate in list)
                    {
                        var c = centers[candidate];
                        if (Math.Abs(c.X - p.X) <= cell && Math.Abs(c.Y - p.Y) <= cell && Math.Abs(c.Z - p.Z) <= cell)
                        {
                            found = candidate;
                            break;
                        }
                    }
                }
                if (found < 0)
                {
                    centers.Add(p);
                    found = centers.Count - 1;
                }
                result[i] = found;
                var key = (cx, cy, cz);
                if (!buckets.TryGetValue(key, out var bucket))
                {
                    bucket = new List<int>(2);
                    buckets[key] = bucket;
                }
                bucket.Add(found);
            }
            return result;
        }

        static long Pack(int a, int b) => ((long)a << 32) | (uint)b;
    }
}
