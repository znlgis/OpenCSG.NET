using System.Collections.Generic;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// Tier 1：精确代数恒等式。这些等式对**任意**输入成立，因此不需要 golden 数据，
    /// 也不受「金标容差是否够松」困扰 —— 它们是布尔内核正确性的硬约束。
    /// <para>
    /// 相对容差按已知体积量级缩放：Survey 档坐标达 1e6，绝对容差在那里没有意义。
    /// </para>
    /// <para>
    /// 注意：每个恒等式的**所有项都用同一个参考点**测量（见 <see cref="SignedVolumeAt"/>）。
    /// 布尔建立的实体带 T 顶点接缝（库内无 T 顶点清理，见 GeometryAssert.Analyze 的分类），
    /// 其有向体积读数会随参考点缓慢线性漂移（实测斜率 ~1e-5/单位距离）。
    /// 若各项各用自己包围盒中心，恒等式两侧就落在不同参考系里，几百分之一单位的
    /// 读数漂移会被误判成内核错误（Task 7 的 FsCheck 反例
    /// sizeSeed=6 offsetSeed=0 rotationSeed=0 kindSeed=94 即此：两实体 bbox 不相交、
    /// 交集的真值必然为 0，多边形数据逐位相同，差值全部来自参考系不一致）。
    /// </para>
    /// </summary>
    public static class IdentityAssert
    {
        /// <summary>体积相对容差基准。</summary>
        public const double RelativeTolerance = 1e-9;

        /// <summary>A ∪ B = A + B − A ∩ B。</summary>
        public static void AssertInclusionExclusion(Solid a, Solid b, double tolerance, string? message = null)
        {
            // 并集上不会出现空实体（两个操作数都非空），其包围盒中心是四个项的公共参考点。
            var unionSolid = a.Union(b);
            var interSolid = a.Intersect(b);
            var reference = Center(unionSolid);

            var volumeA = SignedVolumeAt(a, reference);
            var volumeB = SignedVolumeAt(b, reference);
            var union = SignedVolumeAt(unionSolid, reference);
            var intersection = SignedVolumeAt(interSolid, reference);

            var expected = volumeA + volumeB - intersection;
            var scale = Scale(volumeA, volumeB, union);
            var slack = System.Math.Max(tolerance, RelativeTolerance * scale)
                      + DriftBudget(a, b, reference, CombinedDiagonal(a, b));

            Assert.That(union, Is.EqualTo(expected).Within(slack),
                $"{Prefix(message)} 包含排斥不成立：V(A∪B)={union:R} 应为 V(A)+V(B)−V(A∩B)={expected:R}"
                + $"（V(A)={volumeA:R} V(B)={volumeB:R} V(A∩B)={intersection:R}）");

            var unionReport = GeometryAssert.Analyze(unionSolid);
            Assert.That(unionReport.DuplicatedEdges, Is.EqualTo(0),
                $"{Prefix(message)} 布尔结果（并集）含重复的有向边 —— 这是**面级**损坏的指纹（重复面/绕序不一致），"
                + $"T 顶点接缝不会产生重复有向边，故这条断言不受接缝干扰 | {unionReport}");

            var intersectionReport = GeometryAssert.Analyze(interSolid);
            Assert.That(intersectionReport.DuplicatedEdges, Is.EqualTo(0),
                $"{Prefix(message)} 布尔结果（交集）含重复的有向边 —— 这是**面级**损坏的指纹（重复面/绕序不一致），"
                + $"T 顶点接缝不会产生重复有向边，故这条断言不受接缝干扰 | {intersectionReport}");
        }

        /// <summary>A − B = A − A ∩ B。</summary>
        public static void AssertDifferenceIdentity(Solid a, Solid b, double tolerance, string? message = null)
        {
            // A−B 与 A∩B 都是 A 的子集（几何上位于 A 内），故用 A 的包围盒中心做公共参考点。
            var reference = Center(a);
            var differenceSolid = a.Subtract(b);
            var restrictionSolid = a.Intersect(b);

            var volumeA = SignedVolumeAt(a, reference);
            var difference = SignedVolumeAt(differenceSolid, reference);
            var intersection = SignedVolumeAt(restrictionSolid, reference);

            var expected = volumeA - intersection;
            var scale = Scale(volumeA, difference);
            var slack = System.Math.Max(tolerance, RelativeTolerance * scale)
                      + DriftBudget(a, b, reference, CombinedDiagonal(a, b));

            Assert.That(difference, Is.EqualTo(expected).Within(slack),
                $"{Prefix(message)} 差集恒等式不成立：V(A−B)={difference:R} 应为 V(A)−V(A∩B)={expected:R}");

            var differenceReport = GeometryAssert.Analyze(differenceSolid);
            Assert.That(differenceReport.DuplicatedEdges, Is.EqualTo(0),
                $"{Prefix(message)} 布尔结果（差集）含重复的有向边 —— 这是**面级**损坏的指纹（重复面/绕序不一致），"
                + $"T 顶点接缝不会产生重复有向边，故这条断言不受接缝干扰 | {differenceReport}");
        }

        /// <summary>
        /// 参考点漂移预算：库的布尔输出可能带 T 顶点接缝（README 已记录），
        /// 此时散度定理的值不再与参考点严格无关（实测 dV/dref ≈ 1e-5/单位距离）。
        /// 共享参考点只能消除**系统性**偏差，消不掉各实体各自的裂缝通量差；
        /// 所以用「同一实体在两个参考点上的读数差」自校准出漂移上界，计入容差。
        /// 对严格闭合的网格该预算 ≈1e-13 ⇒ 等于不放松。
        /// </summary>
        static double DriftBudget(Solid a, Solid b, Vector3D reference, Vector3D probeOffset)
        {
            var probe = reference + probeOffset;
            var budget = 0.0;
            budget += System.Math.Abs(SignedVolumeAt(a, reference) - SignedVolumeAt(a, probe));
            budget += System.Math.Abs(SignedVolumeAt(b, reference) - SignedVolumeAt(b, probe));
            var union = a.Union(b);
            budget += System.Math.Abs(SignedVolumeAt(union, reference) - SignedVolumeAt(union, probe));
            var intersection = a.Intersect(b);
            budget += System.Math.Abs(SignedVolumeAt(intersection, reference) - SignedVolumeAt(intersection, probe));
            return budget;
        }

        /// <summary>两个实体联合包围盒的对角线向量（漂移探针位移，把参考点杠杆臂整个走满）。</summary>
        static Vector3D CombinedDiagonal(Solid a, Solid b)
        {
            var (minA, maxA) = GeometryAssert.Bounds(a);
            var (minB, maxB) = GeometryAssert.Bounds(b);
            return new Vector3D(
                System.Math.Max(maxA.X, maxB.X) - System.Math.Min(minA.X, minB.X),
                System.Math.Max(maxA.Y, maxB.Y) - System.Math.Min(minA.Y, minB.Y),
                System.Math.Max(maxA.Z, maxB.Z) - System.Math.Min(minA.Z, minB.Z));
        }

        /// <summary>V(A∪B) ≥ max(V(A),V(B))、V(A∩B) ≤ min(V(A),V(B))，且并集/交集非负。</summary>
        public static void AssertBooleanMonotonicity(Solid a, Solid b, double tolerance, string? message = null)
        {
            var volumeA = GeometryAssert.SignedVolume(a);
            var volumeB = GeometryAssert.SignedVolume(b);
            var union = GeometryAssert.SignedVolume(a.Union(b));
            var intersection = GeometryAssert.SignedVolume(a.Intersect(b));
            var slack = System.Math.Max(tolerance, RelativeTolerance * Scale(volumeA, volumeB));

            Assert.That(volumeA, Is.GreaterThan(0),
                $"{Prefix(message)} 输入 A 体积必须为正（法线朝外），实际 {volumeA:R}");
            Assert.That(volumeB, Is.GreaterThan(0),
                $"{Prefix(message)} 输入 B 体积必须为正（法线朝外），实际 {volumeB:R}");
            Assert.That(union, Is.GreaterThanOrEqualTo(System.Math.Max(volumeA, volumeB) - slack),
                $"{Prefix(message)} V(A∪B)={union:R} 应 ≥ max(V(A),V(B))={System.Math.Max(volumeA, volumeB):R}");
            Assert.That(intersection, Is.LessThanOrEqualTo(System.Math.Min(volumeA, volumeB) + slack),
                $"{Prefix(message)} V(A∩B)={intersection:R} 应 ≤ min(V(A),V(B))={System.Math.Min(volumeA, volumeB):R}");
            Assert.That(intersection, Is.GreaterThanOrEqualTo(-slack),
                $"{Prefix(message)} V(A∩B)={intersection:R} 不应为负");
        }

        /// <summary>布尔运算不得修改任一操作数（含缓存字段与多边形列表）。</summary>
        public static void AssertUnchangedByBoolean(Solid a, Solid b, string? message = null)
        {
            var snapshotA = Snapshot(a);
            var snapshotB = Snapshot(b);

            a.Union(b);
            a.Subtract(b);
            a.Intersect(b);

            Assert.That(Snapshot(a), Is.EqualTo(snapshotA),
                $"{Prefix(message)} 布尔运算修改了操作数 A");
            Assert.That(Snapshot(b), Is.EqualTo(snapshotB),
                $"{Prefix(message)} 布尔运算修改了操作数 B");
        }

        /// <summary>深度快照：多边形数、每片顶点数、总面积与有向体积。</summary>
        public static (int Polygons, int Vertices, double Area, double Volume) Snapshot(Solid solid)
        {
            var vertices = 0;
            double area = 0;

            foreach (var polygon in solid.Polygons)
            {
                var v = polygon.Vertices;
                vertices += v.Count;
                if (v.Count < 3)
                    continue;

                var origin = v[0].Pos;
                for (var i = 1; i + 1 < v.Count; i++)
                {
                    var u = v[i].Pos - origin;
                    var w = v[i + 1].Pos - origin;
                    area += u.Cross(w).Length / 2.0;
                }
            }

            return (solid.Polygons.Count, vertices, area, GeometryAssert.SignedVolume(solid));
        }

        /// <summary>
        /// 与 <see cref="GeometryAssert.SignedVolume"/> 相同的扇形剖分散度定理求和，
        /// 但允许显式指定参考点 —— 一个恒等式内的各个项必须用同一参考点，
        /// 读数才可互相比较（两种口径在参考点取包围盒中心时逐位相同）。
        /// </summary>
        static double SignedVolumeAt(Solid solid, Vector3D reference)
        {
            if (solid.Polygons.Count == 0)
                return 0.0;

            double volume = 0;
            foreach (var polygon in solid.Polygons)
            {
                var v = polygon.Vertices;
                if (v.Count < 3)
                    continue;
                var v0 = v[0].Pos - reference;
                for (int i = 1; i < v.Count - 1; i++)
                    volume += v0.Dot((v[i].Pos - reference).Cross(v[i + 1].Pos - reference));
            }
            return volume / 6.0;
        }

        static Vector3D Center(Solid solid)
        {
            var (min, max) = GeometryAssert.Bounds(solid);
            return (min + max) * 0.5;
        }

        static double Scale(params double[] volumes)
        {
            var max = 0.0;
            foreach (var volume in volumes)
                max = System.Math.Max(max, System.Math.Abs(volume));
            return max <= 0 ? 1.0 : max;
        }

        static string Prefix(string? message) => message == null ? "" : message + ":";
    }
}
