using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// 钉死 CSG → 网格 → 三视图 的坐标约定链。
    /// CSG 空间：Cube 的 size 沿 X/Y/Z；CsgMesh 做 Y↔Z 交换（Z-up）；
    /// 三视图取轴见 ViewProjectorContracts.ViewDirections。
    /// </summary>
    [TestFixture]
    public class ViewBridgeTest : SolidTest
    {
        const double Tol = 1e-9;

        [Test]
        public void Cube_AllThreeViews_AreSquaresOfSideS()
        {
            var cube = Solids.Cube(10, center: true);
            var views = ViewBridge.ProjectViews(cube, ViewDirections.OrthographicThree);

            Assert.That(views.Count, Is.EqualTo(3));
            foreach (var view in views)
            {
                Assert.That(view.Bounds.Width, Is.EqualTo(10.0).Within(Tol), $"{view.Direction.Name} 宽");
                Assert.That(view.Bounds.Height, Is.EqualTo(10.0).Within(Tol), $"{view.Direction.Name} 高");
            }
        }

        [Test]
        public void Box10x20x30_ViewExtentsFollowAxisMapping()
        {
            // CSG 尺寸 (X=10, Y=20, Z=30) → 网格 (X=10, Y=30, Z=20)
            // 主视 Look=+X, Right=−Y_mesh, Up=+Z_mesh  ⇒ 宽 = csg Z = 30, 高 = csg Y = 20
            // 俯视 Look=−Z, Right=−Y_mesh, Up=+X_mesh  ⇒ 宽 = csg Z = 30, 高 = csg X = 10
            // 左视 Look=−Y, Right=−X_mesh, Up=+Z_mesh  ⇒ 宽 = csg X = 10, 高 = csg Y = 20
            var box = Solids.Cube(new Vector3D(10, 20, 30), center: true);
            var views = ViewBridge.ProjectViews(box, ViewDirections.OrthographicThree);

            var front = views[0];
            var top = views[1];
            var left = views[2];

            Assert.That(front.Direction.Name, Is.EqualTo("主视"));
            Assert.That(front.Bounds.Width, Is.EqualTo(30.0).Within(Tol));
            Assert.That(front.Bounds.Height, Is.EqualTo(20.0).Within(Tol));

            Assert.That(top.Direction.Name, Is.EqualTo("俯视"));
            Assert.That(top.Bounds.Width, Is.EqualTo(30.0).Within(Tol));
            Assert.That(top.Bounds.Height, Is.EqualTo(10.0).Within(Tol));

            Assert.That(left.Direction.Name, Is.EqualTo("左视"));
            Assert.That(left.Bounds.Width, Is.EqualTo(10.0).Within(Tol));
            Assert.That(left.Bounds.Height, Is.EqualTo(20.0).Within(Tol));
        }

        [Test]
        public void Cube_ProducesVisibleSegments()
        {
            var cube = Solids.Cube(10, center: true);
            var views = ViewBridge.ProjectViews(cube, ViewDirections.OrthographicThree);

            foreach (var view in views)
            {
                Assert.That(view.Segments.Count, Is.GreaterThan(0), $"{view.Direction.Name} 无线段");
                Assert.That(view.VisibleCount, Is.GreaterThan(0), $"{view.Direction.Name} 无可见线段");
                Assert.That(view.VisibleCount + view.HiddenCount, Is.EqualTo(view.Segments.Count));
            }
        }

        [Test]
        public void DefaultDirections_AreOrthographicThree()
        {
            var cube = Solids.Cube(10, center: true);

            var byDefault = ViewBridge.ProjectViews(cube);
            var explicitThree = ViewBridge.ProjectViews(cube, ViewDirections.OrthographicThree);

            Assert.That(byDefault.Count, Is.EqualTo(explicitThree.Count));
            for (int i = 0; i < byDefault.Count; i++)
                Assert.That(byDefault[i].Direction.Name, Is.EqualTo(explicitThree[i].Direction.Name));
        }

        [Test]
        public void ToViewTriangles_TriangleCountMatchesCsgMesh()
        {
            var cube = Solids.Cube(10, center: true);

            Assert.That(ViewBridge.ToViewTriangles(cube).Count,
                Is.EqualTo(CsgMesh.TrianglesOf(cube).Count));
        }
    }
}
