using System;
using System.Collections.Generic;
using Csg;

namespace Csg.Test
{
    /// <summary>
    /// CSG 节点树生成器：种子 → <see cref="CsgNode"/> 树。
    /// 复现凭据是「种子 + <see cref="CsgSerialization.ToJson(CsgNode, bool)"/> 的输出」，
    /// 因此不走 FsCheck 收缩（树不是可收缩的连续参数空间）。
    /// </summary>
    public static class NodeGen
    {
        /// <summary>默认最大嵌套深度。深度越大布尔树越复杂，也越容易触发退化情形。</summary>
        public const int DefaultMaxDepth = 4;

        public static CsgNode Random(long seed, int maxDepth = DefaultMaxDepth)
        {
            var rng = new SplitMix64((ulong)seed ^ 0x5DEECE66DUL);
            return Build(rng, maxDepth);
        }

        static CsgNode Build(SplitMix64 rng, int depth)
        {
            // 到深度上限后只产出叶子
            if (depth <= 1)
                return Primitive(rng);

            var roll = rng.NextInt(10);
            switch (roll)
            {
                case 0:
                case 1:
                case 2:
                    return Bool(rng, depth, (children) => CsgNodes.Union(children.ToArray()));
                case 3:
                case 4:
                    return Bool(rng, depth, (children) => CsgNodes.Subtract(children.ToArray()));
                case 5:
                    return Bool(rng, depth, (children) => CsgNodes.Intersect(children.ToArray()));
                case 6:
                case 7:
                    // 平移 + 绕全局原点按 X→Y→Z 旋转（度）
                    return CsgNodes.Transform(
                        new Vector3D(rng.NextSigned() * 10.0, rng.NextSigned() * 10.0, rng.NextSigned() * 10.0),
                        new Vector3D(rng.NextSigned() * 180.0, rng.NextSigned() * 180.0, rng.NextSigned() * 180.0),
                        Build(rng, depth - 1));
                default:
                    return Primitive(rng);
            }
        }

        /// <summary>布尔节点的子节点数固定为 2..4（求值器要求至少 1 个，1 个时退化为恒等）。</summary>
        static CsgNode Bool(SplitMix64 rng, int depth, Func<List<CsgNode>, CsgNode> make)
        {
            var count = 2 + rng.NextInt(3);
            var children = new List<CsgNode>(count);
            for (var i = 0; i < count; i++)
                children.Add(Build(rng, depth - 1));
            return make(children);
        }

        static CsgNode Primitive(SplitMix64 rng)
        {
            var extent = 1.0 + rng.NextDouble() * 10.0;
            var center = new Vector3D(
                rng.NextSigned() * extent, rng.NextSigned() * extent, rng.NextSigned() * extent);

            switch (rng.NextInt(6))
            {
                case 0:
                    return CsgNodes.Box(center, new Vector3D(
                        extent, 1.0 + rng.NextDouble() * 10.0, 1.0 + rng.NextDouble() * 10.0));

                case 1:
                    return CsgNodes.Sphere(center, 0.5 + rng.NextDouble() * 5.0);

                case 2:
                    return CsgNodes.Cylinder(center, 0.5 + rng.NextDouble() * 5.0, 1.0 + rng.NextDouble() * 10.0);

                case 3:
                    var bottom = 0.5 + rng.NextDouble() * 5.0;
                    return CsgNodes.Cone(center, bottom * (0.1 + 0.9 * rng.NextDouble()), bottom,
                        1.0 + rng.NextDouble() * 10.0);

                case 4:
                    return CsgNodes.Extrude(Profile(rng), 1.0 + rng.NextDouble() * 10.0);

                default:
                    return CsgNodes.Wedge(new Vector3D(
                        rng.NextSigned() * 5.0, rng.NextSigned() * 5.0, rng.NextSigned() * 5.0),
                        extent, 1.0 + rng.NextDouble() * 10.0, 1.0 + rng.NextDouble() * 10.0);
            }
        }

        static Profile2D Profile(SplitMix64 rng)
        {
            var w = 1.0 + rng.NextDouble() * 20.0;
            var h = 1.0 + rng.NextDouble() * 20.0;
            var thin = Math.Max(Math.Min(w, h) / 8.0, 0.05);

            switch (rng.NextInt(8))
            {
                case 0: return Profiles.Rectangle(w, h);
                case 1: return Profiles.HBeam(h, w, thin, thin);
                case 2: return Profiles.Channel(h, w, thin, thin);
                case 3: return Profiles.SquareTube(w, h, thin);
                case 4: return Profiles.Trapezoid(w / 2, w, h);
                case 5: return Profiles.Capsule(w, Math.Max(h / 2, 0.05));
                case 6: return Profiles.LShape(h, w, Math.Max(Math.Min(w, h) / 4, 0.05));
                default:
                    return new PolygonProfile(new[]
                    {
                        new Vector2D(0, 0),
                        new Vector2D(w, 0),
                        new Vector2D(w, h),
                        new Vector2D(w / 2, h * 1.5),
                        new Vector2D(0, h),
                    });
            }
        }
    }
}
