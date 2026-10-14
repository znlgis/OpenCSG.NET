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
    /// <para>
    /// <b>CI 绝对预算系数（2026-10-14 第四跑 run 38516368026 后引入）</b>：绝对墙钟预算的定位是
    /// <b>挂起探测器 + 本机回归信号</b>，不是跨机器精度仪器 —— 同一用例（Clean n=5000）在 ubuntu
    /// runner 实测 205.1ms（第四跑，net8.0）vs 本机 min 33.0ms ≈ <b>6.2×</b>；且第三跑
    /// （run 38514979215）同一用例在同一平台 runner 上通过 ⇒ runner 之间还叠加倍数级方差。故 CI
    /// 环境（GitHub Actions 对所有步骤默认注入环境变量 <c>CI=true</c>，跨产品通用约定）绝对预算统一
    /// × <see cref="CiAbsoluteBudgetFactor"/> = 10：覆盖观测平台差 6.2× 后仍留 ≈1.6× runner 方差余量，
    /// 同时挂起/量级级回归（≫10× 于紧预算 ⇒ 本机 165ms 级用例在 CI 需 &gt;1.65s）依旧必红。本机
    /// （无 <c>CI</c> 变量）保持 ×5 紧预算不变。机器无关的判别力由<b>比值守卫</b>承担（如 Clean
    /// 20k/5k &lt; 32）：该族在两个平台都保持紧、<b>不乘 CI 系数</b> —— 任何情况下不得调大比值界。
    /// </para>
    /// </summary>
    internal static class BudgetClock
    {
        /// <summary>CI 绝对预算系数：<c>CI=true</c>（GitHub Actions 默认）时绝对预算 ×10；本机 ×1。理由见类注释。</summary>
        internal const double CiAbsoluteBudgetFactor = 10.0;

        /// <summary>是否 CI 运行环境（GitHub Actions 对所有步骤默认注入 <c>CI=true</c>，跨产品通用约定）。</summary>
        internal static bool IsCiRun => Environment.GetEnvironmentVariable("CI") == "true";

        /// <summary>当前环境生效的绝对预算：常量（实测基准 ×5）× CI 系数。</summary>
        internal static double EffectiveBudgetMs(double budgetMs)
            => budgetMs * (IsCiRun ? CiAbsoluteBudgetFactor : 1.0);

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

        /// <summary>预算断言：min 超**生效预算**（常量 × CI 系数）即失败；失败消息带实测值、系数分解与协议（便于重钉时取证）。</summary>
        internal static void AssertWithinBudget(
            string what, double measuredMs, double budgetMs, string protocol, string measuredBasis)
        {
            var effective = EffectiveBudgetMs(budgetMs);
            var factorNote = IsCiRun
                ? $"CI 环境系数 ×{CiAbsoluteBudgetFactor:F0}（绝对预算 = 挂起探测器 + 本机回归信号；跨机器精度由比值守卫承担）"
                : "本机 ×1";
            NUnit.Framework.Assert.That(measuredMs, NUnit.Framework.Is.LessThanOrEqualTo(effective),
                $"{what}：实测 {measuredMs:F1}ms > 生效预算 {effective:F1}ms（基准 ×5 = {budgetMs:F1}ms，{factorNote}；"
                + $"口径 {protocol}；基准 {measuredBasis}）。"
                + "注意：先确认不是机器状态（重跑 / 检查负载）；确属回归再排查代码，不得直接放宽预算。");
        }
    }
}
