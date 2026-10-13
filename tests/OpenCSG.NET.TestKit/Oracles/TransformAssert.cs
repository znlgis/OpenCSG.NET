using System;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// Tier 1 续：变换等价、包围盒代数、镜像对合、同形判定。
    /// </summary>
    public static class TransformAssert
    {
        /// <summary>bbox(A∪B) = bbox(A) ∪ bbox(B)；bbox(A∩B) ⊆ bbox(A) ∩ bbox(B)。</summary>
        public static void AssertBoundingBoxAlgebra(Solid a, Solid b, double tolerance, string? message = null)
        {
            var (minA, maxA) = GeometryAssert.Bounds(a);
            var (minB, maxB) = GeometryAssert.Bounds(b);

            if (a.Polygons.Count == 0 || b.Polygons.Count == 0)
                return;   // 空实体的包围盒是 (0,0,0)-(0,0,0)，代数不适用

            var (minU, maxU) = GeometryAssert.Bounds(a.Union(b));
            var (minI, maxI) = GeometryAssert.Bounds(a.Intersect(b));

            var slackX = tolerance * Math.Max(1.0, Math.Max(Math.Abs(minA.X), Math.Abs(maxB.X)));
            var slackY = tolerance * Math.Max(1.0, Math.Max(Math.Abs(minA.Y), Math.Abs(maxB.Y)));
            var slackZ = tolerance * Math.Max(1.0, Math.Max(Math.Abs(minA.Z), Math.Abs(maxB.Z)));

            Assert.That(minU.X, Is.EqualTo(Math.Min(minA.X, minB.X)).Within(slackX), $"{Prefix(message)} ∪.Min.X");
            Assert.That(minU.Y, Is.EqualTo(Math.Min(minA.Y, minB.Y)).Within(slackY), $"{Prefix(message)} ∪.Min.Y");
            Assert.That(minU.Z, Is.EqualTo(Math.Min(minA.Z, minB.Z)).Within(slackZ), $"{Prefix(message)} ∪.Min.Z");
            Assert.That(maxU.X, Is.EqualTo(Math.Max(maxA.X, maxB.X)).Within(slackX), $"{Prefix(message)} ∪.Max.X");
            Assert.That(maxU.Y, Is.EqualTo(Math.Max(maxA.Y, maxB.Y)).Within(slackY), $"{Prefix(message)} ∪.Max.Y");
            Assert.That(maxU.Z, Is.EqualTo(Math.Max(maxA.Z, maxB.Z)).Within(slackZ), $"{Prefix(message)} ∪.Max.Z");

            // 交集必然落在两个包围盒的交内；空交集（零多边形）跳过
            if (a.Intersect(b).Polygons.Count == 0)
                return;

            Assert.That(minI.X, Is.GreaterThanOrEqualTo(Math.Max(minA.X, minB.X) - slackX), $"{Prefix(message)} ∩.Min.X");
            Assert.That(minI.Y, Is.GreaterThanOrEqualTo(Math.Max(minA.Y, minB.Y) - slackY), $"{Prefix(message)} ∩.Min.Y");
            Assert.That(minI.Z, Is.GreaterThanOrEqualTo(Math.Max(minA.Z, minB.Z) - slackZ), $"{Prefix(message)} ∩.Min.Z");
            Assert.That(maxI.X, Is.LessThanOrEqualTo(Math.Min(maxA.X, maxB.X) + slackX), $"{Prefix(message)} ∩.Max.X");
            Assert.That(maxI.Y, Is.LessThanOrEqualTo(Math.Min(maxA.Y, maxB.Y) + slackY), $"{Prefix(message)} ∩.Max.Y");
            Assert.That(maxI.Z, Is.LessThanOrEqualTo(Math.Min(maxA.Z, maxB.Z) + slackZ), $"{Prefix(message)} ∩.Max.Z");
        }

        /// <summary>V(k·A) = k³V(A)；V(旋转 A) = V(A)；V(镜像 A) = V(A) 且仍朝外；V(平移 A) = V(A) 且 bbox 精确平移。</summary>
        public static void AssertTransformEquivariance(Solid a, double tolerance, string? message = null)
        {
            var prefix = Prefix(message);
            var volume = GeometryAssert.SignedVolume(a);
            var (min, max) = GeometryAssert.Bounds(a);
            var slack = Math.Max(tolerance, 1e-9 * Math.Max(1.0, volume));

            // 均匀缩放：体积按 k³ 缩放
            const double k = 2.5;
            var scaled = a.Scale(k);
            Assert.That(GeometryAssert.SignedVolume(scaled),
                Is.EqualTo(volume * k * k * k).Within(slack * k * k * k),
                $"{prefix} V(k·A) 应为 k³V(A)：实际 {GeometryAssert.SignedVolume(scaled):R}，期望 {volume * k * k * k:R}");

            // 旋转：体积不变，且仍朝外（正值）
            var rotated = a.RotateX(37.0).RotateY(-23.0).RotateZ(91.0);
            Assert.That(GeometryAssert.SignedVolume(rotated),
                Is.EqualTo(volume).Within(slack),
                $"{prefix} 旋转不改变体积：实际 {GeometryAssert.SignedVolume(rotated):R}，期望 {volume:R}");

            // 镜像：体积数值不变、符号仍为正
            var mirrored = a.Scale(-1, 1, 1);
            var mirroredVolume = GeometryAssert.SignedVolume(mirrored);
            Assert.That(mirroredVolume, Is.EqualTo(volume).Within(slack),
                $"{prefix} 镜像不改变体积数值：实际 {mirroredVolume:R}，期望 {volume:R}");
            Assert.That(mirroredVolume, Is.GreaterThan(0),
                $"{prefix} 镜像后必须仍朝外，实际体积 {mirroredVolume:R}（负值说明绕序未翻回）");

            // 平移：体积不变，bbox 精确平移
            const double tx = 12.5, ty = -7.25, tz = 3.125;
            var translated = a.Translate(tx, ty, tz);
            Assert.That(GeometryAssert.SignedVolume(translated),
                Is.EqualTo(volume).Within(slack),
                $"{prefix} 平移不改变体积");

            var (tmin, tmax) = GeometryAssert.Bounds(translated);
            var shiftSlack = tolerance * Math.Max(1.0, Math.Max(Math.Abs(min.X), Math.Abs(tx)));
            Assert.That(tmin.X, Is.EqualTo(min.X + tx).Within(shiftSlack), $"{prefix} 平移后 bbox.Min.X");
            Assert.That(tmax.Y, Is.EqualTo(max.Y + ty).Within(tolerance * Math.Max(1.0, Math.Abs(ty))), $"{prefix} 平移后 bbox.Max.Y");
            Assert.That(tmin.Z, Is.EqualTo(min.Z + tz).Within(tolerance * Math.Max(1.0, Math.Abs(tz))), $"{prefix} 平移后 bbox.Min.Z");
        }

        /// <summary>镜像两次回到原形（顶点、面积、体积、包围盒全部还原）。</summary>
        public static void AssertMirrorIsInvolution(Solid a, string? message = null)
        {
            var twice = a.Scale(-1, 1, 1).Scale(-1, 1, 1);

            AssertSameShape(a, twice, 0.0, Prefix(message) + " 镜像对合");
        }

        /// <summary>同形判定：多边形数、顶点数、总面积、有向体积、包围盒全部一致。</summary>
        public static void AssertSameShape(Solid expected, Solid actual, double tolerance, string? message = null)
        {
            var prefix = Prefix(message);
            var slack = Math.Max(tolerance, 0.0);

            var expectedSnapshot = IdentityAssert.Snapshot(expected);
            var actualSnapshot = IdentityAssert.Snapshot(actual);

            Assert.That(actualSnapshot.Polygons, Is.EqualTo(expectedSnapshot.Polygons),
                $"{prefix} 多边形数不同：{actualSnapshot.Polygons} vs {expectedSnapshot.Polygons}");
            Assert.That(actualSnapshot.Vertices, Is.EqualTo(expectedSnapshot.Vertices),
                $"{prefix} 顶点数不同：{actualSnapshot.Vertices} vs {expectedSnapshot.Vertices}");
            Assert.That(actualSnapshot.Area,
                Is.EqualTo(expectedSnapshot.Area).Within(Math.Max(slack, 1e-9 * Math.Max(1.0, expectedSnapshot.Area))),
                $"{prefix} 总面积不同");
            Assert.That(actualSnapshot.Volume,
                Is.EqualTo(expectedSnapshot.Volume).Within(Math.Max(slack, 1e-9 * Math.Max(1.0, Math.Abs(expectedSnapshot.Volume)))),
                $"{prefix} 有向体积不同");

            var (expMin, expMax) = GeometryAssert.Bounds(expected);
            var (actMin, actMax) = GeometryAssert.Bounds(actual);
            var boxSlack = Math.Max(slack, 1e-9 * Math.Max(1.0, Math.Max(Math.Abs(expMin.X), Math.Abs(expMax.X))));

            Assert.That(actMin.X, Is.EqualTo(expMin.X).Within(boxSlack), $"{prefix} bbox.Min.X");
            Assert.That(actMin.Y, Is.EqualTo(expMin.Y).Within(boxSlack), $"{prefix} bbox.Min.Y");
            Assert.That(actMin.Z, Is.EqualTo(expMin.Z).Within(boxSlack), $"{prefix} bbox.Min.Z");
            Assert.That(actMax.X, Is.EqualTo(expMax.X).Within(boxSlack), $"{prefix} bbox.Max.X");
            Assert.That(actMax.Y, Is.EqualTo(expMax.Y).Within(boxSlack), $"{prefix} bbox.Max.Y");
            Assert.That(actMax.Z, Is.EqualTo(expMax.Z).Within(boxSlack), $"{prefix} bbox.Max.Z");
        }

        /// <summary>多边形面积（按首顶点扇形剖分）。</summary>
        public static double PolygonArea(Polygon polygon)
        {
            var v = polygon.Vertices;
            if (v.Count < 3)
                return 0.0;

            var origin = v[0].Pos;
            var area = 0.0;
            for (var i = 1; i + 1 < v.Count; i++)
            {
                var u = v[i].Pos - origin;
                var w = v[i + 1].Pos - origin;
                area += u.Cross(w).Length / 2.0;
            }
            return area;
        }

        static string Prefix(string? message) => message == null ? "" : message + ":";
    }
}
