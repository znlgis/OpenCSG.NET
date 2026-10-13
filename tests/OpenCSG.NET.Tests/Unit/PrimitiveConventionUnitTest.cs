using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// PrimitiveConvention 的 L1 单元回填（Plan C / Task 8）：**功能面**——用约定 + <see cref="PrimitiveConvention.Offset"/>
    /// 补偿宿主生成体，使「中心 + 尺寸」语义在两种宿主口径下落到同一位置。
    /// （ctor / 7 属性 / OriginBased / FrustumRadii / IsCanonicalFactor / Offset 的逐值断言已由 Task 7 的
    /// PrimitiveConventionContractTest 覆盖，此处不重复；本用例补的是约定→实体的组装路径。）
    /// </summary>
    [TestFixture]
    public class PrimitiveConventionUnitTest
    {
        [Test]
        public void Offset_DrivesUnitCubeToSameCenterAndSize_UnderBothHostConventions()
        {
            // 目标（与宿主无关的 CSG 语义）：边长 4、中心 (10, −6, 2) 的盒 ⇒ 最小角 (8, −8, 0)。
            const double size = 4;
            var desiredCenter = new Vector3D(10, -6, 2);
            var desiredMin = desiredCenter - new Vector3D(size / 2, size / 2, size / 2);

            // 宿主 A：自原点沿正轴生成（factor 0）⇒ 单位盒占 [0,size]，最小角在 0 ⇒ 平移 = min − 0。
            var originBased = PrimitiveConvention.OriginBased;
            var viaA = Solids.Cube(size, center: false)
                .Translate(PrimitiveConvention.Offset(desiredCenter.X, size, originBased.BoxX),
                           PrimitiveConvention.Offset(desiredCenter.Y, size, originBased.BoxY),
                           PrimitiveConvention.Offset(desiredCenter.Z, size, originBased.BoxZ));

            // 宿主 B：以原点为中心生成（factor −0.5）⇒ 单位盒占 [−size/2,size/2]，最小角在 −size/2。
            var centeredHost = new PrimitiveConvention(
                boxX: -0.5, boxY: -0.5, boxZ: -0.5,
                frustumZ: -0.5, wedgeX: -0.5, wedgeY: -0.5, wedgeZ: -0.5);
            var viaB = Solids.Cube(size, center: true)
                .Translate(PrimitiveConvention.Offset(desiredCenter.X, size, centeredHost.BoxX),
                           PrimitiveConvention.Offset(desiredCenter.Y, size, centeredHost.BoxY),
                           PrimitiveConvention.Offset(desiredCenter.Z, size, centeredHost.BoxZ));

            GeometryAssert.AssertBounds(viaA, desiredMin, desiredMin + new Vector3D(size, size, size), 1e-9,
                "OriginBased 约定：平移 = min − 0");
            GeometryAssert.AssertBounds(viaB, desiredMin, desiredMin + new Vector3D(size, size, size), 1e-9,
                "居中约定：平移 = min − (−size/2)");
            GeometryAssert.AssertVolume(viaA, size * size * size, 1e-9, "两种约定得到同一实体（体积）");
            GeometryAssert.AssertVolume(viaB, size * size * size, 1e-9, "两种约定得到同一实体（体积）");
        }
    }
}
