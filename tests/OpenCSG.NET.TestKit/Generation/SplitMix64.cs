using System;

namespace Csg.Test
{
    /// <summary>
    /// SplitMix64 确定性伪随机数发生器（零依赖、跨运行时逐位可复现）。
    /// 刻意不引入 System.Random：它的算法在不同 .NET 版本间不保证一致，
    /// 而语料的可复现性依赖「同一种子在同一实现下永远产出同一结果」。
    /// </summary>
    internal sealed class SplitMix64
    {
        ulong _state;

        public SplitMix64(ulong seed) => _state = seed;

        public ulong NextUInt64()
        {
            _state += 0x9E3779B97F4A7C15UL;
            var z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>[0,1) 均匀。</summary>
        public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

        /// <summary>[0, exclusiveMax) 整数。</summary>
        public int NextInt(int exclusiveMax) => (int)(NextUInt64() % (ulong)exclusiveMax);

        /// <summary>[min,max] 对数均匀（覆盖跨数量级的尺寸）。</summary>
        public double NextLogUniform(double min, double max)
            => Math.Exp(Math.Log(min) + NextDouble() * (Math.Log(max) - Math.Log(min)));

        /// <summary>[-1,1]。</summary>
        public double NextSigned() => NextDouble() * 2.0 - 1.0;
    }
}
