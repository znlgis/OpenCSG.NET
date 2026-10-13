using System;
using System.Text.Json;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// CsgSerialization 的 L2 契约（Plan C / Task 7 Step 2 的「超深 JSON」项）：
    /// null 字面量拒绝、未知节点/截面类型的写出拒绝、超递归深度的 JsonException 守卫，
    /// 以及中等深度的往返对照。
    /// <para>
    /// 超深输入的实测结论（探针，一次性）：默认序列化深度上限 64 —— FromJson 深度 100 抛
    /// JsonException「The maximum configured depth of 64 has been exceeded」；ToJson 在深度 63
    /// （62 层 Transform 包 Box）即抛 JsonException、深度 62 仍可往返。**对本夹具的 JSON 输入
    /// 向量与 100 层写出向量，深度守卫先于栈耗尽生效**（实测可捕获、可断言）；该结论**不可外推**到
    /// 程序构造的更深树写出：写出路径（<c>CsgNodeConverter.Write</c>）逐层递归、没有独立深度守卫，
    /// 深树（默认栈 ≳1020 层）先栈耗尽 ⇒ 不可捕获的 StackOverflow（进程中止）。
    /// 该单向守卫残留风险已登记为 DEFECT-007（tests/DEFECTS.md；复现与探针读数见
    /// <c>Defects/D7DeepTreeStackOverflowTest.cs</c>）。
    /// </para>
    /// </summary>
    [TestFixture]
    public class SerializationContractTest
    {
        [Test]
        public void FromJson_NullLiteral_ThrowsJsonException()
        {
            // 读实现：Deserialize 返回 null ⇒ JsonException("Deserialization returned null")。
            var ex = Assert.Throws<JsonException>(() => CsgSerialization.FromJson("null"));
            Assert.That(ex.Message, Does.Contain("null"));
        }

        [Test]
        public void FromJsonArray_NullLiteral_ThrowsJsonException()
        {
            var ex = Assert.Throws<JsonException>(() => CsgSerialization.FromJsonArray("null"));
            Assert.That(ex.Message, Does.Contain("null"));
        }

        [Test]
        public void ToJson_UnknownNodeOrProfileType_ThrowsJsonException()
        {
            // 写出侧的 default 分支同样是明确契约（读实现 CsgNodeConverter.Write / Profile2DConverter.Write）。
            var nodeEx = Assert.Throws<JsonException>(() => CsgSerialization.ToJson(new UnknownNode()));
            Assert.That(nodeEx.Message, Does.Contain("Unknown CsgNode type").And.Contain(nameof(UnknownNode)));

            var profileEx = Assert.Throws<JsonException>(
                () => CsgSerialization.ToJson(new ExtrudeNode(new UnknownProfile(), 1)));
            Assert.That(profileEx.Message, Does.Contain("Unknown Profile2D type").And.Contain(nameof(UnknownProfile)));
        }

        [Test]
        public void FromJson_ExceedingMaxDepth_ThrowsJsonException_NotStackOverflow()
        {
            // 100 层嵌套远超默认深度上限 64 ⇒ 解析期即拒绝（可捕获的 JsonException，非 StackOverflow）。
            var json = NestJson(100);
            var ex = Assert.Throws<JsonException>(() => CsgSerialization.FromJson(json));
            Assert.That(ex.Message, Does.Contain("depth"), "守卫消息应点明配置深度上限");
        }

        [Test]
        public void ToJson_ExceedingMaxDepth_ThrowsJsonException_NotStackOverflow()
        {
            var deep = NestTree(100);
            var ex = Assert.Throws<JsonException>(() => CsgSerialization.ToJson(deep));
            Assert.That(ex.Message, Does.Contain("serialized").Or.Contain("depth"));
        }

        [Test]
        public void DeepTree_AtModerateDepth_RoundTrips()
        {
            // 浅层对照：30 层往返（深度上限 64 以内，正常序列化链路不受影响）。
            const int depth = 30;
            var restored = CsgSerialization.FromJson(CsgSerialization.ToJson(NestTree(depth)));

            var node = restored;
            for (var i = 0; i < depth; i++)
            {
                Assert.That(node, Is.TypeOf<TransformNode>(), $"第 {i} 层应为 Transform");
                node = ((TransformNode)node).Child;
            }
            Assert.That(node, Is.TypeOf<BoxNode>(), "最内层应为 Box");
        }

        static CsgNode NestTree(int depth)
        {
            CsgNode node = new BoxNode(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1));
            for (var i = 0; i < depth; i++)
                node = new TransformNode(new Vector3D(0, 0, 0), new Vector3D(0, 0, 0), node);
            return node;
        }

        static string NestJson(int depth)
        {
            var json = "{\"$type\":\"Box\"}";
            for (var i = 0; i < depth; i++)
                json = "{\"$type\":\"Transform\",\"translation\":{\"x\":0,\"y\":0,\"z\":0},"
                     + "\"rotation\":{\"x\":0,\"y\":0,\"z\":0},\"child\":" + json + "}";
            return json;
        }

        sealed record UnknownNode : CsgNode;
        sealed record UnknownProfile : Profile2D;
    }
}
