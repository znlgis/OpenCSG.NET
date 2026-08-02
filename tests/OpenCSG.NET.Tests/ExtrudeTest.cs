using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    [TestFixture]
    public class ExtrudeTest : SolidTest
    {
        [Test]
        public void Rectangle_Simple()
        {
            var node = new ExtrudeNode(Profiles.Rectangle(4, 2), 1);
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "Extrude_Rectangle");
        }

        [Test]
        public void Trapezoid()
        {
            var node = new ExtrudeNode(Profiles.Trapezoid(2, 3, 2), 1);
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "Extrude_Trapezoid");
        }

        [Test]
        public void LShape()
        {
            var node = new ExtrudeNode(Profiles.LShape(3, 2, 0.5), 1);
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "Extrude_LShape");
        }

        [Test]
        public void Capsule()
        {
            var node = new ExtrudeNode(Profiles.Capsule(3, 1), 1);
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "Extrude_Capsule");
        }

        [Test]
        public void HBeam()
        {
            var node = new ExtrudeNode(Profiles.HBeam(100, 80, 10, 12), 50);
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "Extrude_HBeam");
        }

        [Test]
        public void Channel()
        {
            var node = new ExtrudeNode(Profiles.Channel(100, 60, 8, 10), 50);
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "Extrude_Channel");
        }

        [Test]
        public void SquareTube()
        {
            var node = new ExtrudeNode(Profiles.SquareTube(80, 80, 5), 50);
            var solid = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(solid, "Extrude_SquareTube");
        }
    }
}
