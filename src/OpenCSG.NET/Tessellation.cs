using System;

namespace Csg
{
    /// <summary>
    /// **三角化（离散）口径**：圆弧/圆柱/球面在网格里只能以多边形逼近，
    /// 分段数按"**弦高容差**"（chord tolerance，模型单位 mm）自适应决定 ——
    /// 半径越大分段越多，保证弦与弧的最大偏差不超过容差。
    /// <para>
    /// 背景（2026-10-10 用户口径）：「导出 stl 时，有些圆弧被近似成折线，期望圆弧能正常导出」。
    /// 原因：原先 <c>Solid.DefaultResolution3D = 12</c>（整圆仅 12 段）⇒ R1930 的弧弦高偏差约
    /// **65 mm**，肉眼就是折线。现改为自适应：R1930 在 0.5 mm 容差下取 128 段（上限），偏差 ≤ 0.5 mm。
    /// </para>
    /// <para>
    /// 注：STL 是三角网格格式，**不存在真正的圆弧**；要"真圆弧"必须用 BREP 格式
    /// （STEP/IGES/DWG 等）。本类只保证"折线足够贴近圆弧"。
    /// </para>
    /// </summary>
    public static class Tessellation
    {
        /// <summary>
        /// 弦高容差（模型单位 mm）。**默认 0 = 关闭自适应、沿用旧口径**
        /// （<see cref="Solid.DefaultResolution3D"/> = 12 段，保持库的既有行为与既有测试不变）；
        /// 由调用方（如 STL 导出）显式设为 &gt;0 才启用自适应加密。
        /// <para>
        /// **按线程隔离**（<see cref="ThreadStaticAttribute"/>）：STL 导出在命令线程上设置、同线程求值；
        /// 这样并行运行的单测之间不会互相影响（曾因全局静态被并行测试串扰，出现无关用例失败 ✗）。
        /// </para>
        /// </summary>
        [ThreadStatic]
        private static double _chordTolerance;

        /// <summary>弦高容差（模型单位 mm，按线程隔离）。</summary>
        public static double ChordTolerance
        {
            get => _chordTolerance;
            set => _chordTolerance = value;
        }

        /// <summary>是否已启用自适应（弦高容差 &gt; 0）。</summary>
        public static bool IsAdaptive => _chordTolerance > 0;

        /// <summary>分段数下限（太小会把小半径的圆也画成多边形）。</summary>
        public const int MinSegments = 16;

        /// <summary>分段数上限（防止大半径把三角面数撑爆）。</summary>
        public const int MaxSegments = 128;

        /// <summary>旧口径下"半圆/圆弧"的固定分段数（胶囊轮廓原先固定 16）。</summary>
        public const int LegacyArcSegments = 16;

        /// <summary>
        /// 按半径与弦高容差求**整圆**分段数：单段圆心角 θ 满足 r(1−cos(θ/2)) ≤ tol
        /// ⇒ θ = 2·acos(1 − tol/r)，N = ceil(2π/θ)，夹在 [MinSegments, MaxSegments]。
        /// </summary>
        public static int SegmentsFor(double radius, double? tolerance = null, int? min = null, int? max = null)
        {
            var tol = tolerance ?? _chordTolerance;
            var lo = min ?? MinSegments;
            var hi = max ?? MaxSegments;

            // 未启用自适应 ⇒ 旧口径（与 Solid.DefaultResolution3D 一致，保证既有行为/测试不变）
            if (tol <= 0) return Solid.DefaultResolution3D;
            if (radius <= 0 || tol >= radius) return lo;

            var half = Math.Acos(1.0 - tol / radius);
            if (double.IsNaN(half) || half <= 0) return hi;

            var n = (int)Math.Ceiling(Math.PI / half);
            return Math.Min(Math.Max(n, lo), hi);
        }

        /// <summary>按弦高容差求**半圆/圆弧**分段数（给定圆心角，度）。</summary>
        public static int SegmentsForArc(double radius, double sweepDegrees,
            double? tolerance = null, int? min = null, int? max = null)
        {
            if ((tolerance ?? _chordTolerance) <= 0) return LegacyArcSegments;

            var full = SegmentsFor(radius, tolerance, min, max);
            var sweep = Math.Min(Math.Abs(sweepDegrees), 360.0) / 360.0;
            var scaled = (int)Math.Ceiling(full * sweep);
            var lo = min ?? MinSegments;
            var hi = max ?? MaxSegments;
            return Math.Min(Math.Max(scaled, Math.Max(4, lo / 2)), hi);
        }
    }
}
