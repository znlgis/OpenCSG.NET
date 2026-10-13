using System;
using System.Collections.Generic;

namespace Csg.Test
{
    /// <summary>
    /// Tier 5：射线-三角形求交（Möller–Trumbore，标量实现）与最近命中查询。
    /// <para>
    /// <b>独立实现</b>：本类是遮挡判定的几何基元 oracle，必须**独立于被测消隐链路** ——
    /// 不得调用 <see cref="ViewProjector"/> / <c>DepthRaster</c> 的任何成员；
    /// 一旦复用被测代码，判定就不再是独立对照（与 <see cref="ConvexHull"/> 的定位一致）。
    /// 与视图方向（<c>ViewDirection</c>）无关，纯几何。
    /// </para>
    /// <para>
    /// 全部用**标量**运算：<c>ViewPoint − ViewPoint</c> 返回 <see cref="ViewPoint"/>（非向量），
    /// 且标量形式避免了隐藏的类型语义转换，独立性更强。
    /// </para>
    /// </summary>
    public static class RayCast
    {
        /// <summary>
        /// Möller–Trumbore 射线-三角形求交。
        /// <para>
        /// <b>契约</b>：`t` 为沿 <paramref name="direction"/> 的命中距离参数 ——
        /// 命中点 = <paramref name="origin"/> + `t` × <paramref name="direction"/>；
        /// <paramref name="direction"/> 不必归一化，但调用方传单位向量，此时 `t` 即欧氏距离
        /// （`t` 按传入向量长度计）。仅接受正向命中（`t &gt; 0`）；射线与三角形异面/背离/在三角形外
        /// 时返回 <c>false</c>；返回 false 时 `t` 的值无意义（调用方不得读取）。
        /// </para>
        /// <para>
        /// <b>边界容差</b>：<c>|det| &lt; 1e-14</c> 判为射线平行于三角形平面（退化三角形 det ≡ 0，
        /// 恒不命中、除零由该分支挡掉）；重心坐标判据放宽到 <c>[−1e-9, 1+1e-9]</c> ——
        /// 网格化产生的 T 接缝/共享边上的点会因浮点误差落在三角形外侧几个 ulp，
        /// 严格 `[0,1]` 会导致同一位置在两相邻面上都判「未命中」（漏判遮挡）；
        /// 容差内接受该点，保证边界命中不漏。
        /// </para>
        /// </summary>
        public static bool Intersect(ViewTriangle triangle, ViewPoint origin, ViewVector direction, out double t)
        {
            t = 0;

            // 两条边 e1 = B − A、e2 = C − A
            var e1x = triangle.B.X - triangle.A.X;
            var e1y = triangle.B.Y - triangle.A.Y;
            var e1z = triangle.B.Z - triangle.A.Z;
            var e2x = triangle.C.X - triangle.A.X;
            var e2y = triangle.C.Y - triangle.A.Y;
            var e2z = triangle.C.Z - triangle.A.Z;

            // p = direction × e2
            var px = direction.Y * e2z - direction.Z * e2y;
            var py = direction.Z * e2x - direction.X * e2z;
            var pz = direction.X * e2y - direction.Y * e2x;

            // det = e1 · p（= e1 · (direction × e2)）
            var det = e1x * px + e1y * py + e1z * pz;
            if (Math.Abs(det) < 1e-14)
                return false;

            var inv = 1.0 / det;

            // u 坐标（重心坐标之一）
            var tvx = origin.X - triangle.A.X;
            var tvy = origin.Y - triangle.A.Y;
            var tvz = origin.Z - triangle.A.Z;
            var u = (tvx * px + tvy * py + tvz * pz) * inv;
            if (u < -1e-9 || u > 1 + 1e-9)
                return false;

            // q = tv × e1；v 坐标
            var qx = tvy * e1z - tvz * e1y;
            var qy = tvz * e1x - tvx * e1z;
            var qz = tvx * e1y - tvy * e1x;
            var v = (direction.X * qx + direction.Y * qy + direction.Z * qz) * inv;
            if (v < -1e-9 || u + v > 1 + 1e-9)
                return false;

            // t = e2 · q / det；仅接受正向命中
            t = (e2x * qx + e2y * qy + e2z * qz) * inv;
            return t > 0;
        }

        /// <summary>
        /// 遍历 <paramref name="triangles"/> 求沿射线的最近命中距离。
        /// <para>
        /// <b>契约</b>：忽略下标 <paramref name="skipA"/>/<paramref name="skipB"/> 指示的三角形
        /// （调用方用于跳过「源边相邻面」——源边所在的两个面在自身上命中不算遮挡），
        /// 并丢弃命中距离 <c>≤ <paramref name="minDistance"/></c> 的命中（自命中过滤：
        /// 射线起点常位于源边/相邻面上，`t ≈ 0` 是数值噪声而非遮挡物）；
        /// 返回满足 <c>t &gt; <paramref name="minDistance"/></c> 的最小 `t`。
        /// 无命中（或被全部过滤）返回 <see cref="double.PositiveInfinity"/>。
        /// 默认 <paramref name="skipA"/>/<paramref name="skipB"/> = −1（不跳过任何三角形），
        /// <paramref name="minDistance"/> = 1e-7。
        /// </para>
        /// </summary>
        public static double NearestHitDistance(
            IReadOnlyList<ViewTriangle> triangles,
            ViewPoint origin,
            ViewVector direction,
            int skipA = -1,
            int skipB = -1,
            double minDistance = 1e-7)
        {
            var nearest = double.PositiveInfinity;
            for (var i = 0; i < triangles.Count; i++)
            {
                if (i == skipA || i == skipB)
                    continue;
                if (!Intersect(triangles[i], origin, direction, out var t))
                    continue;
                if (t <= minDistance)
                    continue;
                if (t < nearest)
                    nearest = t;
            }

            return nearest;
        }
    }
}
