using System;
using System.Collections.Generic;
using System.Threading;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    [TestFixture]
    public class ViewArcFitterDiagnosticsTest
    {
        [Test]
        [Explicit("DEFECT-001: ViewArcFitter.DiagnosticsEnabled/LastDiagnostics 非线程隔离，并行测试下串扰")]
        public void DiagnosticsEnabled_IsNotVisibleOnOtherThreads()
        {
            var segments = new[]
            {
                new ViewSegment(new ViewPoint2(0, 0), new ViewPoint2(1, 0), true),
                new ViewSegment(new ViewPoint2(1, 0), new ViewPoint2(2, 0), true),
            };

            ViewArcFitter.DiagnosticsEnabled = true;
            ViewArcFitter.Fit(segments);   // 触发一次写入 LastDiagnostics

            bool? seenOnOtherThread = null;
            var thread = new Thread(() => seenOnOtherThread = ViewArcFitter.DiagnosticsEnabled);
            thread.Start();
            thread.Join();

            try
            {
                // 期望：诊断开关是线程局部状态，另一个线程不应看到它。
                // 实际（DEFECT-001）：普通 static ⇒ 另一线程读到 true，本断言失败。
                Assert.That(seenOnOtherThread, Is.False,
                    "DEFECT-001：另一线程读到了 DiagnosticsEnabled=true，说明它是进程级全局态");
            }
            finally
            {
                ViewArcFitter.DiagnosticsEnabled = false;
            }
        }

        [Test]
        [Explicit("DEFECT-001: ViewArcFitter.DiagnosticsEnabled/LastDiagnostics 非线程隔离，并行测试下串扰")]
        public void LastDiagnostics_IsOverwrittenByAConcurrentFitFromAnotherThread()
        {
            // 并行整套件（4 worker，tests/ParallelRun.runsettings）实跑暴露的串扰方向（2026-10-14）：
            // ViewArcFitterContractTest.Diagnostics_Enabled_RecordsChainSummary 在多 worker 下失败，
            // 它读到的 LastDiagnostics 是另一 worker 的矩形视图（「输入 4 段…首(-15.00,-5.00)
            // 末(15.00,5.00)」），而其自身输入是 8 段 90° 弧。
            // 本用例用两个屏障把该时序钉成确定性复现：
            //   主线程（= 那个开诊断的用例）：开 DiagnosticsEnabled=true → Fit(8 段弧) → 读 LastDiagnostics；
            //   另一线程（= 任意并行投影用例）：什么都不开，仅因进程级静态读到 true → Fit(4 段矩形)
            //   ⇒ 覆写主线程的 LastDiagnostics。
            var ownSegments = ArcSegments(radius: 10, startDeg: 0, sweepDeg: 90, steps: 8);
            var otherSegments = RectSegments(width: 30, height: 10);

            using var barrier = new Barrier(2);
            var timeout = TimeSpan.FromSeconds(20);
            bool? otherThreadSawEnabled = null;
            var otherThreadDone = false;

            var other = new Thread(() =>
            {
                barrier.SignalAndWait(timeout);                 // 等主线程先写完自己的 LastDiagnostics
                otherThreadSawEnabled = ViewArcFitter.DiagnosticsEnabled;
                ViewArcFitter.Fit(otherSegments);               // 串扰写入：覆写 LastDiagnostics
                otherThreadDone = true;
                barrier.SignalAndWait(timeout);                 // 让主线程在其后读取
            });

            try
            {
                ViewArcFitter.DiagnosticsEnabled = true;
                ViewArcFitter.Fit(ownSegments);
                var ownDiagnostics = ViewArcFitter.LastDiagnostics;
                Assert.That(ownDiagnostics, Does.Contain("输入 8 段"), "夹具前提：本线程的 Fit 应写下自己的链统计");

                other.Start();
                barrier.SignalAndWait(timeout);                 // 放行另一线程
                barrier.SignalAndWait(timeout);                 // 等另一线程写完
                other.Join(timeout);
                Assert.That(otherThreadDone, Is.True, "夹具前提：另一线程应完成（屏障/超时未卡死）");

                var afterCrosstalk = ViewArcFitter.LastDiagnostics;

                Assert.Multiple(() =>
                {
                    // 预期：另一线程读不到主线程打开的诊断开关。
                    // 实际（DEFECT-001）：进程级 static ⇒ 读到 true，其 Fit 还会写全局 LastDiagnostics。
                    Assert.That(otherThreadSawEnabled, Is.False,
                        "DEFECT-001(1)：另一线程读到了 DiagnosticsEnabled=true，说明它是进程级全局态");

                    // 预期：主线程读完自己的诊断结果前，LastDiagnostics 不被别的线程改写。
                    // 实际（DEFECT-001）：被另一线程的 Fit 覆写成别的输入的链统计（并行整套件实跑同签名）。
                    Assert.That(afterCrosstalk, Is.EqualTo(ownDiagnostics),
                        "DEFECT-001(2)：主线程的 LastDiagnostics 被另一线程的 Fit 覆写");
                });
            }
            finally
            {
                ViewArcFitter.DiagnosticsEnabled = false;
                other.Join(timeout);   // 断言失败/超时时不遗留后台线程
            }
        }

        /// <summary>圆上 90° 弧折线（steps 段），与 ContractTest 的 Arc(radius, startDeg, sweepDeg, steps) 同构。</summary>
        static ViewSegment[] ArcSegments(double radius, double startDeg, double sweepDeg, int steps)
        {
            var segments = new ViewSegment[steps];
            var start = startDeg * Math.PI / 180.0;
            var step = sweepDeg * Math.PI / 180.0 / steps;
            var points = new List<ViewPoint2>(steps + 1);
            for (var i = 0; i <= steps; i++)
            {
                var angle = start + i * step;
                points.Add(new ViewPoint2(radius * Math.Cos(angle), radius * Math.Sin(angle)));
            }
            for (var i = 0; i < steps; i++)
                segments[i] = new ViewSegment(points[i], points[i + 1], true);
            return segments;
        }

        /// <summary>width×height 矩形环（4 段）——复刻实跑中覆写 LastDiagnostics 的那类矩形视图输入。</summary>
        static ViewSegment[] RectSegments(double width, double height)
        {
            var hw = width / 2;
            var hh = height / 2;
            var corners = new[]
            {
                new ViewPoint2(-hw, -hh),
                new ViewPoint2(hw, -hh),
                new ViewPoint2(hw, hh),
                new ViewPoint2(-hw, hh),
            };
            var segments = new ViewSegment[4];
            for (var i = 0; i < 4; i++)
                segments[i] = new ViewSegment(corners[i], corners[(i + 1) % 4], true);
            return segments;
        }
    }
}
