using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>SystemTests 骨架自检：工程可构建、TestKit 可引用、断言库可用。</summary>
    [TestFixture]
    public class SolidSpecPropertyTest
    {
        [Test]
        public void Skeleton_CanEvaluateSolidAndAssertInvariants()
        {
            var cube = Solids.Cube(2, center: true);

            GeometryAssert.AssertSolidInvariants(cube);
            GeometryAssert.AssertVolume(cube, 8.0, 1e-9);
        }
    }
}
