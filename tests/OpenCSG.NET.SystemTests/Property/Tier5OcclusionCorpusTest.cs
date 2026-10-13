using System.Collections.Generic;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// Tier 5：遮挡 oracle 铺到 Smoke 语料（64 个种子 × 主/俯/左 三视图）。
    /// <para>
    /// 每个种子用与 <see cref="Tier1BooleanIdentityTest"/> 同口径的采样建 **两个**实体
    /// （<c>SolidGen.RandomPair(seed, tier)</c>），对**并集** <c>A ∪ B</c> 做三视图遮挡 oracle；
    /// 用并集而非单实体：语料的遮挡几乎都来自「双实体并集的相互遮挡」，单实体在轴对齐视图下自遮挡多为 0。
    /// 每个视图**单独**调用 oracle —— 防空洞守卫按调用判定（Task 2 修正轮的结论：聚合调用无法发现单个视图零判定）。
    /// </para>
    /// <para>
    /// <b>豁免与归因（首轮实测 59/64 种子有视图失败；全部归因于 DEFECT-004 机制②）</b>：
    /// 失败明细的形状统一为「射线 9 探针全说可见（robust=[V]）而投影器把该段画成隐藏（proj=[H]）」，
    /// 即投影器在**消隐栅格的格量化**下把可见棱边切成 H 碎段。逐点反例（复刻 src 的 DepthRaster 后逐格复核）：
    /// <list type="bullet">
    /// <item>种子 03（Cube/Cube，无曲面）主视唯一失败点：轮廓边 #12-15 末端 1.0 格（0.994 cell）H 碎段，
    /// 其起点格 nearest=23.29374 由**该边自身的前向相邻面**（tri#16，|n·Look|=0.767）在该格中心提供，
    /// 而边点深度 23.434997 —— 超出量 +0.0157 仅为 bias(0.12554) 的 12.5% ⇒ 栅格把面自己当遮挡体（自遮挡量化）。</item>
    /// <item>种子 08 俯视：一条轮廓边（相邻面 |n·Look| 0.57/0.79）被切成 10 段 V + 10 段**单格 H 碎段**，
    /// 每段 H 的起点格 nearest 都比边点深度近 0.012–0.124（bias 的 7%–75%）；9 探针全 V。</item>
    /// <item>种子 11 左视：10.8 格 H 长段，owner=该边自身相邻面（|n·Look|=0.606，**非掠射**），
    /// 超出量 0.06–0.15 = bias 的 12%–29% ⇒ 触发条件不是「掠射」而是 **cell·tanφ &gt; bias**（格子尺度深度变化超过固定 bias）。</item>
    /// <item>种子 18：相邻面 |n·Look| 0.013–0.015（0.75°–0.86°，极端掠射），14 格 H 碎段，owner=掠射面自身。</item>
    /// <item>种子 63（Survey）：|n·Look| 0.021–0.051，超出量 3–9×bias —— Survey 档 cell 与 bias 同比例放大，
    /// 但量化误差随 1/|n·Look| 放大，故伪影比 Normal 档更严重。</item>
    /// <item>种子 25 左视：逐视图空洞守卫（43 个采样点全部 indet、checked=0）—— 同一掠射带的极端形态。</item>
    /// </list>
    /// 全量 1358 条明细中 1320 条（97.2%）可复刻「该重合候选边自己的 Split 就产出覆盖 q 的 H 段」（repro=H），
    /// 即 oracle 探的 3D 边与投影器画的边是同一条；余 38 条（2.8%，分布在 18 个种子上）的 H 段由
    /// <c>ViewSegmentCleanup</c> 的「只延长不缩短」合并/重叠折叠从相邻共线段延来（2D 重合抽象的有意成分，
    /// 该 H 与覆盖 q 的 V 重叠、制图上被实线盖住，见 <c>ViewSegmentCleanup</c> 类注释「不跨可见性合并」）。
    /// 两者都是**消隐栅格 V/H 交替碎段**（DEFECT-004 机制②）的直接/下游产物，故全部引用 DEFECT-004 豁免，
    /// 不新登记缺陷。逐种子失败视图与明细形态见 <see cref="OcclusionExemptSeeds"/> 上方注释。
    /// </para>
    /// <para>
    /// <b>为什么不动 oracle</b>：单点反例（种子 03，见上）证明 oracle 的判定与几何一致 ——
    /// 边点处沿 −Look 的 9 探针（跳过该边相邻面）确实无遮挡物，投影器的「遮挡物」是**同一张面**在
    /// 相邻格中心的位置；即违反契约的是投影器的格量化近似，不是 oracle 的容差口径（Task 10 式「单点反例改 oracle」
    /// 的前提不成立），故本任务不改 <c>OcclusionAssert</c>。
    /// </para>
    /// <para>
    /// 覆盖：oracle 自身的空洞守卫按视图生效（Task 2 修正轮）；语料级「是否真的在测遮挡」由
    /// <see cref="Corpus_HasNonTrivialOcclusionCoverage"/> 的轻量扫描门兜底（防退化）。
    /// </para>
    /// </summary>
    [TestFixture]
    public class Tier5OcclusionCorpusTest
    {
        /// <summary>
        /// DEFECT-004 机制② 的语料豁免（brief 允许的唯一形式：按 index 豁免 + 注明编号与理由）。
        /// <para>
        /// 实测口径（首轮全绿运行前的失败扫描，逐 (seed, view) 一个进程以避免 NUnit 失败消息跨断言累积）：
        /// 失败视图用「主/俯/左」记录；「短碎段」= 覆盖采样点的隐藏输出段长度 ≤ 2 个栅格格（消隐碎段特征）；
        /// 「owner=相邻面」= 采样点所在格的最近深度由**该重合候选边自己的相邻面**提供（自遮挡量化，逐格复刻 DepthRaster 判定）；
        /// 「repro=H」= 复刻该候选边自己的 Split 会产出覆盖该采样点的 H 段（oracle 与投影器看的是同一条 3D 边）。
        /// 全量 1358 条明细：repro=H 1320 / repro=V 38；owner=相邻面 877 / 其他 443；短碎段(≤2 格) 1227。
        /// </para>
        /// 逐种子（种子下标: 失败视图；短碎段数；owner=相邻面数；repro=H 数[；repro=V 数]）：
        /// </summary>
        //  00: 失败视图 主—/俯9/左≥10；短碎段 —/9/10；owner=相邻面 —/5/0；repro=H —/9/10
        //  02: 失败视图 主≥10/俯≥10/左—；短碎段 10/9/—；owner=相邻面 5/10/—；repro=H 10/10/—
        //  03: 失败视图 主1/俯—/左—；短碎段 1/—/—；owner=相邻面 1/—/—；repro=H 1/—/—
        //  04: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/5/10；owner=相邻面 2/8/7；repro=H 10/10/10
        //  05: 失败视图 主—/俯—/左≥10；短碎段 —/—/10；owner=相邻面 —/—/0；repro=H —/—/10
        //  06: 失败视图 主—/俯1/左—；短碎段 —/1/—；owner=相邻面 —/1/—；repro=H —/1/—
        //  08: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/10/10；owner=相邻面 6/5/10；repro=H 10/10/10
        //  09: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/10/10；owner=相邻面 2/10/1；repro=H 10/10/10
        //  10: 失败视图 主≥10/俯≥10/左≥10；短碎段 0/10/10；owner=相邻面 9/9/5；repro=H 10/10/10
        //  11: 失败视图 主—/俯7/左3；短碎段 —/7/0；owner=相邻面 —/6/2；repro=H —/7/3
        //  12: 失败视图 主3/俯2/左≥10；短碎段 3/2/10；owner=相邻面 3/0/4；repro=H 3/2/9；repro=V 0/0/1
        //  13: 失败视图 主≥10/俯≥10/左≥10；短碎段 3/10/1；owner=相邻面 10/6/10；repro=H 10/10/10
        //  15: 失败视图 主≥10/俯3/左—；短碎段 0/3/—；owner=相邻面 0/0/—；repro=H 10/3/—
        //  16: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/10/10；owner=相邻面 4/6/5；repro=H 10/10/10
        //  17: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/7/0；owner=相邻面 6/7/10；repro=H 10/10/10
        //  18: 失败视图 主5/俯3/左—；短碎段 0/0/—；owner=相邻面 0/3/—；repro=H 2/3/—；repro=V 3/0/—
        //  19: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/10/10；owner=相邻面 8/6/5；repro=H 10/10/10
        //  20: 失败视图 主≥10/俯≥10/左6；短碎段 10/10/6；owner=相邻面 10/5/6；repro=H 10/10/6
        //  21: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/10/10；owner=相邻面 2/3/7；repro=H 10/10/10
        //  22: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/10/10；owner=相邻面 10/10/8；repro=H 10/10/10
        //  23: 失败视图 主≥10/俯≥10/左6；短碎段 8/10/5；owner=相邻面 0/0/6；repro=H 10/9/6；repro=V 0/1/0
        //  24: 失败视图 主≥10/俯≥10/左—；短碎段 10/10/—；owner=相邻面 9/3/—；repro=H 10/10/—
        //  25: 失败视图 主—/俯—/左 空洞守卫（indeterminate=43、checked=0）
        //  27: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/7/10；owner=相邻面 3/10/9；repro=H 10/10/10
        //  28: 失败视图 主1/俯—/左—；短碎段 1/—/—；owner=相邻面 1/—/—；repro=H 0/—/—；repro=V 1/—/—
        //  29: 失败视图 主≥10/俯≥10/左≥10；短碎段 9/10/10；owner=相邻面 10/6/7；repro=H 10/10/10
        //  30: 失败视图 主≥10/俯—/左—；短碎段 10/—/—；owner=相邻面 5/—/—；repro=H 10/—/—
        //  31: 失败视图 主—/俯3/左—；短碎段 —/3/—；owner=相邻面 —/0/—；repro=H —/3/—
        //  32: 失败视图 主9/俯≥10/左≥10；短碎段 9/10/7；owner=相邻面 3/8/4；repro=H 9/10/10
        //  33: 失败视图 主—/俯≥10/左3；短碎段 —/10/0；owner=相邻面 —/5/3；repro=H —/10/2；repro=V —/0/1
        //  34: 失败视图 主—/俯8/左≥10；短碎段 —/4/10；owner=相邻面 —/8/5；repro=H —/8/9；repro=V —/0/1
        //  35: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/7/10；owner=相邻面 4/10/10；repro=H 10/10/10
        //  36: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/3/10；owner=相邻面 8/9/2；repro=H 10/10/10
        //  37: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/10/10；owner=相邻面 10/10/10；repro=H 10/10/10
        //  38: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/10/10；owner=相邻面 10/10/8；repro=H 10/10/10
        //  39: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/10/10；owner=相邻面 0/5/6；repro=H 10/10/10
        //  40: 失败视图 主—/俯≥10/左≥10；短碎段 —/0/7；owner=相邻面 —/10/8；repro=H —/10/10
        //  41: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/8/10；owner=相邻面 9/10/5；repro=H 10/4/10；repro=V 0/6/0
        //  42: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/10/4；owner=相邻面 5/9/7；repro=H 10/10/10
        //  43: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/10/10；owner=相邻面 10/7/10；repro=H 10/10/10
        //  44: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/10/6；owner=相邻面 7/10/7；repro=H 10/10/7；repro=V 0/0/3
        //  45: 失败视图 主6/俯7/左≥10；短碎段 6/1/10；owner=相邻面 3/6/5；repro=H 6/7/10
        //  46: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/10/10；owner=相邻面 5/10/3；repro=H 10/10/10
        //  47: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/10/10；owner=相邻面 5/10/5；repro=H 10/10/10
        //  48: 失败视图 主5/俯5/左—；短碎段 5/5/—；owner=相邻面 5/4/—；repro=H 5/4/—；repro=V 0/1/—
        //  49: 失败视图 主≥10/俯≥10/左≥10；短碎段 0/4/4；owner=相邻面 10/8/10；repro=H 10/10/10
        //  50: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/10/10；owner=相邻面 10/2/4；repro=H 10/10/10
        //  51: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/10/10；owner=相邻面 9/0/10；repro=H 10/10/10
        //  52: 失败视图 主≥10/俯1/左≥10；短碎段 10/0/10；owner=相邻面 0/1/5；repro=H 10/0/10；repro=V 0/1/0
        //  53: 失败视图 主—/俯—/左≥10；短碎段 —/—/10；owner=相邻面 —/—/0；repro=H —/—/10
        //  54: 失败视图 主≥10/俯≥10/左≥10；短碎段 6/10/10；owner=相邻面 10/8/7；repro=H 10/10/10
        //  55: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/10/10；owner=相邻面 4/9/10；repro=H 10/10/9；repro=V 0/0/1
        //  56: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/10/10；owner=相邻面 7/7/8；repro=H 8/10/10；repro=V 2/0/0
        //  57: 失败视图 主≥10/俯3/左≥10；短碎段 10/3/10；owner=相邻面 7/0/2；repro=H 9/3/10；repro=V 1/0/0
        //  58: 失败视图 主≥10/俯≥10/左≥10；短碎段 6/10/10；owner=相邻面 10/6/5；repro=H 4/9/10；repro=V 6/1/0
        //  60: 失败视图 主≥10/俯≥10/左≥10；短碎段 7/10/8；owner=相邻面 9/6/10；repro=H 10/10/8；repro=V 0/0/2
        //  61: 失败视图 主≥10/俯≥10/左≥10；短碎段 10/10/9；owner=相邻面 8/9/4；repro=H 10/10/10
        //  62: 失败视图 主8/俯—/左≥10；短碎段 8/—/10；owner=相邻面 6/—/8；repro=H 8/—/10
        //  63: 失败视图 主≥10/俯≥10/左≥10；短碎段 7/3/9；owner=相邻面 10/6/9；repro=H 7/10/7；repro=V 3/0/3
        // 未列入 = 首轮全绿（5 个：1, 7, 14, 26, 59）。
        static readonly HashSet<int> OcclusionExemptSeeds = new HashSet<int>
        {
            0, 2, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 27, 28, 29,
            30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53,
            54, 55, 56, 57, 58, 60, 61, 62, 63,
        };

        static IEnumerable<TestCaseData> CorpusCases()
        {
            var seeds = Corpus.SmokeSeeds();
            for (var i = 0; i < seeds.Count; i++)
            {
                var data = new TestCaseData(seeds[i], Corpus.TierOf(i))
                    .SetName($"Corpus_{i:D2}_{Corpus.TierOf(i)}");
                if (OcclusionExemptSeeds.Contains(i))
                    data.Explicit("DEFECT-004: 消隐栅格逐格采样把可见棱边切成 1 格 H 碎段（机制②，见 tests/DEFECTS.md）");
                yield return data;
            }
        }

        [TestCaseSource(nameof(CorpusCases))]
        [Category("Smoke")]
        public void UnionOcclusionMatchesRayCast(long seed, CoordTier tier)
        {
            var (specA, specB) = SolidGen.RandomPair(seed, tier);
            var solid = SolidGen.Materialize(specA).Union(SolidGen.Materialize(specB));
            var triangles = ViewBridge.ToViewTriangles(solid);
            var views = ViewBridge.ProjectViews(solid, ViewDirections.OrthographicThree);

            // 逐视图调用（而非聚合一次）：防空洞守卫按调用判定，聚合调用无法发现单个视图零判定。
            foreach (var view in views)
                OcclusionAssert.AssertVisibilityMatchesRayCast(
                    new[] { view }, triangles, $"{Describe(seed, tier)}/{view.Direction.Name}");
        }

        /// <summary>
        /// 轻量扫描：只构建 + 投影 + 统计（不跑射线 oracle），确定性、与逐种子用例解耦。
        /// 下限 = 首次全绿运行实测值 × 0.8（实测值写在 <see cref="Corpus"/> 语料当前状态上）：
        /// <list type="bullet">
        /// <item>有遮挡（H&gt;0）的种子数实测 <b>63/64</b>（唯一无遮挡的种子不因本门而失败）⇒ 下限 <b>50</b>；</item>
        /// <item>隐藏段总数实测 <b>35076</b>（192 个视图里 180 个有遮挡，线段总数 69043）⇒ 下限 <b>28060</b>。</item>
        /// </list>
        /// 门值取 0.8 只为吸收语料/生成器的非语义改动；一旦 H 覆盖率真退化（例如消隐整片失效），
        /// 两式都会跌破 —— 这正是逐种子用例被大面积豁免后需要的兜底（豁免后仍有 5 个种子在跑 oracle）。
        /// </summary>
        [Test]
        [Category("Smoke")]
        public void Corpus_HasNonTrivialOcclusionCoverage()
        {
            var seeds = Corpus.SmokeSeeds();
            var specsWithHidden = 0;
            var totalHiddenSegments = 0;
            for (var i = 0; i < seeds.Count; i++)
            {
                var (specA, specB) = SolidGen.RandomPair(seeds[i], Corpus.TierOf(i));
                var solid = SolidGen.Materialize(specA).Union(SolidGen.Materialize(specB));
                var views = ViewBridge.ProjectViews(solid, ViewDirections.OrthographicThree);
                var hasHidden = false;
                foreach (var view in views)
                {
                    if (view.HiddenCount > 0)
                        hasHidden = true;
                    totalHiddenSegments += view.HiddenCount;
                }

                if (hasHidden)
                    specsWithHidden++;
            }

            Assert.That(specsWithHidden, Is.GreaterThanOrEqualTo(50), "有遮挡（H>0）的语料种子覆盖退化");
            Assert.That(totalHiddenSegments, Is.GreaterThanOrEqualTo(28060), "语料隐藏段总量退化");
        }

        static string Describe(long seed, CoordTier tier) => $"seed={seed} tier={tier}";
    }
}
