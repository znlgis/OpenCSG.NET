using System;
using System.Text.Json;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// DEFECT-007 回归（2026-10-14 修复；登记于 tests/DEFECTS.md 已修复表）。
    /// <para>
    /// 原缺陷：写出路径（<c>CsgNodeConverter.Write</c>）逐层递归下钻且**无独立深度守卫**——
    /// 唯一防线是 System.Text.Json writer 的嵌套上限，要到递归回卷合并时才可能触发，
    /// 于是默认栈下 ≳1020 层树的 <c>ToJson</c> ⇒ StackOverflow（进程级中止、不可捕获）。
    /// </para>
    /// <para>
    /// 修复：<c>CsgNodeConverter</c> 加 [ThreadStatic] 写出深度计数，**下钻前**检查；
    /// 阈值 63 与「修复前可成功写出的最深树」精确对齐（62 层 Transform + Box = 63 节点，
    /// JSON 文本深 64；63 层起修复前本就抛 JsonException）⇒ 所有既有可写出树行为不变，
    /// 更深树以可捕获的 JsonException 收敛。读路径 <c>FromJson</c> 的 64 层守卫保持不变。
    /// </para>
    /// </summary>
    [TestFixture]
    public class D7DeepTreeStackOverflowTest
    {
        /// <summary>
        /// 修复前的进程中止 repro：默认栈主线程 <c>ToJson(DeepTree(2000))</c> 实测 StackOverflow
        /// （exit 127、stderr「Stack overflow.」）。修复后同一张树在**默认栈**上即以可捕获的
        /// JsonException 收敛——本用例直接在当前线程断言这一点。
        /// </summary>
        [Test]
        public void ToJson_DeepTree_ThrowsCatchableJsonException_NotStackOverflow()
        {
            var ex = Assert.Throws<JsonException>(() => CsgSerialization.ToJson(DeepTree(2000)),
                "2000 层树应抛可捕获的 JsonException；进程中止（StackOverflow）即修复回退");
            Assert.That(ex!.Message, Does.Contain("depth"),
                "守卫消息应点明写出侧深度上限（write depth guard）");
        }

        /// <summary>
        /// 边界与修复前对齐：63 节点链（62 层 Transform + Box，JSON 文本深 64）仍可写出并往返；
        /// 64 节点（63 层 Transform + Box）抛 JsonException。
        /// </summary>
        [Test]
        public void ToJson_AtNodeDepthBoundary_63Writes_64Throws()
        {
            var deepest = CsgSerialization.ToJson(DeepTree(62), indented: false);
            var restored = CsgSerialization.FromJson(deepest);
            var node = restored;
            for (var i = 0; i < 62; i++)
            {
                Assert.That(node, Is.TypeOf<TransformNode>(), $"第 {i} 层应为 Transform");
                node = ((TransformNode)node).Child;
            }
            Assert.That(node, Is.TypeOf<BoxNode>(), "最内层应为 Box（最深可写出形态往返无损）");

            Assert.Throws<JsonException>(() => CsgSerialization.ToJson(DeepTree(63), indented: false),
                "64 节点链应抛 JsonException（修复前为 writer 64 层上限、修复后为写出侧守卫）");
        }

        /// <summary>
        /// 守卫失败后深度计数必须复位（[ThreadStatic] 计数在 finally 中递减）：
        /// 同一线程上先做一次深树失败，再做正常写出——若计数泄漏，第二次会误报。
        /// 同时断言线程隔离：另一线程的深树失败不影响本线程的正常写出。
        /// </summary>
        [Test]
        public void WriteDepthCounter_ResetsAfterFailure_AndIsThreadLocal()
        {
            Assert.Throws<JsonException>(() => CsgSerialization.ToJson(DeepTree(2000), indented: false));

            // 同一线程：计数已复位。
            var ok = CsgSerialization.ToJson(DeepTree(62), indented: false);
            Assert.That(ok, Is.Not.Null.And.Not.Empty, "深树失败后同线程的正常写出应不受影响（计数已复位）");

            // 另一线程：并行的深树失败不影响本线程。
            Exception otherThreadError = null!;
            var other = new System.Threading.Thread(() =>
            {
                try { CsgSerialization.ToJson(DeepTree(2000), indented: false); }
                catch (Exception caught) { otherThreadError = caught; }
            });
            other.Start();
            other.Join();

            Assert.That(otherThreadError, Is.TypeOf<JsonException>(),
                "另一线程的深树也应抛可捕获 JsonException（计数按线程隔离）");
            Assert.DoesNotThrow(() => CsgSerialization.ToJson(DeepTree(62), indented: false),
                "另一线程的深树失败不得污染本线程的写出深度计数");
        }

        static CsgNode DeepTree(int depth)
        {
            CsgNode node = new BoxNode(new Vector3D(0, 0, 0), new Vector3D(1, 1, 1));
            for (var i = 0; i < depth; i++)
                node = new TransformNode(new Vector3D(0, 0, 0), new Vector3D(0, 0, 0), node);
            return node;
        }
    }
}
