using System;
using System.Collections.Generic;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// Tier 1 续：变换等价、包围盒代数、镜像对合、序列化往返、平面分割、相等契约。
    /// </summary>
    [TestFixture]
    public class Tier1TransformIdentityTest
    {
        const double Tol = 1e-6;

        static IEnumerable<TestCaseData> SmokeCases()
        {
            var seeds = Corpus.SmokeSeeds();
            for (var i = 0; i < seeds.Count; i++)
                yield return new TestCaseData(seeds[i], Corpus.TierOf(i)).SetName($"Smoke_{i:D2}_{Corpus.TierOf(i)}");
        }

        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        public void BoundingBoxAlgebra(long seed, CoordTier tier)
        {
            var (specA, specB) = SolidGen.RandomPair(seed, tier);
            var a = SolidGen.Materialize(specA);
            var b = SolidGen.Materialize(specB);

            TransformAssert.AssertBoundingBoxAlgebra(a, b, Tol, $"seed={seed} tier={tier}");
        }

        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        public void TransformEquivariance(long seed, CoordTier tier)
        {
            var a = SolidGen.Materialize(SolidGen.Random(seed, tier));

            TransformAssert.AssertTransformEquivariance(a, Tol, $"seed={seed} tier={tier}");
        }

        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        public void MirrorIsInvolution(long seed, CoordTier tier)
        {
            var a = SolidGen.Materialize(SolidGen.Random(seed, tier));

            TransformAssert.AssertMirrorIsInvolution(a, $"seed={seed} tier={tier}");
        }

        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        public void SerializationRoundTrip_PreservesGeometry(long seed, CoordTier tier)
        {
            var tree = NodeGen.Random(seed);

            var direct = CsgEvaluator.Evaluate(tree);
            var roundTripped = CsgEvaluator.Evaluate(CsgSerialization.FromJson(CsgSerialization.ToJson(tree)));

            TransformAssert.AssertSameShape(direct, roundTripped, Tol,
                $"seed={seed} tier={tier} 树：{CsgSerialization.ToJson(tree, indented: false)}");
        }

        [TestCaseSource(nameof(SmokeCases))]
        [Category("Smoke")]
        public void SerializationCompactAndIndented_Agree(long seed, CoordTier tier)
        {
            var tree = NodeGen.Random(seed);

            var indented = CsgSerialization.ToJson(tree, indented: true);
            var compact = CsgSerialization.ToJson(tree, indented: false);

            var fromIndented = CsgEvaluator.Evaluate(CsgSerialization.FromJson(indented));
            var fromCompact = CsgEvaluator.Evaluate(CsgSerialization.FromJson(compact));

            TransformAssert.AssertSameShape(fromIndented, fromCompact, Tol, $"seed={seed} tier={tier}");
        }

        [Test]
        public void PlaneSplitPolygon_PartitionAreasMatch()
        {
            // 一个跨越平面的四边形（对角两个顶点在平面两侧）
            var polygon = Square(new Vector3D(0, 0, 0), 4.0);

            foreach (var offset in new[] { -1.5, -0.5, 0.0, 0.5, 1.5 })
            {
                var plane = new Plane(new Vector3D(1, 0, 0), offset);   // x = offset
                plane.SplitPolygon(polygon, out var result);

                var total = TransformAssert.PolygonArea(polygon);
                // SplitPolygon 的契约（见 Plane.SplitPolygon 与 Tree.SplitPolygonByPlane 的用法）：
                // Type=2/3 表示整片多边形落在该侧，Front/Back 保持 null，调用方沿用原多边形；
                // 只有 Type=4（跨越）才真正产出 Front/Back 两片。
                var front = result.Type == 2 ? total
                    : result.Front == null ? 0.0 : TransformAssert.PolygonArea(result.Front);
                var back = result.Type == 3 ? total
                    : result.Back == null ? 0.0 : TransformAssert.PolygonArea(result.Back);

                switch (result.Type)
                {
                    case 2: // 全在前侧
                        Assert.That(front, Is.EqualTo(total).Within(1e-9), $"offset={offset} Type=2");
                        Assert.That(result.Back, Is.Null, $"offset={offset} Type=2 不应有后侧片");
                        break;

                    case 3: // 全在后侧
                        Assert.That(back, Is.EqualTo(total).Within(1e-9), $"offset={offset} Type=3");
                        Assert.That(result.Front, Is.Null, $"offset={offset} Type=3 不应有前侧片");
                        break;

                    case 4: // 跨平面：面积必须守恒
                        Assert.That(front + back, Is.EqualTo(total).Within(1e-9),
                            $"offset={offset} 跨平面分割面积不守恒：front={front:R} back={back:R} total={total:R}");
                        break;

                    default:
                        Assert.Fail($"offset={offset} 意外 Type={result.Type}（四边形跨越 x=offset 平面，不应共面）");
                        break;
                }
            }
        }

        [Test]
        public void PlaneSplitPolygon_AllFiveTypeCodesAreReachable()
        {
            var polygon = Square(new Vector3D(0, 0, 0), 4.0);
            var seen = new HashSet<int>();

            // 0/1 共面：用多边形自身平面与其翻转平面
            polygon.Plane.SplitPolygon(polygon, out var coplanarFront);
            seen.Add(coplanarFront.Type);

            polygon.Plane.Flipped().SplitPolygon(polygon, out var coplanarBack);
            seen.Add(coplanarBack.Type);

            // 2/3 单侧：正方形占据 x∈[0,4]，正向法线的「全前」必须用整片在正侧的平面（x=−100），
            // 「全后」用整片在负侧的平面（x=+100）—— 反过来只会得到另一个码。
            new Plane(new Vector3D(1, 0, 0), -100.0).SplitPolygon(polygon, out var front);
            seen.Add(front.Type);
            new Plane(new Vector3D(1, 0, 0), 100.0).SplitPolygon(polygon, out var back);
            seen.Add(back.Type);

            // 4 跨越：平面必须严格穿过多边形内部。x=0 只与正方形的一条边相切（顶点全在 0 侧），
            // 命中 Type=2 而非 4；取 x=2 才能得到真正的跨越。
            new Plane(new Vector3D(1, 0, 0), 2.0).SplitPolygon(polygon, out var spanning);
            seen.Add(spanning.Type);

            Assert.That(seen, Is.EquivalentTo(new[] { 0, 1, 2, 3, 4 }),
                $"5 个分割类型码应全部可达，实际只见到 {string.Join(",", seen)}");

            Assert.That(coplanarFront.Type, Is.EqualTo(0), "同向共面应为 0");
            Assert.That(coplanarBack.Type, Is.EqualTo(1), "反向共面应为 1");
            Assert.That(front.Type, Is.EqualTo(2), "全前应为 2");
            Assert.That(back.Type, Is.EqualTo(3), "全后应为 3");
            Assert.That(spanning.Type, Is.EqualTo(4), "跨越应为 4");
        }

        [Test]
        public void VectorEquality_AgreesWithHashCode()
        {
            var pairs = new (Vector3D A, Vector3D B)[]
            {
                (new Vector3D(0, 0, 0), new Vector3D(0, 0, 0)),
                (new Vector3D(1.5, -2.25, 3e-7), new Vector3D(1.5, -2.25, 3e-7)),
                (new Vector3D(-0.0, 0.0, 0.0), new Vector3D(0.0, -0.0, 0.0)),
                (new Vector3D(1e6, -2e6, 3e6), new Vector3D(1e6, -2e6, 3e6)),
            };

            foreach (var (a, b) in pairs)
            {
                Assert.That(a.Equals(b), Is.True, $"{a} 与 {b} 应相等");
                Assert.That(b.Equals(a), Is.True, "Equals 应对称");
                Assert.That(a.Equals(a), Is.True, "Equals 应自反");
                Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()),
                    $"相等对象的哈希必须相同：{a} vs {b}");
            }

            var v2pairs = new (Vector2D A, Vector2D B)[]
            {
                (new Vector2D(0, 0), new Vector2D(0, 0)),
                (new Vector2D(1.5, -2.25), new Vector2D(1.5, -2.25)),
            };

            foreach (var (a, b) in v2pairs)
            {
                Assert.That(a.Equals(b), Is.True, $"{a} 与 {b} 应相等");
                Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()),
                    $"相等对象的哈希必须相同：{a} vs {b}");
            }
        }

        [Test]
        public void Vector3D_RejectsNaN()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Vector3D(double.NaN, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Vector3D(0, double.NaN, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Vector3D(0, 0, double.NaN));
        }

        /// <summary>z = z0 平面上的正方形（原点在 (x0,y0,z0)），顶点逆时针。</summary>
        static Polygon Square(Vector3D origin, double side)
        {
            var tex = new Vector2D(0, 0);
            return new Polygon(
                new Vertex(origin, tex),
                new Vertex(new Vector3D(origin.X + side, origin.Y, origin.Z), tex),
                new Vertex(new Vector3D(origin.X + side, origin.Y + side, origin.Z), tex),
                new Vertex(new Vector3D(origin.X, origin.Y + side, origin.Z), tex));
        }
    }
}
