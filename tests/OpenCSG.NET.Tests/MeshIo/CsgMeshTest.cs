using System.Collections.Generic;
using System.Linq;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    [TestFixture]
    public class CsgMeshTest : SolidTest
    {
        [Test]
        public void Cube100_FromCsg_HasPositiveSignedVolume()
        {
            var cube = Solids.Cube(100, center: true);
            var triangles = CsgMesh.TrianglesOf(cube);

            // CsgMesh 做 Y↔Z 交换（行列式 −1 的镜像）并反转绕序补偿；
            // 补偿正确则有向体积必须为正（法线朝外），数值为 100³。
            Assert.That(MeshAssert.SignedVolume(triangles), Is.EqualTo(1_000_000.0).Within(1e-6),
                "CsgMesh 的绕序补偿失败 ⇒ STL 导出会内外翻转");
        }

        [Test]
        public void Cube100_FromCsg_SwapsYAndZ()
        {
            var cube = Solids.Cube(new Vector3D(10, 20, 30), center: true);
            var triangles = CsgMesh.TrianglesOf(cube);
            var (min, max) = MeshAssert.Bounds(triangles);

            // CSG 尺寸 (10, 20, 30) → 网格尺寸 (10, 30, 20)：Y 与 Z 互换
            Assert.That(max.X - min.X, Is.EqualTo(10.0).Within(1e-9));
            Assert.That(max.Y - min.Y, Is.EqualTo(30.0).Within(1e-9));
            Assert.That(max.Z - min.Z, Is.EqualTo(20.0).Within(1e-9));
        }

        [Test]
        public void Cube100_FromCsg_AreaIsPreserved()
        {
            var cube = Solids.Cube(100, center: true);
            var triangles = CsgMesh.TrianglesOf(cube);

            // 6 面 × 100 × 100；镜像不改变面积
            Assert.That(MeshAssert.TotalArea(triangles), Is.EqualTo(60_000.0).Within(1e-6));
        }

        [Test]
        public void Cube100_FromCsg_IsClosedOrientedManifold()
        {
            var cube = Solids.Cube(100, center: true);
            var triangles = CsgMesh.TrianglesOf(cube);

            MeshAssert.AssertClosedOrientedManifold(triangles);
        }

        [Test]
        public void TwoSolids_Overload_TriangleCountIsSum()
        {
            var a = Solids.Cube(10, center: true);
            var b = Solids.Cube(10, center: true).Translate(x: 100);

            var single = CsgMesh.TrianglesOf(new[] { a, b });

            Assert.That(single.Count,
                Is.EqualTo(CsgMesh.TrianglesOf(a).Count + CsgMesh.TrianglesOf(b).Count));
        }

        [Test]
        public void Cube100_GridArea_MatchesSolidPolygonArea()
        {
            var cube = Solids.Cube(100, center: true);
            var triangles = CsgMesh.TrianglesOf(cube);

            double solidArea = 0;
            foreach (var polygon in cube.Polygons)
            {
                var v = polygon.Vertices;
                var a = v[0].Pos;
                for (int i = 1; i + 1 < v.Count; i++)
                {
                    var u = v[i].Pos - a;
                    var w = v[i + 1].Pos - a;
                    var n = u.Cross(w);
                    solidArea += n.Length / 2.0;
                }
            }

            Assert.That(MeshAssert.TotalArea(triangles), Is.EqualTo(solidArea).Within(1e-9));
        }

        [Test]
        public void Cube100_FromCsg_HasExpectedTopology()
        {
            var cube = Solids.Cube(100, center: true);
            var triangles = CsgMesh.TrianglesOf(cube);

            var report = MeshAssert.Analyze(triangles);

            // 立方体扇形剖分：8 个焊接顶点、18 条焊接边（12 条棱 + 6 条面对角线）、12 个三角面
            Assert.That(report.VertexCount, Is.EqualTo(8), $"| {report}");
            Assert.That(report.EdgeCount, Is.EqualTo(18), $"| {report}");
            Assert.That(report.TriangleCount, Is.EqualTo(12), $"| {report}");
            Assert.That(report.EulerCharacteristic, Is.EqualTo(2), $"| {report}");
        }

        [Test]
        public void Cube100_MeshVolume_MatchesSolidSideVolume()
        {
            var cube = Solids.Cube(100, center: true);
            var triangles = CsgMesh.TrianglesOf(cube);

            var solidVolume = GeometryAssert.SignedVolume(cube);

            // 分别断言两侧符号，才能排除「CSG 侧绕序朝内 + CsgMesh 未翻绕序」互相抵消的情形：
            // 那种配错下两个体积都"看着自洽"，但 Solid 侧会是负值。
            Assert.That(solidVolume, Is.GreaterThan(0), "CSG 侧立方体绕序朝内 ⇒ 与 CsgMesh 的配对反转会互相掩盖");
            Assert.That(MeshAssert.SignedVolume(triangles), Is.EqualTo(solidVolume).Within(1e-6));
        }
    }
}
