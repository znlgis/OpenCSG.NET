using System;
using System.IO;
using System.Reflection;
using System.Text;
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
    /// golden 约定与既有 45 个基线一致（SolidTest.AssertAcceptedStl 因文件冻结不可改，
    /// 按同约定最小复制为下方私有辅助）：文件名 &lt;Fixture&gt;.&lt;TestName&gt;.stl；
    /// 内容 = Solid.ToStlString("&lt;TestName&gt;")（ASCII / UTF-8 无 BOM）；基线缺失 ⇒ 写
    /// &lt;Fixture&gt;.&lt;TestName&gt;_.stl 并 Assert.Inconclusive（GoldenGuard 随即转红）；
    /// 基线存在 ⇒ 与 ToStlString 输出逐字节比对，不一致即失败。
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
            // θ<30° 时覆盖不足到「圆头顶与盒顶相接处」⇒ 圆头外侧残留平台 + 竖壁台阶（task-2 报告记录），
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
        /// 与既有 45 个基线一致的断言：文件名 <c>&lt;fixture&gt;.&lt;TestName&gt;.stl</c>；
        /// 内容 = <c>Solid.ToStlString(TestName)</c> 的 UTF-8 无 BOM 字节。
        /// 基线缺失 ⇒ 写 <c>&lt;fixture&gt;.&lt;TestName&gt;_.stl</c> 残留 + Inconclusive
        /// （由 GoldenGuard 门禁在整套件收尾时判红）；
        /// 基线存在 ⇒ File.ReadAllBytes 逐字节比对，不一致即失败。
        /// </summary>
        static void AssertAcceptedStl(Solid solid, string fixture)
        {
            var testName = TestContext.CurrentContext.Test.Name;
            var results = GoldenPaths.ResultsDirectory(Assembly.GetExecutingAssembly().Location);
            Assert.That(results, Is.Not.Null.And.Not.Empty, "GoldenPaths 未能定位 Results 目录");
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                .GetBytes(solid.ToStlString(testName));

            var acceptedPath = Path.Combine(results!, $"{fixture}.{testName}.stl");
            if (!File.Exists(acceptedPath))
            {
                File.WriteAllBytes(Path.Combine(results!, $"{fixture}.{testName}_.stl"), bytes);
                Assert.Inconclusive(
                    $"golden 基线缺失：已写 {fixture}.{testName}_.stl；核对后用 tools/promote-goldens.sh 升级。");
            }

            Assert.That(File.ReadAllBytes(acceptedPath), Is.EqualTo(bytes),
                $"golden 基线与当前输出不一致：{acceptedPath}");
        }
    }
}
