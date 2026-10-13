using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// 体积 oracle 的数值口径。
    /// 散度定理用**全局原点**做参考点时，离原点很远的实体（测绘坐标 ±1e6）会出现灾难性抵消：
    /// 每项量级 ~1e18 而真实体积 ~1e1，双精度在 1e18 处的 ULP≈128，体积被舍入噪声吞掉。
    /// 改用**相对包围盒中心**的局部参考点后，对闭合曲面数值不变而条件数大幅改善。
    /// </summary>
    [TestFixture]
    public class SignedVolumeInvarianceTest
    {
        const double SurveyOffset = 1_000_000.0;

        [Test]
        public void SignedVolume_IsTranslationInvariant()
        {
            var sphere = Solids.Sphere(new SphereOptions
            {
                Center = new Vector3D(0, 0, 0), Radius = 1, Resolution = 48,
            });
            var moved = sphere.Translate(SurveyOffset, -2 * SurveyOffset, 3 * SurveyOffset);

            var atOrigin = GeometryAssert.SignedVolume(sphere);
            var displaced = GeometryAssert.SignedVolume(moved);

            // 注：res=48 内接球的多面体体积实测 4.15897，比 4π/3 小 0.0298（0.71% 内接亏格，
            // 与积分参考点无关），故容差取 0.05 而非计划草案的 0.02 —— 该断言只做量级合理性检查。
            Assert.That(atOrigin, Is.EqualTo(4.18879).Within(0.05), "原点处应接近球体积 4π/3");
            Assert.That(displaced, Is.EqualTo(atOrigin).Within(System.Math.Abs(atOrigin) * 1e-6),
                $"平移不改变体积：原点 {atOrigin:R}，平移后 {displaced:R}");
            Assert.That(displaced, Is.GreaterThan(0),
                "平移 1e6 后仍必须为正 —— 负值说明积分参考点仍在全局原点");
        }

        [Test]
        public void SignedVolume_SurveyTierPrimitives_AreAllPositive()
        {
            for (var i = 0; i < 32; i++)
            {
                var spec = SolidGen.Random(i * 977 + 13, CoordTier.Survey);
                var solid = SolidGen.Materialize(spec);

                Assert.That(GeometryAssert.SignedVolume(solid), Is.GreaterThan(0),
                    $"seed-index {i}: Survey 档实体的有向体积必须为正；spec={spec}");
            }
        }

        [Test]
        public void MeshSignedVolume_IsTranslationInvariant()
        {
            var sphere = Solids.Sphere(new SphereOptions
            {
                Center = new Vector3D(0, 0, 0), Radius = 1, Resolution = 48,
            });
            // 必须用**各轴同时**平移：单轴平移 (1e6,0,0) 的抵消是良性的，
            // 那条位移下修复前后都通过 —— 测不出这个修复。均匀平移才会真正触发抵消误差。
            var moved = sphere.Translate(SurveyOffset, SurveyOffset, SurveyOffset);

            var atOrigin = MeshAssert.SignedVolume(CsgMesh.TrianglesOf(sphere));
            var displaced = MeshAssert.SignedVolume(CsgMesh.TrianglesOf(moved));

            Assert.That(displaced, Is.EqualTo(atOrigin).Within(System.Math.Abs(atOrigin) * 1e-6),
                $"网格侧平移不改变体积：{atOrigin:R} vs {displaced:R}");
        }
    }
}
