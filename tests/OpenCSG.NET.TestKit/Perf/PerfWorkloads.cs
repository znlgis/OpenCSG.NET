using System;
using System.Collections.Generic;
using Csg;

namespace Csg.Test
{
    /// <summary>
    /// Plan E（L6 性能/复杂度）的**确定性输入集**：perf 工具（<c>perf/OpenCSG.NET.PerfTest</c>）
    /// 与 SystemTests 的 <c>[Category("Perf")]</c> 预算用例共用同一份定义。
    /// <para>
    /// 为什么放在 TestKit：预算口径是「实测 × 倍数」，两侧必须测**同一批输入**
    /// （工具侧 ×4、测试侧 ×5），输入若各写一份就会各自演化、预算失去同源含义；
    /// TestKit 是 test-side 基础设施（<c>IsPackable=false</c>、仅 nunit 依赖），
    /// perf 引用它不会把任何测试依赖带进 <c>src/</c>。
    /// </para>
    /// <para>
    /// 全部输入由固定种子 / 固定语料下标导出 ⇒ 跨进程、跨机器可复现；任何一项的
    /// 形状或规模变化都必须重新实测并重钉两侧预算（见 Plan E 任务 1 报告）。
    /// </para>
    /// </summary>
    public static class PerfWorkloads
    {
        // ── ① 布尔：语料中号实体对 ─────────────────────────────────────────────

        /// <summary>语料下标 A（Normal 档 1152 面球面 —— 语料内面数最高的档位）。</summary>
        public const int UnionIndexA = 19;

        /// <summary>语料下标 B（同档 1152 面球面）。</summary>
        public const int UnionIndexB = 146;

        /// <summary>
        /// B 沿 X 的平移量（mm）。语料 Normal 档的 Offset 恒为 0（SolidGen 的 maxOffset=0）
        /// ⇒ 不平移时两球同心嵌套、布尔退化为「包含判定」；平移后为真实部分相交，
        /// 交线/裁剪面才进入被测路径。
        /// </summary>
        public const double UnionShiftX = 300.0;

        /// <summary>中号语料实体对（A 原样、B 沿 +X 平移 <see cref="UnionShiftX"/>）。</summary>
        public static (Solid A, Solid B) UnionPair()
        {
            var specs = Corpus.CorpusSpecs();
            var a = SolidGen.Materialize(specs[UnionIndexA]);
            var b = SolidGen.Materialize(specs[UnionIndexB]).Translate(UnionShiftX, 0, 0);
            return (a, b);
        }

        // ── ② NodeGen 深树（Evaluate / ToJson / FromJson） ─────────────────────

        /// <summary>中档树深度：NodeGen 默认 <see cref="NodeGen.DefaultMaxDepth"/>（4）之上取 5。</summary>
        public const int DeepTreeMaxDepth = 5;

        static readonly long[] DeepTreeSeedList = { 0, 1, 2, 3, 4, 5, 6, 7 };

        /// <summary>固定 8 个种子（深度 5）：含 2 棵求值量级 ≈45ms 的树（seed 3/6，实测），
        /// 其余为廉价树 —— 合计构成「中档」求值负载。</summary>
        public static IReadOnlyList<long> DeepTreeSeeds => DeepTreeSeedList;

        /// <summary>8 棵深树（确定性；生成本身不进计时）。</summary>
        public static IReadOnlyList<CsgNode> DeepTrees()
        {
            var trees = new CsgNode[DeepTreeSeedList.Length];
            for (var i = 0; i < trees.Length; i++)
                trees[i] = NodeGen.Random(DeepTreeSeedList[i], DeepTreeMaxDepth);
            return trees;
        }

        /// <summary>8 棵深树的紧凑 JSON（ToJson/FromJson 的输入料）。</summary>
        public static IReadOnlyList<string> DeepTreeJson()
        {
            var json = new string[DeepTreeSeedList.Length];
            for (var i = 0; i < json.Length; i++)
                json[i] = CsgSerialization.ToJson(NodeGen.Random(DeepTreeSeedList[i], DeepTreeMaxDepth), indented: false);
            return json;
        }

        // ── ③ 线段集（Fit / Clean 的 5k/20k 输入） ─────────────────────────────

        /// <summary>批次基准种子（批次 i 用 <c>SegmentBatchSeed + i</c>）。</summary>
        public const int SegmentBatchSeed = 7000;

        /// <summary>批次布局的物理跨度（mm）——**恒定**，不随总段数变化。</summary>
        public const double SegmentSpan = 2400.0;

