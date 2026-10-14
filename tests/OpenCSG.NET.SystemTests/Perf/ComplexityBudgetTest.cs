using System;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// L6 复杂度基线的**测试侧复核**（Plan E / Task 1.3 与 1.4 的第三、六条）：
    /// 与 <c>perf/OpenCSG.NET.PerfTest --complexity</c> **同源输入**（<see cref="PerfWorkloads"/>），
    /// 预算 = 实测 × 5（工具侧 ×4），实测值写在常量注释与断言消息里。
    /// <para>
    /// 计时协议见 <see cref="BudgetClock"/>（采样 min）；比值口径同样用 min/min
    /// （单次采样的中位数会被堆状态/机器状态抬高 1.4–2×，比值会从 ≈17 漂到 ≈25）。
    /// </para>
    /// <para>
    /// CI 环境（<c>CI=true</c>）绝对预算再 ×<see cref="BudgetClock.CiAbsoluteBudgetFactor"/> = 10
    /// （runner 方差实例：Clean n=5000 本机 33.0ms / ubuntu runner 205.1ms，第四跑 run 38516368026）；
    /// **比值守卫不乘系数** —— 见下方 O(n²) 判定处注释（机器无关不变量，两平台保持紧）。
    /// </para>
    /// </summary>
    [TestFixture]
    [Category("Perf")]
    [NonParallelizable]
    // Plan E / Task 2 归因：本组断言含**墙钟预算**（实测 ×5），而预算口径
    // （BudgetClock 的 min）以「单实例机器的能力下界」为前提；进程内 4 worker 并行整套件时其他
    // worker 同时跑重活，min 被争抢抬高——实测（2026-10-14，4 worker runsettings）本类 Clean
    // n=5000 的 min 33.0ms → 287.9ms（×8.7），超预算 ×1.74 判失败。这是测量环境属性（非产品
    // 缺陷）；Perf 用例改走 NUnit 非并行队列（仍逐条执行、计数不变）。
    public class ComplexityBudgetTest
    {
        // ── Clean n=5k/20k ──────────────────────────────────────────────────

        /// <summary>Clean n=5000 预算（ms）：实测基准 33.0ms × 5。</summary>
        const double CleanBudget5kMs = 165.0;

        /// <summary>Clean n=20000 预算（ms）：实测基准 674.1ms × 5。</summary>
        const double CleanBudget20kMs = 3370.5;

        [Test]
        public void Clean_LargeInput_BudgetAndQuadraticGuard()
        {
            var segments5k = PerfWorkloads.Segments(5000);
            var segments20k = PerfWorkloads.Segments(20000);
            var bounds5k = ViewSegGen.BoundsOf(segments5k);
            var bounds20k = ViewSegGen.BoundsOf(segments20k);

            // 非空转守卫：结果段数实测 2150（5k）/ 8831（20k）—— 输入构造失效时会整体空转。
            var result5k = ViewSegmentCleanup.Clean(segments5k, bounds5k);
            var result20k = ViewSegmentCleanup.Clean(segments20k, bounds20k);
            Assert.That(result5k.Count, Is.EqualTo(2150), $"Clean(5k) 输出段数实测 {result5k.Count}（钉值 2150）");
            Assert.That(result20k.Count, Is.EqualTo(8831), $"Clean(20k) 输出段数实测 {result20k.Count}（钉值 8831）");

            var measured5k = BudgetClock.MinMs(3, 1, () => ViewSegmentCleanup.Clean(segments5k, bounds5k));
            var measured20k = BudgetClock.MinMs(3, 1, () => ViewSegmentCleanup.Clean(segments20k, bounds20k));
            TestContext.Progress.WriteLine(
                $"[Perf] Clean 5k={measured5k:F1}ms/{BudgetClock.EffectiveBudgetMs(CleanBudget5kMs):F1}ms，"
                + $"20k={measured20k:F1}ms/{BudgetClock.EffectiveBudgetMs(CleanBudget20kMs):F1}ms，"
                + $"比值 {measured20k / measured5k:F2}（CI={BudgetClock.IsCiRun}）");

            BudgetClock.AssertWithinBudget("Clean n=5000", measured5k, CleanBudget5kMs,
                "3 采样 × 1 次调用，取 min", "33.0ms（工具 --complexity 实测 min，2026-10-14）");
            BudgetClock.AssertWithinBudget("Clean n=20000", measured20k, CleanBudget20kMs,
                "3 采样 × 1 次调用，取 min", "674.1ms（探针同协议实测中位数；工具侧 min 567.0ms）");

            // O(n²) 判定：理论 20k/5k ≈ 16×（Clean 的折叠步骤按对比较），实测 17.0–17.6（同机同输入）；
            // 关闭动态 PGO 的复测（失真配置）实测 24.54 ⇒ 上限 32 的余量 ≈ 32 ÷ 24.54 ≈ 1.3×。
            // <32 是**宽界**：抓「忽然多项式升级」——n^2.5 级 ⇒ 4^2.5 = 32（恰为上限、按 < 判定即失败）、
            // n^3 级 ⇒ 4^3 = 64；不是 1.5× 级抖动。**本守卫是机器无关不变量：CI 环境不乘
            // BudgetClock.CiAbsoluteBudgetFactor，两个平台都保持紧** —— 绝对预算随 runner 放宽（挂起
            // 探测器），复杂度升级由本比值（与本机回归信号）承担；任何情况下不得调大比值界。
            var ratio = measured20k / measured5k;
            Assert.That(ratio, Is.LessThan(32.0),
                $"Clean 20k/5k 耗时比实测 {ratio:F2}（min/min；理论 O(n²) ≈16，实测正常域 17.0–17.6，"
                + "PGO 关闭复测 24.54/余量 ≈1.3×）"
                + "—— 达到 32 意味着出现多项式升级或输入规模漂移，须排查（不得放宽上限）");
        }

        // ── Project 900² 栅格 ───────────────────────────────────────────────

        /// <summary>Project 5000 三角形预算（ms）：实测基准 214.7ms × 5。</summary>
        const double ProjectBudget5kMs = 1073.5;

        /// <summary>Project 20000 三角形预算（ms）：实测基准 961.9ms × 5。</summary>
        const double ProjectBudget20kMs = 4809.5;

        [Test]
        public void Project_DepthRaster900_Budget()
        {
            // 圆柱长 900 / 直径 900 ⇒ 三视图屏面跨度均 ≈900mm ⇒ DepthRaster ≈ 900×900/视图
            // （900² × 3 视图 = 2700 格列；RasterResolution=900 为库常量，见 ViewProjector）。
            var mesh5k = PerfWorkloads.ProjectMesh(PerfWorkloads.ProjectSmallTriangles);
            var mesh20k = PerfWorkloads.ProjectMesh(PerfWorkloads.ProjectLargeTriangles);

            // 非空转守卫：三视图输出段数/弧数实测（5k: 123/0/123 段、0/1/0 弧；20k: 226/0/226、0/1/0）。
            var views5k = new ViewProjector(mesh5k).Project();
            Assert.That(views5k.Count, Is.EqualTo(3), "三视图数量");
            Assert.That(views5k[0].Segments.Count + views5k[1].Segments.Count + views5k[2].Segments.Count,
                Is.EqualTo(246), "5k 网格三视图总段数实测 246（钉值）");
            Assert.That(views5k[1].Arcs.Count, Is.EqualTo(1), "5k 网格俯视 1 条弧（轴向视图，钉值）");

            var measured5k = BudgetClock.MinMs(3, 1, () => new ViewProjector(mesh5k).Project());
            var measured20k = BudgetClock.MinMs(3, 1, () => new ViewProjector(mesh20k).Project());
            TestContext.Progress.WriteLine(
                $"[Perf] Project 5k={measured5k:F1}ms/{BudgetClock.EffectiveBudgetMs(ProjectBudget5kMs):F1}ms，"
                + $"20k={measured20k:F1}ms/{BudgetClock.EffectiveBudgetMs(ProjectBudget20kMs):F1}ms（CI={BudgetClock.IsCiRun}）");

            BudgetClock.AssertWithinBudget("Project 5000 三角形（三视图 900² 栅格）", measured5k, ProjectBudget5kMs,
                "3 采样 × 1 次 Project（含拓扑焊接），取 min", "214.7ms（工具 --complexity 实测 min，2026-10-14）");
            BudgetClock.AssertWithinBudget("Project 20000 三角形（三视图 900² 栅格）", measured20k, ProjectBudget20kMs,
                "3 采样 × 1 次 Project（含拓扑焊接），取 min", "961.9ms（工具 --complexity 实测 min，2026-10-14）");
        }
    }
}
