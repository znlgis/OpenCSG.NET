using System;
using Csg;

namespace Csg.Test
{
    /// <summary>
    /// <see cref="Tessellation.ChordTolerance"/> 的作用域守卫。
    /// 该开关是 [ThreadStatic] 可变全局态 —— 忘记复位会污染同线程后续用例，
    /// 而自适应一旦打开会把每个基本体的分段数从 12/16 改成 16..128，
    /// 从而改变所有既有断言的结果。用 using 强制成对。
    /// </summary>
    public sealed class TessellationScope : IDisposable
    {
        readonly double _previous;

        public TessellationScope(double chordTolerance)
        {
            _previous = Tessellation.ChordTolerance;
            Tessellation.ChordTolerance = chordTolerance;
        }

        public void Dispose() => Tessellation.ChordTolerance = _previous;
    }
}
