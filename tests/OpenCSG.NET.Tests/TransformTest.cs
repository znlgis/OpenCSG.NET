using System;
using Csg;
using NUnit.Framework;
using static Csg.Solids;

namespace Csg.Test
{
    /// <summary>
    /// 变换与镜像测试（审查项 3/4）：
    /// - 负行列式（镜像）变换后必须仍然法线朝外（净体积为正）；
    /// - 平面法线必须与顶点绕序一致（GeometryAssert.AssertPlanesMatchWinding）；
    /// - 旋转在定点后应用且合成顺序为 X→Y→Z（与宿主 CadCsgEvaluator/TransformNode 注释一致）；
    /// - 变换/布尔不得修改源实体（示例 Runner.Examples、宿主零件构建均复用同一实体对象）。
    /// </summary>
    [TestFixture]
    public class TransformTest : SolidTest
    {
        const double Tol = 1e-9;

        [Test]
        public void Mirror_ScaleNegativeX_KeepsOutwardNormals()
        {
            var mirrored = Cube(1, center: true).Scale(-1, 1, 1);
            GeometryAssert.AssertSolidInvariants(mirrored, "Scale(-1,1,1)");
            GeometryAssert.AssertVolume(mirrored, 1.0, Tol, "Scale(-1,1,1)");
            GeometryAssert.AssertBounds(mirrored, new Vector3D(-0.5, -0.5, -0.5), new Vector3D(0.5, 0.5, 0.5), Tol, "Scale(-1,1,1)");

            // 镜像后位于 x = +0.5 的面，其存储法线必须朝 +X
            foreach (var polygon in mirrored.Polygons)
            {
                bool onPositiveXFace = true;
                foreach (var vertex in polygon.Vertices)
                    if (Math.Abs(vertex.Pos.X - 0.5) > Tol) { onPositiveXFace = false; break; }
                if (onPositiveXFace)
                    Assert.That(polygon.Plane.Normal.X, Is.GreaterThan(0.99), "镜像后 x=+0.5 面的法线应朝 +X（绕序需翻转）");
            }
        }

        [Test]
        public void Mirror_UniformNegativeScale_KeepsOutwardNormals()
        {
            var mirrored = Cube(new Vector3D(2, 4, 6), true).Scale(-1);
            GeometryAssert.AssertSolidInvariants(mirrored, "Scale(-1)");
            GeometryAssert.AssertVolume(mirrored, 48.0, Tol, "Scale(-1)");
            GeometryAssert.AssertBounds(mirrored, new Vector3D(-1, -2, -3), new Vector3D(1, 2, 3), Tol, "Scale(-1)");
        }

        [Test]
        public void Mirror_ScaleNegativeY_KeepsOutwardNormals()
        {
            var mirrored = Cube(2, center: true).Scale(1, -1, 1);
            GeometryAssert.AssertSolidInvariants(mirrored, "Scale(1,-1,1)");
            GeometryAssert.AssertVolume(mirrored, 8.0, Tol, "Scale(1,-1,1)");
        }

        [Test]
        public void Mirror_ThenBoolean_StaysOutward()
        {
            var mirrored = Cube(3, center: true).Scale(-1, 1, 1);
            var union = mirrored.Union(Sphere(2, center: true));
            GeometryAssert.AssertOrientationConsistent(union, "镜像体 ∪ 球");
            GeometryAssert.AssertPlanesMatchWinding(union, "镜像体 ∪ 球");
            Assert.That(GeometryAssert.SignedVolume(union), Is.GreaterThan(0), "镜像参与布尔后仍须法线朝外");

            var difference = mirrored.Subtract(Sphere(2, center: true));
            GeometryAssert.AssertOrientationConsistent(difference, "镜像体 − 球");
            GeometryAssert.AssertPlanesMatchWinding(difference, "镜像体 − 球");
            Assert.That(GeometryAssert.SignedVolume(difference), Is.GreaterThan(0));
        }

        [Test]
        public void Rotation_KeepsVolumeAndOrientation()
        {
            var rotated = Cube(new Vector3D(2, 4, 6), true).RotateX(30).RotateY(45).RotateZ(60);
            GeometryAssert.AssertSolidInvariants(rotated, "旋转体");
            GeometryAssert.AssertVolume(rotated, 48.0, 1e-6, "旋转体");
        }

