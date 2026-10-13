using System;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// A2 回归（spec §9 批准的最小修复；出自 spec 缺陷勘察表，从未分配 DEFECT 编号）：
    /// <c>Solids.Cylinder</c> 曾只处理 <c>SectorAngle &gt; 360</c>（<c>alpha % 360</c>）——
    /// 非零整圈（<c>720 % 360 == 0</c>、<c>-360</c>）与负数角（<c>-90</c>）都使 <c>alpha &gt; 0</c> 为假，
    /// 静默落进空实体分支（0 多边形，无异常）。修复：非零角按 mod 360 归一到 (0, 360]，0 保持空实体。
    /// <para>
    /// 契约（钉在 <see cref="CylinderOptions.SectorAngle"/> 注释上）：<c>0</c> = 空实体；
    /// 非零角按 mod 360 归一到 <c>(0, 360]</c>；整圈角度（360/720/-360/1080…）等价于默认 360° 完整圆柱。
    /// </para>
    /// <para>
    /// 离散口径（r=10、h=20、<c>slices = Resolution = 12</c>）：侧壁是从轴心扇出的 <c>slices</c> 个三角形，
    /// 扇形被内接多边形逼近 ⇒ 离散体积 = <c>n·½·r²·sin(α_rad/n)·h = 12000·sin(α_rad/12)</c>，
    /// 对解析扇形 <c>α_rad·r²·h/2</c> 的亏量 = <c>1 − n·sin(α_rad/n)/α_rad</c>：
    /// 90° → 0.285%（brief 的 0.5% 容差成立）；180° → 1.138%（brief 表的 0.5% 假设**不成立**，
    /// 已按解析公式修正为 1.2%）；270° → 2.551%；360° → 4.507%（实测 6000.0 = 12·½·100·sin(30°)·20）。
    /// </para>
    /// </summary>
    [TestFixture]
    public class A2SectorAngleTest
    {
        /// <summary>解析离散期望：n=12 段内接扇形的棱柱体积（r=10、h=20）。</summary>
        static double DiscreteVolume(double degrees)
            => 12000.0 * Math.Sin(Math.PI * degrees / 180.0 / 12.0);

        static Solid Cylinder(double sector)
            => Solids.Cylinder(new CylinderOptions
            {
                Start = new Vector3D(0, 0, 0),
                End = new Vector3D(0, 0, 20),
                RadiusStart = 10,
                RadiusEnd = 10,
                SectorAngle = sector,
            });

        /// <summary>
        /// A2 复现 1：整圈角度必须是完整圆柱。修复前 720 命中 <c>720 % 360 == 0</c>、-360 保持负值
        /// ⇒ 都走 <c>alpha &gt; 0</c> 为假的静默空实体分支（0 多边形、体积 0）。
        /// </summary>
        [Test]
        public void FullTurns_AreFullCylinders_NotEmpty()
        {
            foreach (var angle in new[] { 360.0, 720.0, -360.0 })
            {
                var solid = Cylinder(angle);
                Assert.That(solid.Polygons.Count, Is.EqualTo(36),
                    $"SectorAngle={angle} 应是完整圆柱（36 个多边形 = 12 段 × 3），实际 {solid.Polygons.Count} 个："
                    + (solid.Polygons.Count == 0 ? "落进了静默空实体分支" : "多边形数不符"));
                GeometryAssert.AssertVolume(solid, 6000.0, 1e-6,
                    $"SectorAngle={angle} 完整圆柱离散体积（6000 = 12·½·10²·sin(30°)·20）");
            }

            // 归一后与默认 360° 路径代数等价：多边形数相同、体积一致。
            var reference = Cylinder(360);
            foreach (var angle in new[] { 720.0, -360.0 })
            {
                var solid = Cylinder(angle);
                Assert.That(solid.Polygons.Count, Is.EqualTo(reference.Polygons.Count),
                    $"SectorAngle={angle} 应与默认 360° 路径多边形数一致（{reference.Polygons.Count}）");
                Assert.That(GeometryAssert.SignedVolume(solid),
                    Is.EqualTo(GeometryAssert.SignedVolume(reference)).Within(1e-9),
                    $"SectorAngle={angle} 的体积应与默认 360° 路径一致");
            }
        }

        /// <summary>
        /// A2 复现 2：负数角按 mod 360 归一。-90 ≡ 270（逐位一致：多边形数相同 + 体积差 &lt; 1e-9）。
        /// 修复前 -90 不归一 ⇒ 0 多边形空实体。
        /// </summary>
        [Test]
        public void NegativeAngle_IsWrapped_EqualTo270()
        {
            var wrapped = Cylinder(-90);
            var explicit270 = Cylinder(270);

            Assert.That(wrapped.Polygons.Count, Is.EqualTo(explicit270.Polygons.Count),
                $"SectorAngle=-90 应归一到 270° 扇形：实际 {wrapped.Polygons.Count} 个多边形，270° 基准 {explicit270.Polygons.Count} 个"
                + (wrapped.Polygons.Count == 0 ? "（落进静默空实体分支）" : ""));
            Assert.That(GeometryAssert.SignedVolume(wrapped),
                Is.EqualTo(GeometryAssert.SignedVolume(explicit270)).Within(1e-9),
                "SectorAngle=-90 与 270 的体积应逐位一致");

            // 归一后的 270° 扇形本身也钉住绝对读数（解析离散公式），避免「两边同错」通过。
            GeometryAssert.AssertVolume(wrapped, DiscreteVolume(270.0), 1e-6, "SectorAngle=-90 ≡ 270° 的离散体积");
        }

        /// <summary>
        /// A2 契约面：部分扇形（&lt; 360°）为封闭定向流形 + 侧封盖（4 个多边形）+ 解析体积对比。
        /// 离散亏量按公式推导（90° → 0.285%、180° → 1.138%）——brief 表里对 180° 沿用的
        /// 0.5% 容差不成立，此处按解析亏量取 1.2%。
        /// </summary>
        [Test]
        public void Sector_IsClosedAndVolumeMatchesAnalytic()
        {
            var quarter = Cylinder(90);
            GeometryAssert.AssertVolume(quarter, DiscreteVolume(90.0), 1e-6,
                "90° 扇形离散体积（12 段内接扇形的棱柱）");
            GeometryAssert.AssertVolume(quarter, Math.PI * 100 * 20 / 4, 0.005 * Math.PI * 100 * 20 / 4,
                "90° 扇形 vs 解析 π·10²·20/4（内接亏量 0.285% < 0.5%）");
            GeometryAssert.AssertClosedOrientedManifold(quarter, "90° 扇形（含 4 个侧封盖）应是封闭定向流形");
            Assert.That(quarter.Polygons.Count, Is.EqualTo(36 + 4),
                "90° 扇形多边形数 = 360° 路径 36 + 侧封盖 4（if (alpha < 360) 分支）");

            var half = Cylinder(180);
            GeometryAssert.AssertVolume(half, DiscreteVolume(180.0), 1e-6,
                "180° 扇形离散体积（12 段内接扇形的棱柱）");
            GeometryAssert.AssertVolume(half, Math.PI * 100 * 20 / 2, 0.012 * Math.PI * 100 * 20 / 2,
                "180° 扇形 vs 解析 π·10²·20/2（内接亏量 1.138%；brief 的 0.5% 对半圆不成立，按公式推导取 1.2%）");
            GeometryAssert.AssertClosedOrientedManifold(half, "180° 扇形（含 4 个侧封盖）应是封闭定向流形");
        }

        /// <summary>A2 契约：0 保持静默空实体（0 多边形）——现状即契约，不算缺陷。</summary>
        [Test]
        public void ZeroAngle_RemainsEmpty()
        {
            Assert.That(Cylinder(0).Polygons.Count, Is.EqualTo(0),
                "SectorAngle=0 按契约是空实体（0 多边形）");
        }
    }
}
