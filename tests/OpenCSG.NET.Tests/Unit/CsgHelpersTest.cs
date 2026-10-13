using System;
using System.Collections.Generic;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// CsgHelpers 的 L1 单元回填（Plan C / Task 8）：16 个方法逐一的**解析期望**功能用例。
    /// Throw 路径（RevolvedStack 4 条 + HalfPlaneCut 零方向）已由 Task 7 的 CsgHelpersContractTest 覆盖，
    /// 此处不重复。断言口径：包络 = GeometryAssert.Bounds（顶点统计）、体积 = GeometryAssert.SignedVolume
    /// （散度定理，法线朝外为正）。凡体积来自布尔组装者用 1e-6 相对容差（BSP 数值噪声），纯仿射体用 1e-9。
    /// </summary>
    [TestFixture]
    public class CsgHelpersTest
    {
        const double Tol = 1e-9;

        static Solid Eval(CsgNode node) => CsgEvaluator.Evaluate(node);

        static void AssertBounds(Solid solid, Vector3D min, Vector3D max, string message, double tolerance = Tol)
            => GeometryAssert.AssertBounds(solid, min, max, tolerance, message);

        static void AssertVolumeRel(Solid solid, double expected, string message)
            => GeometryAssert.AssertVolume(solid, expected, Math.Abs(expected) * 1e-6 + 1e-9, message);

        static double RegularPolygonArea(double radius, int segments)
            => segments / 2.0 * radius * radius * Math.Sin(2 * Math.PI / segments);

        // ── 1. Place ────────────────────────────────────────────────────────────────

        [Test]
        public void Place_RotateAboutOriginThenTranslate_BoundsAnalytic()
        {
            // 语义（文档）：先绕全局原点旋转、再平移。基体 (4,2,2) 绕 Z 转 90° ⇒ X±1/Y±2，再 +（10,5,0）。
            var body = CsgNodes.Box(0, 0, 0, 4, 2, 2);
            var placed = Eval(CsgHelpers.Place(body, new Vector3D(10, 5, 0), new Vector3D(0, 0, 90)));

            AssertBounds(placed, new Vector3D(9, 3, -1), new Vector3D(11, 7, 1), "Place（旋转→平移）");
            GeometryAssert.AssertVolume(placed, 16.0, Tol, "Place 刚体变换保体积");
        }

        [Test]
        public void Place_DiffersFromNaiveTransformNode_TranslateThenRotate()
        {
            // 文档点名的坑：CsgNodes.Transform(t, r, node) 是「先平移、后绕**全局原点**旋转」。
            // (4,2,2) 先 +(10,5,0)：X[8,12]、Y[4,6]；再 (x,y)→(−y,x)：X[−6,−4]、Y[8,12]。
            var body = CsgNodes.Box(0, 0, 0, 4, 2, 2);
            var naive = Eval(CsgNodes.Transform(new Vector3D(10, 5, 0), new Vector3D(0, 0, 90), body));

            AssertBounds(naive, new Vector3D(-6, 8, -1), new Vector3D(-4, 12, 1), "TransformNode 先平移后绕原点旋转");
        }

        // ── 2. RotateAboutCenter ───────────────────────────────────────────────────

        [Test]
        public void RotateAboutCenter_OffsetCenterZ90_BoundsAnalytic()
        {
            // T(c)·R·T(−c)：基点 (2,0,0)，盒点相对中心 (x−2, y) ∈ [−4,0]×[−1,1]；
            // 转 90° 后 (X,Y)→(−y, x−2) ⇒ X∈[−1,1]、Y∈[−4,0]，再 +c ⇒ X[1,3]、Y[−4,0]。
            var body = CsgNodes.Box(0, 0, 0, 4, 2, 2);
            var rotated = Eval(CsgHelpers.RotateAboutCenter(body, new Vector3D(2, 0, 0), new Vector3D(0, 0, 90)));

            AssertBounds(rotated, new Vector3D(1, -4, -1), new Vector3D(3, 0, 1), "RotateAboutCenter((2,0,0), Z90°)");
            GeometryAssert.AssertVolume(rotated, 16.0, Tol, "绕任意点旋转保体积");
        }

        // ── 3/4. CylinderAlongX / CylinderAlongZ ──────────────────────────────────

        [Test]
        public void CylinderAlongX_AxisAlongX_LengthOnX_RadiusOnYZ()
        {
            const double r = 2, h = 6;
            var solid = Eval(CsgHelpers.CylinderAlongX(new Vector3D(10, 20, 30), r, h));

            AssertBounds(solid, new Vector3D(7, 18, 28), new Vector3D(13, 22, 32), "CylinderAlongX 轴向 = X", 1e-6);
            // 12 段离散整圆面积 = 0.9549πr²（相对 −4.5%），故对 πr²h 放宽到 5%。
            GeometryAssert.AssertVolume(solid, Math.PI * r * r * h, 0.05 * Math.PI * r * r * h,
                "CylinderAlongX 体积 ≈ πr²h（12 段离散）");
        }

        [Test]
        public void CylinderAlongZ_AxisAlongZ_LengthOnZ_RadiusOnXY()
        {
            const double r = 2, h = 6;
            var solid = Eval(CsgHelpers.CylinderAlongZ(new Vector3D(10, 20, 30), r, h));

            AssertBounds(solid, new Vector3D(8, 18, 27), new Vector3D(12, 22, 33), "CylinderAlongZ 轴向 = Z", 1e-6);
            // 12 段离散面积 = 3r²：比值 3/π ≈ 0.9549，相对 πr²h 的差 ≈ 4.5%。
            AssertVolumeRel(solid, RegularPolygonArea(r, Solid.DefaultResolution3D) * h, "CylinderAlongZ 12 段多边形体积");
        }

        // ── 5. PrismXY ─────────────────────────────────────────────────────────────

        [Test]
        public void PrismXY_RectangleSection_ExtrudedAlongZ_CenteredAtZCenter()
        {
            // (X,Y) 断面向 Z 拉伸（Extrude z∈[0,t] + 平移 zCenter−t/2）⇒ 以 zCenter 居中。
            var points = new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 5.0), (0.0, 5.0) };
            var solid = Eval(CsgHelpers.PrismXY(points, zCenter: 20, thickness: 3));

            AssertBounds(solid, new Vector3D(0, 0, 18.5), new Vector3D(10, 5, 21.5), "PrismXY 以 zCenter 居中");
            GeometryAssert.AssertVolume(solid, 10 * 5 * 3, Tol, "PrismXY 体积 = 断面面积 × 高");
        }

        [Test]
        public void PrismXY_ClockwiseSection_NormalizedToPositiveVolume()
        {
            // 绕向由 PolygonProfile 求值自动规整为逆时针 ⇒ 顺时针输入不得得到负体积。
            var clockwise = new[] { (0.0, 0.0), (0.0, 5.0), (10.0, 5.0), (10.0, 0.0) };
            var solid = Eval(CsgHelpers.PrismXY(clockwise, zCenter: 0, thickness: 2));

            AssertBounds(solid, new Vector3D(0, 0, -1), new Vector3D(10, 5, 1), "顺时针输入包络不变");
            GeometryAssert.AssertVolume(solid, 100.0, Tol, "顺时针断面经规整后体积仍为正 100");
        }

        // ── 6. PrismXZ ─────────────────────────────────────────────────────────────

        [Test]
        public void PrismXZ_SectionExtrudedAlongY_CenteredAtYCenter()
        {
            // 文档口径：断面 (X 径向, Z 轴向) 绕 X +90°（x,y,z)→(x,−z,y)：拉伸 z∈[0,l] ⇒ y∈[−l,0]，
            // 再 +l/2 居中后整体平移 yCenter ⇒ Y ∈ [yCenter−l/2, yCenter+l/2]；断面 Z 原样落到 Z。
            var points = new[] { (0.0, 0.0), (10.0, 0.0), (0.0, 4.0) };
            var solid = Eval(CsgHelpers.PrismXZ(points, yCenter: 100, length: 3));

            AssertBounds(solid, new Vector3D(0, 98.5, 0), new Vector3D(10, 101.5, 4), "PrismXZ 的 Y 以 yCenter 居中");
            GeometryAssert.AssertVolume(solid, 0.5 * 10 * 4 * 3, Tol, "PrismXZ 体积 = 三角形面积 × 周向长度");
        }

        // ── 7/8. AddArc（两例：正向含端点、反向单步） ─────────────────────────────

        [Test]
        public void AddArc_SkipsStartAndIncludesEnd_ExactPoints()
        {
            var points = new List<(double X, double Y)>();
            CsgHelpers.AddArc(points, cx: 0, cy: 0, r: 2, aFromDeg: 0, aToDeg: 90, steps: 2);

            Assert.That(points.Count, Is.EqualTo(2), "AddArc 追加 steps 个点（跳过起点）");
            Assert.That(points[0].X, Is.EqualTo(2 * Math.Cos(Math.PI / 4)).Within(Tol), "AddArc[0].X = 45° 处");
            Assert.That(points[0].Y, Is.EqualTo(2 * Math.Sin(Math.PI / 4)).Within(Tol), "AddArc[0].Y = 45° 处");
            Assert.That(points[1].X, Is.EqualTo(0.0).Within(Tol), "AddArc[1] = 90° 终点 X");
            Assert.That(points[1].Y, Is.EqualTo(2.0).Within(Tol), "AddArc[1] = 90° 终点 Y");
        }

        [Test]
        public void AddArc_ReverseSweep_EndsAtStartAngle()
        {
            // 90→0 反向、steps=2：跳过起点 90°，采样 45° 与终点 0°。
            var points = new List<(double X, double Y)>();
            CsgHelpers.AddArc(points, cx: 1, cy: 1, r: 2, aFromDeg: 90, aToDeg: 0, steps: 2);

            Assert.That(points.Count, Is.EqualTo(2));
            Assert.That(points[0].X, Is.EqualTo(1 + 2 * Math.Cos(Math.PI / 4)).Within(Tol), "反向中点在 45°");
            Assert.That(points[0].Y, Is.EqualTo(1 + 2 * Math.Sin(Math.PI / 4)).Within(Tol), "反向中点在 45°");
            Assert.That(points[1].X, Is.EqualTo(3.0).Within(Tol), "反向终点 = 0°（跳过的是起点 90°）");
            Assert.That(points[1].Y, Is.EqualTo(1.0).Within(Tol), "反向终点 = 0°");
        }

        // ── 9. CornerWedge（两例：正腿、负腿） ────────────────────────────────────

        [Test]
        public void CornerWedge_PositiveLegs_RightTriangularPrism()
        {
            // 直角三角形 (0,0)、(leg,0)、(0,leg) 拉厚 ⇒ V = ½·leg²·thickness。
            var solid = Eval(CsgHelpers.CornerWedge(0, 0, dx: 1, dy: 1, leg: 3, zCenter: 5, thickness: 2));

            AssertBounds(solid, new Vector3D(0, 0, 4), new Vector3D(3, 3, 6), "CornerWedge 正两腿包络");
            GeometryAssert.AssertVolume(solid, 0.5 * 3 * 3 * 2, Tol, "CornerWedge 体积 = ½·leg²·厚");
        }

        [Test]
        public void CornerWedge_NegativeLegs_ThirdQuadrantTriangle()
        {
            var solid = Eval(CsgHelpers.CornerWedge(10, 20, dx: -1, dy: -1, leg: 4, zCenter: 0, thickness: 5));

            AssertBounds(solid, new Vector3D(6, 16, -2.5), new Vector3D(10, 20, 2.5), "CornerWedge 负两腿包络");
            GeometryAssert.AssertVolume(solid, 0.5 * 4 * 4 * 5, Tol, "负腿输入经绕向规整后体积仍为正");
        }

        // ── 10/11. TabbedPlateSlantAngleDeg / TabbedPlate ─────────────────────────

        [Test]
        public void TabbedPlateSlantAngleDeg_MatchesDocumentedDrawingAngles()
        {
            // 源码注释实测读数（与图纸名义 ≈70° 吻合的三件零件）：
            // (1275/2850/R150) ⇒ 71.27°、(375/340/R125) ⇒ 70.90°、(323/190/R125) ⇒ 70.92°。
            Assert.That(CsgHelpers.TabbedPlateSlantAngleDeg(1275, 0, 2850, 150),
                Is.EqualTo(71.27).Within(0.01), "SlantAngle 件宽 1275 / 孔心高 2850 / R150");
            Assert.That(CsgHelpers.TabbedPlateSlantAngleDeg(375, 0, 340, 125),
                Is.EqualTo(70.90).Within(0.01), "SlantAngle 件宽 375 / 孔心高 340 / R125");
            Assert.That(CsgHelpers.TabbedPlateSlantAngleDeg(323, 0, 190, 125),
                Is.EqualTo(70.92).Within(0.01), "SlantAngle 件宽 323 / 孔心高 190 / R125");
        }

        [Test]
        public void TabbedPlate_BoundsAnalytic_AndSlantFaceAngleMatchesHelper()
        {
            const double xCenter = 0, thickness = 20, zLeft = 50, width = 400;
            const double yBottom = 0, yCenter = 300, headRadius = 100, holeRadius = 40;

            var solid = Eval(CsgHelpers.TabbedPlate(
                xCenter, thickness, zLeft, width, yBottom, yCenter, headRadius, holeRadius));

            // 包络（解析）：X = ±厚/2；Y = [yBottom, yCenter+R]（圆头顶，高于盒顶 yT）；Z = [zLeft, zLeft+width]
            // （盒的右下角点恰在切线延长线上，底边到 zOut 保留）。
            AssertBounds(solid, new Vector3D(-10, 0, 50), new Vector3D(10, 400, 450), "TabbedPlate 包络", 1e-6);

            // 斜边切面（法向 X≈0、Y/Z 分量显著）与水平（Z 轴）的夹角应等于 SlantAngleDeg 的返回值
            // （两者同源：切面法向 (0, run, yT)/L ⇒ 夹角 = atan2(|nz|,|ny|) = atan2(yT, run)）。
            var expected = CsgHelpers.TabbedPlateSlantAngleDeg(width, yBottom, yCenter, headRadius);
            var measured = MeasureSlantAngleDeg(solid);
            Assert.That(measured, Is.EqualTo(expected).Within(1e-6),
                $"TabbedPlate 斜切面夹角 {measured:F6}° 应等于 TabbedPlateSlantAngleDeg {expected:F6}°");

            Assert.That(GeometryAssert.SignedVolume(solid), Is.GreaterThan(0.0), "成品法线朝外（净体积为正）");
        }

        [Test]
        public void TabbedPlate_NegativeZLeft_MirrorsAboutZ_EqualVolume()
        {
            const double xCenter = 0, thickness = 20, width = 400;
            const double yBottom = 0, yCenter = 300, headRadius = 100, holeRadius = 40;

            var positive = Eval(CsgHelpers.TabbedPlate(
                xCenter, thickness, +50, width, yBottom, yCenter, headRadius, holeRadius));
            var negative = Eval(CsgHelpers.TabbedPlate(
                xCenter, thickness, -50, width, yBottom, yCenter, headRadius, holeRadius));

            AssertBounds(negative, new Vector3D(-10, 0, -450), new Vector3D(10, 400, -50),
                "zLeft<0 关于 Z=0 的镜像包络", 1e-6);

            var vp = GeometryAssert.SignedVolume(positive);
            var vn = GeometryAssert.SignedVolume(negative);
            Assert.That(vn, Is.EqualTo(vp).Within(1e-9 * Math.Abs(vp) + 1e-6),
                $"镜像（刚体变换）保体积：{vn:R} vs {vp:R}");
        }

        /// <summary>从成品里找斜切面（法向 X 分量 ≈ 0、Y/Z 分量显著）并按 atan2(|nz|,|ny|) 反解与水平夹角（度）。</summary>
        static double MeasureSlantAngleDeg(Solid solid)
        {
            foreach (var polygon in solid.Polygons)
            {
                var n = polygon.Plane.Normal;
                if (Math.Abs(n.X) > 1e-12 || Math.Abs(n.Y) < 1e-3 || Math.Abs(n.Z) < 1e-3)
                    continue;
                return Math.Atan2(Math.Abs(n.Z), Math.Abs(n.Y)) * 180.0 / Math.PI;
            }
            Assert.Fail("未找到斜切面（法向 X≈0 且 Y/Z 分量显著的多边形）");
            return double.NaN;
        }

        // ── 12/13. CornerCut（两例：独立方盒、板角切除） ─────────────────────────

        [Test]
        public void CornerCut_Standalone_RotatedSquare_VolumeAndBounds()
        {
            // 边长 size·√2 的方盒绕 Z 转 45°：轴对齐包络 = ±size，体积 = 2·size²·over。
            var solid = Eval(CsgHelpers.CornerCut(x0: 100, y0: 200, zC: 0, over: 30, size: 5));

            AssertBounds(solid, new Vector3D(95, 195, -15), new Vector3D(105, 205, 15), "CornerCut 45° 方盒包络");
            GeometryAssert.AssertVolume(solid, 2 * 25 * 30, Tol, "CornerCut 体积 = 2·size²·over");
        }

        [Test]
        public void CornerCut_SubtractedFromPlateCorner_RemovesSizeSquaredRightTriangle()
        {
            // 板角在 (0,0) 且料朝 +X+Y：切掉的 = size×size 等腰直角三角形（面积 size²/2），板其余不动。
            const double size = 10, over = 40;
            var plate = CsgNodes.Box(50, 50, 0, 100, 100, over); // X,Y∈[0,100]、Z∈[−20,20]
            var solid = Eval(CsgNodes.Subtract(plate, CsgHelpers.CornerCut(0, 0, 0, over, size)));

            AssertBounds(solid, new Vector3D(0, 0, -20), new Vector3D(100, 100, 20), "切角后包络不变");
            AssertVolumeRel(solid, 100 * 100 * over - 0.5 * size * size * over, "角部三角被按 ½·size² 精确切除");
        }

        // ── 14. RoundConvexCorner ─────────────────────────────────────────────────

        [Test]
        public void RoundConvexCorner_TipReplacedByTangentQuarterDisc_VolumeAnalytic()
        {
            // 凸角 (50,50)、料在 (−1,−1) 方向：先切掉 r×r 角尖方块（全在料内），再补圆心 (50−r,50−r)
            // 与两面相切的圆盘。净体积 = 原体积 − (r² − 四分之一圆盘面积)·厚；
            // 12 段离散下四分之一圆盘 = 3 个 30° 扇段 = 3·½r²·sin30°。
            const double r = 10, len = 10;
            const int segments = Solid.DefaultResolution3D; // 12（ChordTolerance=0 时的旧口径）
            double quarterDisc = 3 * 0.5 * r * r * Math.Sin(2 * Math.PI / segments); // 3 个 30° 扇段
            double expected = 100 * 100 * len - (r * r - quarterDisc) * len;

            var body = CsgNodes.Box(0, 0, 0, 100, 100, len);
            var solid = Eval(CsgHelpers.RoundConvexCorner(body, cx: 50, cy: 50, r: r, length: len, inX: -1, inY: -1));

            AssertBounds(solid, new Vector3D(-50, -50, -5), new Vector3D(50, 50, 5), "圆角只改角尖、包络不变");
            AssertVolumeRel(solid, expected, "凸角圆角 = 切角尖 + 相切补圆（解析净体积）");
        }

        // ── 15. FilletConcave ─────────────────────────────────────────────────────

        [Test]
        public void FilletConcave_AddsTangentDiscAtReentrantCorner_VolumeAnalytic()
        {
            // L 型（100×100 板挖掉 [10,50]² 的角块）凹角 (10,10)、空料方向 (+1,+1)：
            // 补圆心 (10+r,10+r)、与两面相切的圆盘 ⇒ 净体积 = L 型体积 + 圆盘面积×厚。
            const double r = 5, len = 10;
            const int segments = Solid.DefaultResolution3D;
            double disc = RegularPolygonArea(r, segments);

            var plate = CsgNodes.Box(50, 50, 0, 100, 100, len); // X,Y ∈ [0,100]
            var notch = CsgNodes.Box(30, 30, 0, 40, 40, len);   // 挖掉 X,Y ∈ [10,50]
            var bodyNode = CsgNodes.Subtract(plate, notch);
            var body = Eval(bodyNode);
            AssertVolumeRel(body, (100 * 100 - 40 * 40) * len, "前置：L 型板体积 = 大板 − 角块");

            var filleted = Eval(CsgHelpers.FilletConcave(bodyNode, cx: 10, cy: 10, r: r, length: len, sx: 1, sy: 1));

            AssertBounds(filleted, new Vector3D(0, 0, -5), new Vector3D(100, 100, 5), "凹角圆角不越出外轮廓");
            AssertVolumeRel(filleted, (100 * 100 - 40 * 40) * len + disc * len, "凹角圆角 = 增补相切圆盘（解析体积）");
        }

        // ── 16. ArcSegment（两例：首末段钳制、内部段居中） ────────────────────────

        [Test]
        public void ArcSegment_ClampsFirstAndLastSegments_ExactMidAndExtent()
        {
            // 4 段 360°：step = 90°、外扩 overlap·step = 15.75°；首段 [0,105.75]（低端钳制到 a0）、
            // 末段 [254.25,360]（高端钳制到 a0+sweep）；返回 (角中点, radius·角宽)。
            var first = CsgHelpers.ArcSegment(a0: 0, sweep: 360, segments: 4, i: 0, radius: 10);
            Assert.That(first.AMid, Is.EqualTo(52.875).Within(Tol), "首段角中点 = (0 + 105.75)/2");
            Assert.That(first.SegLen, Is.EqualTo(1057.5).Within(Tol), "首段长度 = 10·105.75");

            var last = CsgHelpers.ArcSegment(a0: 0, sweep: 360, segments: 4, i: 3, radius: 10);
            Assert.That(last.AMid, Is.EqualTo(307.125).Within(Tol), "末段角中点 = (254.25 + 360)/2");
            Assert.That(last.SegLen, Is.EqualTo(1057.5).Within(Tol), "末段长度 = 10·105.75");
        }

        [Test]
        public void ArcSegment_InteriorSegment_SymmetricOverlap_ExactMid()
        {
            // 内部段无钳制：8 段 160°（step = 20°、外扩 3.5°）⇒ 第 3 段 [56.5°, 83.5°]。
            var seg = CsgHelpers.ArcSegment(a0: 0, sweep: 160, segments: 8, i: 3, radius: 10);

            Assert.That(seg.AMid, Is.EqualTo(70.0).Within(Tol), "内部段角中点 = 段中心（两侧等量外扩）");
            Assert.That(seg.SegLen, Is.EqualTo(10 * 27.0).Within(Tol), "内部段长度 = 10·(20+2·3.5)");
        }

        // ── 17. PlaceRotateThenTranslate ──────────────────────────────────────────

        [Test]
        public void PlaceRotateThenTranslate_RotateAboutLocalOriginThenMove_BoundsAnalytic()
        {
            // T∘R：先绕局部原点把 (4,2,2) 转 90°（X↔Y 互换 ⇒ X±1/Y±2），再平移到 (10,5,0)。
            var body = CsgNodes.Box(0, 0, 0, 4, 2, 2);
            var placed = Eval(CsgHelpers.PlaceRotateThenTranslate(body, new Vector3D(10, 5, 0), new Vector3D(0, 0, 90)));

            AssertBounds(placed, new Vector3D(9, 3, -1), new Vector3D(11, 7, 1), "PlaceRotateThenTranslate 包络");
            GeometryAssert.AssertVolume(placed, 16.0, Tol, "刚体变换保体积");
        }

        // ── 18. RevolvedStack（功能路径解析量；throw 由 T7 覆盖） ────────────────

        [Test]
        public void RevolvedStack_CylinderPlusFrustum_VolumeAndBoundsAnalytic()
        {
            // 站点 [(1,0),(1,2),(3,4)]：下段等半径圆柱 r=1 h=2、上段圆台 r 1→3 h=2（两段面贴合，体积相加）。
            // V = πr²h + πh/3·(r0²+r0r1+r1²) = 2π + 26π/3 ≈ 33.51。
            var node = CsgHelpers.RevolvedStack(
                new[] { (Radius: 1.0, Y: 0.0), (Radius: 1.0, Y: 2.0), (Radius: 3.0, Y: 4.0) },
                centerX: 0, baseY: 0, centerZ: 0);
            var solid = Eval(node);

            AssertBounds(solid, new Vector3D(-3, 0, -3), new Vector3D(3, 4, 3), "RevolvedStack 包络（径向取最大半径 3）", 1e-6);
            // 12 段离散下圆柱/圆台面积均按正多边形收缩（截面 ≈ −4.5%），故对 π 解析值放宽 5%。
            double expected = Math.PI * 1 * 1 * 2 + Math.PI * 2 * (1 + 3 + 9) / 3.0;
            GeometryAssert.AssertVolume(solid, expected, 0.05 * expected,
                "RevolvedStack 体积 = 圆柱 + 圆台（π 解析值，5% 容差）");
        }
    }
}
