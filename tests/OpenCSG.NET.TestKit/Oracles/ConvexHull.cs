using System;
using System.Collections.Generic;
using System.Linq;

namespace Csg.Test
{
    /// <summary>
    /// 2D 凸包（Andrew 单调链）与周长。**独立于被测代码实现** ——
    /// 它是 Tier 2 的对照物，一旦复用了被测代码就不再是 oracle。
    /// </summary>
    public static class ConvexHull
    {
        /// <summary>返回逆时针凸包（不含重复的收尾点）。输入少于 3 个点时原样返回。</summary>
        public static IReadOnlyList<ViewPoint2> MonotoneChain(IReadOnlyList<ViewPoint2> points)
        {
            if (points.Count <= 2)
                return points.ToArray();

            var sorted = points
                .OrderBy(p => p.X)
                .ThenBy(p => p.Y)
                .ToArray();

            var hull = new List<ViewPoint2>(sorted.Length * 2);

            // 下凸壳
            foreach (var p in sorted)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }

            // 上凸壳
            var lower = hull.Count + 1;
            for (var i = sorted.Length - 2; i >= 0; i--)
            {
                var p = sorted[i];
                while (hull.Count >= lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }

            // 收尾点与首点重复，去掉
            hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        public static double Perimeter(IReadOnlyList<ViewPoint2> hull)
        {
            if (hull.Count < 2)
                return 0.0;

            var total = 0.0;
            for (var i = 0; i < hull.Count; i++)
            {
                var a = hull[i];
                var b = hull[(i + 1) % hull.Count];
                var dx = b.X - a.X;
                var dy = b.Y - a.Y;
                total += Math.Sqrt(dx * dx + dy * dy);
            }
            return total;
        }

        public static double PerimeterOf(IReadOnlyList<ViewPoint2> points) => Perimeter(MonotoneChain(points));

        /// <summary>叉积 (b−a)×(c−a)；&gt;0 表示左转（逆时针）。</summary>
        static double Cross(ViewPoint2 a, ViewPoint2 b, ViewPoint2 c)
            => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    }
}
