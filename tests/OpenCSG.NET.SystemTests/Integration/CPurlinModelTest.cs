using System;
using System.Collections.Generic;
using System.Linq;
using Csg;
using NUnit.Framework;
using Runner.CPurlin;

namespace Csg.Test
{
    /// <summary>
    /// L5 集成：真实模型 <see cref="CpurlinModel.Build"/>（样例几何，D4 已抽取并由 SystemTests 引用）——
    /// 三视图包络的轴映射、冲孔位置可观察量、运行时长。
    /// <para>
    /// <b>坐标轴映射推导（先实测后写死）</b>：<c>CsgMesh.FromCsg</c> 做 Y↔Z 反射 ⇒ 网格坐标
    /// <c>(x, z, y)</c>；三个视图方向的取轴（<c>ViewProjectorContracts.cs</c>）换算到 CSG 坐标：
    /// 主视 <c>h = −mesh_y = −z_csg</c>、<c>v = mesh_z = y_csg</c>；
    /// 俯视 <c>h = −z_csg</c>、<c>v = x_csg</c>；
    /// 左视 <c>h = −x_csg</c>、<c>v = y_csg</c>。
    /// 模型的 CSG 包络实测 <c>x∈[−32,32]</c>（64 = <see cref="CpurlinModel.Flange"/>）、
    /// <c>y∈[−76,76]</c>（152 = <see cref="CpurlinModel.Web"/>）、<c>z∈[0,1000]</c>（1000 = <see cref="CpurlinModel.Height"/>）
    /// ⇒ 三视图包络应为：主视 宽×高 = 1000×152、俯视 1000×64、左视 64×152（探针实测：
    /// 主视 (−1000, −76.00000000000009)–(4.65365783675994e-15, 76.00000000000009)、
    /// 俯视 (−1000.0000000000001, −32)–(8.378428854032438e-15, 32)、
    /// 左视 (−32, −76.00000000000009)–(32, 76.00000000000009)）。
    /// </para>
    /// <para>
    /// <b>冲孔可观察量（全部实测）</b>：冲孔在成品坐标里位于 <c>Z = PunchingWebZ/FlangeZ/CenterZ</c>，
    /// 主视图高度轴 <c>|h| = z_csg</c>。主视实测恰有 2 条 r=9 的弧：<c>(h,v)=(−33,−30)</c>
    /// （web 孔帽圆，h = −(PunchingWebZ − 2) = −33，v = −WebCenter/2 = −30）与 <c>(−103,0)</c>
    /// （中心孔帽圆，h = −(PunchingCenterZ − 2) = −103）；Z=35 孔在 |h|∈[32,38]/|v|≈21 处留有
    /// 2 条药丸切线段（长 = HoleSize2 − HoleSize1 = 4）；Z=105 下帽圆（圆心 −107、r=9）上留有
    /// 5 条碎段（端点落在帽圆 12 边形镶嵌的弦环带 [R·cos15°, R] 内 —— raster 采样把缘弦再切分，
    /// 端点不一定在圆上，实测最小半径 8.6933324366…）。
    /// 法兰孔（Z=70、孔轴沿 y_csg）主视不可见：其轮廓投影与 v=±76 的长棱共线被合并
    /// （实测主视任一端点落在 |h|∈[67,73] 的段为零）；改由俯视观察 —— 实测 2 条 r=9 弧在
    /// <c>(h,v)=(−68,0)</c>（=（−(PunchingFlangeZ − 2),0) 帽圆，近/远法兰孔缘经孔膛同屏可见）+ 2 条
    /// 位于圆心 (−72,0) 的帽圆 C72 上的完整缘弦（长 4.65874281184539… = 2R·sin15°）。
    /// </para>
    /// <para>
    /// <b>缺陷域规避</b>：本文件不断言任何 V/H 分类（DEFECT-004/005 的栅格量化域），
    /// 只断言弧/段的几何读数；也不对 <c>view.Bounds</c> 做断言以外的用途（DEFECT-009 域）。
    /// </para>
    /// <para>
    /// <b>运行时长（实测）</b>：<c>Build()</c> 53 ms（314 多边形、652 三角形）、三视图投影 35 ms
    /// ⇒ 单用例 ≪ 2 s，不需要 <c>[Category("Corpus")]</c>。
    /// </para>
    /// </summary>
    [TestFixture]
    public class CPurlinModelTest
    {
        /// <summary>包络/圆心位置容差：brief 口径 ±0.5（实测偏差 ≤ 1e-13）。</summary>
        const double PositionTolerance = 0.5;

