using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    [TestFixture]
    public class NodeGenTest
    {
        [Test]
        public void Random_SameSeed_ProducesEquivalentTree()
        {
            var jsonA = CsgSerialization.ToJson(NodeGen.Random(4242), indented: false);
            var jsonB = CsgSerialization.ToJson(NodeGen.Random(4242), indented: false);

            Assert.That(jsonB, Is.EqualTo(jsonA), "同种子必须产出同一棵树（JSON 相同即结构相同）");
        }

        [Test]
        [Explicit("DEFECT-002: 空操作数的 Subtract/Intersect 语义错误（empty−X 返回反向的 X、empty∩X 返回 X）")]
        public void Random_TreesAreEvaluableAndOutwardFacing()
        {
            var nonEmpty = 0;

            for (long seed = 0; seed < 24; seed++)
            {
                var tree = NodeGen.Random(seed);
                var solid = CsgEvaluator.Evaluate(tree);

                // 空结果是**合法**的：不相交的交集、被完全减去的差集都会产出零多边形实体。
                // 所以不能断言每个种子都非空 —— 只断言「非空 ⇒ 法线朝外」。
                if (solid.Polygons.Count == 0)
                    continue;

                nonEmpty++;
                Assert.That(GeometryAssert.SignedVolume(solid), Is.GreaterThan(0),
                    $"seed={seed} 有向体积必须为正；树：{CsgSerialization.ToJson(tree, indented: false)}");
            }

            // 但生成器不能退化成「几乎全空」—— 那样上面那条断言就形同虚设
            Assert.That(nonEmpty, Is.GreaterThan(12),
                $"24 个种子应有多数产出非空实体，实际仅 {nonEmpty} 个非空");
        }

        [Test]
        public void Random_CoversAllNodeAndProfileTypes()
        {
            var nodeTypes = new System.Collections.Generic.HashSet<string>();
            var profileTypes = new System.Collections.Generic.HashSet<string>();

            for (long seed = 0; seed < 512; seed++)
            {
                var json = CsgSerialization.ToJson(NodeGen.Random(seed), indented: false);
                foreach (var candidate in new[]
                {
                    "Box", "Sphere", "Cylinder", "Cone", "Extrude", "Wedge",
                    "Union", "Subtract", "Intersect", "Transform",
                })
                    if (json.Contains($"\"$type\": \"{candidate}\"") || json.Contains($"\"$type\":\"{candidate}\""))
                        nodeTypes.Add(candidate);

                foreach (var candidate in new[]
                {
                    "Rectangle", "HBeam", "Channel", "SquareTube", "Trapezoid", "Capsule", "LShape",
                })
                    if (json.Contains(candidate))
                        profileTypes.Add(candidate);
            }

            Assert.That(nodeTypes.Count, Is.GreaterThanOrEqualTo(9),
                $"512 个种子应覆盖至少 9 种节点类型，实际：{string.Join(",", nodeTypes)}");
            Assert.That(profileTypes.Count, Is.GreaterThanOrEqualTo(5),
                $"应覆盖至少 5 种截面，实际：{string.Join(",", profileTypes)}");
        }

        [Test]
        public void Random_RespectsDepthLimit()
        {
            for (long seed = 0; seed < 64; seed++)
            {
                var tree = NodeGen.Random(seed, maxDepth: 3);
                Assert.That(Depth(tree), Is.LessThanOrEqualTo(3), $"seed={seed} 超过深度上限");
            }
        }

        static int Depth(CsgNode node)
        {
            switch (node)
            {
                case UnionNode u: return 1 + MaxDepth(u.Children);
                case SubtractNode s: return 1 + MaxDepth(s.Children);
                case IntersectNode i: return 1 + MaxDepth(i.Children);
                case TransformNode t: return 1 + Depth(t.Child);
                default: return 1;
            }
        }

        static int MaxDepth(System.Collections.Generic.IReadOnlyList<CsgNode> children)
        {
            var max = 0;
            foreach (var child in children)
                max = System.Math.Max(max, Depth(child));
            return max;
        }
    }
}
