using System;
using System.Diagnostics;

namespace Csg.Test
{
    /// <summary>
    /// 性能预算用例的计时器（Plan E / L6 边界用例专用）。
    /// <para>
    /// 口径：预热 1 次后取 <paramref name="samples"/> 个采样的**最小值**，每个采样 = 同一操作连续
    /// <paramref name="repeatsPerSample"/> 次（抵单次 &lt;5ms 的计时噪声）。用 min 而非平均值/中位数：
    /// 本机（AMD Ryzen AI 9 HX 370，24 逻辑核，共享开发机）实测同一操作的 5 次采样可见 1.4–2× 的
    /// 整轮状态漂移（电源/调度/后台负载），min 是「该机器的能力下界」，跨运行复现性 ±0.5%（Clean 20k），
    /// 平均值/中位数会把整轮状态当成结果。真实回归会抬高全部采样 ⇒ min 一并抬高，逃不掉。
    /// </para>
    /// <para>
    /// 预算常量一律 = 实测 × 5（Plan E 纪律：先实测、预算 = 实测 × 明确倍数并在注释注明实测值）；
    /// 工具侧（perf/OpenCSG.NET.PerfTest）同源输入用 ×4，测试侧放宽一档抵 CI 抖动。
    /// </para>
    /// </summary>
    internal static class BudgetClock
    {
        internal static double MinMs(int samples, int repeatsPerSample, Action action)
        {
            if (samples < 1) throw new ArgumentOutOfRangeException(nameof(samples));
            if (repeatsPerSample < 1) throw new ArgumentOutOfRangeException(nameof(repeatsPerSample));

            action();   // 预热（JIT + 首次分配）

            var best = double.MaxValue;
            for (var sample = 0; sample < samples; sample++)
            {
                var sw = Stopwatch.StartNew();
                for (var repeat = 0; repeat < repeatsPerSample; repeat++)
                    action();
                sw.Stop();

                if (sw.Elapsed.TotalMilliseconds < best)
                    best = sw.Elapsed.TotalMilliseconds;
            }
            return best;
        }

        /// <summary>预算断言：min 超预算即失败，失败消息带实测值与协议（便于重钉时取证）。</summary>
        internal static void AssertWithinBudget(
            string what, double measuredMs, double budgetMs, string protocol, string measuredBasis)
        {
            NUnit.Framework.Assert.That(measuredMs, NUnit.Framework.Is.LessThanOrEqualTo(budgetMs),
                $"{what}：实测 {measuredMs:F1}ms > 预算 {budgetMs:F1}ms（口径 {protocol}；"
                + $"预算 = 实测基准 × 5，基准 {measuredBasis}）。"
                + "注意：先确认不是机器状态（重跑 / 检查负载）；确属回归再排查代码，不得直接放宽预算。");
        }
    }
}
