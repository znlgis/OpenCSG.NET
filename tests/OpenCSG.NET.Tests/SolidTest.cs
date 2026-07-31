using System;
using System.Runtime.CompilerServices;
using System.IO;
using System.Globalization;
using NUnit.Framework;

namespace Csg.Test
{
	public class SolidTest
	{
		const double STL_TOLERANCE = 1e-10;

		protected void AssertAcceptedStl(Solid csg, string fixtureName, [CallerMemberName] string testName = "")
		{
			var aname = $"{fixtureName}.{testName}.stl";
			var rname = $"{fixtureName}.{testName}_.stl";
			var asmPath = System.Reflection.Assembly.GetCallingAssembly().Location;
			var repoPath = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(asmPath))));
			var resultsPath = Path.Combine(repoPath, "Results");
			if (!Directory.Exists(resultsPath))
			{
				Assert.Inconclusive("Test results directory not found at {0}", resultsPath);
			}

			var acceptedPath = Path.Combine(resultsPath, aname);
			var rejectedPath = Path.Combine(resultsPath, rname);
			File.Delete(rejectedPath);

			var testStl = csg.ToStlString(testName);

			if (!File.Exists(acceptedPath))
			{
				File.WriteAllText(rejectedPath, testStl);
				Assert.Inconclusive("No results have been marked as accepted.");
			}
			else {
				var acceptedStl = File.ReadAllText(acceptedPath);
				if (!StlStringEquals(testStl, acceptedStl, STL_TOLERANCE, out var diffMsg))
				{
					File.WriteAllText(rejectedPath, testStl);
					Assert.Fail(diffMsg);
				}
			}
		}

		struct Triangle
		{
			public double X1, Y1, Z1, X2, Y2, Z2, X3, Y3, Z3;
		}

		static bool StlStringEquals(string a, string b, double tolerance, out string message)
		{
			var aHeader = GetStlHeader(a);
			var bHeader = GetStlHeader(b);
			if (!string.Equals(aHeader, bHeader, StringComparison.Ordinal))
			{
				message = $"STL header differs: '{aHeader}' vs '{bHeader}'.";
				return false;
			}

			var aTriangles = ParseTriangles(a);
			var bTriangles = ParseTriangles(b);

			if (aTriangles.Length != bTriangles.Length)
			{
				message = $"Triangle count differs: {aTriangles.Length} vs {bTriangles.Length}.";
				return false;
			}

			var aArea = ComputeTotalArea(aTriangles);
			var bArea = ComputeTotalArea(bTriangles);
			var aBounds = ComputeBounds(aTriangles);
			var bBounds = ComputeBounds(bTriangles);

			if (Math.Abs(aArea - bArea) > tolerance && Math.Abs(aArea - bArea) > tolerance * Math.Max(aArea, bArea))
			{
				message = $"Total area differs: {aArea:R} vs {bArea:R} (diff={Math.Abs(aArea - bArea):E2}).";
				return false;
			}

			if (Math.Abs(aBounds.minX - bBounds.minX) > tolerance ||
			    Math.Abs(aBounds.minY - bBounds.minY) > tolerance ||
			    Math.Abs(aBounds.minZ - bBounds.minZ) > tolerance ||
			    Math.Abs(aBounds.maxX - bBounds.maxX) > tolerance ||
			    Math.Abs(aBounds.maxY - bBounds.maxY) > tolerance ||
			    Math.Abs(aBounds.maxZ - bBounds.maxZ) > tolerance)
			{
				message = $"Bounding box differs.";
				return false;
			}

			message = string.Empty;
			return true;
		}

		static string GetStlHeader(string stl)
		{
			var idx = stl.IndexOf('\n');
			return idx >= 0 ? stl.Substring(0, idx).Trim() : stl.Trim();
		}

		static Triangle[] ParseTriangles(string stl)
		{
			var lines = stl.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
			if (lines.Length < 7)
				return new Triangle[0];

			var count = (lines.Length - 2) / 7;
			var tri = new Triangle[count];

			for (var i = 0; i < count; i++)
			{
				var offset = 1 + i * 7;
				// facet normal line: offset
				// outer loop line: offset + 1
				var v1 = lines[offset + 2].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
				var v2 = lines[offset + 3].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
				var v3 = lines[offset + 4].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

				tri[i] = new Triangle
				{
					X1 = double.Parse(v1[1], CultureInfo.InvariantCulture),
					Y1 = double.Parse(v1[2], CultureInfo.InvariantCulture),
					Z1 = double.Parse(v1[3], CultureInfo.InvariantCulture),
					X2 = double.Parse(v2[1], CultureInfo.InvariantCulture),
					Y2 = double.Parse(v2[2], CultureInfo.InvariantCulture),
					Z2 = double.Parse(v2[3], CultureInfo.InvariantCulture),
					X3 = double.Parse(v3[1], CultureInfo.InvariantCulture),
					Y3 = double.Parse(v3[2], CultureInfo.InvariantCulture),
					Z3 = double.Parse(v3[3], CultureInfo.InvariantCulture),
				};
			}

			return tri;
		}

		static double ComputeTotalArea(Triangle[] triangles)
		{
			var total = 0.0;
			for (var i = 0; i < triangles.Length; i++)
			{
				var t = triangles[i];
				var ux = t.X2 - t.X1;
				var uy = t.Y2 - t.Y1;
				var uz = t.Z2 - t.Z1;
				var vx = t.X3 - t.X1;
				var vy = t.Y3 - t.Y1;
				var vz = t.Z3 - t.Z1;
				var crossX = uy * vz - uz * vy;
				var crossY = uz * vx - ux * vz;
				var crossZ = ux * vy - uy * vx;
				total += 0.5 * Math.Sqrt(crossX * crossX + crossY * crossY + crossZ * crossZ);
			}

			return total;
		}

		static (double minX, double minY, double minZ, double maxX, double maxY, double maxZ) ComputeBounds(Triangle[] triangles)
		{
			if (triangles.Length == 0)
				return (0, 0, 0, 0, 0, 0);

			var minX = double.MaxValue;
			var minY = double.MaxValue;
			var minZ = double.MaxValue;
			var maxX = double.MinValue;
			var maxY = double.MinValue;
			var maxZ = double.MinValue;

			for (var i = 0; i < triangles.Length; i++)
			{
				var t = triangles[i];
				minX = Math.Min(minX, Math.Min(t.X1, Math.Min(t.X2, t.X3)));
				minY = Math.Min(minY, Math.Min(t.Y1, Math.Min(t.Y2, t.Y3)));
				minZ = Math.Min(minZ, Math.Min(t.Z1, Math.Min(t.Z2, t.Z3)));
				maxX = Math.Max(maxX, Math.Max(t.X1, Math.Max(t.X2, t.X3)));
				maxY = Math.Max(maxY, Math.Max(t.Y1, Math.Max(t.Y2, t.Y3)));
				maxZ = Math.Max(maxZ, Math.Max(t.Z1, Math.Max(t.Z2, t.Z3)));
			}

			return (minX, minY, minZ, maxX, maxY, maxZ);
		}
	}
}