        [Test]
        public void TransformNode_RotationOrderIsXThenYThenZ()
        {
            var box = new BoxNode(new Vector3D(0, 0, 0), new Vector3D(2, 4, 8));
            var evaluated = CsgEvaluator.Evaluate(CsgNodes.Transform(new Vector3D(0, 0, 0), new Vector3D(90, 90, 0), box));

            var manualXY = Cube(new Vector3D(2, 4, 8), true).RotateX(90).RotateY(90);
            var manualYX = Cube(new Vector3D(2, 4, 8), true).RotateY(90).RotateX(90);

            var e = GeometryAssert.Bounds(evaluated);
            var xy = GeometryAssert.Bounds(manualXY);
            var yx = GeometryAssert.Bounds(manualYX);

            // 先 X 后 Y：包围盒尺寸 (4,8,2)；先 Y 后 X：尺寸 (8,2,4) —— 两者可区分
            Assert.That(e.Min.X, Is.EqualTo(xy.Min.X).Within(Tol), "旋转合成顺序应为 X→Y→Z");
            Assert.That(e.Max.X, Is.EqualTo(xy.Max.X).Within(Tol), "旋转合成顺序应为 X→Y→Z");
            Assert.That(e.Min.Z, Is.EqualTo(xy.Min.Z).Within(Tol), "旋转合成顺序应为 X→Y→Z");
            Assert.That(e.Max.Z, Is.EqualTo(xy.Max.Z).Within(Tol), "旋转合成顺序应为 X→Y→Z");
            // 两种顺序的 X 尺寸分别为 4 与 8，必须能区分，否则本用例无判别力
            Assert.That(Math.Abs(e.Max.X - yx.Max.X) > 1.0, Is.True, "测试用例必须能区分 X→Y→Z 与 Y→X→Z 两种顺序");
        }

        [Test]
        public void TransformNode_TranslationHappensBeforeRotationAboutGlobalOrigin()
        {
            var box = new BoxNode(new Vector3D(0, 0, 0), new Vector3D(2, 2, 2));
            var translated = CsgEvaluator.Evaluate(
                CsgNodes.Transform(new Vector3D(2, 0, 0), new Vector3D(0, 0, 90), box));

            // 先平移到 x∈[1,3]、再绕全局原点转 90°（(x,y,z)→(−y,x,z)）→ x∈[−1,1], y∈[1,3]
            GeometryAssert.AssertBounds(translated, new Vector3D(-1, 1, -1), new Vector3D(1, 3, 1), Tol, "TransformNode 先平移后旋转");
        }

        [Test]
        public void Transform_DoesNotMutateSource()
        {
            var source = Cube(2, center: true);
            var moved = source.Translate(5, 0, 0).RotateZ(30).Scale(2);
            var mirrored = source.Scale(-1, 1, 1);

            GeometryAssert.AssertBounds(source, new Vector3D(-1, -1, -1), new Vector3D(1, 1, 1), Tol, "源实体");
            GeometryAssert.AssertVolume(source, 8.0, Tol, "源实体");
            GeometryAssert.AssertVolume(moved, 64.0, 1e-6, "变换结果");
            GeometryAssert.AssertVolume(mirrored, 8.0, Tol, "镜像结果");
        }

        [Test]
        public void Boolean_DoesNotMutateOperands()
        {
            var a = Cube(3, center: true);
            var b = Sphere(2, center: true);
            var aVolume = GeometryAssert.SignedVolume(a);
            var aPolygons = a.Polygons.Count;
            var bVolume = GeometryAssert.SignedVolume(b);
            var bPolygons = b.Polygons.Count;

            a.Union(b);
            a.Subtract(b);
            a.Intersect(b);

            Assert.That(a.Polygons.Count, Is.EqualTo(aPolygons), "布尔不得修改左操作体");
            Assert.That(b.Polygons.Count, Is.EqualTo(bPolygons), "布尔不得修改右操作体");
            Assert.That(GeometryAssert.SignedVolume(a), Is.EqualTo(aVolume).Within(Tol), "布尔不得改变左操作体体积");
            Assert.That(GeometryAssert.SignedVolume(b), Is.EqualTo(bVolume).Within(Tol), "布尔不得改变右操作体体积");
        }
    }
}
