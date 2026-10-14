using System;
using System.Collections.Generic;
using System.Linq;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// DEFECT-006 回归（2026-10-14 修复；登记于 tests/DEFECTS.md 已修复表）。
    /// <para>
    /// 原缺陷：<see cref="Polygon"/> 的 <c>cachedBoundingSphere</c>/<c>cachedBoundingBox</c>
    /// 是无失效机制的惰性缓存，而 <c>Vertices</c> 是公共可变列表 —— 缓存建立后原地改顶点，
    /// 公共读数与 <c>Union</c> 里 <c>MayOverlap</c> 都读到陈旧包络（A1 同模式）。
    /// 修复：删除两个缓存、每次现算。
    /// </para>
    /// <para>
    /// 残留边界（不改，登记于 DEFECTS.md 影响面注记）：<c>Plane</c> 是构造期快照且 <c>readonly</c>，
    /// 「整实体搬运类」的原地顶点变更会同时使 Plane 与顶点不一致，依赖 Plane 的布尔/BSP 路径
    /// 对这类输入不受支持（探针实测可长时间不返回）—— 故本夹具只对**平面自洽**的原地变更
    /// 断言并集结果；场景 1 只断言公共读数（修复面）。
    /// </para>
    /// <para>探针原始读数（artifacts/planC/polygonProbe）与修复后复测见 tests/DEFECTS.md 已修复表。</para>
    /// </summary>
    [TestFixture]
    public class D6PolygonCacheTest
    {
        private static Solid CubeAt(double y) => Solids.Cube(10, center: true).Translate(0, y, 0);

        /// <summary>
        /// 场景 1（A1 同款构造）：预热缓存后把 a 的每个面原地换成远处盒的对应面顶点。
        /// 修复面 = 公共读数：BoundingBox/BoundingSphere 随顶点即时更新
        /// （修复前停留在变更前的 y∈[20,30]）。
        /// 注：该顶点搬运同时使只读 Plane 过期，布尔/BSP 路径对这类输入不受支持（见类注），
        /// 故不对该实体调用 Union —— 并集行为由场景 2（平面自洽变体）承担。
        /// </summary>
        [Test]
        public void InPlaceVertexSwap_PublicBoundsReadsFollowVertices()
        {
            var a = CubeAt(25); // y∈[20,30]
            _ = a.Intersect(Solids.Cube(10, center: true)); // 预热：读 a.Bounds ⇒ 修复前在此建立逐 polygon 缓存

            var target = CubeAt(-20); // 目标几何：y∈[-25,-15]
            ReplaceAllVerticesInPlace(a, target);

            // 修复后：逐 polygon 的公共读数随顶点更新（修复前仍报 20/30）。
            foreach (var p in a.Polygons)
            {
                var min = p.Vertices[0].Pos;
                var max = min;
                foreach (var v in p.Vertices)
                {
                    min = min.Min(v.Pos);
                    max = max.Max(v.Pos);
                }

                Assert.That(p.BoundingBox.Min.Y, Is.EqualTo(min.Y).Within(1e-12),
                    "DEFECT-006 修复回归：BoundingBox 应随顶点更新");
                Assert.That(p.BoundingBox.Max.Y, Is.EqualTo(max.Y).Within(1e-12),
                    "DEFECT-006 修复回归：BoundingBox 应随顶点更新");
                Assert.That(p.BoundingSphere.Center.Y, Is.EqualTo((min.Y + max.Y) * 0.5).Within(1e-12),
                    "DEFECT-006 修复回归：BoundingSphere 应随顶点更新（修复前停在新平面之外的旧球心）");
            }

            var box = UnionOfPolygonBoxes(a);
            var truth = VertexBox(a);
            Assert.That(box.Min.Y, Is.EqualTo(-25.0), "DEFECT-006 修复回归：变更后整体包络应读新位置");
            Assert.That(box.Max.Y, Is.EqualTo(-15.0), "DEFECT-006 修复回归：变更后整体包络应读新位置");
            Assert.That(truth.Min.Y, Is.EqualTo(-25.0), "前置：顶点确已搬到 y∈[-25,-15]");
            Assert.That(truth.Max.Y, Is.EqualTo(-15.0), "前置：顶点确已搬到 y∈[-25,-15]");

            // 对照 oracle（干净等价几何，不依赖该缺陷路径）：target 与 b 的正确并集 = 1500 / 6 个多边形。
            var b = CubeAt(-25); // y∈[-30,-20]
            var clean = target.Union(b);
            GeometryAssert.AssertVolume(clean, 1500.0, 1e-6, "DEFECT-006 对照：干净等价几何的正确并集");
            Assert.That(clean.Polygons.Count, Is.EqualTo(6), "DEFECT-006 对照：干净等价几何的正确并集面数");
        }

        /// <summary>
        /// 场景 2（平面自洽变体，排除「变更同时弄坏了 Plane」的替代解释）：变更只把 ±X 面在自身平面
        /// （x=±5）内沿 Y 伸长到 y∈[-40,40] —— 每个 polygon 仍凸、仍在自身平面，Plane 未过期。
        /// 修复后读数只由几何数据决定：预热 == 未预热 == 干净等价体（同为 12000 / 14 个多边形）。
        /// </summary>
        [Test]
        public void InPlaceEditOnOwnPlanes_UnionResultIndependentOfCacheHistory()
        {
            // b 覆盖 y∈[-30,-20]、x,z∈[-15,15]：先预热时陈旧包络（y∈[20,30]）与其 Y 向不重叠
            // ⇒ 修复前 MayOverlap 误判「不相交」直接拼接（12 个多边形、12333.33…）；修复后走 BSP 裁剪。
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
            _ = aWarm.Intersect(Solids.Cube(10, center: true)); // 预热：修复前在此建立变更前的缓存
            ExtendXPlanesInPlace(aWarm);
            var warmUnion = aWarm.Union(b);

            // 对照基线：干净等价体（12000 = b 的 9000 + 翼被裁后 a 部分贡献 3000）。
            GeometryAssert.AssertVolume(cleanUnion, 12000.0, 1e-6,
                "DEFECT-006 对照：干净等价体（12000 = b 的 9000 + 翼被裁后 a 部分贡献 3000）");
            Assert.That(cleanUnion.Polygons.Count, Is.EqualTo(14), "DEFECT-006 对照：干净等价体面数");

            // 修复后：未预热读数 == 干净等价体（逐位）。
            Assert.That(coldUnion.Polygons.Count, Is.EqualTo(cleanUnion.Polygons.Count),
                "DEFECT-006 修复回归：未预热读数应等于干净等价体重建后的读数");
            Assert.That(GeometryAssert.SignedVolume(coldUnion),
                Is.EqualTo(GeometryAssert.SignedVolume(cleanUnion)).Within(1e-9),
                "DEFECT-006 修复回归：未预热读数应逐位等于干净等价体读数");

            // 修复后：预热读数 == 干净等价体（修复前读 12333.333333333334 / 12 个多边形 ——
            // 陈旧包络使 MayOverlap 误判「不相交」，翼未被 b 裁剪）。
            Assert.That(warmUnion.Polygons.Count, Is.EqualTo(cleanUnion.Polygons.Count),
                "DEFECT-006 修复回归：预热后并集读数应等于干净等价体（修复前 12，期望 14）");
            Assert.That(GeometryAssert.SignedVolume(warmUnion),
                Is.EqualTo(GeometryAssert.SignedVolume(cleanUnion)).Within(1e-9),
                "DEFECT-006 修复回归：预热后并集体积应等于干净等价体（修复前 12333.33…，期望 12000）");
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
        private static BoundingBox UnionOfPolygonBoxes(Solid s)
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
