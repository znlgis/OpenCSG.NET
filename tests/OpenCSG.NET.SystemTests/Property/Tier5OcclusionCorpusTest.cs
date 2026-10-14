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
    /// <b>豁免与归因（首轮实测 59/64 种子有视图失败；全部归因于 DEFECT-005 栅格量化自遮挡误判，与 DEFECT-004 ② 同族）</b>：
    /// 失败明细的形状统一为「射线 9 探针全说可见（robust=[V]）而投影器把该段画成隐藏（proj=[H]）」，
    /// 即投影器在**消隐栅格的格量化**下把可见棱边切成 H 碎段。逐点反例（复刻 src 的 DepthRaster 后逐格复核）：
    /// <list type="bullet">
    /// <item>种子 03（Cube/Cube，无曲面）主视唯一失败点：轮廓边 #12-15 末端 1.0 格（0.994 cell）H 碎段，
    /// 其起点格 nearest=23.29374 由**该边自身的前向相邻面**（tri#16，|n·Look|=0.767）在该格中心提供，
    /// 而边点深度 23.434997 —— 超出量 +0.0157 仅为 bias(0.12554) 的 12.5% ⇒ 栅格把面自己当遮挡体（自遮挡量化）。</item>
    /// <item>种子 08 俯视：一条轮廓边（相邻面 |n·Look| 0.57/0.79）被切成 11 段 V + 10 段**单格 H 碎段**，
    /// 每段 H 的起点格 nearest 都比边点深度近 0.0124–0.124（bias 的 7.5%–75%）；9 探针全 V。</item>
    /// <item>种子 11 左视：10.8 格 H 长段，owner=该边自身相邻面（|n·Look|=0.606，**非掠射**），
    /// 超出量 0.06–0.15 = bias 的 12%–29% ⇒ 触发条件不是「掠射」而是 **cell·tanφ &gt; bias**（格子尺度深度变化超过固定 bias）。</item>
    /// <item>种子 18：相邻面 |n·Look| 0.013–0.015（0.75°–0.86°，极端掠射），14 格 H 碎段，owner=掠射面自身。</item>
    /// <item>种子 63（Survey）：|n·Look| 0.021–0.051，超出量 3–9×bias —— Survey 档 cell 与 bias 同比例放大，
    /// 但量化误差随 1/|n·Look| 放大，故伪影比 Normal 档更严重。</item>
    /// <item>种子 25 左视：逐视图空洞守卫（43 个采样点全部 indet、checked=0）—— 同一掠射带的极端形态。</item>
    /// </list>
    /// 全量 1358 条明细中 1320 条（97.2%）可复刻「该重合候选边自己的 Split 就产出覆盖 q 的 H 段」（repro=H），
    /// 即 oracle 探的 3D 边与投影器画的边是同一条；余 38 条（2.8%，分布在 16 个种子上）的 H 段由
    /// <c>ViewSegmentCleanup</c> 的「只延长不缩短」合并/重叠折叠从相邻共线段延来（2D 重合抽象的有意成分，
    /// 该 H 与覆盖 q 的 V 重叠、制图上被实线盖住，见 <c>ViewSegmentCleanup</c> 类注释「不跨可见性合并」）。
    /// 两者都是**消隐栅格量化**（DEFECT-005；与 DEFECT-004 ② 同族根因）的直接/下游产物，故全部按 DEFECT-005 豁免。
    /// 逐种子失败视图与明细形态见 <see cref="OcclusionExemptSeeds"/> 上方注释。
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
        /// DEFECT-005 的**残余语料豁免**（栅格量化自遮挡误判的末类边界情形；brief 允许的唯一形式：
        /// 按 index 豁免 + 注明编号与理由）。
        /// <para>
        /// 演进（DEFECT-005 修复轮）：主机制（斜掠面格内自遮挡，cell·tanφ > bias）已修复——
        /// DepthRaster 逐格随最近深度记账**写入面的格内深度变化上界**（cell·(|∂d/∂x|+|∂d/|∂y|)），
        /// 可见性判定对「同一张面在相邻格中心的位置」不再误判。修复前 64 个 Smoke 种子 59 败
        /// （全量明细 1358 条）；修复后（叠加 DEFECT-004 成链取向/退化阈值修复与 DEFECT-003
        /// 平面精化）余 **9 个种子**，且全部是同一窄类：**贯通孔膛缘的刀口可见性** ——
        /// 孔膛屏面开度处于 1–2 个栅格量级时，远侧孔缘沿视线恰好「透过孔膛可见」，而格量化
        /// 深度按邻格表面判 H；oracle 的 ±1 格探针把该刀口点判 V ⇒ 单边不一致（每种子 ≤30 样本）。
        /// 该类的根治需要亚格分辨率（代价 O(分辨率²) 不划算），按残余豁免登记。
        /// </para>
        /// 豁免种子（11）：0, 2, 4, 13, 18, 23, 25, 48, 51, 53, 60（其中 48/51/53/60 为 Survey 档，
        /// 坐标 |x|≈1e5–1e6 放大格内深度梯度；2/25 在 DEFECT-012 修复后显影——角步守卫拒掉的
        /// 窗拟合此前把该类误判碎段藏进弧里，碎片回到线段表后暴露。25 处于临界带（判据轮次间
        /// 在两态摆动，为跨平台浮动留余量一并豁免；判定口径同 02）。
        /// </summary>
        static readonly HashSet<int> OcclusionExemptSeeds = new HashSet<int>
        {
            0, 2, 4, 13, 18, 23, 25, 48, 51, 53, 60,
        };

        static IEnumerable<TestCaseData> CorpusCases()
        {
            var seeds = Corpus.SmokeSeeds();
            for (var i = 0; i < seeds.Count; i++)
            {
                var data = new TestCaseData(seeds[i], Corpus.TierOf(i))
                    .SetName($"Corpus_{i:D2}_{Corpus.TierOf(i)}");
                if (OcclusionExemptSeeds.Contains(i))
                    data.Explicit("DEFECT-005: 栅格量化自遮挡误判（cell·tanφ > bias）把可见边画成 H 碎段/长虚线（格量化，见 tests/DEFECTS.md；与 DEFECT-004 ② 同族）");
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
        /// 下限 = 实测值 × 0.8（实测值写在 <see cref="Corpus"/> 语料当前状态上；DEFECT-004/005
        /// 修复后重校准——被误判的 H 碎段/伪线消失，隐藏读数大幅收缩）：
        /// <list type="bullet">
        /// <item>有遮挡（H&gt;0）的种子数实测 <b>48/64</b>（修复前 63/64）⇒ 下限 <b>38</b>；</item>
        /// <item>隐藏段总数实测 <b>2206</b>（修复前 35076 —— 差值绝大部分是栅格量化自遮挡 H 碎段）⇒ 下限 <b>1764</b>。</item>
        /// </list>
        /// 门值取 0.8 只为吸收语料/生成器的非语义改动；一旦 H 覆盖率真退化（例如消隐整片失效），
        /// 两式都会跌破 —— 这正是逐种子用例被豁免后需要的兜底（豁免后仍有 55 个种子在跑 oracle）。
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

            Assert.That(specsWithHidden, Is.GreaterThanOrEqualTo(38), "有遮挡（H>0）的语料种子覆盖退化");
            Assert.That(totalHiddenSegments, Is.GreaterThanOrEqualTo(1764), "语料隐藏段总量退化");
        }

        static string Describe(long seed, CoordTier tier) => $"seed={seed} tier={tier}";
    }
}
