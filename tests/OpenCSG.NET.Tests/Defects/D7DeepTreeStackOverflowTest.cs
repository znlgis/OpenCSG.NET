using System;
using System.Text.Json;
using System.Threading;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// DEFECT-007 复现（登记于 tests/DEFECTS.md 未修复表；Plan C 只报告不修）。
    /// <para>
    /// <b>写出路径没有独立深度守卫</b>：<c>CsgNodeConverter.Write</c> 对 <c>child</c> 逐层递归下钻
    /// （<c>JsonSerializer.Serialize</c> → 转换器 <c>Write</c> → 再 <c>Serialize</c>，每层新起 writer），
    /// 唯一深度防线是 System.Text.Json writer 的 64 层上限，且要递归回卷合并时才可能触发
    /// ⇒ 下钻**先于**守卫耗尽栈：深树触发 <b>StackOverflow——进程级中止，任何 try/catch 都接不住</b>。
    /// 读路径 <c>FromJson</c> 有 64 层守卫，所以「深度守卫先于栈耗尽」只对 JSON 输入向量与
    /// ≤~100 层写出向量成立（见 SerializationContractTest 类注释的限缩说明）。
    /// </para>
    /// <para>
    /// 独立进程探针（<c>artifacts/planC/soProbe</c>，gitignored，Release；一次读数 = 一个全新进程）实测：
    /// 默认栈主线程崩溃边界 ≈1020–1030 层（n≤1010 稳定可捕获 JsonException 3/3、n≥1030 稳定 SO 3/3；
    /// 1020–1029 因 JIT/OSR 抖动不定——n=1028 复跑得 1×JSONEX + 3×SO）；阈值随栈容量近线性放大
    /// （每层 ≈1.5 KB：1 MB 线程边界 ≈660–690、4 MB ≈2850、8 MB ≈5600–5750）。
    /// </para>
    /// <para>
    /// <b>2000 级崩溃 repro（需独立进程验证，测试宿主内不可捕获）</b>：主线程
    /// <c>ToJson(DeepTree(2000))</c> ⇒ 进程中止（exit 127、stderr「Stack overflow.」，无异常可捕）；
    /// 同一棵树在 8 MB 栈线程上 3/3 稳定得到可捕获的 JsonException（8 MB 边界 ≈5600–5750
    /// ⇒ 留 ~2.8× 余量）。本用例断言的就是后者——与崩溃 repro **同深**的最深可捕获一侧；
    /// 异常链与 n=63 相同：<c>JsonException[The object or value could not be serialized. Path: $.Child.]
    /// ← InvalidOperationException[CurrentDepth (64) is equal to or larger than the maximum allowed
    /// depth of 64. Cannot write the next JSON object or array.]</c>。
    /// </para>
    /// </summary>
    [TestFixture]
    public class D7DeepTreeStackOverflowTest
    {
        /// <summary>与崩溃 repro 相同的树深（默认栈主线程下实测进程中止，见类注释）。</summary>
        const int CrashReproDepth = 2000;

        /// <summary>
        /// 断言线程显式栈容量：8 MB ⇒ 实测边界 ≈5600–5750 层，2000 层留 ~2.8× 余量，
        /// 跨机器 JIT/OSR 抖动下安全；同时与测试宿主默认栈解耦（宿主线程默认 ≈1.5 MB，边界 ≈1020）。
        /// </summary>
        const int AssertionStackBytes = 8 * 1024 * 1024;

        [Test]
        [Explicit("DEFECT-007: 深树 ToJson 无写出侧深度守卫——默认栈 ≳1020 层进程中止（StackOverflow 不可捕获）；本用例在显式 8 MB 栈线程上断言 2000 层树的最深可捕获侧")]
        public void ToJson_DeepTree_WithAdequateStack_ThrowsCatchableJsonException()
        {
            Exception caught = null;
            var thread = new Thread(() =>
            {
                try
                {
                    _ = CsgSerialization.ToJson(DeepTree(CrashReproDepth));
                }
                catch (Exception ex)
                {
                    caught = ex;
                }
            }, AssertionStackBytes);

            thread.Start();
            thread.Join();

            Assert.That(caught, Is.Not.Null,
                "8 MB 栈下 2000 层树应完成下钻并抛守卫异常；为 null 说明行为已漂移（预期仍抛 JsonException）");
            Assert.That(caught, Is.TypeOf<JsonException>(),
                "应为可捕获的 JsonException（64 层 writer 深度上限），而非进程中止或其他异常类型");
            Assert.That(caught.Message, Does.Contain("serialized").Or.Contain("depth"),
                "外层消息应点明序列化失败（与 SerializationContractTest 的既有断言同口径）");

            var inner = caught.InnerException;
            Assert.That(inner, Is.Not.Null, "异常链应携带内层守卫信息（DEFECT-007 归因依据）");
            Assert.That(inner.Message, Does.Contain("64"),
                "内层消息应点明 64 层深度上限（CurrentDepth (64) … maximum allowed depth of 64）");
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
