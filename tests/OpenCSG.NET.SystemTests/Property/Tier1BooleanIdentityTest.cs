using System;
using System.Collections.Generic;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// Tier 1：精确代数恒等式。对任意随机实体对都必须成立，不需要任何 golden 数据。
    /// 这是整个属性测试体系里最强的一层 —— 一旦某条恒等式被破坏，
    /// 说明布尔内核真的算错了（体积缺失或重复计数）。
    /// </summary>
    [TestFixture]
    public class Tier1BooleanIdentityTest
    {
        const double Tol = 1e-6;

        static IEnumerable<TestCaseData> SmokeCases()
        {
            var seeds = Corpus.SmokeSeeds();
            for (var i = 0; i < seeds.Count; i++)
                yield return new TestCaseData(seeds[i], Corpus.TierOf(i)).SetName($"Smoke_{i:D2}_{Corpus.TierOf(i)}");
        }

        // ---- DEFECT-003 语料豁免（**已清空**，2026-10-14 修复轮）----
        // 原缺陷：Survey 档（|坐标| ≈ 1e6）下 Union 损坏几何（重合面重复保留/丢失，体积最大偏差 47%）。
        // 根因：Plane.Transform 用「平面三点重建」在 ~1e6 平移下发生灾难性抵消（W 漂移达 5e-5 >
        // Plane.EPSILON 1e-5 ⇒ 共面判定失效）；修复为精确仿射映射 (A⁻ᵀ·n, W + n·t)。
        // 原豁免集合（InclusionExclusion 16 个 / UnionSelf 19 个）修复后全部通过 ⇒ 集合清空，
        // [Explicit] 标注随之移除（原集合清单与逐种子分析留档于修复轮报告与 DEFECTS.md 行）。
        static readonly HashSet<int> InclusionExclusionDefectSeeds = new HashSet<int>();

        static readonly HashSet<int> UnionSelfDefectSeeds = new HashSet<int>();

        static IEnumerable<TestCaseData> CasesWithDefectExclusions(HashSet<int> defectSeeds)
        {
            var seeds = Corpus.SmokeSeeds();
            for (var i = 0; i < seeds.Count; i++)
            {
                var data = new TestCaseData(seeds[i], Corpus.TierOf(i)).SetName($"Smoke_{i:D2}_{Corpus.TierOf(i)}");
                if (defectSeeds.Contains(i))
                    data.Explicit("DEFECT-003: Survey 档 Union 在 |坐标|≈1e6 时重复保留/丢失重合面（见 tests/DEFECTS.md）");
                yield return data;
            }
        }

        static IEnumerable<TestCaseData> InclusionExclusionCases() => CasesWithDefectExclusions(InclusionExclusionDefectSeeds);

        [TestCaseSource(nameof(InclusionExclusionCases))]
        [Category("Smoke")]
        public void InclusionExclusion(long seed, CoordTier tier)
        {
            var (specA, specB) = SolidGen.RandomPair(seed, tier);
            var a = SolidGen.Materialize(specA);
            var b = SolidGen.Materialize(specB);

            IdentityAssert.AssertInclusionExclusion(a, b, Tol, Describe(seed, tier));
            // 面积版无参考点（不受 T 顶点接缝漂移影响），补在体积版之后：
            // 体积版靠自校准漂移预算放宽容差，修正轮 1 里 7 个 DEFECT-003 种子被吞掉；
            // 面积口径独立接住其中 6 个（第 7 个 seed 53 两个口径都接不住，见 tests/DEFECTS.md）。
            IdentityAssert.AssertAreaInclusionExclusion(a, b, Tol, Describe(seed, tier));
        }

        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        public void DifferenceIdentity(long seed, CoordTier tier)
        {
            var (specA, specB) = SolidGen.RandomPair(seed, tier);
            var a = SolidGen.Materialize(specA);
            var b = SolidGen.Materialize(specB);

            IdentityAssert.AssertDifferenceIdentity(a, b, Tol, Describe(seed, tier));
        }

        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        public void Monotonicity(long seed, CoordTier tier)
        {
            var (specA, specB) = SolidGen.RandomPair(seed, tier);
            var a = SolidGen.Materialize(specA);
            var b = SolidGen.Materialize(specB);

            IdentityAssert.AssertBooleanMonotonicity(a, b, Tol, Describe(seed, tier));
        }

        /// <summary>
        /// FsCheck 驱动的 Tier 1：在**固定语料之外**探索输入，并让 FsCheck 把失败**收缩**成最小的参数元组。
        /// 设计要点：不用 FsCheck 去生成 `SolidSpec`（那要写 2.x 版的 Arbitrary/Gen 组合子，而 Task 1 的 spike
        /// 只验证过「`[Property]` + 基本类型参数」这一种形状），而是让它生成**几个整数**，
        /// 再由 <see cref="SpecFrom"/> 确定性地映射成两个 spec。
        /// 这样收缩作用在整数上 ⇒ 最小反例就是四个能读懂的数，而不是一个无法解释的网格。
        /// <para>
        /// 本属性断言的是**面积**包含排斥恒等式而**不是体积**的 —— 修正轮 3 的有意替换，不是删断言：
        /// 体积用散度定理，必须选一个积分参考点；库的布尔输出带 T 顶点接缝时读数会随参考点漂移
        /// （实测斜率 ~1e-5/单位）—— Task 7 为此引入公共参考点，其后（修正轮 1）又补了「自校准漂移预算」。
        /// 但该预算测的是同一实体在不同参考点上的读数差，是拿被检验的输出自己校准出来的，
        /// 对**梯度 ~0** 的残差按构造看不见：实测一个 7.34e-6 的残差（梯度 5e-11）就这样穿过预算，
        /// 约 6% 的随机运行中招（见 `tests/DEFECTS.md` DEFECT-003）。而体积 oracle 的真实偏差
        /// （相对 ≥ 2.8e-9）与它的数值残差（绝对可达 ~7.3e-6）之间不存在能把两者分开的容差 ——
        /// 没有任何容差让它在此同时灵敏且稳定。面积**不需要参考点**：它是 |边×边|/2 的求和，
        /// 平移不变、与三角剖分方式无关 ⇒ T 顶点接缝（重剖分）不扰动它，而重复面会加进自身面积、
        /// 丢失面会丢掉自身面积。因此本属性只断言面积恒等式；体积恒等式保留在语料驱动的
        /// <see cref="InclusionExclusion"/> 上 —— 那里的 16 个豁免是按体积失败逐种子推导并验证过的，
        /// 换口径会作废那份验证，故其两条断言都不动。逐种子覆盖与残留盲区（seed 53）见 `tests/DEFECTS.md`。
        /// </para>
        /// </summary>
        [FsCheck.NUnit.Property(MaxTest = 200)]
        public void InclusionExclusion_OnFsCheckGeneratedSpecs(
            int sizeSeed, int offsetSeed, int rotationSeed, byte kindSeed)
        {
            var specA = SpecFrom(sizeSeed, offsetSeed, rotationSeed, kindSeed, 0);
            var specB = SpecFrom(sizeSeed, offsetSeed, rotationSeed, kindSeed, 1);

            var a = SolidGen.Materialize(specA);
            var b = SolidGen.Materialize(specB);

            var message = $"FsCheck 反例：sizeSeed={sizeSeed} offsetSeed={offsetSeed} rotationSeed={rotationSeed} kindSeed={kindSeed}";
            // 面积口径无参考点 ⇒ 无接缝漂移，也就不需要「自校准漂移预算」那种给损坏开后门的补偿（理由见上方 XML doc）。
            IdentityAssert.AssertAreaInclusionExclusion(a, b, Tol, message);
        }

        /// <summary>把几个整数确定性地映射成一个 spec（`variant` 区分 A/B 两侧）。</summary>
        static SolidSpec SpecFrom(int sizeSeed, int offsetSeed, int rotationSeed, byte kindSeed, int variant)
        {
            var baseSpec = SolidGen.Random((long)sizeSeed * 31 + variant, CoordTier.Normal);
            var rng = new SplitMix64((ulong)offsetSeed * 0x9E3779B97F4A7C15UL + (ulong)variant);

            return baseSpec with
            {
                Kind = (PrimitiveKind)(kindSeed % 6),
                // 偏移按 baseSpec 的尺寸缩放，使 B 既可能相交也可能分离；range 取 ±2× 尺寸量级
                OffsetX = rng.NextSigned() * Math.Max(baseSpec.SizeX, 1.0) * 2.0,
                OffsetY = rng.NextSigned() * Math.Max(baseSpec.SizeY, 1.0) * 2.0,
                OffsetZ = rng.NextSigned() * Math.Max(baseSpec.SizeZ, 1.0) * 2.0,
                RotateX = (rotationSeed % 360),
                RotateY = (rotationSeed / 360 % 360),
                RotateZ = (rotationSeed / 129600 % 360),
            };
        }

        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        public void Commutativity(long seed, CoordTier tier)
        {
            var (specA, specB) = SolidGen.RandomPair(seed, tier);
            var a = SolidGen.Materialize(specA);
            var b = SolidGen.Materialize(specB);

            var unionAb = a.Union(b);
            var unionBa = b.Union(a);

            // 容差与 IdentityAssert 同口径：按体积量级缩放（max(Tol, 1e-9·V)）。
            // 绝对 1e-6 在 Survey 档体积 ~1e9 时不可达（1e-6/1.49e9 ≈ 6.7e-16，低于 double 精度）；
            // 实测唯一失败 seed=41 的相对偏差 4.8e-11，远在 1e-9 相对预算之内。
            // 两个并集是两次不同顺序的布尔计算（合法但网格划分不同），比较它们必须用相对预算。
            var volumeAb = GeometryAssert.SignedVolume(unionAb);
            var volumeBa = GeometryAssert.SignedVolume(unionBa);
            Assert.That(volumeBa,
                Is.EqualTo(volumeAb).Within(UnionSlack(volumeAb, volumeBa)),
                $"{Describe(seed, tier)}: ∪ 不可交换");

            var intersectAb = a.Intersect(b);
            var intersectBa = b.Intersect(a);

            var volumeIAb = GeometryAssert.SignedVolume(intersectAb);
            var volumeIBa = GeometryAssert.SignedVolume(intersectBa);
            Assert.That(volumeIBa,
                Is.EqualTo(volumeIAb).Within(UnionSlack(volumeIAb, volumeIBa)),
                $"{Describe(seed, tier)}: ∩ 不可交换");
        }

        static double UnionSlack(double left, double right)
            => Math.Max(Tol, IdentityAssert.RelativeTolerance * Math.Max(Math.Abs(left), Math.Abs(right)));

        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        public void BooleanDoesNotMutateOperands(long seed, CoordTier tier)
        {
            var (specA, specB) = SolidGen.RandomPair(seed, tier);
            var a = SolidGen.Materialize(specA);
            var b = SolidGen.Materialize(specB);

            IdentityAssert.AssertUnchangedByBoolean(a, b, Describe(seed, tier));
        }

        /// <summary>
        /// 自反性。A ∪ A 与 A ∩ A / A − A **分开成两个方法**：DEFECT-003 只让 A ∪ A 在 Survey 档
        /// 19/24 个种子上失败，A ∩ A = A 与 A − A = ∅ 在全部 64 个种子上通过 ——
        /// 混在一起会让这两条仍然有效的断言连坐失去保护（与下面空操作数用例同样的拆分理由）。
        /// </summary>
        [TestCaseSource(nameof(UnionSelfCases))]
        [Category("Smoke")]
        public void Idempotence_UnionSelf(long seed, CoordTier tier)
        {
            var (specA, _) = SolidGen.RandomPair(seed, tier);
            var a = SolidGen.Materialize(specA);
            var volumeA = GeometryAssert.SignedVolume(a);

            // 自反：A ∪ A = A
            Assert.That(GeometryAssert.SignedVolume(a.Union(a)),
                Is.EqualTo(volumeA).Within(SelfIdentitySlack(volumeA)),
                $"{Describe(seed, tier)}: A ∪ A ≠ A");
        }

        static IEnumerable<TestCaseData> UnionSelfCases() => CasesWithDefectExclusions(UnionSelfDefectSeeds);

        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        public void Idempotence_IntersectAndSubtract(long seed, CoordTier tier)
        {
            var (specA, _) = SolidGen.RandomPair(seed, tier);
            var a = SolidGen.Materialize(specA);
            var volumeA = GeometryAssert.SignedVolume(a);

            // 自反：A ∩ A = A、A − A = ∅（不涉及空操作数）
            Assert.That(GeometryAssert.SignedVolume(a.Intersect(a)),
                Is.EqualTo(volumeA).Within(SelfIdentitySlack(volumeA)),
                $"{Describe(seed, tier)}: A ∩ A ≠ A");

            Assert.That(GeometryAssert.SignedVolume(a.Subtract(a)),
                Is.EqualTo(0.0).Within(SelfIdentitySlack(volumeA)),
                $"{Describe(seed, tier)}: A − A ≠ ∅");
        }

        /// <summary>
        /// 自反比较的容差与 IdentityAssert 同口径：按体积量级缩放 max(Tol, 1e-9·V)。
        /// Survey 档绝对 1e-6 不可达 —— 体积 ~7e6 时 double 求和本身的量级就是 ~1e-6，
        /// 实测 seed=56 的 A∩A 相对偏差 4.2e-12（3.04e-5 绝对），远在 1e-9 相对预算之内，
        /// 若留在绝对 1e-6 上失败，暴露的是「绝对容差在测绘坐标下无意义」而非内核错误。
        /// </summary>
        static double SelfIdentitySlack(double volume) => Math.Max(Tol, IdentityAssert.RelativeTolerance * Math.Abs(volume));

        /// <summary>
        /// 空集是 ∪ 的单位元、∩ 的零元。
        /// **与上面的自反性质分开的独立用例**：修复前空操作数会命中 DEFECT-002（`empty.Subtract(X)` 返回翻转绕序的 X；
        /// `empty.Intersect(X)` 与 `X.Intersect(empty)` 都返回 X —— 见 `tests/DEFECTS.md`），
        /// 故当时整组标 `[Explicit]` 排除；DEFECT-002 已修复（`Solid.Union`/`SubtractSub`/`IntersectSub`
        /// 空操作数短路），本用例转正为常绿回归。
        /// "≈ A" 两条用 <see cref="SelfIdentitySlack"/>：`Union`/`Subtract` 结果必过
        /// 重剖分+规范化，Survey 档该管线本身的求和噪声远超绝对 1e-6（与自反用例同因），
        /// 但语义错误（如 ∅−X 返回翻转 X、X∩∅ 返回 X）都是整体量级偏差，尺度化容差照样接住。
        /// </summary>
        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        public void IdentityElement_WithEmptyOperand(long seed, CoordTier tier)
        {
            var (specA, specB) = SolidGen.RandomPair(seed, tier);
            var a = SolidGen.Materialize(specA);
            var b = SolidGen.Materialize(specB);
            var empty = new Solid();

            var volumeA = GeometryAssert.SignedVolume(a);
            Assert.That(GeometryAssert.SignedVolume(a.Union(empty)),
                Is.EqualTo(volumeA).Within(SelfIdentitySlack(volumeA)),
                $"{Describe(seed, tier)}: A ∪ ∅ ≠ A");
            Assert.That(GeometryAssert.SignedVolume(a.Subtract(empty)),
                Is.EqualTo(volumeA).Within(SelfIdentitySlack(volumeA)),
                $"{Describe(seed, tier)}: A − ∅ ≠ A");
            Assert.That(GeometryAssert.SignedVolume(a.Intersect(empty)),
                Is.EqualTo(0.0).Within(Tol),
                $"{Describe(seed, tier)}: A ∩ ∅ ≠ ∅");
            Assert.That(GeometryAssert.SignedVolume(b.Intersect(empty)),
                Is.EqualTo(0.0).Within(Tol),
                $"{Describe(seed, tier)}: B ∩ ∅ ≠ ∅");
        }

        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        public void ResultsAreOrientedConsistently(long seed, CoordTier tier)
        {
            var (specA, specB) = SolidGen.RandomPair(seed, tier);
            var a = SolidGen.Materialize(specA);
            var b = SolidGen.Materialize(specB);

            // 球面参与布尔会产生 T 顶点接缝（库内无 T 顶点清理），
            // 所以用允许接缝的 AssertOrientationConsistent，而不是严格封闭断言。
            GeometryAssert.AssertOrientationConsistent(a.Union(b), $"{Describe(seed, tier)} A∪B");
            GeometryAssert.AssertOrientationConsistent(a.Subtract(b), $"{Describe(seed, tier)} A−B");
            GeometryAssert.AssertOrientationConsistent(a.Intersect(b), $"{Describe(seed, tier)} A∩B");
        }

        static string Describe(long seed, CoordTier tier) => $"seed={seed} tier={tier}";
    }
}
