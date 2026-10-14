using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// <see cref="Tessellation.SegmentsForArc"/> 的**自适应分支**回填（Plan F / Task 2）：
    /// 该分支（正容差下的圆弧分段计算）在 Plan F 的「五组 ≥ 90%」门禁下首次暴露为 6 行 0 命中
    /// （Plan C 的类级 ≥50% 口径未触发回填）——入口 <c>CsgEvaluator</c> 的 CapsuleProfile 路径
    /// 只在自适应开启时走这里，而既有用例只覆盖过「未启用自适应 ⇒ 16 段」的旧口径。
    /// <para>
    /// 期望值全部手工推导（公式见 <c>Tessellation.SegmentsForArc</c> 文档）：
    /// full = SegmentsFor(r, tol)（整圆段数）；scaled = ceil(full · min(|sweep|,360)/360)；
    /// 结果 = min(max(scaled, max(4, lo/2)), hi)，lo/hi 缺省 16/128。
    /// </para>
    /// </summary>
    [TestFixture]
    public class TessellationArcSegmentsTest
    {
        // 手推锚点：r=100, tol=0.5 ⇒ half=acos(1−0.005)≈0.100021 rad ⇒ full=ceil(π/half)=32。
        const double R100 = 100.0;
        const double Tol05 = 0.5;

        [Test]
        public void AdaptiveArcSegments_HandComputed_ClampsAndScaling()
        {
            // 整半圆：32 · 0.5 = 16（≥ lower=8，≤ hi=128）
            Assert.That(Tessellation.SegmentsForArc(R100, 180.0, Tol05), Is.EqualTo(16));

            // 整圆：32 · 1 = 32
            Assert.That(Tessellation.SegmentsForArc(R100, 360.0, Tol05), Is.EqualTo(32));

            // 零扫掠：scaled=0 ⇒ 下限 max(4, 16/2)=8
            Assert.That(Tessellation.SegmentsForArc(R100, 0.0, Tol05), Is.EqualTo(8));

            // 负扫掠取绝对值（−90° ⇒ 0.25 ⇒ ceil(8)=8，恰为下限）
            Assert.That(Tessellation.SegmentsForArc(R100, -90.0, Tol05), Is.EqualTo(8));

            // 扫掠 >360° 先夹到 360°（720° ⇒ 与整圆同为 32，而非 64）
            Assert.That(Tessellation.SegmentsForArc(R100, 720.0, Tol05), Is.EqualTo(32));

            // 自定义下限：min=40 ⇒ 下限 max(4, 40/2)=20 > scaled=8
            Assert.That(Tessellation.SegmentsForArc(R100, 90.0, Tol05, min: 40), Is.EqualTo(20));

            // 自定义上限：r=1000 ⇒ full=ceil(π/acos(0.9995))=100，但 max=20 ⇒ 20
            Assert.That(Tessellation.SegmentsForArc(1000.0, 360.0, Tol05, max: 20), Is.EqualTo(20));

            // 对照：容差 0 ⇒ 旧口径 16（既有行为不回归）
            Assert.That(Tessellation.SegmentsForArc(R100, 180.0, tolerance: 0.0),
                Is.EqualTo(Tessellation.LegacyArcSegments));
        }

        [Test]
        public void AdaptiveArcSegments_NullTolerance_FallsBackToThreadScope()
        {
            // 不传 tolerance ⇒ 取 [ThreadStatic] 的 ChordTolerance：r=1000, tol=0.5 ⇒ full=100 ⇒ 50
            using (new TessellationScope(Tol05))
                Assert.That(Tessellation.SegmentsForArc(1000.0, 180.0), Is.EqualTo(50));
        }

        [Test]
        public void Capsule_ToleranceDrivesProfilePointCount_ThroughEvaluate()
        {
            // 端到端：CapsuleProfile 的两端半圆逐段走 SegmentsForArc（CsgEvaluator.ExpandProfile）。
            // 记点数为 P：旧口径 segs=16 ⇒ P=2·(16+1)=34；tol=0.05 时 full=100 ⇒ segs=50 ⇒ P=102。
            // 挤出体多边形数 = 底面三角 + 顶面三角 + 逐边侧面 = 2·(P−2) + P = 3P − 4
            // （凸多边形 ear-clip 恒得 P−2 个三角形）。
            var node = new ExtrudeNode(Profiles.Capsule(rectWidth: 3, radius: 100), 1);

            var legacy = CsgEvaluator.Evaluate(node);
            Assert.That(legacy.Polygons.Count, Is.EqualTo(3 * 34 - 4),
                "旧口径 segs=16 ⇒ P=34 ⇒ 98 个多边形");

            using (new TessellationScope(0.05))
            {
                var adaptive = CsgEvaluator.Evaluate(node);
                Assert.That(adaptive.Polygons.Count, Is.EqualTo(3 * 102 - 4),
                    "自适应 r=100/tol=0.05 ⇒ segs=50 ⇒ P=102 ⇒ 302 个多边形");
            }
        }
    }
}
