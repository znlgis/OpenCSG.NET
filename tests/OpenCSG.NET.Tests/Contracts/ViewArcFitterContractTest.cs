using System;
using System.Collections.Generic;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// ViewArcFitter 五个门限的 ±ε 边界（Plan C / Task 7 Step 3）：
    /// MinRadius(0.5) / MaxRadiusToExtent(5.0) / MinSweepDegrees(10°) / MaxClosingGapDegrees(25°) /
    /// SameCircleRelativeTolerance(1e-3)，各取门限两侧可观察行为（弧数/IsClosed/合并与否）。
    /// 输入用 <see cref="ViewSegGen.ArcSample"/>（TestKit 生成器）；既覆盖分支也钉住契约。
    /// </summary>
    [TestFixture]
    public class ViewArcFitterContractTest
    {
        static IReadOnlyList<ViewSegment> Arc(double radius, double startDeg, double sweepDeg, int steps = 32)
            => ViewSegGen.ArcSample(new ViewPoint2(0, 0), radius, startDeg, sweepDeg, steps, visible: true);

        // —— MinRadius = 0.5（另有随视图对角线的 MinRadiusFraction 项；小视图下退化为常量 0.5）——

        [Test]
        public void MinRadius_JustAbove_RecognizedAsClosedArc()
        {
            var curves = ViewArcFitter.Fit(Arc(0.51, 0, 360));

            Assert.That(curves.Arcs.Count, Is.EqualTo(1), "R=0.51 > MinRadius=0.5 ⇒ 应识别为整圆");
            Assert.That(curves.Arcs[0].Radius, Is.EqualTo(0.51).Within(1e-6));
            Assert.That(curves.Arcs[0].IsClosed, Is.True);
            Assert.That(curves.Segments.Count, Is.EqualTo(0), "识别为弧的段应从折线中移除");
        }

        [Test]
        public void MinRadius_JustBelow_KeptAsSegments()
        {
            var curves = ViewArcFitter.Fit(Arc(0.49, 0, 360));

            Assert.That(curves.Arcs.Count, Is.EqualTo(0), "R=0.49 < MinRadius=0.5 ⇒ 碎片小圈不落 ARC");
            Assert.That(curves.Segments.Count, Is.EqualTo(32), "原折线应全部保留");
        }

        // —— MaxRadiusToExtent = 5.0（曲率显著性：R > 5×链包络对角线 ⇒ 视为直线）——

        [Test]
        public void MaxRadiusToExtent_BoundaryBothSides()
        {
            // R/extent ≈ 1/(2·sin(sweep/2))：11.6° ⇒ ≈4.95（接受）；11.4° ⇒ ≈5.03（拒绝）。
            var accepted = ViewArcFitter.Fit(Arc(100, 0, 11.6, steps: 6));
            Assert.That(accepted.Arcs.Count, Is.EqualTo(1), "R/extent 略低于 5 ⇒ 弧成立");
            Assert.That(accepted.Arcs[0].SweepDegrees, Is.EqualTo(11.6).Within(0.01));

            var rejected = ViewArcFitter.Fit(Arc(100, 0, 11.4, steps: 6));
            Assert.That(rejected.Arcs.Count, Is.EqualTo(0), "R/extent 略高于 5 ⇒ 拒绝为直线链");
            Assert.That(rejected.Segments.Count, Is.EqualTo(6));
        }

        // —— MinSweepDegrees = 10°（Fit 内另有 MaxRadiusToExtent 先行短路；合并侧分支可直达）——

        [Test]
        public void MinSweep_SmallArcChain_NotConvertedToArc()
        {
            // 9° 弧的可观察契约：不落 ARC（本实现里 R/extent≈6.37 先被曲率判据拒绝，
            // Fit 内的 MinSweepDegrees 拒绝分支与其同域、由后者先行短路 —— 可观察行为一致）。
            var curves = ViewArcFitter.Fit(Arc(100, 0, 9.0, steps: 6));
            Assert.That(curves.Arcs.Count, Is.EqualTo(0));
            Assert.That(curves.Segments.Count, Is.EqualTo(6));
        }

        [Test]
        public void MinSweep_MergedSweepBelowMinimum_Dropped()
        {
            // MergeGroup 的 MinSweepDegrees 分支（合并后扫掠 < 10° 的弧直接丢弃）：
            // 4° + 4.5°、缝隙 0.5° ≤ ConcyclicMergeGapDegrees(1°) ⇒ 并成 8.5°，低于门限 ⇒ 丢弃。
            var merged = ViewArcFitter.MergeConcyclic(new[]
            {
                MakeArc(100, startDeg: 0, sweepDeg: 4),
                MakeArc(100, startDeg: 4.5, sweepDeg: 4),
            });
            Assert.That(merged.Count, Is.EqualTo(0), "合并后 8.5° < MinSweepDegrees=10° ⇒ 不输出");
        }

        [Test]
        public void MinSweep_MergedSweepAboveMinimum_Kept()
        {
            var merged = ViewArcFitter.MergeConcyclic(new[]
            {
                MakeArc(100, startDeg: 0, sweepDeg: 5),
                MakeArc(100, startDeg: 5.5, sweepDeg: 5),
            });
            Assert.That(merged.Count, Is.EqualTo(1), "合并后 10.5° ≥ 10° ⇒ 输出单弧");
            Assert.That(merged[0].SweepDegrees, Is.EqualTo(10.5).Within(1e-6));
            Assert.That(merged[0].IsClosed, Is.False);
        }

        // —— MaxClosingGapDegrees = 25°（近闭合补圆；TryBuildArc 的补圆分支）——

        [Test]
        public void ClosingGap_WithinThreshold_CompletesToFullCircle()
        {
            // 350°（缺 10°）与 336°（缺 24°）：缺口 < 25° ⇒ 补成整圆。
            var gap10 = ViewArcFitter.Fit(Arc(50, 0, 350, steps: 36));
            Assert.That(gap10.Arcs.Count, Is.EqualTo(1));
            Assert.That(gap10.Arcs[0].IsClosed, Is.True, "缺 10° 应补成整圆");
            Assert.That(gap10.Arcs[0].SweepDegrees, Is.EqualTo(360.0).Within(1e-9));

            var gap24 = ViewArcFitter.Fit(Arc(50, 0, 336, steps: 36));
            Assert.That(gap24.Arcs.Count, Is.EqualTo(1));
            Assert.That(gap24.Arcs[0].IsClosed, Is.True, "缺 24° 仍应在门限内补齐");
        }

        [Test]
        public void ClosingGap_BeyondThreshold_LeavesOpenArc()
        {
            // 334°（缺 26°）与 300°（缺 60°）：缺口 > 25° ⇒ 保持开口弧，扫掠如实。
            var gap26 = ViewArcFitter.Fit(Arc(50, 0, 334, steps: 36));
            Assert.That(gap26.Arcs.Count, Is.EqualTo(1));
            Assert.That(gap26.Arcs[0].IsClosed, Is.False, "缺 26° 超出 25° 门限 ⇒ 不补圆");
            Assert.That(gap26.Arcs[0].SweepDegrees, Is.EqualTo(334.0).Within(0.01));

            var gap60 = ViewArcFitter.Fit(Arc(50, 0, 300, steps: 36));
            Assert.That(gap60.Arcs.Count, Is.EqualTo(1));
            Assert.That(gap60.Arcs[0].IsClosed, Is.False);
            Assert.That(gap60.Arcs[0].SweepDegrees, Is.EqualTo(300.0).Within(0.01));

            // 备注：恰在 25°（335° 弧）落在 2π−(360°−25°) 与 25°·π/180 的 1 ULP 差上，
            // 本机实测判为「开口」侧；该刀口不钉住（不同平台数学库可能有 1 ULP 摆动）。
        }

        [Test]
        public void FullCircle_EndpointsCoincide_IsClosed()
        {
            var curves = ViewArcFitter.Fit(Arc(50, 0, 360, steps: 36));
            Assert.That(curves.Arcs.Count, Is.EqualTo(1));
            Assert.That(curves.Arcs[0].IsClosed, Is.True);
            Assert.That(curves.Arcs[0].Radius, Is.EqualTo(50.0).Within(1e-6));
            Assert.That(curves.Arcs[0].SweepDegrees, Is.EqualTo(360.0).Within(1e-9));
        }

        // —— SameCircleRelativeTolerance = 1e-3（MergeConcyclic 的 IsSameCircle；scale=100 时相对容差 = 0.1）——

        [Test]
        public void SameCircle_WithinRelativeTolerance_MergedToFullCircle()
        {
            // 半径 100 与 100.09（差 0.09 < 0.1 = 100×1e-3）：同圆 ⇒ 两段 180° 并成整圆。
            var merged = ViewArcFitter.MergeConcyclic(new[]
            {
                MakeArc(100, 0, 180),
                MakeArc(100.09, 180, 180),
            });
            Assert.That(merged.Count, Is.EqualTo(1), "相对容差内应归为同一圆");
            Assert.That(merged[0].IsClosed, Is.True, "两段半圆合并覆盖整圈 ⇒ 补圆");

            // 圆心偏移 0.09 < 0.1：同样视为同圆。
            var offset = ViewArcFitter.MergeConcyclic(new[]
            {
                new ViewArc(new ViewPoint2(0, 0), 100, 0, Math.PI, true, false),
                new ViewArc(new ViewPoint2(0.09, 0), 100, Math.PI, 2 * Math.PI, true, false),
            });
            Assert.That(offset.Count, Is.EqualTo(1), "圆心偏移在相对容差内应归组");
            Assert.That(offset[0].IsClosed, Is.True);
        }

        [Test]
        public void SameCircle_BeyondRelativeTolerance_NotMerged()
        {
            // 半径差 0.11 与圆心距 0.11：均刚超出 0.1 ⇒ 各自成组，保持两段开口弧。
            var radiusDiff = ViewArcFitter.MergeConcyclic(new[]
            {
                MakeArc(100, 0, 180),
                MakeArc(100.11, 180, 180),
            });
            Assert.That(radiusDiff.Count, Is.EqualTo(2), "半径差超出相对容差 ⇒ 不得归为同一圆");
            Assert.That(radiusDiff[0].IsClosed, Is.False);
            Assert.That(radiusDiff[1].IsClosed, Is.False);

            var centerDiff = ViewArcFitter.MergeConcyclic(new[]
            {
                new ViewArc(new ViewPoint2(0, 0), 100, 0, Math.PI, true, false),
                new ViewArc(new ViewPoint2(0.11, 0), 100, Math.PI, 2 * Math.PI, true, false),
            });
            Assert.That(centerDiff.Count, Is.EqualTo(2), "圆心距超出相对容差 ⇒ 不得归组");
        }

        // —— 链诊断（DiagnosticsEnabled 分支；DEFECT-001 已知其为进程级静态，本用例自持自清）——

        [Test]
        [NonParallelizable]
        // [NonParallelizable] 归因 DEFECT-001（不是用例自身的毛病）：DiagnosticsEnabled/LastDiagnostics
        // 是进程级静态，并行整套件下本用例「Fit 返回 → 读 LastDiagnostics」的窗口会被其他 worker 的
        // Fit 覆写。实测证据（2026-10-14，4 worker runsettings 第 2 次跑）：本用例失败，读到的
        // LastDiagnostics 是另一 worker 的「输入 4 段…首(-15.00,-5.00) 末(15.00,5.00)」（矩形视图），
        // 而本用例的输入是 8 段 90° 弧。修复（诊断态线程局部化）前本用例只能在非并行队列里跑。
        public void Diagnostics_Enabled_RecordsChainSummary()
        {
            ViewCurves<ViewSegment> curves;
            try
            {
                ViewArcFitter.DiagnosticsEnabled = true;
                curves = ViewArcFitter.Fit(Arc(10, 0, 90, steps: 8));

                Assert.That(curves.Arcs.Count, Is.EqualTo(1), "夹具前提：8 段 90° 弧应被识别");
                Assert.That(ViewArcFitter.LastDiagnostics, Does.Contain("输入 8 段").And.Contain("链 1 条"),
                    "开启诊断后应记录链统计（DEFECT-001 涉及的静态开关在本用例内即开即关）");
            }
            finally
            {
                ViewArcFitter.DiagnosticsEnabled = false;
            }
        }

        static ViewArc MakeArc(double radius, double startDeg, double sweepDeg)
        {
            var start = startDeg * Math.PI / 180.0;
            var end = (startDeg + sweepDeg) * Math.PI / 180.0;
            return new ViewArc(new ViewPoint2(0, 0), radius, start, end, true, false);
        }
    }
}
