using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// HalfPlaneCut 的 L1 单元回填（Plan C / Task 8）：Build / BuildXY 的**功能路径**解析用例
    /// （零方向 throw 已由 Task 7 的 CsgHelpersContractTest 覆盖）。
    /// 口径：切割体是绕切割线旋转 45° 系列的大方盒（边长 √2·halfExtent），与板做 Subtract 后
    /// 应精确切掉切割线一侧的半平面。断言 = 保留半边的解析体积/包络 + 切面法向。
    /// </summary>
    [TestFixture]
    public class HalfPlaneCutTest
    {
        static Solid Eval(CsgNode node) => CsgEvaluator.Evaluate(node);

        static void AssertVolumeRel(Solid solid, double expected, string message)
            => GeometryAssert.AssertVolume(solid, expected, System.Math.Abs(expected) * 1e-6 + 1e-9, message);

        [Test]
        public void Build_HorizontalLine_KeepsLowerHalfOfYzPlate()
        {
            // 板 20(X 厚)×40(Y)×40(Z)；切割线过 (Y,Z)=(0,0)、方向 +Y（dirZ=0，φ=0 ⇒ 切割体不旋转），
            // sideSign=+1 切法线正侧（n=(−dirZ,dirY)/|dir|=(0,1) ⇒ Z>0）⇒ 保留 Z ∈ [−20, 0] 的半板。
            var plate = CsgNodes.Box(0, 0, 0, 20, 40, 40);
            var cut = HalfPlaneCut.Build(centerX: 0, refY: 0, refZ: 0, dirY: 1, dirZ: 0,
                halfExtent: 50, overshoot: 200, sideSign: +1);

            var solid = Eval(CsgNodes.Subtract(plate, cut));

            GeometryAssert.AssertBounds(solid, new Vector3D(-10, -20, -20), new Vector3D(10, 20, 0),
                1e-6, "保留 Z≤0 半边");
            AssertVolumeRel(solid, 20.0 * 40.0 * 20.0, "半板体积 = 厚×高×半长");
            GeometryAssert.AssertPlanesMatchWinding(solid, "HalfPlaneCut.Build 水平切");
        }

        [Test]
        public void Build_DiagonalLineThroughCenter_HalfVolume_AndCutFaceNormal()
        {
            // 切割线 45°（dirY=dirZ=1）：n = (−1,1)/√2，切 n 正侧即 Z>Y ⇒ 保留 Z≤Y 一侧（含对角线）。
            // 对角面过板中心 ⇒ 恰一半体积 16000；切面外法向 = 保留侧朝外 = (0,−1,1)/√2。
            var plate = CsgNodes.Box(0, 0, 0, 20, 40, 40);
            var cut = HalfPlaneCut.Build(centerX: 0, refY: 0, refZ: 0, dirY: 1, dirZ: 1,
                halfExtent: 50, overshoot: 200, sideSign: +1);

            var solid = Eval(CsgNodes.Subtract(plate, cut));

            GeometryAssert.AssertBounds(solid, new Vector3D(-10, -20, -20), new Vector3D(10, 20, 20),
                1e-6, "对角切保留整体包络");
            AssertVolumeRel(solid, 20.0 * 40.0 * 40.0 / 2.0, "过中心的 45° 对角切 = 半体积");

            var expectedNormal = new Vector3D(0, -1, 1) * (1.0 / System.Math.Sqrt(2));
            bool found = false;
            foreach (var polygon in solid.Polygons)
            {
                if (polygon.Vertices.Count < 3)
                    continue;
                if (polygon.Plane.Normal.Dot(expectedNormal) > 1 - 1e-9)
                    found = true;
            }
            Assert.That(found, Is.True, "切面多边形外法向应为 (0,−√2/2,+√2/2)（保留侧 Z≤Y 朝外）");
        }

        [Test]
        public void BuildXY_HorizontalLine_KeepsLowerHalfOfXyPlate()
        {
            // 板 40(X)×40(Y)×20(Z 厚)；切割线过 (refX,refY)=(0,0)、方向 +X（dirY=0 ⇒ φ=0 ⇒ 不旋转），
            // n = (−dirY,dirX)/|dir| = (0,1) ⇒ sideSign=+1 切 Y>0 ⇒ 保留 Y ∈ [−20, 0] 的半板。
            var plate = CsgNodes.Box(0, 0, 0, 40, 40, 20);
            var cut = HalfPlaneCut.BuildXY(refX: 0, refY: 0, dirX: 1, dirY: 0,
                halfExtent: 50, overshoot: 200, sideSign: +1, centerZ: 0);

            var solid = Eval(CsgNodes.Subtract(plate, cut));

            GeometryAssert.AssertBounds(solid, new Vector3D(-20, -20, -10), new Vector3D(20, 0, 10),
                1e-6, "保留 Y≤0 半边");
            AssertVolumeRel(solid, 40.0 * 20.0 * 20.0, "半板体积 = 长×半宽×厚");
            GeometryAssert.AssertPlanesMatchWinding(solid, "HalfPlaneCut.BuildXY 水平切");
        }

        [Test]
        public void BuildXY_DiagonalLineThroughCenter_HalfVolume_AndCutFaceNormal()
        {
            // 45°（dirX=dirY=1）：n = (−1,1)/√2，切 n 正侧即 Y>X ⇒ 保留 Y≤X 一侧；过中心 ⇒ 半体积。
            var plate = CsgNodes.Box(0, 0, 0, 40, 40, 20);
            var cut = HalfPlaneCut.BuildXY(refX: 0, refY: 0, dirX: 1, dirY: 1,
                halfExtent: 50, overshoot: 200, sideSign: +1, centerZ: 0);

            var solid = Eval(CsgNodes.Subtract(plate, cut));

            GeometryAssert.AssertBounds(solid, new Vector3D(-20, -20, -10), new Vector3D(20, 20, 10),
                1e-6, "对角切保留整体包络");
            AssertVolumeRel(solid, 40.0 * 40.0 * 20.0 / 2.0, "过中心的 45° 对角切 = 半体积");

            var expectedNormal = new Vector3D(-1, 1, 0) * (1.0 / System.Math.Sqrt(2));
            bool found = false;
            foreach (var polygon in solid.Polygons)
            {
                if (polygon.Vertices.Count < 3)
                    continue;
                if (polygon.Plane.Normal.Dot(expectedNormal) > 1 - 1e-9)
                    found = true;
            }
            Assert.That(found, Is.True, "切面多边形外法向应为 (−√2/2,+√2/2,0)（保留侧 Y≤X 朝外）");
        }
    }
}
