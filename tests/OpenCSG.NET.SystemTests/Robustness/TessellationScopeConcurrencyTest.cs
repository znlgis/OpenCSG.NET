using System;
using System.Collections.Generic;
using System.Threading;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// L7 健壮性：<see cref="Tessellation.ChordTolerance"/> 的 <c>[ThreadStatic]</c> 语义与
    /// <see cref="TessellationScope"/> 的**跨线程**契约（Plan E / Task 2.2）。
    /// <para>
    /// B1 只覆盖了单线程（设置/Dispose 复位/异常路径复位，见 <c>TessellationScopeTest</c>，
    /// 在 Tests 程序集）；本文件补三个并发口径：
    /// ① 并发任务各置不同容差（scoped）⇒ 互不串扰（用屏障把「所有 scope 同时打开」钉成确定时序）；
    /// ② scope 退出恢复先前值 —— 并发语境下的**异常路径**（先前值本身也是线程局部的）；
    /// ③ ThreadStatic 的跨代隔离：父线程 scope 不进子线程、子线程设值不污染父线程。
    /// </para>
    /// 工作机制参照：<see cref="TessellationScope"/> 的类注释（全局态守卫）。
    /// </summary>
    [TestFixture]
    public class TessellationScopeConcurrencyTest
    {
        static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(30);

        [Test]
        public void ConcurrentScopes_EachThreadReadsOnlyItsOwnTolerance()
        {
            var tolerances = new[] { 0.01, 0.05, 0.2, 1.0 };
            var seen = new double[tolerances.Length];
            var errors = new List<Exception>();
            var exitCodes = new int[tolerances.Length];

            using var barrier = new Barrier(tolerances.Length);
            var threads = new Thread[tolerances.Length];

            for (var i = 0; i < tolerances.Length; i++)
            {
                var index = i;
                threads[i] = new Thread(() =>
                {
                    try
                    {
                        using var scope = new TessellationScope(tolerances[index]);
                        barrier.SignalAndWait(JoinTimeout);      // 四个 scope 全部打开后才采样
                        seen[index] = Tessellation.ChordTolerance;
                        barrier.SignalAndWait(JoinTimeout);      // 采样完才退出（保证采样窗口重叠）
                        exitCodes[index] = 1;
                    }
                    catch (Exception ex)
                    {
                        lock (errors) errors.Add(ex);
                    }
                });
                threads[i].Start();
            }

            foreach (var thread in threads)
                Assert.That(thread.Join(JoinTimeout), Is.True, "并发线程不应超时（屏障卡死）");

            Assert.That(errors, Is.Empty, "并发线程不应抛异常");
            Assert.That(exitCodes, Is.All.EqualTo(1), "所有线程都应跨过两个屏障");

            // 主线程从未进入 scope ⇒ 始终是默认 0（并发任务不能污染未参与的线程）。
            Assert.That(Tessellation.ChordTolerance, Is.EqualTo(0.0));

            for (var i = 0; i < tolerances.Length; i++)
                Assert.That(seen[i], Is.EqualTo(tolerances[i]),
                    $"线程 {i}（容差 {tolerances[i]}）读到了别线程的容差 {seen[i]} —— [ThreadStatic] 语义破损");
        }

        [Test]
        public void Scope_OnExceptionPath_RestoresPreviousValue_PerThread()
        {
            // 每个线程：先把「先前值」直接写进自己的线程局部态，再进 scope，scope 内抛异常；
            // 期望 scope 退化（Dispose 在栈展开时运行）恢复到**本线程的**先前值，
            // 且四个线程互不串扰（先前值/内层值逐一对应）。
            var previous = new[] { 0.011, 0.022, 0.033, 0.044 };
            var inner = new[] { 0.9, 0.8, 0.7, 0.6 };
            var innerSeen = new double[previous.Length];
            var restored = new double[previous.Length];
            var errors = new List<Exception>();

            using var barrier = new Barrier(previous.Length);
            var threads = new Thread[previous.Length];

            for (var i = 0; i < previous.Length; i++)
            {
                var index = i;
                threads[i] = new Thread(() =>
                {
                    try
                    {
                        var original = Tessellation.ChordTolerance;   // 新线程上应为 0
                        Tessellation.ChordTolerance = previous[index];
                        try
                        {
                            using (new TessellationScope(inner[index]))
                            {
                                barrier.SignalAndWait(JoinTimeout);   // 四个内层 scope 同时在场
                                innerSeen[index] = Tessellation.ChordTolerance;
                                barrier.SignalAndWait(JoinTimeout);
                                throw new InvalidOperationException("模拟用例体抛异常（并发路径）");
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            // 预期：using 已完成 Dispose（栈展开），落到这里时已复位。
                        }

                        restored[index] = Tessellation.ChordTolerance;
                        Tessellation.ChordTolerance = original;       // 线程退出前归位（防御）
                    }
                    catch (Exception ex)
                    {
                        lock (errors) errors.Add(ex);
                    }
                });
                threads[i].Start();
            }

            foreach (var thread in threads)
                Assert.That(thread.Join(JoinTimeout), Is.True, "并发线程不应超时（屏障卡死）");

            Assert.That(errors, Is.Empty, "并发线程不应抛非预期异常");

            for (var i = 0; i < previous.Length; i++)
            {
                Assert.That(innerSeen[i], Is.EqualTo(inner[i]), $"线程 {i}：scope 内应读到自己的内层值");
                Assert.That(restored[i], Is.EqualTo(previous[i]),
                    $"线程 {i}：异常路径后应恢复自己的先前值 {previous[i]}（读到 {restored[i]}）");
            }
        }

        [Test]
        public void ParentScopeIsNotVisibleOnChildThread_AndChildValueDoesNotPolluteParent()
        {
            double? childInitial = null;
            double? childAfterOwnSet = null;

            using (new TessellationScope(0.05))
            {
                var child = new Thread(() =>
                {
                    childInitial = Tessellation.ChordTolerance;      // 期望 0（父线程的值不可见）
                    Tessellation.ChordTolerance = 0.77;              // 子线程自己设值
                    childAfterOwnSet = Tessellation.ChordTolerance;  // 期望 0.77
                });
                child.Start();
                Assert.That(child.Join(JoinTimeout), Is.True, "子线程不应超时");

                Assert.That(childInitial, Is.EqualTo(0.0),
                    "子线程不应继承父线程的 ChordTolerance（[ThreadStatic] 语义）");
                Assert.That(childAfterOwnSet, Is.EqualTo(0.77));
                Assert.That(Tessellation.ChordTolerance, Is.EqualTo(0.05),
                    "子线程设值不得污染父线程（scope 打开期间父线程仍是 0.05）");
            }

            Assert.That(Tessellation.ChordTolerance, Is.EqualTo(0.0), "父线程 scope 退出后复位");
        }
    }
}
