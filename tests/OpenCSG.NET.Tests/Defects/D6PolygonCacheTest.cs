using System;
using System.Collections.Generic;
using System.Linq;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// DEFECT-006 最小复现（登记于 tests/DEFECTS.md 未修复表；Plan C 只报告不修）。
    /// <para>
    /// <see cref="Polygon"/> 与 A1 是同一模式：<c>public readonly List&lt;Vertex&gt; Vertices</c>
    /// 是公共可变列表（<c>Vertex.Pos</c>/<c>Tex</c> 均为 readonly，原地变更几何的唯一路径就是
    /// 该列表的增删改/下标赋值），而 <c>cachedBoundingSphere</c>/<c>cachedBoundingBox</c>
    /// （Polygon.cs:19-20、45-89）是无失效机制的惰性缓存。缓存建立后原地改顶点 ⇒ 缓存陈旧；
    /// A1 修复后 <c>Solid.Bounds</c> 虽然每次现算，但逐个读的是 <c>polygon.BoundingBox</c>（陈旧值）
    /// ⇒ <c>Union</c> 里 <c>MayOverlap</c> 误判「不相交」⇒ 跳过 BSP 裁剪直接把两组多边形拼接。
    /// </para>
    /// <para>
    /// 与 B1 的 DEFECT 用例同风格：断言钉住**当前（错误）读数**，期望值写在注释与对照断言里；
    /// <c>[Explicit]</c> 使套件保持绿色、缺陷可一行复现（规则 2）。
    /// 探针（artifacts/planC/polygonProbe，gitignored）两场景在跨进程复跑下读数一致。
    /// </para>
    /// </summary>
    [TestFixture]
    public class D6PolygonCacheTest
    {
        private static Solid CubeAt(double y) => Solids.Cube(10, center: true).Translate(0, y, 0);

        /// <summary>
        /// 场景 1（A1 同款构造）：预热缓存后把 a 的每个面原地换成远处盒的对应面顶点（等价于整个实体
        /// 几何搬运，6→6 个面，缓存无从察觉，因为变更不经过任何 Polygon/Solid 属性）。
        /// 中心对称技巧（同 A1）：陈旧包络 y∈[20,30] 与 b（y∈[-30,-20]）的联合包围盒中心恰为原点，
        /// <c>Union</c> 不做平移，<c>MayOverlap</c> 直接读到陈旧缓存。
        /// </summary>
        [Test]
        [Explicit("DEFECT-006: Polygon 的 BoundingBox/BoundingSphere 缓存与 A1 同模式——原地改 Vertices 后 Union 的 MayOverlap 误判不相交")]
        public void CachedPolygonBounds_GoStale_AfterInPlaceVertexSwap_UnionSkipsClipping()
        {
            // 注：本用例故意不设未预热对照——未预热路径会读到过期 Plane 与顶点不一致的数据，
            // BSP 实测 >75 s 不返回（见 tests/DEFECTS.md DEFECT-006 影响面），照搬会挂住 CI。
            // 缓存历史的对照由第二个用例（平面自洽变体）承担。
            var a = CubeAt(25); // y∈[20,30]
            _ = a.Intersect(Solids.Cube(10, center: true)); // 预热：读 a.Bounds ⇒ 逐 polygon 缓存 bbox（树构造再缓存 sphere）

            var target = CubeAt(-20); // 目标几何：y∈[-25,-15]
            ReplaceAllVerticesInPlace(a, target);

            // 根因（全部为公共读数）：6/6 个面的 BoundingBox 仍报旧位置，真实顶点已在 y∈[-25,-15]。
            var cached = UnionOfCachedPolygonBoxes(a);
            var truth = VertexBox(a);
            Assert.That(cached.Min.Y, Is.EqualTo(20.0), "DEFECT-006：变更后 BoundingBox 缓存仍读旧位置（期望随顶点更新为 -25）");
            Assert.That(cached.Max.Y, Is.EqualTo(30.0), "DEFECT-006：变更后 BoundingBox 缓存仍读旧位置（期望随顶点更新为 -15）");
            Assert.That(truth.Min.Y, Is.EqualTo(-25.0), "前置：顶点确已搬到 y∈[-25,-15]");
            Assert.That(truth.Max.Y, Is.EqualTo(-15.0), "前置：顶点确已搬到 y∈[-25,-15]");

            // 对照（期望）：几何完全等价的干净实体（target）与 b 的并集 = 1500 / 6 个多边形（A1 回归同款读数）。
            var b = CubeAt(-25); // y∈[-30,-20]
            var clean = target.Union(b);
            GeometryAssert.AssertVolume(clean, 1500.0, 1e-6, "DEFECT-006 对照：干净等价几何的正确并集");
            Assert.That(clean.Polygons.Count, Is.EqualTo(6), "DEFECT-006 对照：干净等价几何的正确并集面数");

            // 实际（当前，错误读数）：陈旧包络 y∈[20,30] 与 b（y∈[-30,-20]）判「不相交」⇒ 拼接两个半重叠壳。
            // 期望：1500 / 6 个多边形（同上一行对照）；实际：2000 / 12 个多边形（6+6 直接拼接，未做任何裁剪）。
            var union = a.Union(b);
            Assert.That(union.Polygons.Count, Is.EqualTo(12),
                "DEFECT-006 实际读数：12 = a 的 6 个面 + b 的 6 个面直接拼接（期望 6：正确并集）");
            GeometryAssert.AssertVolume(union, 2000.0, 1e-6,
                "DEFECT-006 实际读数：2000 = 两个半重叠壳（各 1000）直接拼接（期望 1500：y∈[-30,-15] 的并集盒）");
        }

        /// <summary>
        /// 场景 2（平面自洽变体，排除「变更同时弄坏了 Plane」的替代解释）：变更只把 ±X 面在自身平面
        /// （x=±5）内沿 Y 伸长到 y∈[-40,40] —— 每个 polygon 仍凸、仍在自身平面，唯一被破坏的是实体的
        /// 封闭性。读数只随缓存历史变化：同一顶点数据、同一运算，预热读 12333.33…/12 个多边形，
        /// 未预热读 12000/14，且未预热与「用构造器重建的干净等价体」逐位一致。
        /// </summary>
        [Test]
        [Explicit("DEFECT-006: 平面自洽的原地顶点变更下 Union 读数仍随缓存历史变化（预热 12333.33 vs 未预热/干净等价体 12000）")]
        public void CachedPolygonBounds_MakeUnionResultDependOnCacheHistory_EvenWhenPolygonsStayOnTheirPlanes()
        {
            // b 覆盖 y∈[-30,-20]、x,z∈[-15,15]：陈旧包络（y∈[20,30]）与其联合中心仍为原点（x,z 对称），
            // 真几何的 ±X 面翼（x=±5, y∈[-40,40]）则穿过 b 的内部。
            var b = Solids.Cube(new Vector3D(30, 10, 30), new Vector3D(0, -25, 0));

            var cleanSource = CubeAt(25);
            ExtendXPlanesInPlace(cleanSource);
            var clean = Solid.FromPolygons(cleanSource.Polygons
                .Select(p => new Polygon(
                    new List<Vertex>(p.Vertices),
                    p.Shared,
                    Plane.FromVector3Ds(p.Vertices.Select(v => v.Pos).ToArray())))
                .ToList());
            var cleanUnion = clean.Union(b);

            var aCold = CubeAt(25);
            ExtendXPlanesInPlace(aCold); // 未预热：缓存首读发生在变更之后
            var coldUnion = aCold.Union(b);

            var aWarm = CubeAt(25);
            _ = aWarm.Intersect(Solids.Cube(10, center: true)); // 预热：缓存读到变更前的位置
            ExtendXPlanesInPlace(aWarm);
            var warmUnion = aWarm.Union(b);

            // 期望：读数只由几何数据决定 ⇒ 未预热 == 干净等价体（实测同为 12000 / 14 个多边形）。
            Assert.That(coldUnion.Polygons.Count, Is.EqualTo(cleanUnion.Polygons.Count),
                "DEFECT-006 对照：未预热读数应等于干净等价体重建后的读数");
            Assert.That(GeometryAssert.SignedVolume(coldUnion),
                Is.EqualTo(GeometryAssert.SignedVolume(cleanUnion)).Within(1e-9),
                "DEFECT-006 对照：未预热读数应逐位等于干净等价体读数");
            GeometryAssert.AssertVolume(cleanUnion, 12000.0, 1e-6, "DEFECT-006 对照：干净等价体（12000 = b 的 9000 + 翼被裁后 a 部分贡献 3000）");

            // 实际（当前，错误读数）：预热后读 12333.333333333334 / 12 个多边形（纯拼接，翼未被 b 裁剪）。
            // 期望：12000 / 14（与未预热、干净等价体一致）；实际多出 12333.33… − 12000 = 1000/3
            // = 两片落在 b 内部的翼条带（各 (1/3)·100·5）。
            Assert.That(warmUnion.Polygons.Count, Is.EqualTo(12),
                "DEFECT-006 实际读数：12 = 6+6 直接拼接（期望 14：翼被 b 的平面切成 2 段、b 的 6 个面）");
            GeometryAssert.AssertVolume(warmUnion, 12333.333333333334, 1e-6,
                "DEFECT-006 实际读数：12333.33… = 12000 + 1000/3（两片翼条带未被裁掉；期望 12000）");
        }

        // ------------------------------------------------------------------
        // 变更（全部为 public API 允许的原地列表操作）
        // ------------------------------------------------------------------

        /// <summary>把 a 的每个多面体原地换成 source 中法线相同的面对应的顶点（等价于整实体几何搬运）。</summary>
        private static void ReplaceAllVerticesInPlace(Solid a, Solid source)
        {
            foreach (var p in a.Polygons)
            {
                var q = source.Polygons.OrderByDescending(x => x.Plane.Normal.Dot(p.Plane.Normal)).First();
                p.Vertices.Clear();
                foreach (var v in q.Vertices)
                    p.Vertices.Add(v);
            }
        }

        /// <summary>把法线为 ±X 的面在自身平面内沿 Y 伸长（顶点仍在 x=±5 平面上，多边形仍凸）。</summary>
        private static void ExtendXPlanesInPlace(Solid a)
        {
            foreach (var p in a.Polygons)
            {
                if (Math.Abs(p.Plane.Normal.X) < 0.5)
                    continue;
                var ys = p.Vertices.Select(v => v.Pos.Y).ToList();
                var ymin = ys.Min();
                for (var i = 0; i < p.Vertices.Count; i++)
                {
                    var v = p.Vertices[i];
                    p.Vertices[i] = new Vertex(new Vector3D(v.Pos.X, v.Pos.Y == ymin ? -40.0 : 40.0, v.Pos.Z), v.Tex);
                }
            }
        }

        // ------------------------------------------------------------------
        // 公共读数
        // ------------------------------------------------------------------

        /// <summary>Solid.Bounds 的公开可观测等价物：逐个 polygon 读公共 BoundingBox 再合并。</summary>
        private static BoundingBox UnionOfCachedPolygonBoxes(Solid s)
        {
            var boxes = s.Polygons.Select(p => p.BoundingBox).ToList();
            var min = boxes[0].Min;
            var max = boxes[0].Max;
            foreach (var box in boxes)
            {
                min = min.Min(box.Min);
                max = max.Max(box.Max);
            }

            return new BoundingBox(min, max);
        }

        private static BoundingBox VertexBox(Solid s)
        {
            var points = s.Polygons.SelectMany(p => p.Vertices).Select(v => v.Pos).ToList();
            var min = points[0];
            var max = points[0];
            foreach (var p in points)
            {
                min = min.Min(p);
                max = max.Max(p);
            }

            return new BoundingBox(min, max);
        }
    }
}
