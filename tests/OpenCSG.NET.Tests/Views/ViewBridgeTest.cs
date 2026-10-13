using System.Linq;
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
        public void OffCenterBox_ViewScreenRangesRejectMirroredAxes()
        {
            // 居中且各轴对称的夹具在镜像下不变（宽高、Min/Max 全都一样），所以检测不出镜像的取轴。
            // 这里把三个模型轴都偏置，使每个视图的两个屏幕轴范围都不对称：
            // 任何 Right/Up 的符号翻转都会把某个 Min/Max 翻到另一边。
            var box = Solids.Cube(new Vector3D(10, 20, 30), new Vector3D(2, 3, 7));
            var views = ViewBridge.ProjectViews(box, ViewDirections.OrthographicThree);

            var front = views[0];
            var top = views[1];
            var left = views[2];

            // 推导（CSG 尺寸 10/20/30 以 (2,3,7) 为中心 ⇒ CSG X∈[−3,7] Y∈[−7,13] Z∈[−8,22]；
            // CsgMesh 做 Y↔Z 交换 ⇒ 网格 X∈[−3,7] Y∈[−8,22] Z∈[−7,13]）：
            //   主视 Right=−meshY ⇒ x∈[−22,8]；Up=+meshZ ⇒ y∈[−7,13]
            //   俯视 Right=−meshY ⇒ x∈[−22,8]；Up=+meshX ⇒ y∈[−3,7]
            //   左视 Right=−meshX ⇒ x∈[−7,3]； Up=+meshZ ⇒ y∈[−7,13]
            AssertScreenRange(front, "主视", -22, 8, -7, 13);
            AssertScreenRange(top, "俯视", -22, 8, -3, 7);
            AssertScreenRange(left, "左视", -7, 3, -7, 13);
        }

        static void AssertScreenRange(ViewProjection view, string name,
            double minX, double maxX, double minY, double maxY)
        {
            Assert.That(view.Direction.Name, Is.EqualTo(name));
            Assert.That(view.Bounds.MinX, Is.EqualTo(minX).Within(Tol), $"{name} 屏幕 X 下限（镜像取轴会把它翻到另一侧）");
            Assert.That(view.Bounds.MaxX, Is.EqualTo(maxX).Within(Tol), $"{name} 屏幕 X 上限");
            Assert.That(view.Bounds.MinY, Is.EqualTo(minY).Within(Tol), $"{name} 屏幕 Y 下限");
            Assert.That(view.Bounds.MaxY, Is.EqualTo(maxY).Within(Tol), $"{name} 屏幕 Y 上限");
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
                // VisibleCount 的权威定义来自线段自身的 IsVisible 标记；
                // VisibleCount + HiddenCount == Segments.Count 是恒等式（HiddenCount 就是这么定义的），断言它等于没断言。
                Assert.That(view.VisibleCount, Is.EqualTo(view.Segments.Count(segment => segment.IsVisible)),
                    $"{view.Direction.Name} 可见计数与逐段可见标记不一致");
            }
        }

        [Test]
        public void OrthographicThree_SatisfyAntiMirrorInvariant()
        {
            // ViewProjectorContracts.cs 把 Right × Up == −Look 记为「防镜像」的关键不变量
            // （(Right, Up, −Look) 构成右手正交基 ⇒ 投影行列式为 +1）。
            // 三个内置方向的取轴反了就会画出镜像图，但不影响任何宽度/高度断言，故单独钉住。
            foreach (var direction in ViewDirections.OrthographicThree)
            {
                var cross = ViewVector.Cross(direction.Right, direction.Up);

                Assert.That(cross.X, Is.EqualTo(-direction.Look.X).Within(1e-12), $"{direction.Name}: Right × Up 应为 −Look");
                Assert.That(cross.Y, Is.EqualTo(-direction.Look.Y).Within(1e-12), $"{direction.Name}: Right × Up 应为 −Look");
                Assert.That(cross.Z, Is.EqualTo(-direction.Look.Z).Within(1e-12), $"{direction.Name}: Right × Up 应为 −Look");
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

            // 追加（Plan C / spec §13）：默认路径必须**逐项透传** ViewDirections.OrthographicThree 的同一实例
            // —— ViewProjector.Project 的 `directions ?? ViewDirections.OrthographicThree` 是引用直接落进
            // ViewProjection.Direction；按值重建方向对象时 Name 相同而实例不同，旧断言看不出来。
            for (int i = 0; i < byDefault.Count; i++)
                Assert.That(ReferenceEquals(byDefault[i].Direction, ViewDirections.OrthographicThree[i]), Is.True,
                    $"{i}: 默认三视图必须逐项透传 ViewDirections.OrthographicThree 的同一实例");
        }

        [Test]
        public void CustomDirections_ArePassedThroughVerbatim()
        {
            // 自定义方向列表同样原样透传：Project 不复制 ViewDirection（左视 ≈ 值等价、身份全等）。
            var cube = Solids.Cube(10, center: true);
            var views = ViewBridge.ProjectViews(cube, new[] { ViewDirections.Left });

            Assert.That(views.Count, Is.EqualTo(1), "单方向列表应恰产出 1 个视图");
            Assert.That(ReferenceEquals(views[0].Direction, ViewDirections.Left), Is.True,
                "自定义方向实例必须原样透传（不得按值重建）");
            Assert.That(views[0].Direction.Name, Is.EqualTo("左视"));
        }

        [Test]
        public void ToViewTriangles_TriangleCountMatchesCsgMesh()
        {
            // 原始意图（Plan A）：三角化链的三角形数一致。**粒度已升级**（Plan C / spec §13）：
            // 从「仅比数量」升级为**逐三角形内容比对** —— 每条三角的 A/B/C 顶点坐标与法向逐位相等。
            // 依据：ViewBridge.ToViewPoint 是纯拷贝（new ViewPoint(p.X, p.Y, p.Z)，无变换），
            // 顶点实测逐位一致，故用精确相等；法向由两侧**独立实现**（ViewTriangle.Normal 绕序重算
            // vs StlWriter.NormalOf）给出，立方体全轴对齐下逐位一致（一般浮点下范式化路径可差 1 ULP，
            // 此夹具不触及；若未来夹具含斜三角，需降级为 Within 并注明量级）。
            var cube = Solids.Cube(10, center: true);
            var viewTriangles = ViewBridge.ToViewTriangles(cube);
            var meshTriangles = CsgMesh.TrianglesOf(cube);

            Assert.That(viewTriangles.Count, Is.EqualTo(meshTriangles.Count)); // 数量断言保留
            for (int i = 0; i < meshTriangles.Count; i++)
            {
                AssertSamePoint(viewTriangles[i].A, meshTriangles[i].A, $"[{i}].A");
                AssertSamePoint(viewTriangles[i].B, meshTriangles[i].B, $"[{i}].B");
                AssertSamePoint(viewTriangles[i].C, meshTriangles[i].C, $"[{i}].C");

                var normal = viewTriangles[i].Normal;
                var expected = StlWriter.NormalOf(meshTriangles[i]);
                Assert.That(normal.X, Is.EqualTo(expected.X), $"[{i}].Normal.X");
                Assert.That(normal.Y, Is.EqualTo(expected.Y), $"[{i}].Normal.Y");
                Assert.That(normal.Z, Is.EqualTo(expected.Z), $"[{i}].Normal.Z");
            }
        }

        static void AssertSamePoint(ViewPoint actual, MeshPoint expected, string label)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X), $"{label}.X");
            Assert.That(actual.Y, Is.EqualTo(expected.Y), $"{label}.Y");
            Assert.That(actual.Z, Is.EqualTo(expected.Z), $"{label}.Z");
        }
    }
}
