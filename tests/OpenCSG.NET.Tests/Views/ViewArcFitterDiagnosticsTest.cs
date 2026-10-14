using System;
using System.Collections.Generic;
using System.Threading;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// DEFECT-001 修复回归（2026-10-14；登记于 tests/DEFECTS.md 已修复表）。
    /// <para>
    /// 原缺陷：<c>ViewArcFitter.DiagnosticsEnabled</c>/<c>LastDiagnostics</c> 是进程级静态，
    /// 并行测试互相串扰（4 worker 实跑：契约用例读到另一 worker 的矩形视图链统计）。
    /// 修复：两态改为 <c>[ThreadStatic]</c> 线程局部；本组用例断言线程隔离（修复前为
    /// 钉住错误行为的 <c>[Explicit]</c> 复现）。一行复现：
    /// <c>dotnet test tests/OpenCSG.NET.Tests -c Release --filter "FullyQualifiedName~ViewArcFitterDiagnosticsTest"</c>。
    /// </para>
    /// </summary>
    [TestFixture]
    public class ViewArcFitterDiagnosticsTest
    {
        [Test]
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
                // DEFECT-001 修复回归：诊断开关为线程局部状态，另一线程不应看到它。
                Assert.That(seenOnOtherThread, Is.False,
                    "另一线程不应读到本线程的 DiagnosticsEnabled=true（DEFECT-001 修复后为线程局部态）");
            }
            finally
            {
                ViewArcFitter.DiagnosticsEnabled = false;
            }
        }

        [Test]
        public void LastDiagnostics_IsNotOverwrittenByAConcurrentFitFromAnotherThread()
        {
            // DEFECT-001 的并行串扰方向（2026-10-14 实跑暴露，修复后转为回归）：契约用例
            // ViewArcFitterContractTest.Diagnostics_Enabled_RecordsChainSummary 在多 worker 下失败，
            // 它读到的 LastDiagnostics 是另一 worker 的矩形视图（「输入 4 段…首(-15.00,-5.00)
            // 末(15.00,5.00)」），而其自身输入是 8 段 90° 弧。
            // 本用例用两个屏障把该时序钉成确定性回归：
            //   主线程（= 那个开诊断的用例）：开 DiagnosticsEnabled=true → Fit(8 段弧) → 读 LastDiagnostics；
            //   另一线程（= 任意并行投影用例）：什么都不开 → Fit(4 段矩形)
            //   ⇒ 修复后（线程局部态）：另一线程读不到 true、也覆写不了主线程的 LastDiagnostics。
            // 失败路径硬化（审查 M1）：另一线程的异常存进 otherThreadError（不逃逸终止进程）；
            // finally 仅在确实 Start 过时 Join（前置断言失败时 Join 未启动线程会抛 ThreadStateException 掩盖真因）。
            var ownSegments = ArcSegments(radius: 10, startDeg: 0, sweepDeg: 90, steps: 8);
            var otherSegments = RectSegments(width: 30, height: 10);

            using var barrier = new Barrier(2);
            var timeout = TimeSpan.FromSeconds(20);
            bool? otherThreadSawEnabled = null;
            var otherThreadDone = false;
            Exception otherThreadError = null!;
            var otherStarted = false;

            var other = new Thread(() =>
            {
                try
                {
                    barrier.SignalAndWait(timeout);                 // 等主线程先写完自己的 LastDiagnostics
                    otherThreadSawEnabled = ViewArcFitter.DiagnosticsEnabled;
                    ViewArcFitter.Fit(otherSegments);               // 另一线程并行 Fit（不得产生串扰）
                    otherThreadDone = true;
                    barrier.SignalAndWait(timeout);                 // 让主线程在其后读取
                }
                catch (Exception ex)
                {
                    otherThreadError = ex;   // 线程体未处理异常会终止测试进程 —— 存储后由主线程断言呈现
                }
            });

            try
            {
                ViewArcFitter.DiagnosticsEnabled = true;
                ViewArcFitter.Fit(ownSegments);
                var ownDiagnostics = ViewArcFitter.LastDiagnostics;
                Assert.That(ownDiagnostics, Does.Contain("输入 8 段"), "夹具前提：本线程的 Fit 应写下自己的链统计");

                other.Start();
                otherStarted = true;
                barrier.SignalAndWait(timeout);                 // 放行另一线程
                barrier.SignalAndWait(timeout);                 // 等另一线程写完
                other.Join(timeout);
                Assert.That(otherThreadDone, Is.True,
                    otherThreadError == null
                        ? "夹具前提：另一线程应完成（屏障/超时未卡死）"
                        : $"夹具前提：另一线程未完成（线程体异常已存储）：{otherThreadError}");

                var afterCrosstalk = ViewArcFitter.LastDiagnostics;

                Assert.Multiple(() =>
                {
                    // DEFECT-001 修复回归：另一线程读不到主线程打开的诊断开关。
                    Assert.That(otherThreadSawEnabled, Is.False,
                        "另一线程不应读到 DiagnosticsEnabled=true（线程局部态）");

                    // DEFECT-001 修复回归：主线程的诊断结果不被另一线程的 Fit 覆写。
                    Assert.That(afterCrosstalk, Is.EqualTo(ownDiagnostics),
                        "主线程的 LastDiagnostics 不应被另一线程的 Fit 覆写（线程局部态）");
                });
            }
            finally
            {
                ViewArcFitter.DiagnosticsEnabled = false;
                if (otherStarted)
                    other.Join(timeout);   // 断言失败/超时时不遗留后台线程（未 Start 则不得 Join）
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
