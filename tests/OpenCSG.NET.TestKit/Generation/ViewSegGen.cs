using System;
using System.Collections.Generic;
using Csg;

namespace Csg.Test
{
    /// <summary>
    /// 2D 线段集生成器：为 <see cref="ViewArcFitter"/> 与 <see cref="ViewSegmentCleanup"/>
    /// 提供输入。刻意构造四类结构 —— 直链、圆采样折线、重叠共线簇、短残段 ——
    /// 因为这正是这两个函数的四个分支来源（成链 → 拟合；共线 → 合并；过短 → 丢弃）。
    /// </summary>
    public static class ViewSegGen
    {
        public static IReadOnlyList<ViewSegment> Random(long seed, int maxSegments = 40)
        {
            var rng = new SplitMix64((ulong)seed ^ 0xC0FFEEUL);
            var result = new List<ViewSegment>(maxSegments);

            while (result.Count < maxSegments)
            {
                var remaining = maxSegments - result.Count;
                var block = Math.Min(remaining, 1 + rng.NextInt(12));
                if (block <= 1)
                    break;

                var visible = rng.NextInt(3) != 0;   // 约 2/3 可见
                switch (rng.NextInt(4))
                {
                    case 0: AddChain(result, rng, block, visible); break;
                    case 1: AddArcSampled(result, rng, block, visible); break;
                    case 2: AddCollinearCluster(result, rng, block, visible); break;
                    default: AddShortStubs(result, rng, Math.Min(remaining, 3), visible); break;
                }
            }

            if (result.Count == 0)
                AddChain(result, rng, 2, visible: true);

            return result;
        }

        /// <summary>把一段圆按给定角度范围等分成 count 条首尾相接的弦。</summary>
        public static IReadOnlyList<ViewSegment> ArcSample(
            ViewPoint2 center, double radius, double startDeg, double sweepDeg, int steps, bool visible)
        {
            if (steps < 1) throw new ArgumentOutOfRangeException(nameof(steps), steps, "至少 1 段");

            var segments = new List<ViewSegment>(steps);
            var startRad = startDeg * Math.PI / 180.0;
            var sweepRad = sweepDeg * Math.PI / 180.0;

            for (var i = 0; i < steps; i++)
            {
                var a = startRad + sweepRad * i / steps;
                var b = startRad + sweepRad * (i + 1) / steps;
                segments.Add(new ViewSegment(
                    OnCircle(center, radius, a),
                    OnCircle(center, radius, b),
                    visible));
            }
            return segments;
        }

        public static ViewBounds2 BoundsOf(IReadOnlyList<ViewSegment> segments)
        {
            var points = new List<ViewPoint2>(segments.Count * 2);
            foreach (var segment in segments)
            {
                points.Add(segment.Start);
                points.Add(segment.End);
            }
            return ViewBounds2.Of(points);
        }

        static ViewPoint2 OnCircle(ViewPoint2 center, double radius, double angleRad)
            => new ViewPoint2(center.X + radius * Math.Cos(angleRad),
                              center.Y + radius * Math.Sin(angleRad));

        static void AddChain(List<ViewSegment> output, SplitMix64 rng, int count, bool visible)
        {
            var x = rng.NextSigned() * 100.0;
            var y = rng.NextSigned() * 100.0;
            var angle = rng.NextDouble() * 2 * Math.PI;

            for (var i = 0; i < count; i++)
            {
                var length = 0.5 + rng.NextDouble() * 20.0;
                var nextX = x + length * Math.Cos(angle);
                var nextY = y + length * Math.Sin(angle);
                output.Add(new ViewSegment(new ViewPoint2(x, y), new ViewPoint2(nextX, nextY), visible));
                x = nextX;
                y = nextY;
                // 缓弯，保证仍是一条可拟合的弧
                angle += rng.NextSigned() * 0.25;
            }
        }

        static void AddArcSampled(List<ViewSegment> output, SplitMix64 rng, int count, bool visible)
        {
            var radius = 2.0 + rng.NextDouble() * 50.0;
            var center = new ViewPoint2(rng.NextSigned() * 50.0, rng.NextSigned() * 50.0);
            var startDeg = rng.NextDouble() * 360.0;
            var sweepDeg = 20.0 + rng.NextDouble() * 340.0;

            output.AddRange(ArcSample(center, radius, startDeg, sweepDeg, count, visible));
        }

        /// <summary>同一视线上的重叠共线段 —— 专门打 <c>ViewSegmentCleanup</c> 的合并/塌缩分支。</summary>
        static void AddCollinearCluster(List<ViewSegment> output, SplitMix64 rng, int count, bool visible)
        {
            var x0 = rng.NextSigned() * 50.0;
            var y0 = rng.NextSigned() * 50.0;
            var divisor = 2 + rng.NextInt(3);
            var dx = (rng.NextSigned() * 20.0) / divisor;
            var dy = (rng.NextSigned() * 20.0) / divisor;

            for (var i = 0; i < count; i++)
            {
                // 每段起点落在前一段内部 ⇒ 必然重叠共线
                var offset = rng.NextDouble() * 0.8;
                var a = new ViewPoint2(x0 + dx * (i + offset), y0 + dy * (i + offset));
                var b = new ViewPoint2(x0 + dx * (i + offset + 1.5), y0 + dy * (i + offset + 1.5));
                output.Add(new ViewSegment(a, b, visible));
            }
        }

        /// <summary>远短于清理阈值的残段 —— 打「丢弃过短线段」分支。</summary>
        static void AddShortStubs(List<ViewSegment> output, SplitMix64 rng, int count, bool visible)
        {
            for (var i = 0; i < count; i++)
            {
                var x = rng.NextSigned() * 50.0;
                var y = rng.NextSigned() * 50.0;
                output.Add(new ViewSegment(
                    new ViewPoint2(x, y),
                    new ViewPoint2(x + 1e-5, y + 1e-5),
                    visible));
            }
        }
    }
}
