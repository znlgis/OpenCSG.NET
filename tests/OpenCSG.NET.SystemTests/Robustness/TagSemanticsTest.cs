using System;
using System.Collections.Generic;
using System.Threading;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// L7 健壮性：`Solid.GetTag()` 与实体上的 <c>Tag</c> 继承属性（<see cref="Vertex.Tag"/>、
    /// <see cref="Plane.Tag"/>、<see cref="PolygonShared.Tag"/>）的读写语义与并发唯一性
    /// （Plan E / Task 2.4）。
    /// <para>
    /// 审计结论（2026-10-14，grep 证据见 Task 2 报告）：仓库内**没有任何**既有用例依赖
    /// tag 的具体取值——tag 只被 <c>src</c> 自身用于身份判定（<c>VertexFactory</c>/
    /// <c>PlaneFactory</c> 查重、<c>PolygonsPerPlaneKey</c> 聚合）；测试侧只在生成器/断言里
    /// 读取几何读数。本文件把该契约钉成可执行规格：
    /// ① <see cref="Solid.GetTag"/> 是进程级严格单调、全局唯一的 id 生成器（<c>Interlocked</c>；
    /// 并行下不断言「+1」——其他 worker 也在取号）；
    /// ② 实体 <c>Tag</c> 惰性分配、同对象稳定、异对象唯一，且 0 哨兵不可观测（首次读必 ≥1）；
    /// ③ 默认语义：<see cref="Polygon.Shared"/> 缺省时落在**同一只**默认 <c>PolygonShared</c> 上；
    /// ④ 并发读（并行整套件的主要风险面）下仍两两不同。
    /// </para>
    /// </summary>
    [TestFixture]
    public class TagSemanticsTest
    {
        [Test]
        public void GetTag_IsStrictlyIncreasingAndNeverZero()
        {
            var previous = Solid.GetTag();
            Assert.That(previous, Is.GreaterThan(0), "首个可观测 tag 不能是 0 哨兵");

            var seen = new HashSet<int> { previous };
            for (var i = 0; i < 1000; i++)
            {
                var current = Solid.GetTag();
                // 并行整套件下其他 worker 也在取号（布尔求值会给顶点/平面惰性取号）⇒ 只能断言
                // 跨线程安全的「严格单调 + 全局唯一」；「同线程 +1」只在隔离进程成立，本套件不得依赖。
                Assert.That(current, Is.GreaterThan(previous),
                    $"GetTag 必须严格单调（Interlocked.Increment）：{previous} → {current}");
                Assert.That(seen.Add(current), Is.True, $"GetTag 必须全局唯一：{current} 重复");
                previous = current;
            }
        }

        [Test]
        public void VertexTag_IsLazyStable_AndUniquePerInstance()
        {
            var a = new Vertex(new Vector3D(1, 2, 3), new Vector2D(0, 0));
            var b = new Vertex(new Vector3D(1, 2, 3), new Vector2D(0, 0));   // 同位置同贴图，身份仍应不同

            var a1 = a.Tag;
            var a2 = a.Tag;
            var b1 = b.Tag;

            Assert.That(a1, Is.GreaterThan(0), "首次读应惰性分配（不返回 0 哨兵）");
            Assert.That(a2, Is.EqualTo(a1), "同一对象的 Tag 必须稳定（缓存后不再变）");
            Assert.That(b1, Is.Not.EqualTo(a1), "不同顶点的 Tag 必须两两不同");
            Assert.That(b1, Is.GreaterThan(0));
            Assert.That(typeof(Vertex).GetProperty(nameof(Vertex.Tag))!.CanWrite, Is.False,
                "Tag 是只读契约（「写」只发生在内部惰性赋值）");

            // 变换/翻转产物是新对象 ⇒ 新身份（tag 不随几何流动）。
            Assert.That(a.Flipped().Tag, Is.EqualTo(a1), "Flipped 即返自身 ⇒ 身份不变");
            Assert.That(a.Transform(new Matrix4x4()).Tag, Is.Not.EqualTo(a1), "Transform（单位阵）产生新对象 ⇒ 新身份");
        }

        [Test]
        public void PlaneTag_IsLazyStable_AndFlippedIsANewIdentity()
        {
            var plane = new Plane(new Vector3D(0, 0, 1), 0);
            var t1 = plane.Tag;
            Assert.That(t1, Is.GreaterThan(0));
            Assert.That(plane.Tag, Is.EqualTo(t1), "同一平面的 Tag 必须稳定");

            var flipped = plane.Flipped();
            Assert.That(flipped.Tag, Is.Not.EqualTo(t1), "Flipped 是新对象 ⇒ 新身份（tag 不是几何的、是对象的）");

            // 几何等价的另一个平面对象也应是新身份（tag 与 Equals 无关）。
            var same = new Plane(new Vector3D(0, 0, 1), 0);
            Assert.That(same.Equals(plane), Is.True, "前提：两个平面几何等价");
            Assert.That(same.Tag, Is.Not.EqualTo(t1), "Equals 相等不共享 tag");
        }

        [Test]
        public void PolygonShared_DefaultsToASharedDefaultInstance()
        {
            var v1 = new List<Vertex>
            {
                new Vertex(new Vector3D(0, 0, 0), new Vector2D(0, 0)),
                new Vertex(new Vector3D(1, 0, 0), new Vector2D(0, 0)),
                new Vertex(new Vector3D(0, 1, 0), new Vector2D(0, 0)),
                new Vertex(new Vector3D(0, 0, 1), new Vector2D(0, 0)),
            };

            var p1 = new Polygon(v1);                       // shared 缺省（ctor 默认 null）
            var p2 = new Polygon(new List<Vertex>(v1));     // 另一实体，同样缺省

            Assert.That(p1.Shared, Is.Not.Null, "缺省 Shared 应是默认实例而不是 null");
            Assert.That(p2.Shared, Is.SameAs(p1.Shared),
                "缺省 Shared 应落在同一只默认 PolygonShared 上（跨实体共享）");

            var tag = p1.Shared.Tag;
            Assert.That(tag, Is.GreaterThan(0), "默认 shared 的 Tag 同样惰性分配且非 0");
            Assert.That(p2.Shared.Tag, Is.EqualTo(tag), "同一默认 shared 实例 ⇒ 同一 tag");
            Assert.That(typeof(PolygonShared).GetProperty(nameof(PolygonShared.Tag))!.CanWrite, Is.False,
                "PolygonShared.Tag 同为只读契约");

            var explicitShared = new PolygonShared(null);
            Assert.That(explicitShared.Tag, Is.Not.EqualTo(tag), "显式新 shared 是新身份");
            var p3 = new Polygon(new List<Vertex>(v1), explicitShared);
            Assert.That(p3.Shared, Is.SameAs(explicitShared), "显式传入的 shared 应原样持有");
        }

        [Test]
        public void TagsStayUnique_UnderConcurrentReads()
        {
            const int threads = 8;
            const int perThread = 250;

            var results = new int[threads][];
            var errors = new List<Exception>();

            var workers = new Thread[threads];
            for (var t = 0; t < threads; t++)
            {
                var index = t;
                workers[t] = new Thread(() =>
                {
                    try
                    {
                        var tags = new int[perThread];
                        for (var i = 0; i < perThread; i++)
                        {
                            // 混合两类来源：裸 GetTag 与实体惰性分配（两者共用同一计数器）。
                            tags[i] = (i & 1) == 0
                                ? Solid.GetTag()
                                : new Vertex(new Vector3D(i, index, 0), new Vector2D(0, 0)).Tag;
                        }
                        results[index] = tags;
                    }
                    catch (Exception ex)
                    {
                        lock (errors) errors.Add(ex);
                    }
                });
                workers[t].Start();
            }

            foreach (var worker in workers)
                Assert.That(worker.Join(TimeSpan.FromSeconds(30)), Is.True, "并发线程不应超时");

            Assert.That(errors, Is.Empty, "并发读不应抛异常");

            var seen = new HashSet<int>();
            var total = 0;
            foreach (var tags in results)
            {
                Assert.That(tags, Is.Not.Null);
                foreach (var tag in tags)
                {
                    Assert.That(tag, Is.GreaterThan(0));
                    Assert.That(seen.Add(tag), Is.True, $"tag {tag} 重复 —— Interlocked 唯一性破损");
                    total++;
                }
            }
            Assert.That(total, Is.EqualTo(threads * perThread));
        }
    }
}
