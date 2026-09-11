using Csg;
using NUnit.Framework;
using System.Text.Json;

namespace Csg.Test
{
    [TestFixture]
    public class CsgSerializationTest
    {
        [Test]
        public void BoxNode_RoundTrip()
        {
            var node = new BoxNode(new Vector3D(1, 2, 3), new Vector3D(4, 5, 6));
            var json = CsgSerialization.ToJson(node);
            var restored = CsgSerialization.FromJson(json);

            Assert.That(restored, Is.TypeOf<BoxNode>());
            var box = (BoxNode)restored;
            Assert.That(box.Center.X, Is.EqualTo(1));
            Assert.That(box.Center.Y, Is.EqualTo(2));
            Assert.That(box.Center.Z, Is.EqualTo(3));
            Assert.That(box.Size.X, Is.EqualTo(4));
            Assert.That(box.Size.Y, Is.EqualTo(5));
            Assert.That(box.Size.Z, Is.EqualTo(6));
        }

        [Test]
        public void SphereNode_RoundTrip()
        {
            var node = new SphereNode(new Vector3D(0, 0, 0), 2.5);
            var json = CsgSerialization.ToJson(node);
            var restored = CsgSerialization.FromJson(json);

            Assert.That(restored, Is.TypeOf<SphereNode>());
            var s = (SphereNode)restored;
            Assert.That(s.Radius, Is.EqualTo(2.5));
        }

        [Test]
        public void NestedBoolean_RoundTrip()
        {
            var inner = CsgNodes.Union(
                new SphereNode(new Vector3D(2, 2, 2), 2),
                new CylinderNode(new Vector3D(5, 5, 0), 1, 8));
            var node = CsgNodes.Subtract(
                new BoxNode(new Vector3D(0, 0, 0), new Vector3D(10, 10, 10)),
                inner);

            var json = CsgSerialization.ToJson(node);
            var restored = CsgSerialization.FromJson(json);

            Assert.That(restored, Is.TypeOf<SubtractNode>());
            var sub = (SubtractNode)restored;
            Assert.That(sub.Children, Has.Count.EqualTo(2));
            Assert.That(sub.Children[0], Is.TypeOf<BoxNode>());
        }

        [Test]
        public void ExtrudeNode_WithProfile_RoundTrip()
        {
            var node = new ExtrudeNode(Profiles.HBeam(100, 80, 10, 12), 50);
            var json = CsgSerialization.ToJson(node);
            var restored = CsgSerialization.FromJson(json);

            Assert.That(restored, Is.TypeOf<ExtrudeNode>());
            var ext = (ExtrudeNode)restored;
            Assert.That(ext.Profile, Is.TypeOf<HBeamProfile>());
            Assert.That(ext.Height, Is.EqualTo(50));
        }

        [Test]
        public void JsonContainsTypeDiscriminator()
        {
            var node = new BoxNode(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1));
            var json = CsgSerialization.ToJson(node);

            Assert.That(json, Does.Contain("\"$type\""));
            Assert.That(json, Does.Contain("Box"));
        }

        [Test]
        public void UnknownType_ThrowsJsonException()
        {
            var json = "{\"$type\":\"UnknownType\"}";
            Assert.Throws<JsonException>(() => CsgSerialization.FromJson(json));
        }

        [Test]
        public void ToJsonArray_RoundTrip()
        {
            var nodes = new CsgNode[]
            {
                new BoxNode(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1)),
                new SphereNode(new Vector3D(0, 0, 0), 2),
            };
            var json = CsgSerialization.ToJson(nodes);
            var restored = CsgSerialization.FromJsonArray(json);

            Assert.That(restored.Count, Is.EqualTo(2));
            Assert.That(restored[0], Is.TypeOf<BoxNode>());
            Assert.That(restored[1], Is.TypeOf<SphereNode>());
        }

        [Test]
        public void MissingTypeDiscriminator_ThrowsJsonException()
        {
            Assert.Throws<JsonException>(() => CsgSerialization.FromJson("{\"center\":{\"x\":0,\"y\":0,\"z\":0}}"));
        }

        [Test]
        public void WrongTypedVectorMember_ThrowsJsonException()
        {
            // 数值型分量的位置给了标量：转换器必须立即报错，而不是把读取位置带偏
            var json = "{\"$type\":\"Box\",\"center\":5,\"size\":{\"x\":1,\"y\":1,\"z\":1}}";
            Assert.Throws<JsonException>(() => CsgSerialization.FromJson(json));
        }

        [Test]
        public void NonNumericVectorComponent_ThrowsJsonException()
        {
            var json = "{\"$type\":\"Box\",\"center\":{\"x\":\"a\",\"y\":1,\"z\":1},\"size\":{\"x\":1,\"y\":1,\"z\":1}}";
            Assert.Throws<JsonException>(() => CsgSerialization.FromJson(json));
        }

        [Test]
        public void VectorComponents_AreReadCaseInsensitively()
        {
            // 手写 JSON 常见 PascalCase 分量：不得被静默读成 (0,0,0)
            var json = "{\"$type\":\"Box\",\"center\":{\"X\":1,\"Y\":2,\"Z\":3},\"size\":{\"X\":4,\"Y\":5,\"Z\":6}}";
            var box = (BoxNode)CsgSerialization.FromJson(json);
            Assert.That(box.Center.X, Is.EqualTo(1));
            Assert.That(box.Center.Y, Is.EqualTo(2));
            Assert.That(box.Center.Z, Is.EqualTo(3));
            Assert.That(box.Size.X, Is.EqualTo(4));
            Assert.That(box.Size.Y, Is.EqualTo(5));
            Assert.That(box.Size.Z, Is.EqualTo(6));
        }

        [Test]
        public void NullProfile_EvaluationThrowsEvaluationException()
        {
            var node = CsgSerialization.FromJson("{\"$type\":\"Extrude\",\"profile\":null,\"height\":1}");
            Assert.Throws<CsgEvaluationException>(() => CsgEvaluator.Evaluate(node));
        }

        [Test]
        public void MinimalJson_EvaluatesToEmptySolid()
        {
            // 缺省尺寸 = 0 → Solids.Cube 返回空实体（不抛异常）
            var node = CsgSerialization.FromJson("{\"$type\":\"Box\"}");
            var solid = CsgEvaluator.Evaluate(node);
            Assert.That(solid.Polygons.Count, Is.EqualTo(0));
        }

        [Test]
        public void Idempotent_DoubleSerialize()
        {
            var node = new ExtrudeNode(Profiles.Capsule(3, 1), 5);
            var json1 = CsgSerialization.ToJson(node);
            var restored = CsgSerialization.FromJson(json1);
            var json2 = CsgSerialization.ToJson(restored);
            Assert.That(json1, Is.EqualTo(json2));
        }
    }
}
