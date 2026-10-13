using System.Collections.Generic;
using System.Linq;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// L5 集成：管线贯通 —— 节点路径（<c>CsgNode → CsgEvaluator.Evaluate → Solid</c>）与图元路径
    /// （<c>Solids.*</c> 直接构造）的同几何等价，以及「求值 + 投影」的确定性。
    /// <para>
    /// <b>等价口径（全部实测，见各断言注释）</b>：多边形数、有向体积（<see cref="GeometryAssert.SignedVolume"/>）、
    /// 网格三角形数（<see cref="CsgMesh.TrianglesOf"/>）逐项一致；两者各自经 <see cref="ViewBridge.ProjectViews"/>
    /// 的三视图按「段数/弧数一致 + 按 (Start, End) 排序后逐段端点差」比较。
    /// 探针（<c>artifacts/planD/d5probe</c>）实测读数：Box 节点 vs 图元 6/6 多边形、12/12 三角形、ΔV=0、
    /// 三视图端点最大偏差 0；Cylinder 节点 vs <c>Solids.Cylinder(8, 24, center: true)</c> 36/36、48/48、ΔV=0、偏差 0；
    /// 布尔 + 变换树 vs 手工图元序列 20/20、56/56、ΔV=0、偏差 0（注意 <c>TransformNode</c> 语义是
    /// 「先平移、再按 X→Y→Z 绕全局原点旋转」，手工序列须按 <c>Translate → RotateY</c> 同序拼装才等价 ——
    /// 探针里先写反顺序曾得端点差 ∞/段数 18,8,33 vs 26,8,23，改正后归零）。
    /// 因此等价断言取 <b>容差 0</b>：两条路径走同一构造函数序列 ⇒ 浮点逐位一致，这是实测支撑的严格口径，不是拍脑袋的收紧。
    /// </para>
    /// <para>
    /// <b>确定性</b>：同一树求值两次、各自投影一次 ⇒ 逐段端点差 0（实测段数 18,8,33 两次一致）。
    /// 按 brief 口径不用字节级 JSON 比较（<c>ViewJson</c> 在 Tests 程序集、不可引用），改用端点逐项 <c>Within(0)</c>。
    /// </para>
    /// </summary>
    [TestFixture]
    public class PipelineIntegrationTest
    {
        /// <summary>等价比较的端点容差：实测 0（同一构造序列 ⇒ 逐位一致），此处以 Within(0) 显式钉住。</summary>
        const double ExactTolerance = 0.0;

        [TestCase("Box")]
        [TestCase("Cylinder")]
        public void NodePath_AndPrimitivePath_ProduceSameGeometryAndViews(string kind)
        {
            CsgNode node;
            Solid byPrimitive;
            if (kind == "Box")
            {
                node = CsgNodes.Box(new Vector3D(1, 2, 3), new Vector3D(10, 20, 30));
                byPrimitive = Solids.Cube(new Vector3D(10, 20, 30), new Vector3D(1, 2, 3));
            }
            else
            {
                node = CsgNodes.Cylinder(new Vector3D(0, 0, 0), 8, 24);
                byPrimitive = Solids.Cylinder(8, 24, center: true);
            }

            var byNode = CsgEvaluator.Evaluate(node);

            // 实体事实（实测：Box 6/6、Cylinder 36/36 多边形；体积差 0；三角形 12/12 与 48/48）。
            Assert.That(byNode.Polygons.Count, Is.EqualTo(byPrimitive.Polygons.Count),
                $"{kind}：多边形数 {byNode.Polygons.Count} vs {byPrimitive.Polygons.Count}");
            Assert.That(GeometryAssert.SignedVolume(byNode),
                Is.EqualTo(GeometryAssert.SignedVolume(byPrimitive)).Within(ExactTolerance),
                $"{kind}：有向体积实测 {GeometryAssert.SignedVolume(byNode):R} vs {GeometryAssert.SignedVolume(byPrimitive):R}");
            Assert.That(CsgMesh.TrianglesOf(byNode).Count, Is.EqualTo(CsgMesh.TrianglesOf(byPrimitive).Count),
                $"{kind}：网格三角形数 {CsgMesh.TrianglesOf(byNode).Count} vs {CsgMesh.TrianglesOf(byPrimitive).Count}");

            // 投影事实（实测三视图端点最大偏差 0，段数如 Box 4,4,4；Cylinder 35,0,34）。
            AssertSameViews(
                ViewBridge.ProjectViews(byPrimitive, ViewDirections.OrthographicThree),
                ViewBridge.ProjectViews(byNode, ViewDirections.OrthographicThree),
                ExactTolerance, $"{kind} 节点路径 vs 图元路径");
        }

        /// <summary>
        /// 布尔 + 变换节点树 vs 手工图元运算序列：<c>Union(Box, Transform(Translate(2,0,12) + RotateY(30), Cylinder))</c>
        /// 与 <c>Cube.Union(Cylinder.Translate(2,0,12).RotateY(30))</c>。
        /// 实测：20/20 多边形、56/56 三角形、ΔV=0、三视图段数 18,8,33 两侧一致、端点最大偏差 0。
        /// </summary>
        [Test]
        public void BooleanTransformTree_MatchesEquivalentSolidSequence()
        {
            var tree = CsgNodes.Union(
                CsgNodes.Box(new Vector3D(0, 0, 0), new Vector3D(10, 10, 10)),
                CsgNodes.Transform(
                    new Vector3D(2, 0, 12), new Vector3D(0, 30, 0),
                    CsgNodes.Cylinder(new Vector3D(0, 0, 0), 3, 8)));

            // TransformNode 的 ApplyTransform 语义 = Translate → RotateX → RotateY → RotateZ（绕全局原点）；
            // 手工序列必须同序，否则几何等价不成立（探针实测反序时端点差 ∞）。
            var byPrimitive = Solids.Cube(new Vector3D(10, 10, 10), new Vector3D(0, 0, 0))
                .Union(Solids.Cylinder(3, 8, center: true).Translate(2, 0, 12).RotateY(30));

            var byNode = CsgEvaluator.Evaluate(tree);

            Assert.That(byNode.Polygons.Count, Is.EqualTo(20), "实测布尔输出 20 个多边形");
            Assert.That(byPrimitive.Polygons.Count, Is.EqualTo(20));
            Assert.That(GeometryAssert.SignedVolume(byNode),
                Is.EqualTo(GeometryAssert.SignedVolume(byPrimitive)).Within(ExactTolerance),
                $"有向体积 {GeometryAssert.SignedVolume(byNode):R} vs {GeometryAssert.SignedVolume(byPrimitive):R}");
            Assert.That(CsgMesh.TrianglesOf(byNode).Count, Is.EqualTo(CsgMesh.TrianglesOf(byPrimitive).Count));

            AssertSameViews(
                ViewBridge.ProjectViews(byPrimitive, ViewDirections.OrthographicThree),
                ViewBridge.ProjectViews(byNode, ViewDirections.OrthographicThree),
                ExactTolerance, "布尔+变换树 vs 手工序列");
        }

        /// <summary>
        /// 完整管线（spec §7 L5）：<c>CsgNode → Evaluate → Solid → CsgMesh → ViewProjector.Project → OrthographicViews</c>。
        /// 本用例手工重走整条链（不经 <see cref="ViewBridge"/>），并与桥接口径对拍 ——
        /// 桥的输入/输出等价由本用例的「逐段端点差 0」钉住（实测 18,8,33 段一致）。
        /// </summary>
        [Test]
        public void FullChain_CsgMeshToProjector_MatchesViewBridge()
        {
            var solid = CsgEvaluator.Evaluate(CsgNodes.Union(
                CsgNodes.Box(new Vector3D(0, 0, 0), new Vector3D(10, 10, 10)),
                CsgNodes.Transform(
                    new Vector3D(2, 0, 12), new Vector3D(0, 30, 0),
                    CsgNodes.Cylinder(new Vector3D(0, 0, 0), 3, 8))));

            var mesh = CsgMesh.TrianglesOf(solid);
            var triangles = new List<ViewTriangle>(mesh.Count);
            foreach (var triangle in mesh)
                triangles.Add(new ViewTriangle(
                    new ViewPoint(triangle.A.X, triangle.A.Y, triangle.A.Z),
                    new ViewPoint(triangle.B.X, triangle.B.Y, triangle.B.Z),
                    new ViewPoint(triangle.C.X, triangle.C.Y, triangle.C.Z)));

            var projector = new ViewProjector(triangles);
            Assert.That(projector.TriangleCount, Is.EqualTo(mesh.Count), "完整管线：投影器应收到全部网格三角形（实测 56）");
            Assert.That(projector.TriangleCount, Is.EqualTo(56));

            var manual = projector.Project(ViewDirections.OrthographicThree);
            var views = new OrthographicViews("l5-full-chain", "L5 完整管线", manual);

            Assert.That(views.Views.Count, Is.EqualTo(3), "三视图（主视/俯视/左视）");
            Assert.That(views.Views.Sum(v => v.Segments.Count), Is.GreaterThan(0),
                "完整管线输出不得为空（段数实测 18+8+33）");

            AssertSameViews(
                ViewBridge.ProjectViews(solid, ViewDirections.OrthographicThree),
                manual, ExactTolerance, "ViewBridge vs 手工完整链");
        }

        /// <summary>
        /// 确定性：同一棵树求值两次、各自投影一次。
        /// 实测逐段端点最大偏差 0，两次段数一致（18,8,33）；含圆柱（弧识别路径）也在内。
        /// </summary>
        [Test]
        public void EvaluateTwice_ProjectsIdentically()
        {
            var tree = CsgNodes.Union(
                CsgNodes.Box(new Vector3D(0, 0, 0), new Vector3D(10, 10, 10)),
                CsgNodes.Transform(
                    new Vector3D(2, 0, 12), new Vector3D(0, 30, 0),
                    CsgNodes.Cylinder(new Vector3D(0, 0, 0), 3, 8)));

            var first = ViewBridge.ProjectViews(CsgEvaluator.Evaluate(tree), ViewDirections.OrthographicThree);
            var second = ViewBridge.ProjectViews(CsgEvaluator.Evaluate(tree), ViewDirections.OrthographicThree);

            AssertSameViews(first, second, ExactTolerance, "两次求值 + 两次投影");
        }

        /// <summary>
        /// 视图集合等价：逐视图比较方向名、段数（含 V/H 计数）、弧数，随后把两边的段按
        /// (Start.X, Start.Y, End.X, End.Y) 排序后逐段比较端点（不含朝向修正 —— 实测两边逐位一致，
        /// 无需朝向容忍），并同样对齐弧的中心/半径/扫掠角。
        /// </summary>
        internal static void AssertSameViews(
            IReadOnlyList<ViewProjection> expected,
            IReadOnlyList<ViewProjection> actual,
            double tolerance,
            string context)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count), $"{context}：视图数");

            for (var i = 0; i < expected.Count; i++)
            {
                var e = expected[i];
                var a = actual[i];
                var where = $"{context}/{e.Direction.Name}";

                Assert.That(a.Direction.Name, Is.EqualTo(e.Direction.Name), $"{where}：视图方向顺序");
                Assert.That(a.Segments.Count, Is.EqualTo(e.Segments.Count),
                    $"{where}：段数 {a.Segments.Count} vs {e.Segments.Count}");
                Assert.That(a.VisibleCount, Is.EqualTo(e.VisibleCount), $"{where}：可见段数");
                Assert.That(a.ArcCount, Is.EqualTo(e.ArcCount),
                    $"{where}：弧数 {a.ArcCount} vs {e.ArcCount}");

                var sortedExpected = e.Segments.OrderBy(s => (s.Start.X, s.Start.Y, s.End.X, s.End.Y)).ToList();
                var sortedActual = a.Segments.OrderBy(s => (s.Start.X, s.Start.Y, s.End.X, s.End.Y)).ToList();
                for (var k = 0; k < sortedExpected.Count; k++)
                {
                    Assert.That(sortedActual[k].IsVisible, Is.EqualTo(sortedExpected[k].IsVisible),
                        $"{where}：排序后第 {k} 段可见性 {sortedActual[k].IsVisible} vs {sortedExpected[k].IsVisible}");
                    Assert.That(sortedActual[k].Start.X, Is.EqualTo(sortedExpected[k].Start.X).Within(tolerance),
                        $"{where}：第 {k} 段 Start.X（实测两边逐位一致）");
                    Assert.That(sortedActual[k].Start.Y, Is.EqualTo(sortedExpected[k].Start.Y).Within(tolerance),
                        $"{where}：第 {k} 段 Start.Y");
                    Assert.That(sortedActual[k].End.X, Is.EqualTo(sortedExpected[k].End.X).Within(tolerance),
                        $"{where}：第 {k} 段 End.X");
                    Assert.That(sortedActual[k].End.Y, Is.EqualTo(sortedExpected[k].End.Y).Within(tolerance),
                        $"{where}：第 {k} 段 End.Y");
                }

                for (var k = 0; k < e.Arcs.Count; k++)
                {
                    var ea = e.Arcs[k];
                    var aa = a.Arcs[k];
                    Assert.That(aa.Center.X, Is.EqualTo(ea.Center.X).Within(tolerance), $"{where}：弧 {k} 圆心 X");
                    Assert.That(aa.Center.Y, Is.EqualTo(ea.Center.Y).Within(tolerance), $"{where}：弧 {k} 圆心 Y");
                    Assert.That(aa.Radius, Is.EqualTo(ea.Radius).Within(tolerance), $"{where}：弧 {k} 半径");
                    Assert.That(aa.SweepDegrees, Is.EqualTo(ea.SweepDegrees).Within(tolerance), $"{where}：弧 {k} 扫掠角");
                    Assert.That(aa.IsVisible, Is.EqualTo(ea.IsVisible), $"{where}：弧 {k} 可见性");
                }
            }
        }
    }
}
