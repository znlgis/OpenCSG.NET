using System;
using System.Collections.Generic;

namespace Csg.Test
{
    /// <summary>
    /// 顶点容差聚簇（网格哈希 + 27 邻域精确比较，避免排序单链聚簇的漏合并）。
    /// Solid 侧（<see cref="GeometryAssert"/>，坐标 Vector3D）与网格侧（<see cref="MeshAssert"/>，
    /// 坐标 MeshPoint）共用这一份实现：两侧结论只有在校验容差与聚簇语义完全一致时才可交叉比对，
    /// 各留一份副本会随任一侧的改动静默失效。
    /// </summary>
    internal static class VertexCluster
    {
        /// <summary>顶点合并容差：远大于接缝处 1e-16 浮点噪声、远小于 CSG 的 1e-5 容差。</summary>
        internal const double Tolerance = 1e-6;

        /// <summary>把坐标按容差聚簇，返回每个点的簇 id（0 起连续，首点为簇心）。</summary>
        internal static int[] Cluster<T>(IReadOnlyList<T> points, Func<T, (double X, double Y, double Z)> coordinates)
        {
            var cell = Tolerance;
            var buckets = new Dictionary<(long, long, long), List<int>>();
            var centers = new List<(double X, double Y, double Z)>();
            var result = new int[points.Count];

            for (int i = 0; i < points.Count; i++)
            {
                var p = coordinates(points[i]);
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

        /// <summary>把两个簇 id 打包成一个无向/有向边键。</summary>
        internal static long Pack(int a, int b) => ((long)a << 32) | (uint)b;
    }
}