        /// <summary>半径容差：与弧拟合器自身容差同量级（<c>ViewArcFitter.DefaultTolerance</c> = 0.05；实测偏差 ≤ 4e-11）。</summary>
        const double RadiusTolerance = 0.05;

        /// <summary>点落在圆上的判据（实测偏差 ≤ 1e-11）。</summary>
        const double OnCircleTolerance = 1e-6;

        [Test]
        public void ThreeViews_BoundsShowWebFlangeHeightCombinations()
        {
            var solid = CpurlinModel.Build();
            var views = ViewBridge.ProjectViews(solid, ViewDirections.OrthographicThree);
            var front = ViewOf(views, "主视");
            var top = ViewOf(views, "俯视");
            var left = ViewOf(views, "左视");

            // 主视（web 面）：宽 = 柱高 1000（h = −z_csg）、高 = web 152（v = y_csg）。
            Assert.That(front.Bounds.Width, Is.EqualTo(CpurlinModel.Height).Within(PositionTolerance), "主视宽");
            Assert.That(front.Bounds.Height, Is.EqualTo(CpurlinModel.Web).Within(PositionTolerance), "主视高");
            Assert.That(front.Bounds.MinX, Is.EqualTo(-CpurlinModel.Height).Within(PositionTolerance), "主视 MinX = −Height");
            Assert.That(front.Bounds.MaxX, Is.EqualTo(0.0).Within(PositionTolerance), "主视 MaxX = 0（实测 4.65e-15）");
            Assert.That(front.Bounds.MinY, Is.EqualTo(-CpurlinModel.Web / 2).Within(PositionTolerance), "主视 MinY = −Web/2");
            Assert.That(front.Bounds.MaxY, Is.EqualTo(CpurlinModel.Web / 2).Within(PositionTolerance), "主视 MaxY = +Web/2");

            // 俯视（法兰侧立面）：宽 = 1000、高 = 法兰宽 64（v = x_csg）。
            Assert.That(top.Bounds.Width, Is.EqualTo(CpurlinModel.Height).Within(PositionTolerance), "俯视宽");
            Assert.That(top.Bounds.Height, Is.EqualTo(CpurlinModel.Flange).Within(PositionTolerance), "俯视高");
            Assert.That(top.Bounds.MinX, Is.EqualTo(-CpurlinModel.Height).Within(PositionTolerance), "俯视 MinX");
            Assert.That(top.Bounds.MaxX, Is.EqualTo(0.0).Within(PositionTolerance), "俯视 MaxX（实测 8.38e-15）");
            Assert.That(top.Bounds.MinY, Is.EqualTo(-CpurlinModel.Flange / 2).Within(PositionTolerance), "俯视 MinY");
            Assert.That(top.Bounds.MaxY, Is.EqualTo(CpurlinModel.Flange / 2).Within(PositionTolerance), "俯视 MaxY");

            // 左视（断面）：宽 = 64（h = −x_csg）、高 = 152（v = y_csg）。
            Assert.That(left.Bounds.Width, Is.EqualTo(CpurlinModel.Flange).Within(PositionTolerance), "左视宽");
            Assert.That(left.Bounds.Height, Is.EqualTo(CpurlinModel.Web).Within(PositionTolerance), "左视高");

            // 三数（1000/152/64）必须以上述正确组合出现 —— 若轴映射互换，本组断言会交叉失败。
            var combos = new (double W, double H)[]
            {
                (front.Bounds.Width, front.Bounds.Height),
                (top.Bounds.Width, top.Bounds.Height),
                (left.Bounds.Width, left.Bounds.Height),
            };
            int Matches(double w, double h) => combos.Count(c =>
                Math.Abs(c.W - w) <= PositionTolerance && Math.Abs(c.H - h) <= PositionTolerance);

            Assert.That(Matches(CpurlinModel.Height, CpurlinModel.Web), Is.EqualTo(1),
                "应恰有一视图 (宽,高) = (1000, 152)；实测 " + string.Join("、", combos.Select(c => $"({c.W:R},{c.H:R})")));
            Assert.That(Matches(CpurlinModel.Height, CpurlinModel.Flange), Is.EqualTo(1),
                "应恰有一视图 (宽,高) = (1000, 64)；实测 " + string.Join("、", combos.Select(c => $"({c.W:R},{c.H:R})")));
            Assert.That(Matches(CpurlinModel.Flange, CpurlinModel.Web), Is.EqualTo(1),
                "应恰有一视图 (宽,高) = (64, 152)；实测 " + string.Join("、", combos.Select(c => $"({c.W:R},{c.H:R})")));
        }

