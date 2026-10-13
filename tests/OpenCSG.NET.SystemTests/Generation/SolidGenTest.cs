using System.Collections.Generic;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    [TestFixture]
    public class SolidGenTest
    {
        [Test]
        public void Random_SameSeed_ProducesIdenticalSpec()
        {
            var a = SolidGen.Random(12345, CoordTier.Normal);
            var b = SolidGen.Random(12345, CoordTier.Normal);

            Assert.That(b, Is.EqualTo(a), "同种子必须产出完全相同的 spec（可复现性的基础）");
        }

        [Test]
        public void Random_DifferentSeeds_ProduceDifferentSpecs()
        {
            var seen = new HashSet<SolidSpec>();
            for (long seed = 0; seed < 64; seed++)
                seen.Add(SolidGen.Random(seed, CoordTier.Normal));

            Assert.That(seen.Count, Is.GreaterThan(32), "64 个种子应产出多数互异的 spec");
        }

        [Test]
        public void Materialize_IsDeterministic()
        {
            var spec = SolidGen.Random(777, CoordTier.Normal);

            var first = SolidGen.Materialize(spec);
            var second = SolidGen.Materialize(spec);

            Assert.That(GeometryAssert.SignedVolume(second),
                Is.EqualTo(GeometryAssert.SignedVolume(first)).Within(1e-12));
            Assert.That(second.Polygons.Count, Is.EqualTo(first.Polygons.Count));
        }

        [Test]
        public void Materialize_EveryPrimitiveKind_IsANonEmptyOrientedSolid()
        {
            foreach (var kind in new[]
            {
                PrimitiveKind.Cube, PrimitiveKind.Sphere, PrimitiveKind.Cylinder,
                PrimitiveKind.Cone, PrimitiveKind.Extrude, PrimitiveKind.Wedge,
            })
            {
                var spec = SolidGen.Random(42, CoordTier.Normal) with { Kind = kind };
                var solid = SolidGen.Materialize(spec);

                Assert.That(solid.Polygons.Count, Is.GreaterThan(0), $"{kind}: 不得为空实体");
                Assert.That(GeometryAssert.SignedVolume(solid), Is.GreaterThan(0),
                    $"{kind}: 有向体积必须为正（法线朝外）");
                GeometryAssert.AssertPlanesMatchWinding(solid, kind.ToString());
            }
        }

        [Test]
        public void Materialize_SurveyTier_KeepsVolumeAndShiftsBounds()
        {
            var spec = SolidGen.Random(2026, CoordTier.Normal) with
            {
                Kind = PrimitiveKind.Cube,
                SizeX = 10, SizeY = 20, SizeZ = 30,
                Resolution = 12,
                OffsetX = 1_000_000, OffsetY = -2_000_000, OffsetZ = 3_000_000,
                RotateX = 0, RotateY = 0, RotateZ = 0, Scale = 1, Mirror = false,
            };

            var solid = SolidGen.Materialize(spec);

            Assert.That(GeometryAssert.SignedVolume(solid), Is.EqualTo(6000.0).Within(1e-6));

            var (min, max) = GeometryAssert.Bounds(solid);
            Assert.That(min.X, Is.EqualTo(1_000_000 - 5).Within(1e-6));
            Assert.That(max.Z, Is.EqualTo(3_000_000 + 15).Within(1e-6));
        }

        [Test]
        public void Materialize_MirrorFlagsStillOutwardFacing()
        {
            var spec = SolidGen.Random(9, CoordTier.Normal) with
            {
                Kind = PrimitiveKind.Cube,
                Scale = 1, RotateX = 0, RotateY = 0, RotateZ = 0, Mirror = true,
            };

            var solid = SolidGen.Materialize(spec);

            Assert.That(GeometryAssert.SignedVolume(solid), Is.GreaterThan(0),
                "镜像后法线必须仍朝外（库内 Transform 会翻转绕序补偿）");
        }

        [Test]
        public void RandomPair_OffsetsAreCorrelated_SoSomePairsOverlap()
        {
            var overlapping = 0;
            for (long seed = 0; seed < 64; seed++)
            {
                var (a, b) = SolidGen.RandomPair(seed, CoordTier.Normal);
                var (minA, maxA) = GeometryAssert.Bounds(SolidGen.Materialize(a));
                var (minB, maxB) = GeometryAssert.Bounds(SolidGen.Materialize(b));

                bool disjoint = maxA.X < minB.X || maxB.X < minA.X
                             || maxA.Y < minB.Y || maxB.Y < minA.Y
                             || maxA.Z < minB.Z || maxB.Z < minA.Z;
                if (!disjoint) overlapping++;
            }

            Assert.That(overlapping, Is.GreaterThan(0), "必须存在相交对，否则布尔恒等式毫无信息量");
            Assert.That(overlapping, Is.LessThan(64), "也必须存在不相交对，覆盖 MayOverlap 快速路径");
        }

        [Test]
        public void Random_ExcludesKnownDefectiveInputs()
        {
            for (long seed = 0; seed < 512; seed++)
            {
                var spec = SolidGen.Random(seed, CoordTier.Normal);

                Assert.That(spec.SectorAngle, Is.EqualTo(360.0),
                    "已知缺陷：SectorAngle ∈ {0, 720, 负数} 会静默产出空实体（Plan C 承接），随机采样必须排除");
                Assert.That(spec.RadiusEnd, Is.GreaterThan(0));
            }
        }
    }
}
