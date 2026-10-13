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
        public void Materialize_SquareTube_IsHollowUnlikeRectangle()
        {
            var baseline = SolidGen.Random(42, CoordTier.Normal);

            var rectangle = SolidGen.Materialize(baseline with
            { Kind = PrimitiveKind.Extrude, Profile = ProfileKind.Rectangle });
            var tube = SolidGen.Materialize(baseline with
            { Kind = PrimitiveKind.Extrude, Profile = ProfileKind.SquareTube });

            var solidVolume = GeometryAssert.SignedVolume(rectangle);
            var tubeVolume = GeometryAssert.SignedVolume(tube);

            Assert.That(tubeVolume, Is.GreaterThan(0), "方管必须是有向朝外的实体");
            Assert.That(tubeVolume, Is.LessThan(solidVolume * 0.9),
                $"方管必须是空心的：方管体积 {tubeVolume:R} 应明显小于同尺寸实心块 {solidVolume:R}"
                + "（若两者相等，说明 SquareTubeProfile.Thickness 被忽略、该截面与 Rectangle 退化成同一几何）");
        }

        [Test]
        public void Materialize_AllProfileKinds_ProduceNonEmptySolids()
        {
            var baseline = SolidGen.Random(42, CoordTier.Normal);

            foreach (ProfileKind profile in System.Enum.GetValues(typeof(ProfileKind)))
            {
                var spec = baseline with { Kind = PrimitiveKind.Extrude, Profile = profile };
                var solid = SolidGen.Materialize(spec);

                Assert.That(solid.Polygons.Count, Is.GreaterThan(0), $"{profile}: 不得为空实体");
                Assert.That(GeometryAssert.SignedVolume(solid), Is.GreaterThan(0),
                    $"{profile}: 有向体积必须为正（法线朝外）");
            }
        }

        [Test]
        public void Materialize_MirrorFlagsStillOutwardFacing()
        {
            // 夹具必须沿 X **不对称**，否则删掉 Materialize 里的镜像分支也测不出来：
            // 原用例用中心对称的立方体 + 零偏移 + 零旋转，镜像前后 bbox 完全相同。
            // 这里用 Extrude（Z 跨 [0, Height]，本身就不对称）+ RotateY 把 Z 的不对称映射进 X。
            var spec = SolidGen.Random(9, CoordTier.Normal) with
            {
                Kind = PrimitiveKind.Extrude,
                Profile = ProfileKind.Rectangle,
                SizeX = 10,
                SizeY = 20,
                Height = 30,
                OffsetX = 0,
                OffsetY = 0,
                OffsetZ = 0,
                RotateX = 0,
                RotateY = 30,
                RotateZ = 0,
                Scale = 1,
                Mirror = false,
            };

            var plain = SolidGen.Materialize(spec);
            var mirrored = SolidGen.Materialize(spec with { Mirror = true });

            Assert.That(GeometryAssert.SignedVolume(mirrored), Is.GreaterThan(0),
                "镜像后法线必须仍朝外（库内 Transform 会翻绕序补偿）");
            Assert.That(GeometryAssert.SignedVolume(mirrored),
                Is.EqualTo(GeometryAssert.SignedVolume(plain)).Within(1e-9),
                "镜像不改变体积数值");

            // 镜像**确实发生**：非对称夹具的 bbox 沿 X 按 x→−x 翻转
            var (plainMin, plainMax) = GeometryAssert.Bounds(plain);
            var (mirrorMin, mirrorMax) = GeometryAssert.Bounds(mirrored);

            Assert.That(mirrorMin.X, Is.EqualTo(-plainMax.X).Within(1e-9),
                "bbox 的 X 未按 x→−x 翻转 —— Mirror 分支被漏掉也测不出来");
            Assert.That(mirrorMax.X, Is.EqualTo(-plainMin.X).Within(1e-9),
                "bbox 的 X 未按 x→−x 翻转");
            Assert.That(mirrorMin.Y, Is.EqualTo(plainMin.Y).Within(1e-9), "镜像不应影响 Y");
            Assert.That(mirrorMax.Z, Is.EqualTo(plainMax.Z).Within(1e-9), "镜像不应影响 Z");
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
                    "SectorAngle 非零角已在 Plan C/A2 归一修复；生成器仍只采样 360（如需采样扇角为后续增强）");
                Assert.That(spec.RadiusEnd, Is.GreaterThan(0));
            }
        }
    }
}
