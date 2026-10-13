using System;
using System.Collections.Generic;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// CsgEvaluator 的 L2 契约（Plan C / Task 7 Step 1）：逐条读实现确认的 throw 路径。
    /// 覆盖：Cylinder/Wedge/Extrude 参数校验、布尔空子节点、未知节点/截面类型、
    /// 三角化失败、EvaluateAll 空元素、CsgEvaluationException(message, inner) 构造。
    /// （Cone 的三条 throw 已由 ConeTest/B2 覆盖；Evaluate(null) 已由 CsgEvaluatorTest 覆盖。）
    /// </summary>
    [TestFixture]
    public class EvaluatorContractTest
    {
        [Test]
        public void Cylinder_NonPositiveHeightOrRadius_ThrowCsgEvaluationException()
        {
            var height = Assert.Throws<CsgEvaluationException>(
                () => CsgEvaluator.Evaluate(new CylinderNode(new Vector3D(0, 0, 0), Radius: 1, Height: 0)));
            Assert.That(height.Message, Does.Contain("height").IgnoreCase);

            var radius = Assert.Throws<CsgEvaluationException>(
                () => CsgEvaluator.Evaluate(new CylinderNode(new Vector3D(0, 0, 0), Radius: 0, Height: 1)));
            Assert.That(radius.Message, Does.Contain("radius").IgnoreCase);
        }

        [Test]
        public void Wedge_NonPositiveDimension_ThrowCsgEvaluationException()
        {
            var ex = Assert.Throws<CsgEvaluationException>(
                () => CsgEvaluator.Evaluate(new WedgeNode(new Vector3D(0, 0, 0), Width: 0, Depth: 1, Height: 1)));
            Assert.That(ex.Message, Does.Contain("Wedge dimensions"));
        }

        [Test]
        public void Extrude_NonPositiveHeight_ThrowCsgEvaluationException()
        {
            var ex = Assert.Throws<CsgEvaluationException>(
                () => CsgEvaluator.Evaluate(new ExtrudeNode(Profiles.Rectangle(1, 1), Height: 0)));
            Assert.That(ex.Message, Does.Contain("Extrude height"));
        }

        [Test]
        public void Extrude_DegenerateProfile_ThrowsOnBothPaths()
        {
            // 路径①：顶点足够（4 个）但全部重合 ⇒ ear-clipping 找不到耳朵 ⇒ 专用失败消息。
            var failed = Assert.Throws<CsgEvaluationException>(() => CsgEvaluator.Evaluate(
                new ExtrudeNode(new PolygonProfile(new[]
                {
                    new Vector2D(0, 0), new Vector2D(0, 0), new Vector2D(0, 0), new Vector2D(0, 0),
                }), 1)));
            Assert.That(failed.Message, Does.Contain("Triangulation failed"));

            // 路径②：顶点不足 3 ⇒ 三角化不产出任何三角形 ⇒ 另一个分支的专用消息。
            var none = Assert.Throws<CsgEvaluationException>(() => CsgEvaluator.Evaluate(
                new ExtrudeNode(new PolygonProfile(new[] { new Vector2D(0, 0), new Vector2D(1, 1) }), 1)));
            Assert.That(none.Message, Does.Contain("no triangles"));
        }

        [Test]
        public void Boolean_WithoutChildren_ThrowsCsgEvaluationException()
        {
            var nullChildren = Assert.Throws<CsgEvaluationException>(
                () => CsgEvaluator.Evaluate(new UnionNode(null)));
            Assert.That(nullChildren.Message, Does.Contain("at least one child"));

            var emptyChildren = Assert.Throws<CsgEvaluationException>(
                () => CsgEvaluator.Evaluate(new SubtractNode(new List<CsgNode>())));
            Assert.That(emptyChildren.Message, Does.Contain("at least one child"));
        }

        [Test]
        public void UnknownNodeOrProfileType_ThrowsCsgEvaluationException()
        {
            var nodeEx = Assert.Throws<CsgEvaluationException>(
                () => CsgEvaluator.Evaluate(new UnknownNode()));
            Assert.That(nodeEx.Message, Does.Contain("Unknown node type").And.Contain(nameof(UnknownNode)));

            var profileEx = Assert.Throws<CsgEvaluationException>(
                () => CsgEvaluator.Evaluate(new ExtrudeNode(new UnknownProfile(), 1)));
            Assert.That(profileEx.Message, Does.Contain("Unknown Profile2D type").And.Contain(nameof(UnknownProfile)));
        }

        [Test]
        public void EvaluateAll_NullElement_ThrowsCsgEvaluationException()
        {
            var ex = Assert.Throws<CsgEvaluationException>(
                () => CsgEvaluator.EvaluateAll(new CsgNode[] { null }));
            Assert.That(ex.Message, Does.Contain("must not be null"));
        }

        [Test]
        public void CsgEvaluationException_InnerCtor_PreservesMessageAndInner()
        {
            var inner = new InvalidOperationException("boom");
            var ex = new CsgEvaluationException("求值失败", inner);

            Assert.That(ex.Message, Is.EqualTo("求值失败"));
            Assert.That(ex.InnerException, Is.SameAs(inner));
        }

        sealed record UnknownNode : CsgNode;
        sealed record UnknownProfile : Profile2D;
    }
}
