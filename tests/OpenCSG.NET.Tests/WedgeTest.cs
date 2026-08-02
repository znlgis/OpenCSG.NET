using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    [TestFixture]
    public class WedgeTest : SolidTest
    {
        [Test]
        public void Basic()
        {
            var node = new WedgeNode(new Vector3D(0, 0, 0), 2, 2, 2);
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "Wedge_Basic");
        }

        [Test]
        public void WithCornerOffset()
        {
            var node = new WedgeNode(new Vector3D(5, 5, 0), 3, 1, 2);
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "Wedge_Offset");
        }

        [Test]
        public void Thin()
        {
            var node = new WedgeNode(new Vector3D(0, 0, 0), 4, 0.5, 1);
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "Wedge_Thin");
        }

        [Test]
        public void UnionWithBox()
        {
            var wedge = new WedgeNode(new Vector3D(0, 0, 0), 2, 2, 2);
            var box = new BoxNode(new Vector3D(0.5, 0, 0), new Vector3D(1, 1, 1));
            var node = CsgNodes.Union(wedge, box);
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "Wedge_Union");
        }
    }
}
