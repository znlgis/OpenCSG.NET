using System;
using System.Collections.Generic;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// 值类型 / 杂项单行的 L1 单元回填（Plan C / Task 8）：BoundingBox、Vector2D/Vector3D 运算符、
    /// Solid.Scale、Solids.Cube 三参重载、CsgNodes.Box 双精度重载、ViewPoint.Scale/加法、
    /// ViewProjection.ArcCount、ViewProjector.TriangleCount、DepthRaster 空路径、四处 ToString。
    /// 期望值全部手推；ToString 只断言非空 + 关键数值（不钉死格式）。
    /// 注：<c>Solid.CombinedBounds</c> 为**无调用点的私有静态**（Union 入口走 CombinedBoundsAll），
    /// 测试程序集不可达 ⇒ 结构性不可达，改由本文件的布尔并集包络用例间接旁证并集语义。
    /// </summary>
    [TestFixture]
    public class ValueTypeMiscTest
    {
        const double Tol = 1e-9;

        static void AssertVolumeRel(Solid solid, double expected, string message)
            => GeometryAssert.AssertVolume(solid, expected, Math.Abs(expected) * 1e-6 + 1e-9, message);

        // ── BoundingBox ───────────────────────────────────────────────────────────

        [Test]
        public void BoundingBox_CtorMinMax_AndSizeCtorIsCentered()
        {
            var box = new BoundingBox(new Vector3D(1, 2, 3), new Vector3D(5, 8, 10));
            Assert.That(box.Min.X, Is.EqualTo(1.0));
            Assert.That(box.Min.Y, Is.EqualTo(2.0));
            Assert.That(box.Min.Z, Is.EqualTo(3.0));
            Assert.That(box.Max.X, Is.EqualTo(5.0));
            Assert.That(box.Max.Y, Is.EqualTo(8.0));
            Assert.That(box.Max.Z, Is.EqualTo(10.0));

            var size = box.Size; // Max − Min
            Assert.That(size.X, Is.EqualTo(4.0).Within(Tol), "Size.X");
            Assert.That(size.Y, Is.EqualTo(6.0).Within(Tol), "Size.Y");
            Assert.That(size.Z, Is.EqualTo(7.0).Within(Tol), "Size.Z");

            // 三参 ctor = 以原点为中心、边长为 (dx,dy,dz) 的盒。
            var centered = new BoundingBox(dx: 2, dy: 4, dz: 6);
            Assert.That(centered.Min.X, Is.EqualTo(-1.0), "居中盒 Min.X");
            Assert.That(centered.Min.Y, Is.EqualTo(-2.0), "居中盒 Min.Y");
            Assert.That(centered.Min.Z, Is.EqualTo(-3.0), "居中盒 Min.Z");
            Assert.That(centered.Max.X, Is.EqualTo(1.0), "居中盒 Max.X");
            Assert.That(centered.Max.Y, Is.EqualTo(2.0), "居中盒 Max.Y");
            Assert.That(centered.Max.Z, Is.EqualTo(3.0), "居中盒 Max.Z");
        }

        [Test]
        public void BoundingBox_Addition_TranslatesBothCorners()
        {
            var box = new BoundingBox(new Vector3D(0, 0, 0), new Vector3D(2, 4, 6));
            var moved = box + new Vector3D(10, -5, 1);

            Assert.That(moved.Min.X, Is.EqualTo(10.0), "op_Addition 平移 Min.X");
            Assert.That(moved.Min.Y, Is.EqualTo(-5.0), "op_Addition 平移 Min.Y");
            Assert.That(moved.Min.Z, Is.EqualTo(1.0), "op_Addition 平移 Min.Z");
            Assert.That(moved.Max.X, Is.EqualTo(12.0), "op_Addition 平移 Max.X");
            Assert.That(moved.Max.Y, Is.EqualTo(-1.0), "op_Addition 平移 Max.Y");
            Assert.That(moved.Max.Z, Is.EqualTo(7.0), "op_Addition 平移 Max.Z");
            Assert.That(moved.Size.X, Is.EqualTo(box.Size.X), "平移不改变 Size");
        }

        [Test]
        public void BoundingBox_ToString_CarriesCenterAndSize()
        {
            // 格式 = "{Center}, s={Size}"（Vector3D 定长 3 位小数），只钉关键数值。
            var text = new BoundingBox(new Vector3D(0, 0, 0), new Vector3D(2, 4, 6)).ToString();

            Assert.That(text, Is.Not.Empty, "BoundingBox.ToString 非空");
            Assert.That(text, Does.Contain("[1.000, 2.000, 3.000]"), "含中心 (1,2,3)");
            Assert.That(text, Does.Contain("[2.000, 4.000, 6.000]"), "含 Size (2,4,6)");
        }

        // ── Vector2D / Vector3D 运算符 ────────────────────────────────────────────

        [Test]
        public void Vector2D_MultiplyAndDivide_AreComponentwise()
        {
            var a = new Vector2D(2, 3);
            var b = new Vector2D(4, 5);

            var product = a * b; // 逐分量乘（非叉积/点积）
            Assert.That(product.X, Is.EqualTo(8.0), "Vector2D×Vector2D 是逐分量乘：2·4");
            Assert.That(product.Y, Is.EqualTo(15.0), "Vector2D×Vector2D 是逐分量乘：3·5");

            var scaled = a * 1.5;
            Assert.That(scaled.X, Is.EqualTo(3.0).Within(Tol), "Vector2D×double");
            Assert.That(scaled.Y, Is.EqualTo(4.5).Within(Tol), "Vector2D×double");

            var divided = a / 4;
            Assert.That(divided.X, Is.EqualTo(0.5).Within(Tol), "Vector2D÷double");
            Assert.That(divided.Y, Is.EqualTo(0.75).Within(Tol), "Vector2D÷double");
        }

        [Test]
        public void Vector3D_MultiplyOverloads_ComponentwiseAndScalar()
        {
            var a = new Vector3D(2, 3, 4);
            var b = new Vector3D(5, 6, 7);

            var product = a * b; // 逐分量乘
            Assert.That(product.X, Is.EqualTo(10.0), "Vector3D×Vector3D 逐分量：2·5");
            Assert.That(product.Y, Is.EqualTo(18.0), "Vector3D×Vector3D 逐分量：3·6");
            Assert.That(product.Z, Is.EqualTo(28.0), "Vector3D×Vector3D 逐分量：4·7");

            var scaled = a * 0.5;
            Assert.That(scaled.X, Is.EqualTo(1.0), "Vector3D×double");
            Assert.That(scaled.Y, Is.EqualTo(1.5), "Vector3D×double");
            Assert.That(scaled.Z, Is.EqualTo(2.0), "Vector3D×double");
        }

        // ── Solid.Scale ───────────────────────────────────────────────────────────

        [Test]
        public void Solid_ScaleUniform_VolumeScalesWithCube()
        {
            var cube = Solids.Cube(2, center: true);                        // 边长 2、体积 8
            var scaled = cube.Scale(3);                                      // 边长 6、体积 216 = 8·3³
            GeometryAssert.AssertBounds(scaled, new Vector3D(-3, -3, -3), new Vector3D(3, 3, 3), Tol, "Scale(3) 包络");
            GeometryAssert.AssertVolume(scaled, 216.0, Tol, "Scale(3) 体积 = 原体积 × 3³");

            var viaVector = cube.Scale(new Vector3D(3, 3, 3));
            GeometryAssert.AssertVolume(viaVector, 216.0, Tol, "Scale(Vector3D) 与 Scale(double) 同值");
        }

        [Test]
        public void Solid_ScaleAnisotropic_BoundsAndVolume()
        {
            var cube = Solids.Cube(2, center: true);
            var scaled = cube.Scale(1, 2, 3);                                // 体积 8·1·2·3 = 48
            GeometryAssert.AssertBounds(scaled, new Vector3D(-1, -2, -3), new Vector3D(1, 2, 3), Tol, "Scale(1,2,3) 包络");
            GeometryAssert.AssertVolume(scaled, 48.0, Tol, "Scale(x,y,z) 体积 = 原体积 × x·y·z");
        }

        // ── 图元便捷重载 ──────────────────────────────────────────────────────────

        [Test]
        public void Solids_CubeWidthHeightDepthOverload_BoundsFromOriginOrCenter()
        {
            GeometryAssert.AssertBounds(Solids.Cube(2, 4, 6), new Vector3D(0, 0, 0), new Vector3D(2, 4, 6),
                Tol, "Cube(w,h,d) 默认自原点");
            GeometryAssert.AssertBounds(Solids.Cube(2, 4, 6, center: true), new Vector3D(-1, -2, -3), new Vector3D(1, 2, 3),
                Tol, "Cube(w,h,d,center:true)");
        }

        [Test]
        public void Solid_Union_TwoDisjointBoxes_BoundsAreHandComputedEnvelope()
        {
            // Union 入口经 CombinedBoundsAll 求两盒并集包络（私有 CombinedBounds 无调用点、测试不可达）。
            // a：X,Y,Z ∈ [−5,5]；b：X∈[18,22]、Y∈[3,5]、Z∈[−6,0]（与 a 分离）⇒ 包络与体积均可手推。
            var a = CsgNodes.Box(0, 0, 0, 10, 10, 10);
            var b = CsgNodes.Box(20, 4, -3, 4, 2, 6);
            var union = CsgEvaluator.Evaluate(CsgNodes.Union(a, b));

            GeometryAssert.AssertBounds(union, new Vector3D(-5, -5, -6), new Vector3D(22, 5, 5),
                1e-6, "并集包络 = 两盒包络之并");
            AssertVolumeRel(union, 10 * 10 * 10 + 4 * 2 * 6, "分离两盒并集体积 = 体积和");
        }

        [Test]
        public void CsgNodes_BoxDoubleOverload_EvaluatesToAnalyticBox()
        {
            // Box(cx,cy,cz, sx,sy,sz)：中心 (1,2,3)、尺寸 (4,5,6) ⇒ X[−1,3]、Y[−0.5,4.5]、Z[0,6]。
            var solid = CsgEvaluator.Evaluate(CsgNodes.Box(1, 2, 3, 4, 5, 6));

            GeometryAssert.AssertBounds(solid, new Vector3D(-1, -0.5, 0), new Vector3D(3, 4.5, 6),
                Tol, "CsgNodes.Box(double...) 求值包络");
            GeometryAssert.AssertVolume(solid, 4.0 * 5.0 * 6.0, Tol, "CsgNodes.Box 体积 = 尺寸积");
        }

        // ── ViewPoint ─────────────────────────────────────────────────────────────

        [Test]
        public void ViewPoint_ScaleAndAddition_ArePerAxis()
        {
            var point = new ViewPoint(1, 2, 3);

            var scaled = point.Scale(2, 0.5, -1);
            Assert.That(scaled.X, Is.EqualTo(2.0), "ViewPoint.Scale X（逐轴缩放，用于镜像投影方向）");
            Assert.That(scaled.Y, Is.EqualTo(1.0), "ViewPoint.Scale Y");
            Assert.That(scaled.Z, Is.EqualTo(-3.0), "ViewPoint.Scale Z 可取负（镜像）");

            var sum = point + new ViewPoint(4, 5, 6);
            Assert.That(sum.X, Is.EqualTo(5.0), "ViewPoint.op_Addition X");
            Assert.That(sum.Y, Is.EqualTo(7.0), "ViewPoint.op_Addition Y");
            Assert.That(sum.Z, Is.EqualTo(9.0), "ViewPoint.op_Addition Z");
        }

        // ── ViewProjection / ViewProjector ────────────────────────────────────────

        [Test]
        public void ViewProjection_ArcCount_ZeroWhenNone_TwoWhenProvided()
        {
            var segments = new List<ViewSegment>
            {
                new(new ViewPoint2(0, 0), new ViewPoint2(1, 0), true),
            };

            var withoutArcs = new ViewProjection(ViewDirections.Front, segments);
            Assert.That(withoutArcs.ArcCount, Is.EqualTo(0), "未给 arcs（默认空数组）⇒ ArcCount=0");
            Assert.That(withoutArcs.Arcs.Count, Is.EqualTo(0), "Arcs 为空而非 null");

            var arcs = new List<ViewArc>
            {
                new(new ViewPoint2(0, 0), 5, 0, Math.PI / 2, true, false),
                new(new ViewPoint2(10, 10), 2, Math.PI, 2 * Math.PI, false, false),
            };
            var withArcs = new ViewProjection(ViewDirections.Top, segments, arcs);
            Assert.That(withArcs.ArcCount, Is.EqualTo(2), "给定 2 条弧 ⇒ ArcCount=2（含可见与隐藏）");
        }

        [Test]
        public void ViewProjector_TriangleCount_MatchesInputMesh()
        {
            // 立方体 → 6 凸四边形 × (4−2) 扇形三角形 = 12。
            var triangles = ViewBridge.ToViewTriangles(Solids.Cube(10, center: true));
            Assert.That(triangles.Count, Is.EqualTo(12), "前置：立方体三角化 12 面");

            var projector = new ViewProjector(triangles);
            Assert.That(projector.TriangleCount, Is.EqualTo(12), "ViewProjector.TriangleCount = 输入三角形数");
        }

        [Test]
        public void ViewProjector_EmptyMesh_ProjectView_ProducesEmptyAllGreenResult()
        {
            // 空三角形列表 ⇒ frontFacing 全空 ⇒ DepthRaster.Build 走 empty 分支（不建栅格）⇒ 0 段 0 弧、空包络。
            var projector = new ViewProjector(Array.Empty<ViewTriangle>());
            Assert.That(projector.TriangleCount, Is.EqualTo(0), "空网格 TriangleCount=0");

            var view = projector.ProjectView(ViewDirections.Front);

            Assert.That(view.Segments.Count, Is.EqualTo(0), "空网格投影 0 线段");
            Assert.That(view.ArcCount, Is.EqualTo(0), "空网格投影 0 圆弧");
            Assert.That(view.Bounds.MinX, Is.EqualTo(0.0), "空包络 MinX");
            Assert.That(view.Bounds.MinY, Is.EqualTo(0.0), "空包络 MinY");
            Assert.That(view.Bounds.MaxX, Is.EqualTo(0.0), "空包络 MaxX");
            Assert.That(view.Bounds.MaxY, Is.EqualTo(0.0), "空包络 MaxY");
            Assert.That(view.Direction.Name, Is.EqualTo("主视"), "方向透传");
        }

        // ── ToString 三处（非空 + 关键数值，不钉格式） ────────────────────────────

        [Test]
        public void ToString_VertexViewVectorViewBounds_CarryKeyNumbers()
        {
            var vertex = new Vertex(new Vector3D(1.5, -2, 3), new Vector2D(0, 0));
            var vertexText = vertex.ToString();
            Assert.That(vertexText, Is.Not.Empty, "Vertex.ToString 非空");
            Assert.That(vertexText, Does.Contain("1.500"), "Vertex.ToString 含 Pos.X 数值");
            Assert.That(vertexText, Does.Contain("-2.000"), "Vertex.ToString 含 Pos.Y 数值");

            var vectorText = new ViewVector(1.5, -2.25, 0.5).ToString();
            Assert.That(vectorText, Is.Not.Empty, "ViewVector.ToString 非空");
            Assert.That(vectorText, Does.Contain("1.5"), "ViewVector.ToString 含 X");
            Assert.That(vectorText, Does.Contain("-2.25"), "ViewVector.ToString 含 Y");

            var boundsText = new ViewBounds2(1, 2, 3, 4).ToString();
            Assert.That(boundsText, Is.Not.Empty, "ViewBounds2.ToString 非空");
            Assert.That(boundsText, Does.Contain("1").And.Contain("2").And.Contain("3").And.Contain("4"),
                "ViewBounds2.ToString 含四个角值");
        }
    }
}
