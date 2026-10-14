using System;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// PrimitiveConvention 的 L2 契约（Plan C / Task 7 Step 1）：构造/属性、FrustumRadii 映射、
    /// IsCanonicalFactor 容差（1e-6）两侧、Offset 公式，以及 ValidateProbeFactor 的三类非法输入
    /// （NaN / ±Infinity / |factor| > 1 + 1e-6）与门限边的接受域。
    /// </summary>
    [TestFixture]
    public class PrimitiveConventionContractTest
    {
        [Test]
        public void Ctor_ExposesAllProbeFactors_AndOriginBasedIsAllZero()
        {
            var convention = new PrimitiveConvention(
                boxX: 0.1, boxY: 0.2, boxZ: 0.3, frustumZ: -0.5, wedgeX: 0.4, wedgeY: 0.5, wedgeZ: 0.6);

            Assert.That(convention.BoxX, Is.EqualTo(0.1));
            Assert.That(convention.BoxY, Is.EqualTo(0.2));
            Assert.That(convention.BoxZ, Is.EqualTo(0.3));
            Assert.That(convention.FrustumZ, Is.EqualTo(-0.5));
            Assert.That(convention.WedgeX, Is.EqualTo(0.4));
            Assert.That(convention.WedgeY, Is.EqualTo(0.5));
            Assert.That(convention.WedgeZ, Is.EqualTo(0.6));

            var origin = PrimitiveConvention.OriginBased;
            Assert.That(origin.BoxX, Is.EqualTo(0.0));
            Assert.That(origin.BoxY, Is.EqualTo(0.0));
            Assert.That(origin.BoxZ, Is.EqualTo(0.0));
            Assert.That(origin.FrustumZ, Is.EqualTo(0.0));
            Assert.That(origin.WedgeX, Is.EqualTo(0.0));
            Assert.That(origin.WedgeY, Is.EqualTo(0.0));
            Assert.That(origin.WedgeZ, Is.EqualTo(0.0));
        }

        [Test]
        public void FrustumRadii_MapsBottomRadiusToBothAxes_AndOffsetFollowsFormula()
        {
            var (radiusAlongX, radiusAlongY, topRadius) = PrimitiveConvention.FrustumRadii(bottomRadius: 2, topRadius: 1);
            Assert.That(radiusAlongX, Is.EqualTo(2.0));
            Assert.That(radiusAlongY, Is.EqualTo(2.0));
            Assert.That(topRadius, Is.EqualTo(1.0));

            // Offset = center − size/2 − factor·size（自原点约定 0 ⇒ −size/2；居中约定 −0.5 ⇒ 0）。
            Assert.That(PrimitiveConvention.Offset(center: 10, size: 4, probeMinFactor: 0), Is.EqualTo(8.0));
            Assert.That(PrimitiveConvention.Offset(center: 10, size: 4, probeMinFactor: -0.5), Is.EqualTo(10.0));
            Assert.That(PrimitiveConvention.Offset(center: 10, size: 4, probeMinFactor: 0.25), Is.EqualTo(7.0),
                "区间内非标准因子按线性公式补偿");
        }

        [Test]
        public void IsCanonicalFactor_ToleranceBoundaries()
        {
            Assert.That(PrimitiveConvention.IsCanonicalFactor(0.0), Is.True);
            Assert.That(PrimitiveConvention.IsCanonicalFactor(1e-6), Is.True, "恰在容差上界（≤）");
            Assert.That(PrimitiveConvention.IsCanonicalFactor(1.1e-6), Is.False, "刚出容差 ⇒ 非标准");
            Assert.That(PrimitiveConvention.IsCanonicalFactor(-0.5), Is.True);
            Assert.That(PrimitiveConvention.IsCanonicalFactor(-0.5 + 1e-7), Is.True);
            Assert.That(PrimitiveConvention.IsCanonicalFactor(0.5), Is.False, "区间内的非标准值不算已知约定");
        }

        [Test]
        public void ValidateProbeFactor_InvalidValues_Throw()
        {
            // 读实现：NaN / ±Infinity / |factor| > 1 + 1e-6 一律 InvalidOperationException（消息带宿主调用与轴向）。
            foreach (var factor in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity,
                                            1 + 2e-6, -1 - 2e-6 })
            {
                var ex = Assert.Throws<InvalidOperationException>(
                    () => PrimitiveConvention.ValidateProbeFactor("TestHost.CreateBox", "X", factor),
                    $"factor={factor:R} 应被拒绝");
                Assert.That(ex!.Message, Does.Contain("TestHost.CreateBox"), "消息须含宿主调用");
                Assert.That(ex.Message, Does.Contain("X"), "消息须含轴向");
            }
        }

        [Test]
        public void ValidateProbeFactor_InRangeBoundaries_PassThrough()
        {
            // 接受域边界：±1 及恰在 1+1e-6 容差内（> 才拒绝）。
            foreach (var factor in new[] { 0.0, -0.5, 1.0, 1 + 1e-6, -1 - 1e-6 })
            {
                var returned = PrimitiveConvention.ValidateProbeFactor("TestHost.CreateBox", "Y", factor);
                Assert.That(returned, Is.EqualTo(factor), $"factor={factor:R} 应原样返回");
            }
        }
    }
}
