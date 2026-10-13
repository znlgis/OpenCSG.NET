using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Csg.Test
{
    /// <summary>
    /// 三档语料入口。
    /// Smoke（64 固定种子）每次运行都跑；Corpus（512 个已提交的 spec）供 CI / nightly；
    /// Long 由环境变量 <c>OPENCSG_FUZZ_CASES</c> 驱动（缺省 5000），只在 nightly 跑。
    /// <para>
    /// corpus.json 随 TestKit 复制到输出目录，用程序集自身位置定位 ——
    /// 刻意避开 SolidTest/GeometryAssert 系列里那种 4 层 GetDirectoryName 反推，
    /// 后者在自定义输出路径下会静默失效。
    /// </para>
    /// </summary>
    public static class Corpus
    {
        public const int SmokeCaseCount = 64;
        public const int CorpusCaseCount = 512;
        public const int DefaultLongCaseCount = 5000;

        const string EnvVarName = "OPENCSG_FUZZ_CASES";
        const string CorpusFileName = "corpus.json";

        static IReadOnlyList<long>? _smokeSeeds;
        static IReadOnlyList<SolidSpec>? _corpusSpecs;

        /// <summary>Smoke 档的 64 个固定种子（确定性、跨运行稳定）。</summary>
        public static IReadOnlyList<long> SmokeSeeds()
        {
            if (_smokeSeeds != null)
                return _smokeSeeds;

            var seeds = new long[SmokeCaseCount];
            for (var i = 0; i < SmokeCaseCount; i++)
            {
                // 常规档前 40 个、测绘档后 24 个 —— 两个档位都在 Smoke 里有代表
                var tier = i < 40 ? CoordTier.Normal : CoordTier.Survey;
                seeds[i] = DeterministicSeed(i, tier);
            }
            _smokeSeeds = seeds;
            return _smokeSeeds;
        }

        /// <summary>Corpus 档的 512 个 spec（从已提交的 corpus.json 读取）。</summary>
        public static IReadOnlyList<SolidSpec> CorpusSpecs()
        {
            if (_corpusSpecs != null)
                return _corpusSpecs;

            var path = Path.Combine(
                Path.GetDirectoryName(typeof(Corpus).Assembly.Location) ?? ".",
                CorpusFileName);

            if (!File.Exists(path))
                throw new FileNotFoundException(
                    $"找不到语料文件 {CorpusFileName}。运行 "
                    + $"'dotnet run --project tools/OpenCSG.NET.DataGen -- --write-corpus' 生成并提交它。",
                    path);

            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);

            var specs = new List<SolidSpec>(CorpusCaseCount);
            foreach (var element in document.RootElement.GetProperty("specs").EnumerateArray())
                specs.Add(ReadSpec(element));

            _corpusSpecs = specs;
            return _corpusSpecs;
        }

        /// <summary>Long 档的用例数（环境变量可覆盖）。</summary>
        public static int LongCaseCount()
        {
            var raw = Environment.GetEnvironmentVariable(EnvVarName);
            if (!string.IsNullOrEmpty(raw)
                && int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                                System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                && parsed > 0)
                return parsed;

            return DefaultLongCaseCount;
        }

        /// <summary>Long 档第 index 个种子（与 Smoke/Corpus 不相交）。</summary>
        public static long LongSeed(long index) => DeterministicSeed(index, CoordTier.Normal) + 0x1000000000L;

        /// <summary>
        /// Corpus 档第 index 条 spec 的生成种子。仅由 <c>tools/OpenCSG.NET.DataGen</c> 用于**重建**
        /// corpus.json；测试侧一律以提交的 JSON 为准（这样重建之外没人能悄悄改变语料）。
        /// </summary>
        public static long CorpusSeed(int index)
            => DeterministicSeed(index,
                   index < CorpusCaseCount * 5 / 8 ? CoordTier.Normal : CoordTier.Survey) + 0x2000000000L;

        /// <summary>第 index 个种子所对应的档位（Smoke 的种子与之保持一致）。</summary>
        public static CoordTier TierOf(int index) => index < 40 ? CoordTier.Normal : CoordTier.Survey;

        static long DeterministicSeed(long index, CoordTier tier)
        {
            // 固定常数混合，保证种子列表本身不随实现细节漂移
            var mixed = (ulong)index * 0x9E3779B97F4A7C15UL + (ulong)tier * 0xD1B54A32D192ED03UL;
            return (long)(mixed & 0x7FFFFFFFFFFFFFFFL);
        }

        internal static SolidSpec ReadSpec(JsonElement element) => new SolidSpec
        {
            Kind = (PrimitiveKind)element.GetProperty("kind").GetInt32(),
            SizeX = element.GetProperty("sizeX").GetDouble(),
            SizeY = element.GetProperty("sizeY").GetDouble(),
            SizeZ = element.GetProperty("sizeZ").GetDouble(),
            Radius = element.GetProperty("radius").GetDouble(),
            RadiusEnd = element.GetProperty("radiusEnd").GetDouble(),
            Height = element.GetProperty("height").GetDouble(),
            Resolution = element.GetProperty("resolution").GetInt32(),
            SectorAngle = element.GetProperty("sectorAngle").GetDouble(),
            OffsetX = element.GetProperty("offsetX").GetDouble(),
            OffsetY = element.GetProperty("offsetY").GetDouble(),
            OffsetZ = element.GetProperty("offsetZ").GetDouble(),
            RotateX = element.GetProperty("rotateX").GetDouble(),
            RotateY = element.GetProperty("rotateY").GetDouble(),
            RotateZ = element.GetProperty("rotateZ").GetDouble(),
            Scale = element.GetProperty("scale").GetDouble(),
            Mirror = element.GetProperty("mirror").GetBoolean(),
            Profile = (ProfileKind)element.GetProperty("profile").GetInt32(),
        };
    }
}
