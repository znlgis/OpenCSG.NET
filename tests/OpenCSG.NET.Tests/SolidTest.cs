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
			var alines = a.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
			var blines = b.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

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
					message = $"Line {i + 1} token count differs: '{alines[i].Trim()}' vs '{blines[i].Trim()}'.";
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
							message = $"Line {i + 1} value differs: {atoks[j]} vs {btoks[j]} (diff={Math.Abs(fA - fB):E2}).";
							return false;
						}
					}
					else if (!string.Equals(atoks[j], btoks[j], StringComparison.Ordinal))
					{
						message = $"Line {i + 1} token differs: '{atoks[j]}' vs '{btoks[j]}'.";
						return false;
					}
				}
			}

			message = string.Empty;
			return true;
		}
	}
}

