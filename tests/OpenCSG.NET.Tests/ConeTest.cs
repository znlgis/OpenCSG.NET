using NUnit.Framework;

namespace Csg.Test
{
	[TestFixture]
	public class ConeTest : SolidTest
	{
		[Test]
		public void FullCone()
		{
			// Apex at the top (TopRadius = 0), circular base at the bottom.
			var node = new ConeNode(new Vector3D(0, 0, 0), TopRadius: 0, BottomRadius: 1, Height: 2);
			var solid = CsgEvaluator.Evaluate(node);
			Assert.Greater(solid.Polygons.Count, 0);
			AssertAcceptedStl(solid, "ConeTest");
		}

		[Test]
		public void Frustum()
		{
			// Truncated cone: both radii non-zero.
			var node = new ConeNode(new Vector3D(0, 0, 0), TopRadius: 1, BottomRadius: 2, Height: 2);
			var solid = CsgEvaluator.Evaluate(node);
			Assert.Greater(solid.Polygons.Count, 0);
			AssertAcceptedStl(solid, "ConeTest");
		}

		[Test]
		public void ConeParticipatesInBoolean()
		{
			// A cone must integrate into boolean operations like any other primitive.
			var cone = new ConeNode(new Vector3D(0, 0, 0), TopRadius: 0, BottomRadius: 1, Height: 2);
			var box = new BoxNode(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1));
			var node = CsgNodes.Union(cone, box);
			var solid = CsgEvaluator.Evaluate(node);
			Assert.Greater(solid.Polygons.Count, 0);
			Assert.IsTrue(solid.IsCanonicalized);
			Assert.IsTrue(solid.IsRetesselated);
		}

		[Test]
		public void NonPositiveHeightThrows()
		{
			var node = new ConeNode(new Vector3D(0, 0, 0), TopRadius: 0, BottomRadius: 1, Height: 0);
			Assert.Throws<CsgEvaluationException>(() => CsgEvaluator.Evaluate(node));
		}

		[Test]
		public void ZeroRadiiThrows()
		{
			var node = new ConeNode(new Vector3D(0, 0, 0), TopRadius: 0, BottomRadius: 0, Height: 2);
			Assert.Throws<CsgEvaluationException>(() => CsgEvaluator.Evaluate(node));
		}

		[Test]
		public void NegativeRadiusThrows()
		{
			var node = new ConeNode(new Vector3D(0, 0, 0), TopRadius: -1, BottomRadius: 1, Height: 2);
			Assert.Throws<CsgEvaluationException>(() => CsgEvaluator.Evaluate(node));
		}
	}
}
