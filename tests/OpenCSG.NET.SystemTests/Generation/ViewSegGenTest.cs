using System.Collections.Generic;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    [TestFixture]
    public class ViewSegGenTest
    {
        [Test]
        public void Random_SameSeed_ProducesIdenticalSegments()
        {
            var a = ViewSegGen.Random(1717);
            var b = ViewSegGen.Random(1717);

            Assert.That(b.Count, Is.EqualTo(a.Count));
            for (var i = 0; i < a.Count; i++)
            {
                Assert.That(b[i].Start.X, Is.EqualTo(a[i].Start.X));
                Assert.That(b[i].Start.Y, Is.EqualTo(a[i].Start.Y));
                Assert.That(b[i].End.X, Is.EqualTo(a[i].End.X));
                Assert.That(b[i].IsVisible, Is.EqualTo(a[i].IsVisible));
            }
        }

        [Test]
        public void Random_ProducesBothVisibilitiesAndNoDegenerateSegments()
        {
            var visible = 0;
            var hidden = 0;

            for (long seed = 0; seed < 32; seed++)
                foreach (var segment in ViewSegGen.Random(seed))
                {
                    if (segment.IsVisible) visible++; else hidden++;
                    Assert.That(segment.Length, Is.GreaterThan(1e-6), $"seed={seed} 产出了零长线段");
                }

            Assert.That(visible, Is.GreaterThan(0), "必须有用例覆盖可见层");
            Assert.That(hidden, Is.GreaterThan(0), "必须有用例覆盖隐藏层（否则跨可见性合并的缺陷测不出来）");
        }

        [Test]
        public void Random_RespectsSegmentBudget()
        {
            for (long seed = 0; seed < 64; seed++)
                Assert.That(ViewSegGen.Random(seed, maxSegments: 20).Count,
                    Is.LessThanOrEqualTo(20), $"seed={seed} 超出线段预算");
        }

        [Test]
        public void ArcSample_LiesOnTheCircle()
        {
            var center = new ViewPoint2(3, -4);
            const double radius = 7.5;

            var segments = ViewSegGen.ArcSample(center, radius, 0, 90, 8, visible: true);

            Assert.That(segments.Count, Is.EqualTo(8));
            foreach (var segment in segments)
            {
                Assert.That(DistanceToCircle(center, radius, segment.Start), Is.LessThan(1e-12));
                Assert.That(DistanceToCircle(center, radius, segment.End), Is.LessThan(1e-12));
                Assert.That(segment.IsVisible, Is.True);
            }
        }

        [Test]
        public void BoundsOf_CoversEveryEndpoint()
        {
            var segments = ViewSegGen.Random(31337);
            var bounds = ViewSegGen.BoundsOf(segments);

            foreach (var segment in segments)
            {
                Assert.That(segment.Start.X, Is.GreaterThanOrEqualTo(bounds.MinX - 1e-9));
                Assert.That(segment.Start.X, Is.LessThanOrEqualTo(bounds.MaxX + 1e-9));
                Assert.That(segment.End.Y, Is.GreaterThanOrEqualTo(bounds.MinY - 1e-9));
                Assert.That(segment.End.Y, Is.LessThanOrEqualTo(bounds.MaxY + 1e-9));
            }
        }

        static double DistanceToCircle(ViewPoint2 center, double radius, ViewPoint2 p)
        {
            var dx = p.X - center.X;
            var dy = p.Y - center.Y;
            return System.Math.Abs(System.Math.Sqrt(dx * dx + dy * dy) - radius);
        }
    }
}