        /// <summary>
        /// 主视：web 孔（Z=35，v=∓30 一对）与中心孔（Z=105）的轮廓特征。
        /// 实测（探针 d5probe）：恰 2 条 r≈9 弧（中心见方法体）；Z=35 带 |h|∈[32,38] 内恰 2 条
        /// 药丸切线段（v=±21、长 4）；Z=105 下帽圆（圆心 (−107,0)、r=9）上有 5 条碎段。
        /// </summary>
        [Test]
        public void FrontView_HoleOutlinesAtPunchingZAreObservable()
        {
            var solid = CpurlinModel.Build();
            var front = ViewOf(ViewBridge.ProjectViews(solid, ViewDirections.OrthographicThree), "主视");

            var radius = CpurlinModel.HoleSize1 / 2;                 // 9
            var capOffset = (CpurlinModel.HoleSize2 - CpurlinModel.HoleSize1) / 2;   // 2

            // ① web 孔（PunchingWebZ=35）与中心孔（PunchingCenterZ=105）各留 1 条帽圆弧：
            //    实测 (h,v) = (−32.999999999971266, −30.000000000013546) r=8.999999999997051 sweep=90.00000000009689°
            //    与 (−102.9999999998776, −1.4949418255176637e-09) r=8.999999998567647 sweep=90.00000001027543°。
            var holeArcs = front.Arcs.Where(a => Math.Abs(a.Radius - radius) <= RadiusTolerance).ToList();
            Assert.That(holeArcs.Count, Is.EqualTo(2),
                $"主视 |r−{radius}| ≤ {RadiusTolerance} 的弧应恰为 2 条（web 孔 + 中心孔帽圆），实际 {holeArcs.Count}");

            var expectedCenters = new[]
            {
                (H: -(CpurlinModel.PunchingWebZ - capOffset), V: -CpurlinModel.WebCenter / 2),   // (−33, −30)
                (H: -(CpurlinModel.PunchingCenterZ - capOffset), V: 0.0),                        // (−103, 0)
            };
            foreach (var expected in expectedCenters)
            {
                var match = holeArcs.FirstOrDefault(a =>
                    Math.Abs(a.Center.X - expected.H) <= PositionTolerance
                    && Math.Abs(a.Center.Y - expected.V) <= PositionTolerance);
                Assert.That(match.Radius, Is.EqualTo(radius).Within(RadiusTolerance),
                    $"主视未见孔帽圆 (h,v)≈({expected.H},{expected.V})：r≈{radius} 的弧中心实测为 "
                    + string.Join("；", holeArcs.Select(a => $"({a.Center.X:R},{a.Center.Y:R})")));
            }

            // ② Z=35 药丸孔在 |h|∈[32,38] 留下 2 条切线段（v=±21 = ∓(WebCenter/2 − radius)，
            //    长 4 = HoleSize2 − HoleSize1）：实测 (−37,−21.000000000000025)→(−33,−21.000000000000025)、
            //    (−37.00000000004377,20.999999999999975)→(−32.99999999999619,20.999999999991292)。
            var pillBand = front.Segments
                .Where(s => -38.0 <= s.Start.X && s.Start.X <= -32.0
                         && -38.0 <= s.End.X && s.End.X <= -32.0)
                .ToList();
            Assert.That(pillBand.Count, Is.EqualTo(2),
                "主视 Z=35 药丸带（两端点 h∈[−38,−32]）应恰有 2 条段，实际 "
                + string.Join("；", pillBand.Select(s => $"({s.Start.X:R},{s.Start.Y:R})→({s.End.X:R},{s.End.Y:R})")));

            foreach (var segment in pillBand)
            {
                var where = $"Z=35 切线段 ({segment.Start.X:R},{segment.Start.Y:R})→({segment.End.X:R},{segment.End.Y:R})";
                var expectedV = -(CpurlinModel.WebCenter / 2 - radius);   // −21（孔 v=−30、r=9）
                Assert.That(Math.Abs(segment.Start.Y), Is.EqualTo(Math.Abs(expectedV)).Within(OnCircleTolerance),
                    $"{where}：|v| 应为 {Math.Abs(expectedV)}（= WebCenter/2 − r）");
                Assert.That(segment.Start.Y, Is.EqualTo(segment.End.Y).Within(OnCircleTolerance),
                    $"{where}：切线段应平行于 h 轴");
                Assert.That(Math.Abs(segment.Length), Is.EqualTo(CpurlinModel.HoleSize2 - CpurlinModel.HoleSize1).Within(OnCircleTolerance),
                    $"{where}：长度应为 HoleSize2 − HoleSize1 = {CpurlinModel.HoleSize2 - CpurlinModel.HoleSize1}");
                var hValues = new[] { Math.Abs(segment.Start.X), Math.Abs(segment.End.X) }.OrderBy(x => x).ToArray();
                Assert.That(hValues[0], Is.EqualTo(CpurlinModel.PunchingWebZ - capOffset).Within(OnCircleTolerance),
                    $"{where}：近端 |h| 应为 PunchingWebZ − 2 = {CpurlinModel.PunchingWebZ - capOffset}");
                Assert.That(hValues[1], Is.EqualTo(CpurlinModel.PunchingWebZ + capOffset).Within(OnCircleTolerance),
                    $"{where}：远端 |h| 应为 PunchingWebZ + 2 = {CpurlinModel.PunchingWebZ + capOffset}");
            }

            // 两段分居 v = ±21（web 孔对 v=∓30 的对称切线段）。
            Assert.That(pillBand.Select(s => Math.Sign(s.Start.Y)).Distinct().Count(), Is.EqualTo(2),
                "Z=35 的 2 条切线段应关于 v=0 对称（v=+21 与 −21）");

            // ③ Z=105 下帽圆（圆心 (−(PunchingCenterZ + capOffset), 0) = (−107, 0)、r=9）上的碎段：
            //    实测 5 条（raster 采样把 12 边形镶嵌的缘弦再切分，端点落在弦上、不一定在圆上）——
            //    故按「弦环带」判据断言：所有端点距圆心 ∈ [R·cos(π/12), R]（内接 12 边形每边的
            //    点到圆心距离恰在此带；实测最小 8.6933324366…（弦中点）、最大 9.0（顶点））。
            var capCircle = (H: -(CpurlinModel.PunchingCenterZ + capOffset), V: 0.0);
            var chordInner = radius * Math.Cos(Math.PI / 12);   // 8.693332436602…
            var capFragments = front.Segments
                .Where(s => -112.0 <= s.Start.X && s.Start.X <= -106.0
                         && -112.0 <= s.End.X && s.End.X <= -106.0
                         && Math.Abs(s.Start.Y) <= 9.5 && Math.Abs(s.End.Y) <= 9.5)
                .ToList();
            Assert.That(capFragments.Count, Is.GreaterThanOrEqualTo(3),
                "主视 Z=105 下帽圆带内应有 ≥3 条碎段，实际 " + capFragments.Count);
            foreach (var segment in capFragments)
            {
                foreach (var point in new[] { segment.Start, segment.End })
                {
                    var dx = point.X - capCircle.H;
                    var dy = point.Y - capCircle.V;
                    var distance = Math.Sqrt(dx * dx + dy * dy);
                    Assert.That(distance, Is.InRange(chordInner - OnCircleTolerance, radius + OnCircleTolerance),
                        $"Z=105 帽圆碎段端点 ({point.X:R},{point.Y:R}) 距圆心 ({capCircle.H},0) 应为 {distance:R}，"
                        + $"需落在 12 边形弦环带 [{chordInner:R},{radius}] 内");
                }
            }
        }

