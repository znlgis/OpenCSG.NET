using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// L6 大输入预算（Plan E / Task 1.4 第四、五条）：<see cref="ViewArcFitter.Fit"/> 大样本、
    /// NodeGen 深树求值。输入与 <c>perf/OpenCSG.NET.PerfTest</c> 的 Fit/Evaluate 两项**同源**
    /// （<see cref="PerfWorkloads"/>）；预算 = 实测 × 5，实测值见常量与断言消息。计时协议见
    /// <see cref="BudgetClock"/>（采样 min；单次采样的中位数受堆/机器状态影响可达 1.4–2×）。
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

            // 非空转守卫：识别出的弧/保留折线实测（16000 段 ⇒ 524 弧 / 12587 折线段）。
            var curves = ViewArcFitter.Fit(segments);
            Assert.That(curves.Arcs.Count, Is.EqualTo(524), $"识别弧数实测 {curves.Arcs.Count}（钉值 524）");
            Assert.That(curves.Segments.Count, Is.EqualTo(12587),
                $"保留折线段实测 {curves.Segments.Count}（钉值 12587）");

            // 计时口径 = 工具侧套件同款（连续 5 轮 Fit），抵单轮 ≈6ms 的噪声。
            var measured = BudgetClock.MinMs(3, 5, () => ViewArcFitter.Fit(segments));
            TestContext.Progress.WriteLine($"[Perf] Fit(16k 段 × 5 轮)：实测 {measured:F1}ms / 预算 {FitBudgetMs:F1}ms");
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

            // 非空转守卫 + 确定性读数：8 棵树（深度 5）求值后的多边形总数 —— Windows 实测 518、
            // ubuntu 第三跑（run 38514979215）实测 517（DEFECT-011：布尔求值的面数记账在平台 libm
            // ULP 差下 ±1 级浮动，两平台输出同为同一几何的有效近似）。故钉窄带 515..521（两侧各留
            // 3 格 ≈ ±0.6%：容纳平台浮动，仍能抓空转 0、树缺失/求值失败与量级级回归）；本用例核心
            // 是下方墙钟预算（保持精确）。
            var polygons = 0;
            foreach (var tree in trees)
                polygons += CsgEvaluator.Evaluate(tree).Polygons.Count;
            Assert.That(polygons, Is.InRange(515, 521),
                $"8 棵深树求值多边形总数实测 {polygons}（DEFECT-011 稳健带 515..521；Windows 518 / ubuntu 517）");

            // 计时口径 = 工具套件 Evaluate 项同款（一趟 = 依序求值 8 棵树；其中 seed 3/6 各 ≈45ms）。
            var measured = BudgetClock.MinMs(3, 1, () =>
            {
                foreach (var tree in trees)
                    CsgEvaluator.Evaluate(tree);
            });
            TestContext.Progress.WriteLine(
                $"[Perf] Evaluate d5×8：实测 {measured:F1}ms / 预算 {DeepTreeEvaluateBudgetMs:F1}ms");
            BudgetClock.AssertWithinBudget("NodeGen 深树（d5×8）Evaluate", measured, DeepTreeEvaluateBudgetMs,
                "3 采样 × 1 趟（8 棵树），取 min",
                "88.7ms（探针同协议实测中位数；工具套件 Evaluate 项 min 27.2ms）");
        }
    }
}
