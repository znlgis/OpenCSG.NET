using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// 视图桥（ViewTriangle / ViewMeshReader / ViewArc）与各入口的 null 守卫契约
    /// （Plan C / Task 7 Step 1）。异常类型与 ParamName 逐一读实现确认。
    /// </summary>
    [TestFixture]
    public class ViewContractsTest
    {
        [Test]
        public void ViewTriangle_Indexer_ReturnsVertices_AndRejectsOutOfRange()
        {
            var a = new ViewPoint(0, 0, 0);
            var b = new ViewPoint(1, 0, 0);
            var c = new ViewPoint(0, 1, 0);
            var triangle = new ViewTriangle(a, b, c);

            Assert.That(triangle[0].X, Is.EqualTo(0.0));
            Assert.That(triangle[1].X, Is.EqualTo(1.0));
            Assert.That(triangle[2].Y, Is.EqualTo(1.0));

            foreach (var index in new[] { 3, -1, int.MaxValue })
            {
                var ex = Assert.Throws<ArgumentOutOfRangeException>(() => { var _ = triangle[index]; });
                Assert.That(ex!.ParamName, Is.EqualTo("index"), $"索引 {index} 应报 ArgumentOutOfRangeException");
            }
        }

        [Test]
        public void ViewMeshReader_ShortAsciiFile_IsParsedAsAscii_NotBinary()
        {
            // 读实现：IsBinaryStl 先看文件长度 < 84 ⇒ 判 ASCII（避免短文件按二进制误读）。
            var path = Path.Combine(Path.GetTempPath(), $"opencsg-t7-short-{Guid.NewGuid():N}.stl");
            var content = "solid s\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendsolid\n";
            File.WriteAllText(path, content);
            try
            {
                Assert.That(new FileInfo(path).Length, Is.LessThan(84), "夹具前提：文件短于二进制 STL 最小长度");

                var triangles = ViewMeshReader.ReadStlFile(path);
                Assert.That(triangles.Count, Is.EqualTo(1));
                Assert.That(triangles[0].A.X, Is.EqualTo(0.0));
                Assert.That(triangles[0].B.X, Is.EqualTo(1.0));
                Assert.That(triangles[0].C.Y, Is.EqualTo(1.0));
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void ViewArc_ToString_FormatsSweepAndVisibility()
        {
            var culture = CultureInfo.CurrentCulture;
            var dashed = new ViewArc(new ViewPoint2(1, 2), 3.5, 0, Math.PI, isVisible: false, isClosed: false);
            Assert.That(dashed.ToString(), Is.EqualTo(
                string.Format(culture, "圆心({0:F2},{1:F2}) R{2:F2} {3:F1}° {4}",
                    1.0, 2.0, 3.5, 180.0, "虚线")));

            var closed = new ViewArc(new ViewPoint2(0, 0), 1.0, 0, 2 * Math.PI, isVisible: true, isClosed: true);
            Assert.That(closed.ToString(), Does.Contain("可见").And.Contain("360.0°"),
                "整圆 IsClosed=true ⇒ SweepDegrees 固定 360");
        }

        [Test]
        public void CsgMesh_TrianglesOf_NullSolid_Throws()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => CsgMesh.TrianglesOf((Solid)null!));
            Assert.That(ex!.ParamName, Is.EqualTo("solid"));
        }

        [Test]
        public void ViewMeshTopology_NullTriangles_Throws()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => new ViewMeshTopology(null!));
            Assert.That(ex!.ParamName, Is.EqualTo("triangles"));
        }

        [Test]
        public void ViewProjector_ProjectView_NullDirection_Throws()
        {
            var projector = new ViewProjector(Array.Empty<ViewTriangle>());
            var ex = Assert.Throws<ArgumentNullException>(() => projector.ProjectView(null!));
            Assert.That(ex!.ParamName, Is.EqualTo("view"));
        }

        [Test]
        public void ViewArcFitter_NullInputs_Throw()
        {
            var fit = Assert.Throws<ArgumentNullException>(() => ViewArcFitter.Fit<ViewSegment>(null!));
            Assert.That(fit!.ParamName, Is.EqualTo("segments"));

            var merge = Assert.Throws<ArgumentNullException>(() => ViewArcFitter.MergeConcyclic(null!));
            Assert.That(merge!.ParamName, Is.EqualTo("arcs"));
        }

        [Test]
        public void ViewSegmentCleanup_NullSegments_Throws_EmptyReturnsEmpty()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => ViewSegmentCleanup.Clean(null!, ViewBounds2.Empty));
            Assert.That(ex!.ParamName, Is.EqualTo("segments"));

            var cleaned = ViewSegmentCleanup.Clean(Array.Empty<ViewSegment>(), ViewBounds2.Empty);
            Assert.That(cleaned.Count, Is.EqualTo(0), "空输入 ⇒ 空输出（不抛）");
        }
    }
}
