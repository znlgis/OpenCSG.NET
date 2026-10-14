using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// L4 新模块语义基线（Plan D / Task 2）：为 Plan A 之后的新增/修复路径钉 STL golden。
    /// 覆盖 CsgHelpers 族（PrismXY / PrismXZ / CornerCut / CornerWedge / TabbedPlate /
    /// FilletConcave / RoundConvexCorner）、A2 修复后的扇形柱（CylinderOptions.SectorAngle）、
    /// 定向图元（CubeOptions / SphereOptions 非默认参数）与弦高容差细分（TessellationScope）。
    ///
    /// golden 约定对齐既有 45 个基线（SolidTest.AssertAcceptedStl 因文件冻结不可改，按简报
    /// 最小复制为下方私有辅助）：文件名 &lt;Fixture&gt;.&lt;TestName&gt;.stl；
    /// 内容 = Solid.ToStlString("&lt;TestName&gt;")（ASCII / UTF-8 无 BOM）；基线缺失 ⇒ 写
    /// &lt;Fixture&gt;.&lt;TestName&gt;_.stl 并 Assert.Inconclusive（GoldenGuard 随即转红）；
    /// 基线存在 ⇒ **Windows 上**与 ToStlString 输出**逐字节**比对，不一致即失败
    /// （golden 按 Windows 输出冻结 = Windows-pinned）；**非 Windows 走语义校验**
    /// （DEFECT-011：跨平台 libm 的 ≤1 ULP 差会改变「R」数字文本：`solid &lt;name&gt;` 首行 + 三角形数
    /// 精确、顶点 1e-4 绝对，见 <see cref="FindStlSemanticDivergence"/>）。
    /// 与 legacy 的口径差异为**有意设计**（简报指定的 byte-exact variant）：本套件 Windows 侧逐字节比对；
    /// legacy SolidTest 在 header 序数相等、三角形数相等之后，面积/体积/包围盒按 1e-10 数值容差比较。
    /// 每个用例的几何参数写成可读常量；新增基线须经 tools/promote-goldens.sh 人工确认后升级。
    /// </summary>
    [TestFixture]
    public class ModuleBaselineGoldenTest
    {
        const string Fixture = "ModuleBaselineGoldenTest";

        static Solid Eval(CsgNode node) => CsgEvaluator.Evaluate(node);

        // ─────────────── CsgHelpers 族 ───────────────

        [Test]
        public void PrismXY_Hexagon()
        {
            // (X,Y) 断面 = 正六边形（逆时针给点），外接圆 R=20；沿 Z 拉伸 6、以 Z=0 居中。
            const double radius = 20, thickness = 6, zCenter = 0;
            var points = new (double X, double Y)[6];
            for (int i = 0; i < 6; i++)
            {
                double angle = Math.PI / 3.0 * i; // 0°,60°,…,300°：逆时针
                points[i] = (radius * Math.Cos(angle), radius * Math.Sin(angle));
            }

            AssertAcceptedStl(Eval(CsgHelpers.PrismXY(points, zCenter, thickness)), Fixture);
        }

        [Test]
        public void PrismXY_NonConvexL()
        {
            // 非凸 L 形断面（宽 30 × 高 40、臂厚 12），顶点按**顺时针**给出——
            // 与上例的逆时针凑成「走向两种」（求值时 PolygonProfile 会规整为逆时针）。
            var points = new[]
            {
                (0.0, 0.0), (0.0, 40.0), (12.0, 40.0),
                (12.0, 12.0), (30.0, 12.0), (30.0, 0.0),
            };
            const double thickness = 6, zCenter = 0;

            AssertAcceptedStl(Eval(CsgHelpers.PrismXY(points, zCenter, thickness)), Fixture);
        }

        [Test]
        public void PrismXZ_Triangle()
        {
            // (X,Z) 断面 = 直角三角形（X∈[0,12] 径向、Z∈[0,5] 轴向）；沿 Y 拉伸 4、以 Y=0 居中。
            var points = new[] { (0.0, 0.0), (12.0, 0.0), (0.0, 5.0) };
            const double yCenter = 0, length = 4;

            AssertAcceptedStl(Eval(CsgHelpers.PrismXZ(points, yCenter, length)), Fixture);
        }

        [Test]
        public void CornerCut_Plate()
        {
            // 100×100×40 板在 (0,0) 角被 10×10 直角切角（45° 方盒体精确切除，替代 HalfPlaneCut）。
            const double size = 10, over = 40;
            var plate = CsgNodes.Box(50, 50, 0, 100, 100, over);

            var solid = Eval(CsgNodes.Subtract(plate, CsgHelpers.CornerCut(0, 0, 0, over, size)));
            AssertAcceptedStl(solid, Fixture);
        }

        [Test]
        public void CornerWedge_RightPrism()
        {
            // 独立直角楔块：角点 (0,0)、两腿沿 +X/+Y 各长 8、厚 5（以 Z=0 居中）。
            const double leg = 8, thickness = 5, zCenter = 0;

            var solid = Eval(CsgHelpers.CornerWedge(0, 0, dx: 1, dy: 1, leg, zCenter, thickness));
            AssertAcceptedStl(solid, Fixture);
        }

        [Test]
        public void TabbedPlate_Default()
        {
            // 代表性凸耳板：宽 400、孔心高 300、圆头 R100、Ø80 孔、厚 20、+Z 侧、立边 zLeft=50。
            // 斜边与水平夹角由相切二次方程反解（此参数下 ≈58.63°，属构造干净的参数域）。
            const double xCenter = 0, thickness = 20, zLeft = 50, width = 400;
            const double yBottom = 0, yCenter = 300, headRadius = 100, holeRadius = 40;

            var solid = Eval(CsgHelpers.TabbedPlate(
                xCenter, thickness, zLeft, width, yBottom, yCenter, headRadius, holeRadius));
            AssertAcceptedStl(solid, Fixture);
        }

        [Test]
        public void TabbedPlateSlantAngle_Deg71()
        {
            // 斜边角第二基线：采用图纸实测件参数（宽 375 / 孔心高 340 / R125 ⇒ 名义 ≈70.90°），
            // 先用 TabbedPlateSlantAngleDeg 钉住角度语义，再钉整件 STL。
            // 注：简报原拟 θ≈10° 浅斜边参数域，实测该域肩部矩形只覆盖 [zLeft, zLeft+R·sinθ]，
            // θ<30° 时覆盖不足到「圆头顶与盒顶相接处」⇒ 圆头外侧残留平台 + 竖壁台阶
            // （已登记 DEFECT-008，复现测试 D8TabbedPlateShallowSlantTest），
            // 故基线落在构造干净（θ>30°）的参数域。
            const double xCenter = 0, thickness = 20, zLeft = 50, width = 375;
            const double yBottom = 0, yCenter = 340, headRadius = 125, holeRadius = 40;

            double angle = CsgHelpers.TabbedPlateSlantAngleDeg(width, yBottom, yCenter, headRadius);
            Assert.That(angle, Is.EqualTo(70.90).Within(0.01), $"斜边夹角应 ≈70.90°（实际 {angle:F4}°）");

            var solid = Eval(CsgHelpers.TabbedPlate(
                xCenter, thickness, zLeft, width, yBottom, yCenter, headRadius, holeRadius));
            AssertAcceptedStl(solid, Fixture);
        }

        [Test]
        public void FilletConcave_InnerCorner()
        {
            // 100×100×10 板挖去 [15,55]² 缺口 ⇒ 凹角 (15,15)；r=8 圆盘与两面相切补入（空料方向 +X/+Y）。
            const double filletRadius = 8, length = 10;
            var plate = CsgNodes.Box(50, 50, 0, 100, 100, length);
            var notch = CsgNodes.Box(35, 35, 0, 40, 40, length);

            var solid = Eval(CsgHelpers.FilletConcave(
                CsgNodes.Subtract(plate, notch), cx: 15, cy: 15, filletRadius, length, sx: 1, sy: 1));
            AssertAcceptedStl(solid, Fixture);
        }

        [Test]
        public void RoundConvexCorner_Outer()
        {
            // 100×100×10 板的 +X/+Y 凸角 (50,50) 倒圆 r=12：先切 r×r 角尖、再补相切圆盘（圆心 (38,38)）。
            const double cornerRadius = 12, length = 10;
            var body = CsgNodes.Box(0, 0, 0, 100, 100, length);

            var solid = Eval(CsgHelpers.RoundConvexCorner(
                body, cx: 50, cy: 50, cornerRadius, length, inX: -1, inY: -1));
            AssertAcceptedStl(solid, Fixture);
        }

        // ─────────────── 扇形柱（A2 修复后语义） ───────────────

        [Test]
        public void SectorCylinder_90()
        {
            // 轴沿 Y（长 24）、r=8、SectorAngle=90（四分之一扇形楔）：12 段×3 面 + 两片径向封壁各 2 面。
            const double radius = 8, halfHeight = 12, sectorAngle = 90;
            var solid = Solids.Cylinder(new CylinderOptions
            {
                Start = new Vector3D(0, -halfHeight, 0),
                End = new Vector3D(0, halfHeight, 0),
                RadiusStart = radius,
                RadiusEnd = radius,
                SectorAngle = sectorAngle,
            });

            Assert.That(solid.Polygons.Count, Is.EqualTo(Solid.DefaultResolution3D * 3 + 4),
                "扇形柱 = 12 段×3 面 + 两片径向封壁（各 2 面）");
            AssertAcceptedStl(solid, Fixture);
        }

        [Test]
        public void SectorCylinder_270()
        {
            // 同上参数但 SectorAngle=270（四分之三扇形，A2 归一化路径的非锐角分支）。
            const double radius = 8, halfHeight = 12, sectorAngle = 270;
            var solid = Solids.Cylinder(new CylinderOptions
            {
                Start = new Vector3D(0, -halfHeight, 0),
                End = new Vector3D(0, halfHeight, 0),
                RadiusStart = radius,
                RadiusEnd = radius,
                SectorAngle = sectorAngle,
            });

            Assert.That(solid.Polygons.Count, Is.EqualTo(Solid.DefaultResolution3D * 3 + 4),
                "扇形柱 = 12 段×3 面 + 两片径向封壁（各 2 面）");
            AssertAcceptedStl(solid, Fixture);
        }

        // ─────────────── 定向图元 ───────────────

        [Test]
        public void OrientedCube_Custom()
        {
            // CubeOptions 非默认定向：各轴半长 (2.5, 1, 4)（各向异性盒）+ 偏心 (10,−20,5)。
            var solid = Solids.Cube(new CubeOptions
            {
                Center = new Vector3D(10, -20, 5),
                Radius = new Vector3D(2.5, 1, 4),
            });
            AssertAcceptedStl(solid, Fixture);
        }

        [Test]
        public void OrientedSphere_Custom()
        {
            // SphereOptions 定向路径：基向量绕 Z 轴旋转 30°（保持默认基的旋向），
            // 球心 (50,0,0)、半径 30、Resolution 取默认 12。
            const double rotateDeg = 30, radius = 30;
            double rad = rotateDeg * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            var solid = Solids.Sphere(new SphereOptions
            {
                Center = new Vector3D(50, 0, 0),
                Radius = radius,
                XAxis = new Vector3D(cos, sin, 0),
                YAxis = new Vector3D(sin, -cos, 0), // 默认 Y 轴为 −Y：绕 Z 旋转后保持同一旋向
                ZAxis = new Vector3D(0, 0, 1),
                Resolution = Solid.DefaultResolution3D,
            });
            AssertAcceptedStl(solid, Fixture);
        }

        // ─────────────── 弦高容差细分 ───────────────

        [Test]
        public void Tessellation_Default()
        {
            // ChordTolerance = 0（显式包裹）：CsgNodes.Cylinder 经 CsgEvaluator 求值走旧口径 12 段。
            const double radius = 100, height = 30;
            Solid solid;
            using (new TessellationScope(0.0))
            {
                solid = Eval(CsgNodes.Cylinder(new Vector3D(0, 0, 0), radius, height));
            }

            Assert.That(solid.Polygons.Count, Is.EqualTo(Solid.DefaultResolution3D * 3),
                "旧口径：12 段 ×（下盖三角 + 侧面 + 上盖三角）");
            AssertAcceptedStl(solid, Fixture);
        }

        [Test]
        public void Tessellation_TightTolerance()
        {
            // ChordTolerance = 0.5：R100 圆柱按 r(1−cos(θ/2)) ≤ tol 自适应取 32 段（acos 反解）。
            const double radius = 100, height = 30, tolerance = 0.5;
            Solid solid;
            using (new TessellationScope(tolerance))
            {
                solid = Eval(CsgNodes.Cylinder(new Vector3D(0, 0, 0), radius, height));
            }

            Assert.That(solid.Polygons.Count, Is.EqualTo(32 * 3),
                "自适应：R100 / tol 0.5 ⇒ ceil(π/acos(1−0.005)) = 32 段");
            AssertAcceptedStl(solid, Fixture);
        }

        // ─────────────── golden 私有辅助（SolidTest.AssertAcceptedStl 的最小复刻） ───────────────

        /// <summary>
        /// 对齐既有 45 个基线的断言（byte-exact 变体，与 legacy 的口径差异见类注释）：
        /// 文件名 <c>&lt;fixture&gt;.&lt;TestName&gt;.stl</c>；
        /// 内容 = <c>Solid.ToStlString(TestName)</c> 的 UTF-8 无 BOM 字节。
        /// 进入即删除陈旧 pending 残留（与 legacy SolidTest 的 <c>File.Delete(rejectedPath)</c> 同行为，
        /// 避免中断运行留下的 <c>*_.stl</c> 在基线已恢复后让 GoldenGuard 永久转红）；
        /// 基线缺失 ⇒ 写 <c>&lt;fixture&gt;.&lt;TestName&gt;_.stl</c> 残留 + Inconclusive
        /// （由 GoldenGuard 门禁在整套件收尾时判红）；
        /// 基线存在 ⇒ **Windows 上 File.ReadAllBytes 逐字节比对**（Windows-pinned 指纹），不一致即失败；
        /// **非 Windows** 走语义校验（DEFECT-011：solid 名 + 三角形数精确、顶点 1e-4 绝对，见
        /// <see cref="FindStlSemanticDivergence"/>；行尾 CRLF/LF 不参与比对）。
        /// </summary>
        static void AssertAcceptedStl(Solid solid, string fixture)
        {
            var testName = TestContext.CurrentContext.Test.Name;
            var results = GoldenPaths.ResultsDirectory(Assembly.GetExecutingAssembly().Location);
            Assert.That(results, Is.Not.Null.And.Not.Empty, "GoldenPaths 未能定位 Results 目录");
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                .GetBytes(solid.ToStlString(testName));

            var acceptedPath = Path.Combine(results!, $"{fixture}.{testName}.stl");
            var pendingPath = Path.Combine(results!, $"{fixture}.{testName}_.stl");

            // 同 legacy SolidTest.AssertAcceptedStl：先清掉上次中断运行留下的 pending 残留——
            // 否则基线（升级/修复）恢复后，陈旧 *_.stl 会让 GoldenGuard 永久转红。
            if (File.Exists(pendingPath))
                File.Delete(pendingPath);

            if (!File.Exists(acceptedPath))
            {
                File.WriteAllBytes(pendingPath, bytes);
                Assert.Inconclusive(
                    $"golden 基线缺失：已写 {fixture}.{testName}_.stl；核对后用 tools/promote-goldens.sh 升级。");
            }

            var acceptedBytes = File.ReadAllBytes(acceptedPath);

            if (OperatingSystem.IsWindows())
            {
                // Windows：维持原有**逐字节指纹**（golden 按 Windows 输出冻结 = Windows-pinned；DEFECT-011）。
                Assert.That(acceptedBytes, Is.EqualTo(bytes),
                    $"golden 基线与当前输出不一致：{acceptedPath}");
                return;
            }

            // 非 Windows（DEFECT-011）：跨平台 libm（UCRT vs glibc）的 ≤1 ULP 差经镶嵌/布尔传播后
            // 会改变「R」格式数字文本 ⇒ 字节比对不可移植；本平台改为语义校验（名/三角形数 + 顶点 1e-4）。
            var goldenText = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(acceptedBytes);
            var currentText = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(bytes);
            var semanticDivergence = FindStlSemanticDivergence(goldenText, currentText);
            Assert.That(semanticDivergence, Is.Null,
                semanticDivergence == null
                    ? string.Empty
                    : $"非 Windows 语义比对失败（平台：{RuntimeInformation.OSDescription}）：{semanticDivergence}"
                      + $"；golden 基线：{acceptedPath}。字节级指纹为 Windows-pinned（DEFECT-011），本平台走语义层。");
        }

        // ─────────────── 跨平台语义比对（DEFECT-011；非 Windows 分支） ───────────────

        /// <summary>
        /// 顶点坐标绝对容差：1e-4。写出为最短往返文本（非定点 6 位），跨平台 ULP 级漂移远小于 1e-4；
        /// 真实几何改动（大于 0.1 µm 量级）远超此容差 ⇒ 既容纳 libm 抖动又不放过实际回归。
        /// </summary>
        internal const double StlCoordinateTolerance = 1e-4;

        /// <summary>计数/拓扑不一致的统一提示（DEFECT-011 的「响亮通道」：不静默通过）。</summary>
        internal const string TopologyDivergenceHint =
            "计数/拓扑不一致属 DEFECT-011 的拓扑敏感通道：非 Windows 语义层不静默通过，须人工定性（数字长度 vs 拓扑翻转）。";

        /// <summary>
        /// 非 Windows 语义比对（DEFECT-011）：逐字节指纹是 Windows-pinned（golden 按 Windows 输出冻结），
        /// 跨平台 libm（UCRT vs glibc）的 ≤1 ULP 差经镶嵌传播会改变「R」格式数字文本，故 Linux 侧改为
        /// 语义校验——首行 <c>solid &lt;name&gt;</c> 相等、三角形数相等、逐三角形逐顶点坐标 1e-4 绝对容差；
        /// 行尾（CRLF/LF）不参与比对。三角形数不一致 = 拓扑敏感，照样判红（不静默通过）。
        /// 返回 null = 语义等价；否则为首个分歧的完整描述。
        /// </summary>
        internal static string? FindStlSemanticDivergence(string expectedStl, string actualStl)
        {
            if (expectedStl == null) throw new ArgumentNullException(nameof(expectedStl));
            if (actualStl == null) throw new ArgumentNullException(nameof(actualStl));

            var golden = TryParseAsciiStl(expectedStl, "golden", out var goldenError);
            if (golden == null)
                return goldenError;
            var current = TryParseAsciiStl(actualStl, "当前输出", out var currentError);
            if (current == null)
                return currentError;

            if (golden.Value.Name != current.Value.Name)
                return $"solid 名不一致（首行）：golden \"solid {golden.Value.Name}\" vs 当前 \"solid {current.Value.Name}\"";

            // Vertices 存的是逐三角形展开的分量（每三角形 3 顶点 × 3 分量 = 9 个数）。
            var goldenTriangles = golden.Value.Vertices.Count / 9;
            var currentTriangles = current.Value.Vertices.Count / 9;
            if (goldenTriangles != currentTriangles)
                return $"三角形数不一致：golden {goldenTriangles} vs 当前 {currentTriangles}。{TopologyDivergenceHint}";

            for (var t = 0; t < goldenTriangles; t++)
            {
                for (var v = 0; v < 3; v++)
                {
                    var baseIndex = (t * 3 + v) * 3;
                    for (var k = 0; k < 3; k++)
                    {
                        var goldenValue = golden.Value.Vertices[baseIndex + k];
                        var currentValue = current.Value.Vertices[baseIndex + k];
                        var difference = Math.Abs(goldenValue - currentValue);
                        if (difference > StlCoordinateTolerance)
                            return $"三角形 {t} 顶点 {v} 坐标 [{k}]（x=0/y=1/z=2）：golden {FormatCoordinate(goldenValue)}"
                                 + $" vs 当前 {FormatCoordinate(currentValue)}（绝对差 {FormatCoordinate(difference)}）"
                                 + "超出 1e-4 绝对容差";
                    }
                }
            }

            return null;
        }

        /// <summary>ASCII STL 的语义解析结果：首行名 + 逐三角形展开的顶点分量（每三角形 9 个数：a.xyz b.xyz c.xyz）。</summary>
        readonly struct ParsedStl
        {
            public ParsedStl(string name, List<double> vertices)
            {
                Name = name;
                Vertices = vertices;
            }

            public string Name { get; }

            public List<double> Vertices { get; }
        }

        /// <summary>
        /// 解析 <c>Formats.WriteStl</c> 的 ASCII 形态（solid/facet normal/outer loop/vertex×3/endloop/endfacet/endsolid）。
        /// 行尾 CRLF/LF 均可（跨平台检出与输出的换行差异不参与语义）；结构不符/数字解析失败以分歧消息收敛
        /// （不让异常穿透，保证 Linux 侧问题在断言消息里可直接定位）。
        /// </summary>
        static ParsedStl? TryParseAsciiStl(string text, string what, out string? error)
        {
            var lines = text.Split('\n');
            var vertices = new List<double>();
            var index = 0;

            string? NextNonEmptyLine()
            {
                while (index < lines.Length)
                {
                    var line = lines[index++].TrimEnd('\r');
                    if (line.Length > 0)
                        return line;
                }

                return null;
            }

            var first = NextNonEmptyLine();
            if (first == null)
            {
                error = $"{what} STL 为空文件";
                return null;
            }

            if (!first.StartsWith("solid ", StringComparison.Ordinal))
            {
                error = $"{what} STL 首行不是 \"solid <name>\"：\"{first}\"";
                return null;
            }

            var name = first.Substring("solid ".Length);
            while (true)
            {
                var line = NextNonEmptyLine();
                if (line == null)
                {
                    error = $"{what} STL 缺少 endsolid 行";
                    return null;
                }

                if (line.StartsWith("endsolid", StringComparison.Ordinal))
                {
                    if (line != "endsolid " + name)
                    {
                        error = $"{what} STL 的 endsolid 行与首行名不一致：\"{line}\"";
                        return null;
                    }

                    error = null;
                    return new ParsedStl(name, vertices);
                }

                if (!line.StartsWith("facet normal ", StringComparison.Ordinal)
                    || !TryParseTriple(line.Substring("facet normal ".Length), out _))
                {
                    error = $"{what} STL 期望 \"facet normal nx ny nz\" 行：\"{line}\"";
                    return null;
                }

                if (NextNonEmptyLine() != "outer loop")
                {
                    error = $"{what} STL 缺少 outer loop 行";
                    return null;
                }

                for (var v = 0; v < 3; v++)
                {
                    var vertexLine = NextNonEmptyLine();
                    if (vertexLine == null
                        || !vertexLine.StartsWith("vertex ", StringComparison.Ordinal)
                        || !TryParseTriple(vertexLine.Substring("vertex ".Length), out var vertex))
                    {
                        error = $"{what} STL 缺少/无法解析第 {v + 1} 个 vertex 行";
                        return null;
                    }

                    vertices.AddRange(vertex);
                }

                if (NextNonEmptyLine() != "endloop")
                {
                    error = $"{what} STL 缺少 endloop 行";
                    return null;
                }

                if (NextNonEmptyLine() != "endfacet")
                {
                    error = $"{what} STL 缺少 endfacet 行";
                    return null;
                }
            }
        }

        /// <summary>解析前缀之后载荷里的三个数（如 "vertex …" / "facet normal …" 去掉前缀后的 "x y z"；Invariant）。</summary>
        static bool TryParseTriple(string payload, out double[] values)
        {
            var parts = payload.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            values = new double[3];
            if (parts.Length != 3)
                return false;

            for (var i = 0; i < 3; i++)
            {
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                    return false;
            }

            return true;
        }

        static string FormatCoordinate(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        // ─────────────── 语义比对器自测（DEFECT-011；合成输入，两平台确定性） ───────────────

        [Test]
        public void SemanticStlComparison_LineEndingDifference_IsEquivalent()
        {
            var crlf = SyntheticStl().Replace("\n", "\r\n");
            Assert.That(FindStlSemanticDivergence(crlf, SyntheticStl()), Is.Null,
                "行尾差异（CRLF vs LF：跨平台检出/输出的换行形态）不参与语义比对");
        }

        [Test]
        public void SemanticStlComparison_CoordinateDrift_WithinToleranceIsEquivalent_BeyondIsCaught()
        {
            var golden = SyntheticStl();

            // ULP 级漂移（真实跨平台场景）：1 → 1.0000000000000002 ⇒ 放行。
            var ulpDrifted = golden.Replace("vertex 1 0 0", "vertex 1.0000000000000002 0 0");
            Assert.That(ulpDrifted, Is.Not.EqualTo(golden), "测试前提：漂移必须真的改变字节");
            Assert.That(FindStlSemanticDivergence(golden, ulpDrifted), Is.Null,
                "ULP 级坐标漂移必须在 1e-4 容差内放行");

            // 容差内但超过 ULP（5e-5 < 1e-4）⇒ 仍放行。
            var within = golden.Replace("vertex 1 0 0", "vertex 1.00005 0 0");
            Assert.That(FindStlSemanticDivergence(golden, within), Is.Null);

            // 超容差（1e-3 > 1e-4）⇒ 判分歧并指到首个分歧点。
            var beyond = golden.Replace("vertex 1 0 0", "vertex 1.001 0 0");
            var divergence = FindStlSemanticDivergence(golden, beyond);
            Assert.That(divergence, Is.Not.Null, "超出 1e-4 的坐标漂移必须报分歧");
            StringAssert.Contains("三角形 0 顶点 1", divergence);
            StringAssert.Contains("超出 1e-4 绝对容差", divergence);
        }

        [Test]
        public void SemanticStlComparison_TriangleCountMismatch_IsLoud()
        {
            var golden = SyntheticStl();
            var secondFacet = "facet normal 0 0 1\nouter loop\nvertex 0 0 1\nvertex 2 0 1\nvertex 0 2 1\nendloop\nendfacet\n";
            var truncated = golden.Replace(secondFacet, string.Empty);

            var divergence = FindStlSemanticDivergence(golden, truncated);
            Assert.That(divergence, Is.Not.Null, "三角形数不一致不得静默通过（拓扑敏感通道）");
            StringAssert.Contains("三角形数不一致", divergence);
            StringAssert.Contains("DEFECT-011", divergence);
        }

        [Test]
        public void SemanticStlComparison_SolidNameMismatch_IsCaught()
        {
            var golden = SyntheticStl();
            var renamed = golden.Replace("solid Synthetic", "solid Other").Replace("endsolid Synthetic", "endsolid Other");

            var divergence = FindStlSemanticDivergence(golden, renamed);
            Assert.That(divergence, Is.Not.Null, "solid <name> 首行不一致必须报分歧");
            StringAssert.Contains("solid 名不一致", divergence);
        }

        [Test]
        public void SemanticStlComparison_MalformedInput_IsReportedNotThrown()
        {
            var divergence = FindStlSemanticDivergence(string.Empty, SyntheticStl());
            Assert.That(divergence, Is.Not.Null, "空 golden 必须收敛为分歧消息，不得抛异常");
            StringAssert.Contains("空文件", divergence);

            Assert.That(FindStlSemanticDivergence("solid A\nendsolid A\n", "solid A\nendsolid A\n"), Is.Null,
                "零三角形但结构完好仍应语义等价");
        }

        [Test]
        public void SemanticStlComparison_RealGoldens_ParseAndMatchThemselves()
        {
            var goldens = RealModuleGoldens();
            Assert.That(goldens.Length, Is.GreaterThanOrEqualTo(10),
                "真实 STL golden 数量不足以构成非真空检查");

            foreach (var goldenPath in goldens)
            {
                // 语义解析器必须吃下每一个真实 golden（曲面镶嵌 + 长数字文本 + 检出态行尾）并自比等价；
                // 若写出格式漂移使解析失败，这里（两平台）就会判红，而非等到 Linux 才暴露。
                var text = ReadGoldenText(goldenPath);
                Assert.That(FindStlSemanticDivergence(text, text), Is.Null,
                    $"真实 golden 必须被语义层解析且自比等价：{goldenPath}");
            }
        }

        [Test]
        public void SemanticStlComparison_RealGoldenWithUlpDrift_IsEquivalent()
        {
            var results = GoldenPaths.ResultsDirectory(Assembly.GetExecutingAssembly().Location);
            Assert.That(results, Is.Not.Null.And.Not.Empty, "GoldenPaths 未能定位 Results 目录");
            var goldenPath = Path.Combine(results!, $"{Fixture}.TabbedPlate_Default.stl");
            Assert.That(File.Exists(goldenPath), Is.True, $"golden 缺失：{goldenPath}");

            // 整文件逐数字 1 ULP 上移 = 模拟 glibc vs UCRT 的最坏情形（每个「R」文本都可能漂移）。
            // 对照：旧的字节比对在这种输入上必然失败（本任务的分层动机）。
            var goldenText = ReadGoldenText(goldenPath);
            var drifted = UlpDriftStlNumbers(goldenText);
            Assert.That(drifted, Is.Not.EqualTo(goldenText), "测试前提：漂移必须改变文本");
            Assert.That(FindStlSemanticDivergence(goldenText, drifted), Is.Null,
                "真实 golden 的逐数字 1 ULP 漂移必须被 1e-4 语义容差放行");
        }

        /// <summary>枚举真实模块基线（排除 pending 残留 *_ .stl）。</summary>
        static string[] RealModuleGoldens()
        {
            var results = GoldenPaths.ResultsDirectory(Assembly.GetExecutingAssembly().Location);
            Assert.That(results, Is.Not.Null.And.Not.Empty, "GoldenPaths 未能定位 Results 目录");

            var goldens = new List<string>();
            foreach (var path in Directory.GetFiles(results!, $"{Fixture}.*.stl"))
            {
                if (!path.EndsWith("_.stl", StringComparison.Ordinal))
                    goldens.Add(path);
            }

            goldens.Sort(StringComparer.Ordinal);
            return goldens.ToArray();
        }

        static string ReadGoldenText(string path)
        {
            return File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        /// <summary>把 STL 文本里的每个数字 token 上移 1 ULP（模拟 glibc vs UCRT 的数字文本漂移）。</summary>
        static string UlpDriftStlNumbers(string stl)
        {
            return Regex.Replace(stl, "-?[0-9]+(?:\\.[0-9]+)?(?:[eE][+-]?[0-9]+)?", match =>
            {
                var value = double.Parse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                return UlpUp(value).ToString("R", CultureInfo.InvariantCulture);
            });
        }

        /// <summary>把双精度沿位模式上移 1 ULP（模拟跨平台 libm 的 ≤1 ULP 差）。</summary>
        static double UlpUp(double value)
        {
            return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(value) + 1);
        }

        /// <summary>合成 ASCII STL（语义比对器自测专用：两条 facet、精确可表示坐标；LF 行尾）。</summary>
        static string SyntheticStl()
        {
            return "solid Synthetic\n"
                + "facet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\n"
                + "facet normal 0 0 1\nouter loop\nvertex 0 0 1\nvertex 2 0 1\nvertex 0 2 1\nendloop\nendfacet\n"
                + "endsolid Synthetic\n";
        }
    }
}
