using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// L6 大输入预算（Plan E / Task 1.4 第四、五条）：<see cref="ViewArcFitter.Fit"/> 大样本、
    /// NodeGen 深树求值。输入与 <c>perf/OpenCSG.NET.PerfTest</c> 的 Fit/Evaluate 两项**同源**
    /// （<see cref="PerfWorkloads"/>）；预算 = 实测 × 5，实测值见常量与断言消息。计时协议见
    /// <see cref="BudgetClock"/>（采样 min；单次采样的中位数受堆/机器状态影响可达 1.4–2×）。
    /// CI 环境（<c>CI=true</c>）绝对预算再 ×<see cref="BudgetClock.CiAbsoluteBudgetFactor"/> = 10
    /// （runner 方差：Clean 5k 本机 33.0ms / ubuntu runner 205.1ms，第四跑 run 38516368026）。
    /// </summary>
    [TestFixture]
    [Category("Perf")]
    [NonParallelizable]
    // Plan E / Task 2 归因：本组断言含**墙钟预算**（实测 ×5），而预算口径
    // （BudgetClock 的 min）以「单实例机器的能力下界」为前提；进程内 4 worker 并行整套件时其他
    // worker 同时跑重活，min 被争抢抬高（同批实测：Clean n=5000 超预算 ×1.74）。这是测量环境
    // 属性（非产品缺陷）；Perf 用例改走 NUnit 非并行队列（仍逐条执行、计数不变）。
    public class LargeInputBudgetTest
    {
        // ── Fit（ViewArcFitter）─────────────────────────────────────────────

        /// <summary>Fit 大样本预算（ms）：实测基准 43.0ms（16k 段 × 5 轮） × 5。</summary>
        const double FitBudgetMs = 215.0;

        [Test]
        public void Fit_LargeSegmentSample_Budget()
        {
            var segments = PerfWorkloads.Segments(16000);
            Assert.That(segments.Count, Is.EqualTo(16000));

            // 非空转守卫：识别出的弧/保留折线实测（16000 段 ⇒ 516 弧 / 12613 折线段；
            // 演进：524/12587（修前）→ 523/12589（DEFECT-004 成链取向）→ 516/12613
            //（DEFECT-012 角步离群守卫再拒掉 7 条跨长弦的窗拟合弧））。
            var curves = ViewArcFitter.Fit(segments);
            Assert.That(curves.Arcs.Count, Is.EqualTo(516), $"识别弧数实测 {curves.Arcs.Count}（钉值 516）");
            Assert.That(curves.Segments.Count, Is.EqualTo(12613),
                $"保留折线段实测 {curves.Segments.Count}（钉值 12613）");

            // 计时口径 = 工具侧套件同款（连续 5 轮 Fit），抵单轮 ≈6ms 的噪声。
            var measured = BudgetClock.MinMs(3, 5, () => ViewArcFitter.Fit(segments));
            TestContext.Progress.WriteLine(
                $"[Perf] Fit(16k 段 × 5 轮)：实测 {measured:F1}ms / 生效预算 {BudgetClock.EffectiveBudgetMs(FitBudgetMs):F1}ms（CI={BudgetClock.IsCiRun}）");
            BudgetClock.AssertWithinBudget("ViewArcFitter.Fit（16000 段 × 5 轮）", measured, FitBudgetMs,
                "3 采样 × 每采样 5 轮 Fit，取 min",
                "43.0ms（探针同协议实测中位数；工具套件 Fit 项 min 33.3ms）");
        }

        // ── 深树 Evaluate（CsgEvaluator）────────────────────────────────────

        /// <summary>深树求值预算（ms）：实测基准 88.7ms（8 棵树一趟） × 5。</summary>
        const double DeepTreeEvaluateBudgetMs = 443.5;

        [Test]
        public void DeepTreeEvaluate_Budget()
        {
            var trees = PerfWorkloads.DeepTrees();
            Assert.That(trees.Count, Is.EqualTo(8));

            // 非空转守卫 + 确定性读数：8 棵树（深度 5）求值后的多边形总数。
            // DEFECT-002 修复前实测 Windows 518 / ubuntu 第三跑 517；修复后实测 491（-27）。
            // 差量已探针定位（artifacts/fixround/defect002probe）：tree[6] 的
            // Intersect(54,57,38) 子树几何两两不相交（求值为空集）—— 修复前 ∅∩X 静默返回 X
            // （该结点实测读数 = 第三个操作数自身的 38 面），修复后空集正确传播（0，其余部分
            // 由上游伪多边形消失带来的 ±1 级重剖分差补齐）。属 DEFECT-011 同域（布尔求值的
            // 面数记账在平台 libm ULP 差下 ±1 级浮动）⇒ 钉 488..494（实测 491 两侧各留 3 格
            // ≈ ±0.6%：容纳平台浮动，仍能抓空转 0、树缺失/求值失败与量级级回归）；本用例核心
            // 是下方墙钟预算（保持精确）。
            var polygons = 0;
            foreach (var tree in trees)
                polygons += CsgEvaluator.Evaluate(tree).Polygons.Count;
            Assert.That(polygons, Is.InRange(488, 494),
                $"8 棵深树求值多边形总数实测 {polygons}（DEFECT-002 修复后口径 + DEFECT-011 稳健带 488..494；修复前 Windows 518）");

            // 计时口径 = 工具套件 Evaluate 项同款（一趟 = 依序求值 8 棵树；其中 seed 3/6 各 ≈45ms）。
            var measured = BudgetClock.MinMs(3, 1, () =>
            {
                foreach (var tree in trees)
                    CsgEvaluator.Evaluate(tree);
            });
            TestContext.Progress.WriteLine(
                $"[Perf] Evaluate d5×8：实测 {measured:F1}ms / 生效预算 {BudgetClock.EffectiveBudgetMs(DeepTreeEvaluateBudgetMs):F1}ms（CI={BudgetClock.IsCiRun}）");
            BudgetClock.AssertWithinBudget("NodeGen 深树（d5×8）Evaluate", measured, DeepTreeEvaluateBudgetMs,
                "3 采样 × 1 趟（8 棵树），取 min",
                "88.7ms（探针同协议实测中位数；工具套件 Evaluate 项 min 27.2ms）");
        }
    }
}
