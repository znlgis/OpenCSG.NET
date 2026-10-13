using System.Collections.Generic;
using Csg;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    [TestFixture]
    public class CorpusTest
    {
        [Test]
        public void SmokeSeeds_AreStableAndUnique()
        {
            var seeds = Corpus.SmokeSeeds();

            Assert.That(seeds.Count, Is.EqualTo(Corpus.SmokeCaseCount));
            Assert.That(new HashSet<long>(seeds).Count, Is.EqualTo(seeds.Count), "种子必须互异");
            Assert.That(Corpus.SmokeSeeds(), Is.EqualTo(seeds), "两次调用必须一致");
        }

        [Test]
        public void CorpusSpecs_AreLoadedFromCommittedJson()
        {
            var specs = Corpus.CorpusSpecs();

            Assert.That(specs.Count, Is.EqualTo(Corpus.CorpusCaseCount),
                "corpus.json 必须已由 tools/OpenCSG.NET.DataGen --write-corpus 生成并提交");
        }

        [Test]
        public void CorpusSpecs_AreAllMaterializableAndOutwardFacing()
        {
            foreach (var spec in Corpus.CorpusSpecs())
            {
                var solid = SolidGen.Materialize(spec);

                Assert.That(solid.Polygons.Count, Is.GreaterThan(0), "语料不得含空实体");
                Assert.That(GeometryAssert.SignedVolume(solid), Is.GreaterThan(0),
                    "语料必须全部法线朝外");
            }
        }

        [Test]
        public void CorpusSpecs_CoverBothCoordTiersAndAllPrimitiveKinds()
        {
            var kinds = new HashSet<PrimitiveKind>();
            var tiers = new HashSet<CoordTier>();

            foreach (var spec in Corpus.CorpusSpecs())
            {
                kinds.Add(spec.Kind);
                tiers.Add(spec.OffsetX != 0 || spec.OffsetY != 0 || spec.OffsetZ != 0
                    ? CoordTier.Survey
                    : CoordTier.Normal);
            }

            Assert.That(kinds.Count, Is.EqualTo(6), "语料必须覆盖全部 6 种基本体");
            Assert.That(tiers.Count, Is.EqualTo(2), "语料必须同时覆盖常规档与测绘档");
        }

        [Test]
        public void LongCaseCount_DefaultsTo5000AndIsOverridable()
        {
            var previous = System.Environment.GetEnvironmentVariable("OPENCSG_FUZZ_CASES");
            try
            {
                System.Environment.SetEnvironmentVariable("OPENCSG_FUZZ_CASES", null);
                Assert.That(Corpus.LongCaseCount(), Is.EqualTo(5000));

                System.Environment.SetEnvironmentVariable("OPENCSG_FUZZ_CASES", "123");
                Assert.That(Corpus.LongCaseCount(), Is.EqualTo(123));

                Assert.That(Corpus.LongSeed(0), Is.Not.EqualTo(Corpus.LongSeed(1)));
            }
            finally
            {
                System.Environment.SetEnvironmentVariable("OPENCSG_FUZZ_CASES", previous);
            }
        }
    }
}
