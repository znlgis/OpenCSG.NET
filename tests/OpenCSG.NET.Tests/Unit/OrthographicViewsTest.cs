using System;
using System.Collections.Generic;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// OrthographicViews（Plan C / Task 8）的单元回填：ctor / Name / Label / Views /
    /// <see cref="OrthographicViews.Bounds"/>（多视图 Union 合并路径），并把
    /// <see cref="ViewBounds2.Union"/>/<see cref="ViewBounds2.Empty"/> 一并销账（Offset 于本文件）。
    /// 每件视图的包络由已知线段的端点手推，不依赖投影器。
    /// </summary>
    [TestFixture]
    public class OrthographicViewsTest
    {
        const double Tol = 1e-12;

        static ViewProjection Projection(ViewDirection direction, params ViewSegment[] segments)
            => new(direction, segments);

        [Test]
        public void OrthographicViews_Ctor_ExposesNameLabelAndViews()
        {
            var front = Projection(ViewDirections.Front,
                new ViewSegment(new ViewPoint2(0, 0), new ViewPoint2(10, 5), true));
            var top = Projection(ViewDirections.Top,
                new ViewSegment(new ViewPoint2(-3, 2), new ViewPoint2(4, 7), false));
            var views = new List<ViewProjection> { front, top };

            var piece = new OrthographicViews("某分图#9", "某分图 某零件 支撑板", views);

            Assert.That(piece.Name, Is.EqualTo("某分图#9"), "Name 透传");
            Assert.That(piece.Label, Is.EqualTo("某分图 某零件 支撑板"), "Label 透传");
            Assert.That(piece.Views.Count, Is.EqualTo(2), "Views 透传");
            Assert.That(piece.Views[0], Is.SameAs(front), "Views 顺序保持");
            Assert.That(piece.Views[1], Is.SameAs(top), "Views 顺序保持");
        }

        [Test]
        public void OrthographicViews_Bounds_IsUnionOfPerViewBounds()
        {
            // 视图甲：线段 (0,0)-(10,5) ⇒ 包络 (0,0)-(10,5)；视图乙：线段 (−3,2)-(4,7) ⇒ (−3,2)-(4,7)。
            // 并集 = X[−3,10]、Y[0,7]。
            var front = Projection(ViewDirections.Front,
                new ViewSegment(new ViewPoint2(0, 0), new ViewPoint2(10, 5), true));
            var left = Projection(ViewDirections.Left,
                new ViewSegment(new ViewPoint2(-3, 2), new ViewPoint2(4, 7), true));

            var piece = new OrthographicViews("p", "l", new List<ViewProjection> { front, left });
            var bounds = piece.Bounds;

            Assert.That(bounds.MinX, Is.EqualTo(-3.0).Within(Tol), "并集 MinX = min(0,−3)");
            Assert.That(bounds.MinY, Is.EqualTo(0.0).Within(Tol), "并集 MinY = min(0,2)");
            Assert.That(bounds.MaxX, Is.EqualTo(10.0).Within(Tol), "并集 MaxX = max(10,4)");
            Assert.That(bounds.MaxY, Is.EqualTo(7.0).Within(Tol), "并集 MaxY = max(5,7)");
            Assert.That(bounds.Width, Is.EqualTo(13.0).Within(Tol), "并集宽");
            Assert.That(bounds.Height, Is.EqualTo(7.0).Within(Tol), "并集高");
        }

        [Test]
        public void OrthographicViews_Bounds_SingleViewUsesItsOwnBounds_EmptyListIsEmpty()
        {
            var single = new OrthographicViews("p", "l", new List<ViewProjection>
            {
                Projection(ViewDirections.Front, new ViewSegment(new ViewPoint2(2, 3), new ViewPoint2(6, 9), true)),
            });
            Assert.That(single.Bounds.MinX, Is.EqualTo(2.0), "单视图直接取自身包络（首个不经 Union）");
            Assert.That(single.Bounds.MaxY, Is.EqualTo(9.0), "单视图 MaxY");

            var empty = new OrthographicViews("p", "l", new List<ViewProjection>());
            Assert.That(empty.Bounds.MinX, Is.EqualTo(0.0), "空视图表 ⇒ Empty(0,0,0,0)");
            Assert.That(empty.Bounds.MaxX, Is.EqualTo(0.0), "空视图表 ⇒ Empty(0,0,0,0)");
            Assert.That(empty.Bounds.MinY, Is.EqualTo(0.0), "空视图表 ⇒ Empty(0,0,0,0)");
            Assert.That(empty.Bounds.MaxY, Is.EqualTo(0.0), "空视图表 ⇒ Empty(0,0,0,0)");
        }

        [Test]
        public void ViewBounds2_Offset_ShiftsBothCorners()
        {
            var shifted = new ViewBounds2(1, 2, 5, 7).Offset(10, -4);

            Assert.That(shifted.MinX, Is.EqualTo(11.0), "Offset MinX");
            Assert.That(shifted.MinY, Is.EqualTo(-2.0), "Offset MinY");
            Assert.That(shifted.MaxX, Is.EqualTo(15.0), "Offset MaxX");
            Assert.That(shifted.MaxY, Is.EqualTo(3.0), "Offset MaxY");
        }
    }
}
