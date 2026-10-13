using System;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// Vector.cs 的 L2 契约与边界（Plan C / Task 7 回填）：Matrix4x4 构造校验、乘法与齐次除法、
    /// Plane.FromVector3Ds 的共线检测、Vector3D 的 ±0 相等性/哈希。
    /// 异常类型与 ParamName 全部读实现逐一确认（`Vector.cs` / `Plane.cs`），非猜测。
    /// </summary>
    [TestFixture]
    public class VectorContractTest
    {
        [Test]
        public void Matrix4x4_Ctor_NullOrShortElements_Throw()
        {
            // 读实现：els == null ⇒ ArgumentNullException(nameof(els))；els.Length < 16 ⇒ ArgumentException(nameof(els))。
            var nullEx = Assert.Throws<ArgumentNullException>(() => new Matrix4x4(null));
            Assert.That(nullEx.ParamName, Is.EqualTo("els"));

            var shortEx = Assert.Throws<ArgumentException>(() => new Matrix4x4(new double[15]));
            Assert.That(shortEx.ParamName, Is.EqualTo("els"));
            Assert.That(shortEx.Message, Does.Contain("16"), "消息应点明需要 16 个元素");
        }

        [Test]
        public void Matrix4x4_DefaultIsIdentity_AndMirrorFlagIsDerivedFromElements()
        {
            var identity = new Matrix4x4();
            Assert.That(identity.Elements,
                Is.EqualTo(new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 }),
                "默认构造 = 单位矩阵（Elements 公开同数组）");
            Assert.That(identity.IsMirroring, Is.False, "单位矩阵行列式 +1 ⇒ 非镜像");

            Assert.That(Matrix4x4.Scaling(new Vector3D(1, 1, 1)).IsMirroring, Is.False);
            Assert.That(Matrix4x4.Scaling(new Vector3D(-1, 1, 1)).IsMirroring, Is.True,
                "3×3 子阵行列式为负（镜像）应由构造函数自动判定");
        }

        [Test]
        public void Matrix4x4_Multiply_ComposesTranslations_AndIdentityIsUnit()
        {
            var t1 = Matrix4x4.Translation(new Vector3D(1, 2, 3));
            var t2 = Matrix4x4.Translation(new Vector3D(10, 20, 30));

            var composed = t1 * t2;
            var origin = new Vector3D(0, 0, 0) * composed;
            Assert.That(origin.X, Is.EqualTo(11.0).Within(1e-12), "T1*T2 应把原点平移到两段平移之和");
            Assert.That(origin.Y, Is.EqualTo(22.0).Within(1e-12));
            Assert.That(origin.Z, Is.EqualTo(33.0).Within(1e-12));

            var identityLeft = new Matrix4x4() * t1;
            Assert.That(identityLeft.Elements, Is.EqualTo(t1.Elements), "单位矩阵左乘应保持元素逐位不变（矩阵×矩阵复合从未测）");
        }

        [Test]
        public void Matrix4x4_LeftMultiply_NormalizesNonUnitW()
        {
            // 读实现 LeftMultiply1x3Vector：w != 1 时结果整体除以 w（w 为第 4 行作用量）。
            var els = new double[16];
            els[0] = 1;
            els[5] = 1;
            els[15] = 2;   // 齐次项 ⇒ w = 2
            var m = new Matrix4x4(els);

            var p = new Vector3D(2, 0, 0) * m;
            Assert.That(p.X, Is.EqualTo(1.0).Within(1e-12), "w=2 ⇒ x 应除以 2");
            Assert.That(p.Y, Is.EqualTo(0.0).Within(1e-12));
            Assert.That(p.Z, Is.EqualTo(0.0).Within(1e-12));
        }

        [Test]
        public void Plane_FromVector3Ds_ColinearSet_Throws_NonColinearSuffixIsFound()
        {
            var colinear = new[]
            {
                new Vector3D(0, 0, 0), new Vector3D(1, 1, 1), new Vector3D(2, 2, 2),
            };
            var ex = Assert.Throws<ArgumentException>(() => Plane.FromVector3Ds(colinear));
            Assert.That(ex.ParamName, Is.EqualTo("vectors"));
            Assert.That(ex.Message, Does.Contain("colinear"));

            // 前三个共线但第 4 个不共线：实现须继续向后寻找（increment 循环），不得误抛。
            var plane = Plane.FromVector3Ds(new[]
            {
                new Vector3D(0, 0, 0), new Vector3D(1, 0, 0), new Vector3D(2, 0, 0), new Vector3D(0, 1, 0),
            });
            Assert.That(plane.Normal.X, Is.EqualTo(0.0).Within(1e-12));
            Assert.That(plane.Normal.Y, Is.EqualTo(0.0).Within(1e-12));
            Assert.That(plane.Normal.Z, Is.EqualTo(1.0).Within(1e-12), "X-Y 平面 ⇒ 法线 +Z");
            Assert.That(plane.W, Is.EqualTo(0.0).Within(1e-12));
        }

        [Test]
        public void Vector3D_ZeroLengthUnit_ThrowsViaNaNValidation()
        {
            // 退化输入：零向量 Unit ⇒ 0/0 = NaN ⇒ 构造校验（Vector.cs 的 NaN guard）接手抛错。
            // 契约口径：Vector3D 的公开 API 不产出 NaN 向量（与 Vector3D ctor 的三分量 NaN 校验一致）。
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => { var _ = new Vector3D(0, 0, 0).Unit; });
            Assert.That(ex.Message, Does.Contain("NaN"));
        }

        [Test]
        public void Vector3D_MinusZero_EqualAndHashMatches()
        {
            var negativeZero = new Vector3D(-0.0, 0, 0);
            var positiveZero = new Vector3D(0.0, 0, 0);

            Assert.That(negativeZero.Equals(positiveZero), Is.True, "-0.0 == 0.0 ⇒ 结构相等性成立");
            Assert.That(negativeZero.GetHashCode(), Is.EqualTo(positiveZero.GetHashCode()),
                "相等对象必须同哈希；.NET 8 的 Double 哈希对 ±0 归一（实测两值同），结构体默认哈希随之相等");
        }
    }
}
