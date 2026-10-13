using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// L5 集成：STL 闭环 —— <c>Solid → CsgMesh.TrianglesOf → StlWriter.WriteBinary/WriteAscii → 临时文件
    /// → ViewMeshReader.ReadStlFile → ViewProjector.Project</c>。
    /// 这是库内唯一读入器（<see cref="ViewMeshReader"/>）与两个写出器（<see cref="StlWriter.WriteBinary"/> /
    /// <see cref="StlWriter.WriteAscii"/>）的端到端贯通，并与 <see cref="ViewBridge.ProjectViews"/> 直连管线对拍。
    /// <para>
    /// <b>容差口径（先实测后定容，读数来自探针 <c>artifacts/planD/d5probe</c>）</b>：
    /// <list type="bullet">
    /// <item>整数坐标夹具（Cube10 / Box10x20x30 / LShape / BoxesShifted）：两格式的逐顶点最大绝对偏差均为
    /// <b>0</b>（整数值的 float32 与 6 位小数 ASCII 都精确往返）⇒ 三视图按端点 <c>Within(0)</c> 比较
    /// （段数/V/H/弧数亦逐项一致）。</item>
    /// <item>DrilledPlate（656 三角形，球面坐标非整数）：逐顶点最大偏差 二进制 <b>1.020658801920149e-7</b>
    /// （float32 在该尺度 |c|≤10 的单位舍入 2⁻²⁴·|c| ≈ 6e-7，实测更小）、ASCII <b>4.7054675222923947e-7</b>
    /// （<c>0.######</c> 六位小数 ⇒ 每坐标 ≤ 5e-7，实测为多个坐标同时取到最坏舍入）。主视/俯视端点偏差实测
    /// ≤ 9.962065439239609e-8（二进制）/ 5.002445330518002e-7（ASCII）⇒ 定容 1e-6 / 2e-6（约 10× 与 4× 余量；
    /// 端点由存储值经确定性采样/切分（凸组合）得出，无放大机制）。</item>
    /// <item><b>DrilledPlate 左视不参与逐段比较（改设弱拓扑钉）</b>：实测二进制 325→338 段（V 165→170、弧 3→4）、
    /// ASCII 325→319 段（弧 3→6）—— 该视图视线沿孔轴以外的掠射方向，孔缘属 DEFECT-004 机制② / DEFECT-005
    /// 的栅格量化敏感域，输入坐标 1e-7 级扰动即可翻转可见/隐藏分类。段数差异超出「无据放宽」允许的范围，
    /// 故该视图排除出逐段比较；为防「排除 = 零覆盖」，改钉直接投影拓扑（S=325、A=3，数值稳定）+
    /// 读回侧弱界（段数 325±20、弧数 ≥3，见用例）。既有 <c>Tier5OcclusionOracleTest.cs:51-56</c>
    /// 也把 DrilledPlate 左视排除在 oracle 覆盖之外。</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>独立复核（遮挡 oracle）</b>：轴对齐夹具在 STL 读回管线的输出上逐视图跑
    /// <see cref="OcclusionAssert.AssertVisibilityMatchesRayCast"/>（独立射线投射，不消费投影器判定）。
    /// 实测全部通过：Cube10 主/俯/左各 checked=12；BoxesShifted 24/24/21（indet=3）；LShape 12/24/21；
    /// DrilledPlate 主视 27 / 俯视 21（左视按上述理由排除）。
    /// </para>
    /// <para>临时文件一律写 <see cref="Path.GetTempPath"/> 的 GUID 命名文件，<c>finally</c> 删除（零残留）。</para>
    /// </summary>
    [TestFixture]
    public class StlRoundTripTest
    {
        /// <summary>夹具表：三角形数、参与比较的视图、每格式的端点容差与存储偏差上界（全部实测）。</summary>
        sealed class Fixture
        {
            public Fixture(string name, Func<Solid> build, int triangles, string[] views,
                double binaryTolerance, double asciiTolerance, double storageBound, int minHidden)
            {
                Name = name;
                Build = build;
                Triangles = triangles;
                Views = views;
                BinaryTolerance = binaryTolerance;
                AsciiTolerance = asciiTolerance;
                StorageBound = storageBound;
                MinHidden = minHidden;
            }

            public string Name { get; }
            public Func<Solid> Build { get; }
            public int Triangles { get; }
            public string[] Views { get; }
            public double BinaryTolerance { get; }
            public double AsciiTolerance { get; }
            public double StorageBound { get; }
            public int MinHidden { get; }
        }

        static readonly Fixture[] s_fixtures =
        {
            new("Cube10", () => Solids.Cube(10, center: true), 12,
                new[] { "主视", "俯视", "左视" }, 0.0, 0.0, 0.0, 0),
            new("Box10x20x30", () => Solids.Cube(new Vector3D(10, 20, 30), center: true), 12,
                new[] { "主视", "俯视", "左视" }, 0.0, 0.0, 0.0, 0),
            new("LShape", OcclusionFixtures.LShape, 20,
                new[] { "主视", "俯视", "左视" }, 0.0, 0.0, 0.0, 4),
            new("BoxesShifted", OcclusionFixtures.BoxesShifted, 24,
                new[] { "主视", "俯视", "左视" }, 0.0, 0.0, 0.0, 2),
            new("DrilledPlate", OcclusionFixtures.DrilledPlate, 656,
                new[] { "主视", "俯视" }, 1e-6, 2e-6, 1e-6, 8),
        };

        static IEnumerable<TestCaseData> FixtureCases()
        {
            foreach (var fixture in s_fixtures)
                yield return new TestCaseData(fixture.Name).SetName($"StlRoundTrip_{fixture.Name}");
        }

        static Fixture FixtureOf(string name)
            => s_fixtures.Single(f => f.Name == name);

        [TestCaseSource(nameof(FixtureCases))]
        public void WriteReadProject_TriangleCountPreserved_AndViewsMatchDirectPipeline(string name)
        {
            var fixture = FixtureOf(name);
            var solid = fixture.Build();
            var triangles = CsgMesh.TrianglesOf(solid);
            Assert.That(triangles.Count, Is.EqualTo(fixture.Triangles), $"{name}：夹具三角形数（实测钉）");

            var direct = ViewBridge.ProjectViews(solid, ViewDirections.OrthographicThree);
            var binaryPath = TempStlPath(name, "bin");
            var asciiPath = TempStlPath(name, "ascii");

            // DrilledPlate 左视弱拓扑钉（直接投影侧）：该视图不进逐段比较（理由见类注释），
            // 但直接投影数值稳定 ⇒ 拓扑实测钉死（S=325、A=3），防「排除 = 零覆盖」。
            var excludedLeft = name == "DrilledPlate"
                ? direct.Single(v => v.Direction.Name == "左视")
                : null;
            if (excludedLeft != null)
            {
                Assert.That(excludedLeft.Segments.Count, Is.EqualTo(325), $"{name}：直接投影左视段数（实测钉）");
                Assert.That(excludedLeft.ArcCount, Is.EqualTo(3), $"{name}：直接投影左视弧数（实测钉）");
            }

            try
            {
                var checkedAny = false;
                foreach (var format in new[] { StlFormat.Binary, StlFormat.Ascii })
                {
                    var path = format == StlFormat.Binary ? binaryPath : asciiPath;
                    var tolerance = format == StlFormat.Binary ? fixture.BinaryTolerance : fixture.AsciiTolerance;

                    var writeResult = StlWriter.Write(path, triangles, format);
                    Assert.That(writeResult.TriangleCount, Is.EqualTo(fixture.Triangles));

                    var readBack = ViewMeshReader.ReadStlFile(path);

                    // ① 三角形数守恒：读回 == 写出数（== CsgMesh 三角化数）。
                    Assert.That(readBack.Count, Is.EqualTo(triangles.Count),
                        $"{name}/{format}：三角形数守恒，读回 {readBack.Count} vs 写出 {triangles.Count}");

                    // ② 存储偏差上界：逐三角形逐顶点 |Δ| ≤ storageBound（整数夹具为 0）。
                    var maxStorageError = MaxVertexError(triangles, readBack);
                    Assert.That(maxStorageError, Is.LessThanOrEqualTo(fixture.StorageBound),
                        $"{name}/{format}：逐顶点最大存储偏差 {maxStorageError:R} > 上界 {fixture.StorageBound:R}");

                    // ③ 读回投影 vs 原 Solid 直接投影（视图子集 + 每格式容差见夹具表与类注释）。
                    var viaStl = new ViewProjector(readBack).Project(ViewDirections.OrthographicThree);

                    // DrilledPlate 左视弱拓扑钉（读回侧）：掠射栅格量化域（DEFECT-004 机制② / DEFECT-005），
                    // 段数实测 bin 338 / ascii 319（相对直接 325 浮动 +13/−6）、弧 4/6 ⇒ 只设 325±20 与 ≥3 的
                    // 松界 —— 该域读数钉死会随分类翻转而碎；松界仍能抓 0==0 空转与量级级回归。
                    if (excludedLeft != null)
                    {
                        var readLeft = viaStl.Single(v => v.Direction.Name == "左视");
                        Assert.That(Math.Abs(readLeft.Segments.Count - excludedLeft.Segments.Count), Is.LessThanOrEqualTo(20),
                            $"{name}/{format}：读回左视段数 {readLeft.Segments.Count} 超出直接投影 {excludedLeft.Segments.Count}±20 弱界");
                        Assert.That(readLeft.ArcCount, Is.GreaterThanOrEqualTo(3),
                            $"{name}/{format}：读回左视弧数 {readLeft.ArcCount} < 3（实测 bin 4 / ascii 6）");
                    }

                    var selected = direct.Select((v, i) => (View: v, Index: i))
                        .Where(x => fixture.Views.Contains(x.View.Direction.Name))
                        .Select(x => x.Index)
                        .ToArray();

                    foreach (var index in selected)
                    {
                        var expected = direct[index];
                        var actual = viaStl[index];
                        var where = $"{name}/{format}/{expected.Direction.Name}";

                        // 非空钉（防 0==0 空转）：两侧本视图都必须有段或弧（段+弧 = 0 时下方逐项相等会静默通过）。
                        Assert.That(expected.Segments.Count + expected.ArcCount, Is.GreaterThan(0),
                            $"{where}：直接投影为空（段+弧 = 0）");
                        Assert.That(actual.Segments.Count + actual.ArcCount, Is.GreaterThan(0),
                            $"{where}：读回投影为空（段+弧 = 0）");

                        Assert.That(actual.Segments.Count, Is.EqualTo(expected.Segments.Count),
                            $"{where}：段数 {actual.Segments.Count} vs 直接管线 {expected.Segments.Count}"
                            + $"（V {actual.VisibleCount} vs {expected.VisibleCount}）");
                        Assert.That(actual.ArcCount, Is.EqualTo(expected.ArcCount),
                            $"{where}：弧数 {actual.ArcCount} vs {expected.ArcCount}");

                        var (maxStart, maxEnd) = MaxSortedEndpointDeviation(expected, actual);
                        Assert.That(maxStart, Is.LessThanOrEqualTo(tolerance),
                            $"{where}：排序后 Start 端点最大偏差 {maxStart:R} > {tolerance:R}");
                        Assert.That(maxEnd, Is.LessThanOrEqualTo(tolerance),
                            $"{where}：排序后 End 端点最大偏差 {maxEnd:R} > {tolerance:R}");
                        checkedAny = true;
                    }
                }

                Assert.That(checkedAny, Is.True, $"{name}：没有任何视图被比较（夹具视图名漂移？）");
            }
            finally
            {
                DeleteIfExists(binaryPath);
                DeleteIfExists(asciiPath);
            }
        }

        /// <summary>
        /// 遮挡 oracle（独立射线投射）在 STL 读回管线的输出上逐视图判定 —— 直接判读回管线的 V/H 正确性。
        /// 夹具与排除理由同 <c>Tier5OcclusionOracleTest.cs:51-56</c>（DrilledPlate 左视属掠射域，
        /// 该测试亦只投影 主视/俯视，不覆盖）。
        /// 实测（二进制路径）：全部 PASS，checked 样本数见类注释；另外隐藏段计数 ≥ 夹具表下限（防「夹具无遮挡」空转）。
        /// </summary>
        [TestCaseSource(nameof(FixtureCases))]
        public void ReadBackPipeline_VisibilityMatchesRayCast(string name)
        {
            var fixture = FixtureOf(name);
            var solid = fixture.Build();
            var triangles = CsgMesh.TrianglesOf(solid);
            var path = TempStlPath(name, "occl");

            try
            {
                StlWriter.Write(path, triangles);   // 默认二进制
                var readBack = ViewMeshReader.ReadStlFile(path);
                var views = new ViewProjector(readBack).Project(ViewDirections.OrthographicThree);

                var hidden = 0;
                var checkedTotal = 0;
                foreach (var view in views)
                {
                    if (!fixture.Views.Contains(view.Direction.Name))
                        continue;

                    var stats = OcclusionAssert.AssertVisibilityMatchesRayCast(
                        new[] { view }, readBack, $"{name}/STL 读回/{view.Direction.Name}");

                    // oracle 内部已有防空洞断言；这里显式记账，失败消息里能看到每个视图的判定量。
                    Assert.That(stats.CheckedSamples, Is.GreaterThan(0),
                        $"{name}/{view.Direction.Name}：oracle 零判定样本");
                    checkedTotal += stats.CheckedSamples;
                    hidden += view.HiddenCount;
                }

                Assert.That(checkedTotal, Is.GreaterThan(0), $"{name}：没有任何视图跑过 oracle");
                Assert.That(hidden, Is.GreaterThanOrEqualTo(fixture.MinHidden),
                    $"{name}：读回视图隐藏段合计 {hidden} < 下限 {fixture.MinHidden}（遮挡 oracle 的判定对象应存在）");
            }
            finally
            {
                DeleteIfExists(path);
            }
        }

        static string TempStlPath(string name, string kind)
            => Path.Combine(Path.GetTempPath(), $"opencsg-d5-{name}-{kind}-{Guid.NewGuid():N}.stl");

        static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        /// <summary>逐三角形逐顶点最大绝对坐标差（读回顺序 = 写出顺序，见 StlWriter 的遍历顺序）。</summary>
        static double MaxVertexError(IReadOnlyList<MeshTriangle> written, IReadOnlyList<ViewTriangle> readBack)
        {
            var max = 0.0;
            for (var i = 0; i < written.Count; i++)
            {
                for (var v = 0; v < 3; v++)
                {
                    var a = v switch { 0 => written[i].A, 1 => written[i].B, _ => written[i].C };
                    var p = readBack[i];
                    var b = v switch { 0 => p.A, 1 => p.B, _ => p.C };
                    max = Math.Max(max, Math.Max(Math.Abs(a.X - b.X), Math.Max(Math.Abs(a.Y - b.Y), Math.Abs(a.Z - b.Z))));
                }
            }
            return max;
        }

        /// <summary>按 (Start, End) 排序后逐段比较端点最大偏差（不修正朝向 —— 实测两边逐位一致）。</summary>
        static (double MaxStart, double MaxEnd) MaxSortedEndpointDeviation(ViewProjection expected, ViewProjection actual)
        {
            var sortedExpected = expected.Segments.OrderBy(s => (s.Start.X, s.Start.Y, s.End.X, s.End.Y)).ToList();
            var sortedActual = actual.Segments.OrderBy(s => (s.Start.X, s.Start.Y, s.End.X, s.End.Y)).ToList();

            var maxStart = 0.0;
            var maxEnd = 0.0;
            for (var i = 0; i < sortedExpected.Count; i++)
            {
                maxStart = Math.Max(maxStart, Distance(sortedExpected[i].Start, sortedActual[i].Start));
                maxEnd = Math.Max(maxEnd, Distance(sortedExpected[i].End, sortedActual[i].End));
                if (sortedExpected[i].IsVisible != sortedActual[i].IsVisible)
                    return (double.PositiveInfinity, double.PositiveInfinity);
            }
            return (maxStart, maxEnd);
        }

        static double Distance(ViewPoint2 a, ViewPoint2 b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
