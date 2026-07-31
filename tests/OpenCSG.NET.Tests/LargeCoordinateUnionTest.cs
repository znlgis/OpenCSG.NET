using NUnit.Framework;
using static Csg.Solids;

namespace Csg.Test
{
	[TestFixture]
	public class LargeCoordinateUnionTest
	{
		[Test]
		public void OverlappingCubesAtSurveyCoordinates_UnionRetainsAllFaces()
		{
			var offset = new Vector3D(-49256, 12000, 5);
			var cube1 = Cube(1, offset + new Vector3D(0, 0, 0));
			var cube2 = Cube(1, offset + new Vector3D(0.5, 0, 0));

			var originUnion = Cube(1, new Vector3D(0, 0, 0)).Union(Cube(1, new Vector3D(0.5, 0, 0)));
			var surveyUnion = cube1.Union(cube2);

			Assert.GreaterOrEqual(originUnion.Polygons.Count, 6);
			Assert.AreEqual(originUnion.Polygons.Count, surveyUnion.Polygons.Count,
				"Survey-coordinate union should match origin union face count.");
		}
	}
}
