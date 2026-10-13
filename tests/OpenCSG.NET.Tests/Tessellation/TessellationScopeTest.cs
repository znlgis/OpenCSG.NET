using System;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    [TestFixture]
    public class TessellationScopeTest
    {
        [Test]
        public void Scope_SetsToleranceAndRestoresOnDispose()
        {
            Assert.That(Tessellation.ChordTolerance, Is.EqualTo(0.0), "前置：默认必须关闭自适应");

            using (new TessellationScope(0.05))
            {
                Assert.That(Tessellation.ChordTolerance, Is.EqualTo(0.05));
                Assert.That(Tessellation.IsAdaptive, Is.True);
            }

            Assert.That(Tessellation.ChordTolerance, Is.EqualTo(0.0), "Dispose 必须复位，否则污染后续用例");
            Assert.That(Tessellation.IsAdaptive, Is.False);
        }

        [Test]
        public void Scope_RestoresEvenWhenBodyThrows()
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                using (new TessellationScope(0.05))
                    throw new InvalidOperationException("模拟用例体抛异常");
            });

            Assert.That(Tessellation.ChordTolerance, Is.EqualTo(0.0));
        }

        [Test]
        public void AdaptiveTolerance_ChangesSegmentCount()
        {
            // 关闭自适应 ⇒ 旧口径 12 段；开启 ⇒ 按弦高容差加密（下限 16）
            Assert.That(Tessellation.SegmentsFor(100.0), Is.EqualTo(Solid.DefaultResolution3D));

            using (new TessellationScope(0.05))
                Assert.That(Tessellation.SegmentsFor(100.0),
                    Is.GreaterThan(Solid.DefaultResolution3D));
        }
    }
}
