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

		static bool StlStringEquals(string a, string b, double tolerance, out string message)
		{
			var alines = ParseStl(a, out var aHeader);
			var blines = ParseStl(b, out var bHeader);

			if (!string.Equals(aHeader, bHeader, StringComparison.Ordinal))
			{
				message = $"STL header differs: '{aHeader}' vs '{bHeader}'.";
				return false;
			}

			if (alines.Length != blines.Length)
			{
				message = $"Line count differs: {alines.Length} vs {blines.Length}.";
				return false;
			}

			for (var i = 0; i < alines.Length; i++)
			{
				var atoks = alines[i].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
				var btoks = blines[i].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

				if (atoks.Length != btoks.Length)
				{
					message = $"Line {i + 1} value count differs: {atoks.Length} vs {btoks.Length}.";
					return false;
				}

				for (var j = 0; j < atoks.Length; j++)
				{
					var isFloatA = double.TryParse(atoks[j], NumberStyles.Float, CultureInfo.InvariantCulture, out var fA);
					var isFloatB = double.TryParse(btoks[j], NumberStyles.Float, CultureInfo.InvariantCulture, out var fB);

					if (isFloatA && isFloatB)
					{
						if (Math.Abs(fA - fB) > tolerance && Math.Abs(fA - fB) > tolerance * Math.Max(Math.Abs(fA), Math.Abs(fB)))
						{
							message = $"Facet {i / 7 + 1}, line {i + 1} value differs: {atoks[j]} vs {btoks[j]} (diff={Math.Abs(fA - fB):E2}).";
							return false;
						}
					}
					else if (!string.Equals(atoks[j], btoks[j], StringComparison.Ordinal))
					{
						message = $"Facet {i / 7 + 1}, line {i + 1} token differs: '{atoks[j]}' vs '{btoks[j]}'.";
						return false;
					}
				}
			}

			message = string.Empty;
			return true;
		}

		static string[] ParseStl(string stl, out string header)
		{
			var lines = stl.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
			if (lines.Length == 0)
			{
				header = string.Empty;
				return lines;
			}

			header = lines[0].Trim();

			// Extract facets (7-line blocks: facet normal, outer loop, 3× vertex, endloop, endfacet)
			// Skip header line 0 and footer line (last)
			var facetCount = (lines.Length - 2) / 7;
			var facets = new string[facetCount];
			for (var f = 0; f < facetCount; f++)
			{
				var start = 1 + f * 7;
				var content = string.Join("\n", lines, start, 7);
				facets[f] = content;
			}

			Array.Sort(facets, StringComparer.Ordinal);

			// Reassemble sorted lines
			var footer = lines[lines.Length - 1];
			var result = new string[facetCount * 7 + 2];
			result[0] = header;
			for (var f = 0; f < facetCount; f++)
			{
				var facetLines = facets[f].Split('\n');
				for (var k = 0; k < 7; k++)
					result[1 + f * 7 + k] = facetLines[k];
			}
			result[result.Length - 1] = footer;

			return result;
		}
	}
}

