using System;
using System.Linq;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// DEFECT-008 最小复现（登记于 tests/DEFECTS.md 未修复表；程序规则：只报告不改 src/）。
    /// <para>
    /// <b>浅斜角（θ&lt;30°）下肩部矩形欠覆盖</b>：<c>CsgHelpers.TabbedPlate</c> 的肩部只减
    /// <c>boxUpper</c>（z ∈ [zLeft, zLeft + zT]，zT = R·sinθ，CsgHelpers.cs:169-177）；
    /// 台面标高（y = yT）处圆盘外的左段需覆盖到 z = zLeft + R − R·sinθ
    /// ⇒ 覆盖条件 R·sinθ ≥ R − R·sinθ ⇔ <b>θ ≥ 30°</b>。θ&lt;30°（等价 宽 w &gt; 3R + √3·yC）时
    /// z ∈ [zLeft + R·sinθ, 头部 12 边形面片穿出台面处] 之间「头面片与台面之间」的槽体留料
    /// —— 圆头左上外侧残留水平平台 + 竖壁台阶（静默错误几何）。
    /// </para>
    /// <para>
    /// 本用例钉住**当前（错误）读数**（评审独立探针与本人探针 `artifacts/planD/d8probe` 逐位一致）：
    /// θ = 10.000772281737618°；水平台面 y = 298.4805412343433 跨 z ∈ [67.3661, 167.3661]
    /// （外露台阶片 z ∈ [67.36614517360738, 144.32930268656344]）；z = 67.36614517360738 处
    /// 竖壁 y ∈ [253.96868555205128, 298.4805412343433]；净体积 5,633,605.601992406。
    /// 期望（文档契约 CsgHelpers.cs:43-44「左直立边 + 顶部 R 圆头 + 与圆头相切的斜边落至右下角 +
    /// 底边」——无平台/竖壁，顶边界应为圆头弧面/相切斜边）；corrected construction
    /// （一行修法：肩部跨度 zT → R）体积 5,609,937.983120397，多料 +23,667.618872 mm³（≈0.42%）。
    /// <c>[Explicit]</c> 使套件保持绿色、缺陷可一行复现（规则 2）；修复后按规则 5 摘标注迁移。
    /// </para>
    /// </summary>
    [TestFixture]
    public class D8TabbedPlateShallowSlantTest
    {
        // 浅斜边参数域（评审给出的完整参数集）：宽 1810 / 孔心高 200 / R100 ⇒ 名义 θ ≈ 10.0°（< 30°）。
        const double XCenter = 0, Thickness = 20, ZLeft = 50, Width = 1810;
        const double YBottom = 0, YCenter = 200, HeadRadius = 100, HoleRadius = 40;

        // 当前（错误）读数（Release 实测，见 tests/DEFECTS.md DEFECT-008）。
        const double SlantAngleDeg = 10.000772281737618;     // 欠覆盖域：< 30°
        const double FlatDeckY = 298.4805412343433;          // 台面标高；正确构造此处应为圆头弧面
        const double LedgeZMin = 67.36614517360738;          // 竖壁 z = zLeft + R·sinθ
        const double LedgeZMax = 144.32930268656344;         // 头部 12 边形面片穿出台面处
        const double WallYMin = 253.96868555205128;          // 竖壁下沿（头面片处）
        const double CurrentVolumeMm3 = 5633605.601992406;   // 期望（corrected）：5609937.983120397

        [Test]
        [Explicit("DEFECT-008: TabbedPlate 浅斜角 θ<30° 肩部欠覆盖——圆头外侧残留水平平台 + 竖壁（净体积多 23,667.62 mm³）")]
        public void ShallowSlant_LeavesFlatDeckLedgeAndWall_AndExcessVolume()
        {
            // 前置：θ < 30°（欠覆盖域边界 θ=30° ⇔ 宽 = 3R + √3·yC ≈ 646.410，本例 1810 远在其上）。
            var angle = CsgHelpers.TabbedPlateSlantAngleDeg(Width, YBottom, YCenter, HeadRadius);
            Assert.That(angle, Is.EqualTo(SlantAngleDeg).Within(1e-9),
                $"斜边角应 ≈10.0008°（θ<30° 即 R·sinθ < R − R·sinθ 欠覆盖域；实际 {angle:R}°）");
            Assert.That(angle, Is.LessThan(30.0), "DEFECT-008 触发域：θ<30°");

            var solid = CsgEvaluator.Evaluate(CsgHelpers.TabbedPlate(
                XCenter, Thickness, ZLeft, Width, YBottom, YCenter, HeadRadius, HoleRadius));

            // 包络：X = 厚 ±10、Y 顶 300（圆头顶点 (z=150, y=300) 一个点）、Z ∈ [50, 1860]。
            // 缺陷台面却在 y≈298.48 上横跨百毫米——下方断言把两者区分开。
            GeometryAssert.AssertBounds(solid,
                new Vector3D(-10, 0, ZLeft), new Vector3D(10, 300, ZLeft + Width), 1e-9,
                "DEFECT-008 前置：实体包络");

            // (1) 水平台面（错误读数）：+Y 法向、y≈298.4805412343433 的水平面片共 2 片
            //     （外露台阶片 + 圆头右侧残留小片）；正确构造两片都不应存在（顶边界应为圆头弧面）。
            var deckFaces = solid.Polygons
                .Where(p => Math.Abs(p.Plane.Normal.Y - 1.0) < 1e-9
                    && p.Vertices.All(v => Math.Abs(v.Pos.Y - FlatDeckY) < 1e-6))
                .OrderBy(p => p.Vertices.Min(v => v.Pos.Z))
                .ToList();
            Assert.That(deckFaces.Count, Is.EqualTo(2),
                "DEFECT-008 实际读数：台面高度水平面片 2 片（期望 0 片——正确构造为圆头弧面/相切斜边）");
            var ledge = deckFaces[0];
            Assert.That(ledge.Vertices.Min(v => v.Pos.Z), Is.EqualTo(LedgeZMin).Within(1e-6),
                "DEFECT-008 实际读数：外露台阶起点 z = 竖壁 z = zLeft + R·sinθ ≈ 67.3661");
            Assert.That(ledge.Vertices.Max(v => v.Pos.Z), Is.EqualTo(LedgeZMax).Within(1e-6),
                "DEFECT-008 实际读数：外露台阶终点 z ≈ 144.3293（头面片穿出台面处）");
            Assert.That(ledge.Vertices.Max(v => v.Pos.Z) - ledge.Vertices.Min(v => v.Pos.Z),
                Is.GreaterThan(75.0),
                "DEFECT-008：台阶跨度 ≈ 76.96 mm（远大于面片宽度，不是数值噪声）");

            // (2) 竖壁（错误读数）：z≈67.3661 的竖直面片，y 从台面直落到头面 ≈253.9687。
            var wallFaces = solid.Polygons
                .Where(p => Math.Abs(Math.Abs(p.Plane.Normal.Z) - 1.0) < 1e-9
                    && p.Vertices.All(v => Math.Abs(v.Pos.Z - LedgeZMin) < 1e-6))
                .ToList();
            Assert.That(wallFaces.Count, Is.EqualTo(1),
                "DEFECT-008 实际读数：z≈67.3661 竖壁 1 片（期望 0 片——正确构造圆弧连续过渡）");
            var wall = wallFaces[0];
            Assert.That(wall.Vertices.Min(v => v.Pos.Y), Is.EqualTo(WallYMin).Within(1e-6),
                "DEFECT-008 实际读数：竖壁下沿 y ≈ 253.9687（头面片处）");
            Assert.That(wall.Vertices.Max(v => v.Pos.Y), Is.EqualTo(FlatDeckY).Within(1e-6),
                "DEFECT-008 实际读数：竖壁上沿 y = 台面标高 298.4805");

            // (3) 净体积（错误读数）：比 corrected construction（肩部跨度 zT → R，一行修法）多
            //     +23,667.618872 mm³（≈0.42%）；期望 5,609,937.983120397（评审独立探针 +23,667.62 一致）。
            GeometryAssert.AssertVolume(solid, CurrentVolumeMm3, 1e-6,
                "DEFECT-008 实际读数：净体积（期望 corrected = 5,609,937.983120397，多料 +23,667.62）");
        }
    }
}