        /// <summary>
        /// 大批量线段集（确定性）。
        /// <para>
        /// <see cref="ViewSegGen.Random"/> 受「block ≤ 1 即停」的守卫限制，单次上限约 250 段
        /// （<c>ViewSegGenTest</c> 有覆盖），数量级到不了 5k/20k ⇒ 这里把批次平移拼接：
        /// 每批的平移步长 = <see cref="SegmentSpan"/> / ceil(√批数)，使**总包络跨度恒定**
        /// （≈2400mm）——<see cref="ViewSegmentCleanup.Clean"/> 的阈值是「对角线 × 1e-4」自适应，
        /// 若跨度随 n 增长，阈值随之变大、丢弃/合并行为改变，20k/5k 比值就不再反映算法复杂度。
        /// </para>
        /// </summary>
        public static IReadOnlyList<ViewSegment> Segments(int count)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), count, "段数必须为正");

            var segments = new List<ViewSegment>(count);
            var batches = (count + 255) / 256 + 1;
            var side = (int)Math.Ceiling(Math.Sqrt(batches));
            var step = SegmentSpan / side;

            var batch = 0;
            while (segments.Count < count)
            {
                var offsetX = (batch % side) * step;
                var offsetY = (batch / side) * step;
                foreach (var segment in ViewSegGen.Random(SegmentBatchSeed + batch, 100000))
                {
                    segments.Add(new ViewSegment(
                        new ViewPoint2(segment.Start.X + offsetX, segment.Start.Y + offsetY),
                        new ViewPoint2(segment.End.X + offsetX, segment.End.Y + offsetY),
                        segment.IsVisible));
                }
                batch++;
            }

            segments.RemoveRange(count, segments.Count - count);
            return segments;
        }

        // ── ④ 投影网格（Complexity 的 Project 5k/20k） ─────────────────────────

        /// <summary>Project 复杂度档：5000 三角形（圆柱 res=1250）。</summary>
        public const int ProjectSmallTriangles = 5000;

        /// <summary>Project 复杂度档：20000 三角形（圆柱 res=5000）。</summary>
        public const int ProjectLargeTriangles = 20000;

        /// <summary>
        /// 圆柱网格：三角形数 = 4 × 分辨率（每片 2 个侧面三角形 + 2 个端盖扇形三角形）。
        /// 圆柱长 900、半径 450 ⇒ 三视图的屏面跨度均 ≈900mm，深度栅格 ≈900×900（Plan E
        /// 「DepthRaster 900² 栅格」口径）；轴沿 Y（<see cref="ViewDirections"/>.Left 为轴向视图）。
        /// </summary>
        public static IReadOnlyList<ViewTriangle> ProjectMesh(int triangleCount)
        {
            if (triangleCount <= 0 || triangleCount % 4 != 0)
                throw new ArgumentOutOfRangeException(nameof(triangleCount), triangleCount,
                    "三角形数必须为正且为 4 的倍数（= 4 × 圆柱分辨率）");

            var cylinder = Solids.Cylinder(new CylinderOptions
            {
                Start = new Vector3D(0, -450, 0),
                End = new Vector3D(0, 450, 0),
                RadiusStart = 450,
                RadiusEnd = 450,
                Resolution = triangleCount / 4,
            });

            var triangles = ViewBridge.ToViewTriangles(cylinder);
            if (triangles.Count != triangleCount)
                throw new InvalidOperationException(
                    $"ProjectMesh({triangleCount}) 实际产出 {triangles.Count} 个三角形；圆柱口径已漂移");
            return triangles;
        }

        // ── ⑤ 边界用例夹具 ────────────────────────────────────────────────────

        /// <summary>1e6 长圆柱的长度（mm）。</summary>
        public const double LongCylinderLength = 1e6;

        /// <summary>1e6 长圆柱的半径（mm）。</summary>
        public const double LongCylinderRadius = 500;

        /// <summary>1e6 长圆柱的周向分辨率（12 段 ⇒ 相邻侧面二面角 30° &gt; 锐边阈值 25°，轴向棱是特征边）。</summary>
        public const int LongCylinderResolution = 12;

        /// <summary>
        /// 1e6 长圆柱（轴向沿 Y，居中）。棱边长度 = 1e6（MaxEdgeSamples 边界用例）。
        /// 注：闭合实体的候选边端点必在「正面三角形」的屏面包络内 ⇒ 采样数 ≤ √2×900 ≈ 1273，
        /// 4000 上限对闭合实体不可达（实测最大 901，见任务 1 报告）；用例据此把「不挂起 +
        /// 拓扑计数」钉在实测量级，而不是断言 4000 截断。
        /// </summary>
        public static Solid LongCylinder()
        {
            return Solids.Cylinder(new CylinderOptions
            {
                Start = new Vector3D(0, -LongCylinderLength / 2, 0),
                End = new Vector3D(0, LongCylinderLength / 2, 0),
                RadiusStart = LongCylinderRadius,
                RadiusEnd = LongCylinderRadius,
                Resolution = LongCylinderResolution,
            });
        }

        /// <summary>薄板边长（mm）。</summary>
        public const double ThinPlateSize = 1000;

        /// <summary>薄板厚度（mm）。</summary>
        public const double ThinPlateThickness = 20;

        /// <summary>凸台底面边长（mm）。</summary>
        public const double ThinBossFootprint = 100;

        /// <summary>凸台外露高度（mm）——远低于栅格单元（1000/900 ≈ 1.11mm）与清理长度阈值。</summary>
        public const double ThinBossProtrusion = 0.01;

        /// <summary>
        /// 薄板 z-fighting 夹具：1000×1000×20 板 + 100×100 凸台。
        /// 凸台厚 <c>2×protrusion</c>、底面嵌入板内 <see cref="ThinBossProtrusion"/>、上表面外露同量
        /// （凸台 z ∈ [9.99, 10.01]、板 z ∈ [−10, 10]）——刻意不做「凸台全部没入板内」的退化构造：
        /// 与板顶面严格共面/没入时布尔会把凸台整体吞掉（实测 translate 到 9.99 时 Union 只剩 6 个
        /// 多边形、凸台消失），那样夹具测的就不是投影行为而是布尔退化。
        /// </summary>
        public static Solid ThinPlateWithBoss()
        {
            var plate = Solids.Cube(new Vector3D(ThinPlateSize, ThinPlateSize, ThinPlateThickness), center: true);
            var bossThickness = 2 * ThinBossProtrusion;
            var boss = Solids.Cube(new Vector3D(ThinBossFootprint, ThinBossFootprint, bossThickness), center: true)
                .Translate(0, 0, ThinPlateThickness / 2 + ThinBossProtrusion - bossThickness / 2);
            return plate.Union(boss);
        }
    }
}