        /// <summary>
        /// 俯视：法兰孔（PunchingFlangeZ=70，孔轴沿 y_csg ⇒ 俯视正对孔膛）的轮廓特征。
        /// 实测：恰 2 条 r≈9 弧在 (h,v)=(−68,0)（= (−(PunchingFlangeZ − 2), 0) 帽圆，两条分别来自
        /// 近侧法兰孔缘与透过孔膛可见的远侧孔缘），另测得 2 条完全落在圆心 (−72,0) 帽圆上的碎段
        /// （12 边形缘弦，长 4.6587428118454…）。
        /// </summary>
        [Test]
        public void TopView_FlangeHoleOutlinesAtPunchingFlangeZAreObservable()
        {
            var solid = CpurlinModel.Build();
            var top = ViewOf(ViewBridge.ProjectViews(solid, ViewDirections.OrthographicThree), "俯视");

            var radius = CpurlinModel.HoleSize1 / 2;                          // 9
            var capOffset = (CpurlinModel.HoleSize2 - CpurlinModel.HoleSize1) / 2;   // 2

            // ① 帽圆弧：实测 2 条中心的 (cx,cy) 均为 (−67.99999999998037, −8.22218004034523e-11)，
            //    r=9.000000000033298，sweep 89.9999999996058° / 60.00000001753303°。
            var holeArcs = top.Arcs.Where(a => Math.Abs(a.Radius - radius) <= RadiusTolerance).ToList();
            Assert.That(holeArcs.Count, Is.EqualTo(2),
                $"俯视 |r−{radius}| ≤ {RadiusTolerance} 的弧应恰为 2 条（法兰孔帽圆缘），实际 {holeArcs.Count}");
            var expectedCenter = (H: -(CpurlinModel.PunchingFlangeZ - capOffset), V: 0.0);   // (−68, 0)
            foreach (var arc in holeArcs)
                Assert.That(
                    Math.Abs(arc.Center.X - expectedCenter.H) <= PositionTolerance
                    && Math.Abs(arc.Center.Y - expectedCenter.V) <= PositionTolerance,
                    Is.True, $"俯视法兰孔帽圆弧中心实测 ({arc.Center.X:R},{arc.Center.Y:R})，应为 ≈({expectedCenter.H},{expectedCenter.V})");

            // ② 圆心 (−(PunchingFlangeZ + capOffset), 0) = (−72, 0) 帽圆上的碎段：实测恰 2 条完整缘弦
            //    （(−72,−9)→(−76.5,−7.794) 与 (−76.5,7.794)→(−72,9)，各长 4.65874281184539… = 2R·sin15°）。
            //    判据与主视 Z=105 相同：端点落在帽圆 12 边形镶嵌的弦环带 [R·cos(π/12), R] 内
            //    （实测端点均在半径 9.0 的顶点上，偏差 ≤ 4e-13）。
            var secondCap = (H: -(CpurlinModel.PunchingFlangeZ + capOffset), V: 0.0);
            var chordInner = radius * Math.Cos(Math.PI / 12);
            var onSecondCap = top.Segments
                .Where(s =>
                {
                    var a = RadialDistance(s.Start, secondCap.H, secondCap.V);
                    var b = RadialDistance(s.End, secondCap.H, secondCap.V);
                    return a >= chordInner - OnCircleTolerance && a <= radius + OnCircleTolerance
                        && b >= chordInner - OnCircleTolerance && b <= radius + OnCircleTolerance
                        && Math.Abs(s.Start.Y) <= 9.5 && Math.Abs(s.End.Y) <= 9.5;
                })
                .ToList();
            Assert.That(onSecondCap.Count, Is.GreaterThanOrEqualTo(2),
                "俯视 Z=72 帽圆（圆心 (−72,0)、r=9）上应有 ≥2 条碎段（近/远法兰孔缘），实际 " + onSecondCap.Count);
        }

        static ViewProjection ViewOf(IReadOnlyList<ViewProjection> views, string name)
        {
            var view = views.SingleOrDefault(v => v.Direction.Name == name);
            Assert.That(view, Is.Not.Null, $"视图 {name} 不存在（三视图只有 主视/俯视/左视）");
            return view!;
        }

        static double RadialDistance(ViewPoint2 point, double cx, double cy)
        {
            var dx = point.X - cx;
            var dy = point.Y - cy;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
