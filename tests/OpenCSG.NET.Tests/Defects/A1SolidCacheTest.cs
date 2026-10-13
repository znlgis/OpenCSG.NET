using System.Collections.Generic;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// A1 回归（spec §9 批准的最小修复；出自 spec 缺陷勘察表 A1，从未分配 DEFECT 编号）：
    /// <list type="number">
    /// <item>缓存过期：<see cref="Solid"/> 的 <c>cachedBoundingBox</c> 惰性缓存无失效机制，而
    /// <c>Polygons</c> 是公共可变字段（数量不变或直接替换列表都无法察觉）。缓存过期后
    /// <c>Union</c> 里 <c>MayOverlap</c> 用陈旧包络误判「不相交」，跳过 BSP 裁剪直接拼接多边形。</item>
    /// <item>别名：<c>FromPolygons</c> 曾直接持有调用方 list，调用方改动会同步改动实体。</item>
    /// </list>
    /// 修复方式：删除 <c>cachedBoundingBox</c>（每次现算）+ <c>FromPolygons</c> 防御性复制。
    /// </summary>
    [TestFixture]
    public class A1SolidCacheTest
    {
        /// <summary>
        /// A1 复现 1（缓存过期）：先让 a 读一次 Bounds（缓存 = 远处小盒的包络），
        /// 再原地换掉 a.Polygons 为与 c 半重叠的盒（count 6→6，缓存无从察觉），
        /// 随后 a.Union(c) —— 修复前 MayOverlap 用旧包络判「不相交」而走拼接错路。
        /// 关键设计：陈旧包络 y∈[20,30] 与 c（y∈[-30,-20]）的联合包围盒中心 = 原点
        /// ⇒ Union 不做平移（identity 分支），MayOverlap 直接读到陈旧缓存。
        /// </summary>
        [Test]
        public void CachedBounds_GoStale_AfterInPlacePolygonSwap_UnionStillCorrect()
        {
            // a 先位于 y∈[20,30]，probe 位于原点，互不相交；Intersect 构造 BSP 树时读 Bounds 即缓存。
            var a = Solids.Cube(10, center: true).Translate(0, 25, 0);
            _ = a.Intersect(Solids.Cube(10, center: true));

            // 原地换掉 a 的多边形（6→6）：换成 y∈[-25,-15] 的盒。
            var actualA = Solids.Cube(10, center: true).Translate(0, -20, 0);
            a.Polygons.Clear();
            foreach (var polygon in actualA.Polygons)
                a.Polygons.Add(polygon);

            // c 位于 y∈[-30,-20]，与 actualA 重叠 y∈[-25,-20]（500）⇒ 正确并集 = 10×15×10 盒 = 1500。
            // 修复前：a.Bounds 仍是 [20,30] 的陈旧缓存 ⇒ MayOverlap 误判「不相交」⇒ 两个半重叠
            // 实体被直接拼接（12 个多边形、体积 ≈ 2000）。
            var union = a.Union(Solids.Cube(10, center: true).Translate(0, -25, 0));

            GeometryAssert.AssertVolume(union, 1500.0, 1e-6,
                "A1 复现 1：原地换多边形后陈旧 Bounds 缓存使 MayOverlap 误判不相交");
            GeometryAssert.AssertOrientationConsistent(union, "A1 复现 1 的并集输出");
        }

        /// <summary>A1 复现 2（别名）：FromPolygons 不得持有调用方列表。</summary>
        [Test]
        public void FromPolygons_DoesNotAliasCallerList()
        {
            var callerList = new List<Polygon>(Solids.Cube(10, center: true).Polygons);
            var solid = Solid.FromPolygons(callerList);

            callerList.Clear();

            Assert.That(solid.Polygons.Count, Is.EqualTo(6),
                "A1 复现 2：FromPolygons 别名持有调用方 list —— 清空调用方列表后实体被连带清空");
        }
    }
}
