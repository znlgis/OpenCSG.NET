using System.Collections.Generic;
using NUnit.Framework;

namespace Csg.Test
{
    [TestFixture]
    public class CsgEvaluatorTest : SolidTest
    {
        [Test]
        public void EvaluateSimpleBox()
        {
            var node = new BoxNode(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1));
            var result = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(result, "CsgEvaluatorTest");
        }

        [Test]
        public void EvaluateSimpleSphere()
        {
            var node = new SphereNode(new Vector3D(0, 0, 0), 1);
            var result = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(result, "CsgEvaluatorTest");
        }

        [Test]
        public void EvaluateSimpleCylinder()
        {
            var node = new CylinderNode(new Vector3D(0, 0, 0), 1, 2);
            var result = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(result, "CsgEvaluatorTest");
        }

        [Test]
        public void EvaluateUnion()
        {
            var box = new BoxNode(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1));
            var sphere = new SphereNode(new Vector3D(0.5, 0, 0), 0.6);
            var node = CsgNodes.Union(box, sphere);
            var result = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(result, "CsgEvaluatorTest");
        }

        [Test]
        public void EvaluateSubtract()
        {
            var box = new BoxNode(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1));
            var sphere = new SphereNode(new Vector3D(0.5, 0, 0), 0.6);
            var node = CsgNodes.Subtract(box, sphere);
            var result = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(result, "CsgEvaluatorTest");
        }

        [Test]
        public void EvaluateIntersect()
        {
            var box = new BoxNode(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1));
            var sphere = new SphereNode(new Vector3D(0.5, 0, 0), 0.6);
            var node = CsgNodes.Intersect(box, sphere);
            var result = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(result, "CsgEvaluatorTest");
        }

        [Test]
        public void EvaluateTransform()
        {
            var node = CsgNodes.Transform(
                new Vector3D(2, 0, 0),   // translation
                new Vector3D(0, 0, 45),  // rotation Z 45 degrees
                new BoxNode(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1))
            );
            var result = CsgEvaluator.Evaluate(node);
            AssertAcceptedStl(result, "CsgEvaluatorTest");
        }

        [Test]
        public void EvaluateComplexTree()
        {
            var box = new BoxNode(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1));
            var sphere = new SphereNode(new Vector3D(0.5, 0, 0), 0.6);
            var diff = CsgNodes.Subtract(box, sphere);
            var result = CsgEvaluator.Evaluate(diff);
            AssertAcceptedStl(result, "CsgEvaluatorTest");
        }
    }
}
