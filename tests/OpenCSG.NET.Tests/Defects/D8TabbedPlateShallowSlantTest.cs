using System;
using System.Linq;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// DEFECT-008 回归（2026-10-14 修复；登记于 tests/DEFECTS.md 已修复表）。
    /// <para>
    /// 原缺陷：浅斜角（θ&lt;30°）肩部矩形欠覆盖 —— 只减到切点 zLeft + R·sinθ，而台面标高
    /// （y = yT）处圆盘外的左段需覆盖到 zLeft + R − R·sinθ；θ&lt;30° 时 R·sinθ &lt; R − R·sinθ
    /// ⇒ 圆头左上外侧残留 76.96mm 水平平台 + 竖壁，净体积多 +23,667.62 mm³（≈0.42%），静默无异常。
    /// </para>
    /// <para>
    /// 修复：肩部跨度改条件式 —— θ&lt;30°（2·zT &lt; R）时覆盖到孔心（跨度 R，与评审独立探针复算的
    /// corrected 体积 5,609,937.983120397 一致）；θ≥30° 维持切点跨度 zT（既有 θ≈58.6°/70.9° 基线
    /// 逐字节不变；两域在 θ=30° 处覆盖条件 R·sinθ = R − R·sinθ 恰好连续）。
    /// </para>
    /// <para>
    /// 修复后读数：竖壁消失；平台缩至圆头多边形弦下方的窄残片 z≈[155.67, 167.37]（宽 ≈11.7mm）——
    /// 该残片是 12 边形圆头近似与台面的固有交叠（同族残片在 θ≥30° 域亦存在，如默认件宽 ≈0.83mm），
    /// 非覆盖缺失；净体积 5,609,937.983（修前 5,633,605.602）。
    /// </para>
    /// </summary>
    [TestFixture]
    public class D8TabbedPlateShallowSlantTest
    {
        // 浅斜边参数域（评审给出的完整参数集）：宽 1810 / 孔心高 200 / R100 ⇒ 名义 θ ≈ 10.0°（< 30°）。
        const double XCenter = 0, Thickness = 20, ZLeft = 50, Width = 1810;
        const double YBottom = 0, YCenter = 200, HeadRadius = 100, HoleRadius = 40;

        const double SlantAngleDeg = 10.000772281737618;          // 触发域：θ < 30°
        const double DeckY = 298.4805412343433;                    // 台面标高 yT
        const double CorrectedVolumeMm3 = 5609937.983120397;       // 评审 corrected 构造

        [Test]
        public void ShallowSlant_PlatformReducedToSliver_NoWall_CorrectedVolume()
        {
            // 前置：θ < 30°（修复触发域边界 θ=30° ⇔ 宽 = 3R + √3·yC ≈ 646.410，本例 1810 远在其上）。
            var angle = CsgHelpers.TabbedPlateSlantAngleDeg(Width, YBottom, YCenter, HeadRadius);
            Assert.That(angle, Is.EqualTo(SlantAngleDeg).Within(1e-9),
                $"斜边角应 ≈10.0008°（θ<30° 即 R·sinθ < R − R·sinθ 欠覆盖域；实际 {angle:R}°）");
            Assert.That(angle, Is.LessThan(30.0), "修复触发域：θ<30°");

            var solid = CsgEvaluator.Evaluate(CsgHelpers.TabbedPlate(
                XCenter, Thickness, ZLeft, Width, YBottom, YCenter, HeadRadius, HoleRadius));

            // 包络不变：X = 厚 ±10、Y 顶 300（圆头顶点）、Z ∈ [50, 1860]。
            GeometryAssert.AssertBounds(solid,
                new Vector3D(-10, 0, ZLeft), new Vector3D(10, 300, ZLeft + Width), 1e-9,
                "DEFECT-008 前置：实体包络");

            // (1) 台面（修复后）：仅剩圆头多边形（12 边形）上弦低于台面的窄残片 ——
            //     z ∈ [155.6707, 167.3661]（宽 ≈11.7mm，弦与台面交点至切点）。
            //     原 76.96mm 外露台阶（z ∈ [67.37, 144.33]）被消除。
            var deckFaces = solid.Polygons
                .Where(p => Math.Abs(p.Plane.Normal.Y - 1.0) < 1e-9
                    && p.Vertices.All(v => Math.Abs(v.Pos.Y - DeckY) < 1e-6))
                .ToList();
            Assert.That(deckFaces.Count, Is.EqualTo(1),
                "修复后：台面仅剩圆头多边形弦下的窄残片 1 片（修复前为 2 片含 76.96mm 外露台阶）");
            var sliver = deckFaces[0];
            Assert.That(sliver.Vertices.Min(v => v.Pos.Z), Is.EqualTo(155.67069731343656).Within(1e-2),
                "残片起点 z ≈ 155.6707（12 边形圆头上弦与台面标高的交点；修复前外露台阶起点 ≈67.3661）");
            Assert.That(sliver.Vertices.Max(v => v.Pos.Z), Is.EqualTo(167.36614517360613).Within(1e-2),
                "残片终点 z ≈ 167.3661 = 切点 z（孔心 zc + zT；修复前台阶终点 ≈144.3293）");
            Assert.That(sliver.Vertices.Max(v => v.Pos.Z) - sliver.Vertices.Min(v => v.Pos.Z),
                Is.LessThan(12.0),
                "残片宽度 ≈11.7mm，为圆头多边形近似与台面的固有交叠（同族残片在 θ≥30° 基线亦存在）");

            // (2) 竖壁消失：z ≈ 67.3661（修前台阶竖壁 z = zLeft + R·sinθ）不得存在竖直面片。
            var wallFaces = solid.Polygons
                .Where(p => Math.Abs(Math.Abs(p.Plane.Normal.Z) - 1.0) < 1e-9
                    && p.Vertices.All(v => Math.Abs(v.Pos.Z - 67.36614517360738) < 1e-6))
                .ToList();
            Assert.That(wallFaces.Count, Is.EqualTo(0),
                "修复后：修复前的 z≈67.3661 竖壁（1 片）应消失（圆头弧面连续过渡）");

            // (3) 净体积 = corrected 构造（θ<30° 时肩部覆盖到孔心；评审独立探针复算逐位一致）。
            //     修复前读数 5,633,605.601992406（多料 +23,667.62）；容差取 1e-3（整数级信号量级
            //     为 2.4e4，且为跨平台 libm ULP 留余量）。
            GeometryAssert.AssertVolume(solid, CorrectedVolumeMm3, 1e-3,
                "修复后：净体积 = corrected 构造（实测 5,609,937.983120362，与评审值差 3.5e-8）");
        }
    }
}
