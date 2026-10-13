using System;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// Matrix4x4 的 L1 单元回填（Plan C / Task 8）：ctor / <see cref="Matrix4x4.Elements"/> /
    /// <see cref="Matrix4x4.operator *(Matrix4x4, Matrix4x4)"/>（矩阵×矩阵复合此前从未测过）。
    /// 存储口径（读实现）：元素按**行优先**存放；点变换 = 行向量左乘（<c>v * M</c> 或
    /// <c>M.LeftMultiply1x3Vector(v)</c>），复合 <c>P = L * M</c> 表示「先施加 L、再施加 M」。
    /// 全部期望值由基向量手推（旋转矩阵、平移矩阵逐元素展开），非对拍实现。
    /// （ctor 的 null/短数组 throw 由 Task 7 的 VectorContractTest 覆盖，此处不重复。）
    /// </summary>
    [TestFixture]
    public class Matrix4x4Test
    {
        const double Tol = 1e-12;

        static void AssertElements(Matrix4x4 matrix, double[] expected, string message)
        {
            Assert.That(matrix.Elements.Length, Is.EqualTo(16), $"{message}: Elements 长度");
            for (int i = 0; i < 16; i++)
            {
                Assert.That(matrix.Elements[i], Is.EqualTo(expected[i]).Within(Tol),
                    $"{message}: Elements[{i}]（行优先，第 {i / 4} 行第 {i % 4} 列）");
            }
        }

        [Test]
        public void Matrix4x4_DefaultCtor_IsIdentity_ArrayCtorExposesElements()
        {
            var identity = new Matrix4x4();
            AssertElements(identity, new[]
            {
                1.0, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                0, 0, 0, 1,
            }, "默认 ctor = 单位阵");

            // Elements 读回构造时传入的 16 个元素（行优先序：前 4 个 = 第 0 行）。
            var custom = new Matrix4x4(new[] { 11.0, 12, 13, 14, 21, 22, 23, 24, 31, 32, 33, 34, 41, 42, 43, 44 });
            Assert.That(custom.Elements[0], Is.EqualTo(11.0));
            Assert.That(custom.Elements[3], Is.EqualTo(14.0), "第 0 行末列");
            Assert.That(custom.Elements[12], Is.EqualTo(41.0), "第 3 行首列 = 平移分量槽位");
            Assert.That(custom.Elements[15], Is.EqualTo(44.0));
        }

        [Test]
        public void Matrix4x4_Factories_ElementsAnalytic()
        {
            AssertElements(Matrix4x4.Translation(new Vector3D(1, 2, 3)), new[]
            {
                1.0, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                1, 2, 3, 1, // 平移落在第 3 行前三位（行优先 + 行向量左乘口径）
            }, "Translation");

            AssertElements(Matrix4x4.Scaling(new Vector3D(2, 3, 4)), new[]
            {
                2.0, 0, 0, 0,
                0, 3, 0, 0,
                0, 0, 4, 0,
                0, 0, 0, 1,
            }, "Scaling");

            // RotationX(90°)：cos=0、sin=1 ⇒ 第 1 行 (0,cos,sin)、第 2 行 (0,−sin,cos)。
            AssertElements(Matrix4x4.RotationX(90), new[]
            {
                1.0, 0, 0, 0,
                0, 0, 1, 0,
                0, -1, 0, 0,
                0, 0, 0, 1,
            }, "RotationX(90°)");

            AssertElements(Matrix4x4.RotationY(90), new[]
            {
                0.0, 0, -1, 0,
                0, 1, 0, 0,
                1, 0, 0, 0,
                0, 0, 0, 1,
            }, "RotationY(90°)");

            AssertElements(Matrix4x4.RotationZ(90), new[]
            {
                0.0, 1, 0, 0,
                -1, 0, 0, 0,
                0, 0, 1, 0,
                0, 0, 0, 1,
            }, "RotationZ(90°)");

            // 180°：cos=−1、sin=0。
            AssertElements(Matrix4x4.RotationZ(180), new[]
            {
                -1.0, 0, 0, 0,
                0, -1, 0, 0,
                0, 0, 1, 0,
                0, 0, 0, 1,
            }, "RotationZ(180°)");
        }

        [Test]
        public void Matrix4x4_Multiply_WithIdentity_IsNoOp()
        {
            var m = Matrix4x4.Translation(new Vector3D(1, 2, 3));
            var identity = new Matrix4x4();

            var left = m * identity;
            var right = identity * m;
            for (int i = 0; i < 16; i++)
            {
                Assert.That(left.Elements[i], Is.EqualTo(m.Elements[i]), $"M×I 的 Elements[{i}]");
                Assert.That(right.Elements[i], Is.EqualTo(m.Elements[i]), $"I×M 的 Elements[{i}]");
            }
            Assert.That(m.IsMirroring, Is.False, "平移非镜像");
        }

        [Test]
        public void Matrix4x4_Multiply_RotationTimesTranslation_ElementsAnalytic()
        {
            // P = Rz(90°) × T(1,2,3)：逐元素 = L 的第 i 行 × M 的各列。
            // 手推：Rz90 行 (0,1,0,0)/(−1,0,0,0)/(0,0,1,0)/(0,0,0,1)；
            // 结果前两行取 M 的第 1/第 0 行并取负交叉项，第 3 行 = M 第 3 行（平移）。
            var product = Matrix4x4.RotationZ(90) * Matrix4x4.Translation(new Vector3D(1, 2, 3));
            AssertElements(product, new[]
            {
                0.0, 1, 0, 0,
                -1, 0, 0, 0,
                0, 0, 1, 0,
                1, 2, 3, 1,
            }, "Rz(90°) × T(1,2,3)");

            // 语义确认：P 先转再平移 ⇒ 点 (1,0,0) 转 90° 到 (0,1,0) 再 +（1,2,3） = (1,3,3)。
            var p = new Vector3D(1, 0, 0) * product;
            Assert.That(p.X, Is.EqualTo(1.0).Within(Tol), "R×T 点变换 X");
            Assert.That(p.Y, Is.EqualTo(3.0).Within(Tol), "R×T 点变换 Y");
            Assert.That(p.Z, Is.EqualTo(3.0).Within(Tol), "R×T 点变换 Z");
        }

        [Test]
        public void Matrix4x4_Multiply_TranslationTimesRotation_ElementsAnalytic_AndOrderMatters()
        {
            // P = T(1,2,3) × Rz(90°)：先平移再旋转 ⇒ 点 (1,0,0) → (2,2,3) → 转 90° → (−2,2,3)。
            var product = Matrix4x4.Translation(new Vector3D(1, 2, 3)) * Matrix4x4.RotationZ(90);
            var p = new Vector3D(1, 0, 0) * product;

            Assert.That(p.X, Is.EqualTo(-2.0).Within(Tol), "T×R 点变换 X（与 R×T 的 (1,3,3) 不同，证明序敏感）");
            Assert.That(p.Y, Is.EqualTo(2.0).Within(Tol), "T×R 点变换 Y");
            Assert.That(p.Z, Is.EqualTo(3.0).Within(Tol), "T×R 点变换 Z");
        }

        [Test]
        public void Matrix4x4_Multiply_ScalingTimesTranslation_ElementsAnalytic()
        {
            // S(2,3,4) × T(5,0,0)：缩放先作用于点，平移分量原样保留（平移不被缩放）。
            var product = Matrix4x4.Scaling(new Vector3D(2, 3, 4)) * Matrix4x4.Translation(new Vector3D(5, 0, 0));
            AssertElements(product, new[]
            {
                2.0, 0, 0, 0,
                0, 3, 0, 0,
                0, 0, 4, 0,
                5, 0, 0, 1,
            }, "S(2,3,4) × T(5,0,0)");

            var p = new Vector3D(1, 1, 1) * product;
            Assert.That(p.X, Is.EqualTo(7.0).Within(Tol), "缩放再平移：1·2 + 5");
            Assert.That(p.Y, Is.EqualTo(3.0).Within(Tol), "缩放再平移：1·3");
            Assert.That(p.Z, Is.EqualTo(4.0).Within(Tol), "缩放再平移：1·4");
        }

        [Test]
        public void Matrix4x4_Multiply_Associativity_HoldsElementwise()
        {
            var a = Matrix4x4.Translation(new Vector3D(1, 2, 3));
            var b = Matrix4x4.RotationZ(37);
            var c = Matrix4x4.Scaling(new Vector3D(2, 3, 4));

            var left = (a * b) * c;
            var right = a * (b * c);
            for (int i = 0; i < 16; i++)
            {
                Assert.That(left.Elements[i], Is.EqualTo(right.Elements[i]).Within(1e-12),
                    $"结合律 (A×B)×C 与 A×(B×C) 的 Elements[{i}]");
            }
        }

        [Test]
        public void Matrix4x4_IsMirroring_DetectsNegativeDeterminant_AndProductsCompose()
        {
            Assert.That(new Matrix4x4().IsMirroring, Is.False, "单位阵非镜像");
            Assert.That(Matrix4x4.RotationX(30).IsMirroring, Is.False, "旋转（det=+1）非镜像");
            Assert.That(Matrix4x4.Scaling(new Vector3D(-1, 1, 1)).IsMirroring, Is.True, "单轴负缩放 det=−1 ⇒ 镜像");
            Assert.That(Matrix4x4.Translation(new Vector3D(-5, 0, 0)).IsMirroring, Is.False,
                "平移不改变 3×3 子阵（负平移不是镜像）");

            var mirror = Matrix4x4.Scaling(new Vector3D(-1, 1, 1));
            Assert.That((mirror * mirror).IsMirroring, Is.False, "两个镜像之积 det=+1 ⇒ 非镜像");
            Assert.That((mirror * Matrix4x4.RotationZ(90)).IsMirroring, Is.True, "镜像 × 旋转 det=−1 ⇒ 仍镜像");
        }
    }
}
