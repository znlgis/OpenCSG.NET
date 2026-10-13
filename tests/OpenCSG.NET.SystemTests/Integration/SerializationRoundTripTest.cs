using System;
using System.Collections.Generic;
using System.Linq;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// L5 集成：JSON 闭环 —— <c>CsgNode 树 → CsgSerialization.ToJson → FromJson → CsgEvaluator.Evaluate</c>。
    /// 等价口径：多边形数、有向体积（<see cref="GeometryAssert.SignedVolume"/>）、网格三角形数
    /// （<see cref="CsgMesh.TrianglesOf"/>）与三视图逐段端点（排序后比较）。
    /// <para>
    /// <b>深度口径（实测，探针 <c>artifacts/planD/d5probe</c>）</b>：手工「变换 + 布尔」链每层
    /// 消耗 3 层 JSON 嵌套（实测公式 3·d + 2：d=4 → 14、8 → 26、12 → 38、16 → 50、20 → 62）。
    /// 本用例取 <b>d = 16</b>（JSON 嵌套实测 50，留 14 层余量）：d=24 起 <c>ToJson</c> 抛可捕获的
    /// <c>JsonException</c>（System.Text.Json writer 的 64 层上限，写出侧先于栈耗尽命中。
    /// 与 <b>DEFECT-007</b> 的域（程序构造 ≳1000 层深树 ⇒ StackOverflow、进程中止、不可捕获）不同层级，
    /// 但同属该缺陷说明的深度约束 —— 本文件全部树显式控制在 64 层 JSON 嵌套以内并逐条断言）。
    /// </para>
    /// <para>
    /// <b>实测读数</b>：DeepTree(16) 296 多边形、533 三角形、体积 1875.321488427817（Δ=0）、
    /// 三视图段数 234,245,958 两侧一致、端点最大偏差 0；NodeGen seed∈{1,7,4242} 往返体积 Δ=0、
    /// 三角形 8/44/48、端点偏差 0。故等价断言取容差 0。
    /// </para>
    /// </summary>
    [TestFixture]
    public class SerializationRoundTripTest
    {
        /// <summary>手工「变换 + 布尔」中等深度树：<c>Transform(Union(子树, 盒))</c> 递归 d 层。</summary>
        static CsgNode DeepTree(int transformLevels)
        {
            CsgNode node = CsgNodes.Box(new Vector3D(0, 0, 0), new Vector3D(10, 10, 10));
            for (var i = 0; i < transformLevels; i++)
            {
                node = CsgNodes.Union(
                    CsgNodes.Transform(new Vector3D(1.5, 0, 0), new Vector3D(0, 0, 2.5), node),
                    CsgNodes.Box(new Vector3D(0, 0, 15 + i * 3), new Vector3D(4, 4, 4)));
            }
            return node;
        }

        [Test]
        public void MediumDepthTree_RoundTripPreservesGeometry()
        {
            // 16 层变换链 = 33 个节点层；JSON 嵌套实测 50（见类注释）。
            var tree = DeepTree(16);

            var json = CsgSerialization.ToJson(tree);
            Assert.That(JsonMaxDepth(json), Is.LessThan(64),
                $"JSON 实测嵌套 {JsonMaxDepth(json)} 必须 < 64（读守卫上限；超限即进入 DEFECT-007 记录的深度约束域）");

            var direct = CsgEvaluator.Evaluate(tree);
            var viaJson = CsgEvaluator.Evaluate(CsgSerialization.FromJson(json));

            Assert.That(direct.Polygons.Count, Is.EqualTo(296), "实测：直接求值 296 个多边形");
            Assert.That(viaJson.Polygons.Count, Is.EqualTo(direct.Polygons.Count),
                $"多边形数 {viaJson.Polygons.Count} vs {direct.Polygons.Count}");
            Assert.That(GeometryAssert.SignedVolume(viaJson),
                Is.EqualTo(GeometryAssert.SignedVolume(direct)).Within(0.0),
                $"有向体积 {GeometryAssert.SignedVolume(viaJson):R} vs {GeometryAssert.SignedVolume(direct):R}（实测 Δ=0）");
            Assert.That(CsgMesh.TrianglesOf(viaJson).Count, Is.EqualTo(CsgMesh.TrianglesOf(direct).Count),
                "网格三角形数（实测 533/533）");
            Assert.That(CsgMesh.TrianglesOf(direct).Count, Is.EqualTo(533));

            // 三视图（实测段数 234,245,958；端点最大偏差 0）。
            PipelineIntegrationTest.AssertSameViews(
                ViewBridge.ProjectViews(direct, ViewDirections.OrthographicThree),
                ViewBridge.ProjectViews(viaJson, ViewDirections.OrthographicThree),
                0.0, "DeepTree(16) 直接 vs JSON 往返");
        }

        [TestCase(1)]
        [TestCase(7)]
        [TestCase(4242)]
        public void NodeGenTree_RoundTripPreservesGeometry(long seed)
        {
            var tree = NodeGen.Random(seed);
            var json = CsgSerialization.ToJson(tree);

            var direct = CsgEvaluator.Evaluate(tree);
            var viaJson = CsgEvaluator.Evaluate(CsgSerialization.FromJson(json));

            Assert.That(viaJson.Polygons.Count, Is.EqualTo(direct.Polygons.Count),
                $"seed={seed} 多边形数 {viaJson.Polygons.Count} vs {direct.Polygons.Count}");
            Assert.That(GeometryAssert.SignedVolume(viaJson),
                Is.EqualTo(GeometryAssert.SignedVolume(direct)).Within(0.0),
                $"seed={seed} 有向体积 {GeometryAssert.SignedVolume(viaJson):R} vs {GeometryAssert.SignedVolume(direct):R}");
            Assert.That(CsgMesh.TrianglesOf(viaJson).Count, Is.EqualTo(CsgMesh.TrianglesOf(direct).Count),
                $"seed={seed} 网格三角形数");
            Assert.That(GeometryAssert.SignedVolume(direct), Is.GreaterThan(0),
                $"seed={seed} 实测夹具必须法线朝外（该种子不在 DEFECT-002 的 13 个错误种子表内）");
        }

        /// <summary>
        /// 序列化往返后的实体投影不得有任何段/弧差异（实测端点差 0，含布尔与变换节点）。
        /// 本条固定树实测：46 多边形、96 三角形、体积 2891.7128129211033、三视图段数 20,38,15 两侧一致；
        /// 直接侧读数另以绝对钉固定（防两侧同空使比较空转）。
        /// </summary>
        [Test]
        public void RoundTrippedTree_ProjectsIdentically()
        {
            var tree = CsgNodes.Subtract(
                CsgNodes.Box(new Vector3D(0, 0, 0), new Vector3D(20, 20, 10)),
                CsgNodes.Transform(
                    new Vector3D(0, 0, 0), new Vector3D(0, 0, 30),
                    CsgNodes.Cylinder(new Vector3D(0, 0, 0), 4, 30)));

            var direct = CsgEvaluator.Evaluate(tree);
            var viaJson = CsgEvaluator.Evaluate(CsgSerialization.FromJson(CsgSerialization.ToJson(tree)));

            // 绝对读数钉（实测钉，防 0==0 空转）：46 多边形、96 三角形、三视图段数 20,38,15。
            Assert.That(direct.Polygons.Count, Is.EqualTo(46), "实测直接求值 46 个多边形");
            Assert.That(CsgMesh.TrianglesOf(direct).Count, Is.EqualTo(96), "实测直接求值 96 个三角形");
            var directViews = ViewBridge.ProjectViews(direct, ViewDirections.OrthographicThree);
            var roundViews = ViewBridge.ProjectViews(viaJson, ViewDirections.OrthographicThree);
            Assert.That(directViews.Select(v => v.Segments.Count).ToArray(), Is.EqualTo(new[] { 20, 38, 15 }),
                "实测三视图段数 20,38,15（防比较空转）");

            PipelineIntegrationTest.AssertSameViews(
                directViews, roundViews,
                0.0, $"Subtract(Box, Transform(Cylinder)) 直接 vs JSON 往返（seed 等价树）");
        }

        /// <summary>
        /// JSON 文本的最大「对象/数组」嵌套层数（字符串外的 {} / [] 配对深度）——与
        /// <c>System.Text.Json</c> 的 <c>MaxDepth</c> 同一记法，用于断言未越过 64 层读守卫。
        /// </summary>
        static int JsonMaxDepth(string json)
        {
            var depth = 0;
            var max = 0;
            var inString = false;
            for (var i = 0; i < json.Length; i++)
            {
                var c = json[i];
                if (inString)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                if (c is '{' or '[') { depth++; max = Math.Max(max, depth); }
                else if (c is '}' or ']') depth--;
            }
            return max;
        }
    }
}
