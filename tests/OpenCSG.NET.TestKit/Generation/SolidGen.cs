using System;
using System.Collections.Generic;
using Csg;

namespace Csg.Test
{
    /// <summary>
    /// 实体生成器：种子 → <see cref="SolidSpec"/>（纯参数）→ <see cref="Solid"/>（物化）。
    /// 物化只用公开 API，不触碰内核内部状态。
    /// </summary>
    public static class SolidGen
    {
        static readonly PrimitiveKind[] AllKinds =
        {
            PrimitiveKind.Cube, PrimitiveKind.Sphere, PrimitiveKind.Cylinder,
            PrimitiveKind.Cone, PrimitiveKind.Extrude, PrimitiveKind.Wedge,
        };

        static readonly ProfileKind[] AllProfiles =
        {
            ProfileKind.Rectangle, ProfileKind.HBeam, ProfileKind.Channel,
            ProfileKind.SquareTube, ProfileKind.Trapezoid, ProfileKind.Capsule, ProfileKind.LShape,
        };

        static readonly int[] Resolutions = { 4, 8, 12, 24, 48 };

        /// <summary>由种子确定性生成一个 spec。</summary>
        public static SolidSpec Random(long seed, CoordTier tier)
        {
            var rng = new SplitMix64((ulong)seed);

            var (minSize, maxSize, maxOffset) = tier == CoordTier.Survey
                ? (1.0, 1e3, 1e6)
                : (1e-2, 1e3, 0.0);

            var x = rng.NextLogUniform(minSize, maxSize);
            var y = rng.NextLogUniform(minSize, maxSize);
            var z = rng.NextLogUniform(minSize, maxSize);
            var radius = rng.NextLogUniform(minSize, maxSize);
            var height = rng.NextLogUniform(minSize, maxSize);

            return new SolidSpec
            {
                Kind = AllKinds[rng.NextInt(AllKinds.Length)],
                SizeX = x,
                SizeY = y,
                SizeZ = z,
                Radius = radius,
                RadiusEnd = radius * (0.25 + 0.75 * rng.NextDouble()),
                Height = height,
                Resolution = Resolutions[rng.NextInt(Resolutions.Length)],
                // 随机采样固定 360：其余取值会静默产出空实体（Plan C 承接的已知缺陷），
                // 纳入随机采样只会制造噪音而不会增加信息。
                SectorAngle = 360.0,
                OffsetX = maxOffset == 0 ? 0 : rng.NextSigned() * maxOffset,
                OffsetY = maxOffset == 0 ? 0 : rng.NextSigned() * maxOffset,
                OffsetZ = maxOffset == 0 ? 0 : rng.NextSigned() * maxOffset,
                RotateX = rng.NextSigned() * 180.0,
                RotateY = rng.NextSigned() * 180.0,
                RotateZ = rng.NextSigned() * 180.0,
                Scale = 0.25 + 1.75 * rng.NextDouble(),
                // 约 1/8 的样本走镜像路径
                Mirror = rng.NextInt(8) == 0,
                Profile = AllProfiles[rng.NextInt(AllProfiles.Length)],
            };
        }

        /// <summary>
        /// 生成一对 spec，B 的偏移以 A 为基准加一个小扰动 —— 这样既会产生相交对
        /// （布尔恒等式有信息量），也会产生不相交对（覆盖 MayOverlap 快速路径）。
        /// </summary>
        public static (SolidSpec A, SolidSpec B) RandomPair(long seed, CoordTier tier)
        {
            var a = Random(seed, tier);
            var rng = new SplitMix64((ulong)seed ^ 0xA5A5A5A5A5A5A5A5UL);

            var scale = Math.Max(Math.Max(a.SizeX, Math.Max(a.SizeY, a.SizeZ)),
                                 Math.Max(a.Radius, a.Height));
            var reach = rng.NextInt(4) == 0
                ? 4.0 * scale          // 1/4 概率明显分离
                : 1.2 * scale;         // 其余概率可能相交

            var b = Random(seed * 31 + 7, tier) with
            {
                OffsetX = a.OffsetX + rng.NextSigned() * reach,
                OffsetY = a.OffsetY + rng.NextSigned() * reach,
                OffsetZ = a.OffsetZ + rng.NextSigned() * reach,
            };
            return (a, b);
        }

        /// <summary>把 spec 物化成实体（确定性、无随机）。</summary>
        public static Solid Materialize(SolidSpec spec)
        {
            if (spec.Scale <= 0)
                throw new ArgumentOutOfRangeException(nameof(spec), spec.Scale, "Scale 必须为正");

            var body = BuildPrimitive(spec);

            if (spec.RotateX != 0) body = body.RotateX(spec.RotateX);
            if (spec.RotateY != 0) body = body.RotateY(spec.RotateY);
            if (spec.RotateZ != 0) body = body.RotateZ(spec.RotateZ);

            if (spec.Scale != 1.0) body = body.Scale(spec.Scale);
            // 负行列式镜像：库内 Transform 会翻顶点绕序并翻法线，镜像后仍朝外
            if (spec.Mirror) body = body.Scale(-1, 1, 1);

            if (spec.OffsetX != 0 || spec.OffsetY != 0 || spec.OffsetZ != 0)
                body = body.Translate(spec.OffsetX, spec.OffsetY, spec.OffsetZ);

            return body;
        }

        static Solid BuildPrimitive(SolidSpec spec)
        {
            switch (spec.Kind)
            {
                case PrimitiveKind.Cube:
                    return Solids.Cube(new Vector3D(spec.SizeX, spec.SizeY, spec.SizeZ), center: true);

                case PrimitiveKind.Sphere:
                    return Solids.Sphere(new SphereOptions
                    {
                        Center = new Vector3D(0, 0, 0),
                        Radius = spec.Radius,
                        Resolution = spec.Resolution,
                    });

                case PrimitiveKind.Cylinder:
                    // 轴向沿 ±Y，Center 为轴线中点
                    return Solids.Cylinder(new CylinderOptions
                    {
                        Start = new Vector3D(0, -spec.Height / 2, 0),
                        End = new Vector3D(0, spec.Height / 2, 0),
                        RadiusStart = spec.Radius,
                        RadiusEnd = spec.Radius,
                        SectorAngle = spec.SectorAngle,
                        Resolution = spec.Resolution,
                    });

                case PrimitiveKind.Cone:
                    return CsgEvaluator.Evaluate(CsgNodes.Cone(
                        new Vector3D(0, 0, 0), spec.RadiusEnd, spec.Radius, spec.Height));

                case PrimitiveKind.Extrude:
                    if (spec.Profile == ProfileKind.SquareTube)
                        return BuildSquareTube(spec);
                    return CsgEvaluator.Evaluate(CsgNodes.Extrude(
                        BuildProfile(spec.Profile, spec), spec.Height));

                case PrimitiveKind.Wedge:
                    return CsgEvaluator.Evaluate(CsgNodes.Wedge(
                        new Vector3D(0, 0, 0), spec.SizeX, spec.SizeY, spec.SizeZ));

                default:
                    throw new ArgumentOutOfRangeException(nameof(spec), spec.Kind, "未知基本体种类");
            }
        }

        /// <summary>
        /// 方管必须手工用「外轮廓 − 内轮廓」构造，不能走 <see cref="Profiles.SquareTube"/>：
        /// 该工厂的 Thickness 参数被库内的 ExpandProfile 忽略（只产出外轮廓），
        /// 结果与同尺寸的 <see cref="Profiles.Rectangle"/> 展开成完全相同的 4 个点 ——
        /// 那样 7 种截面实际只有 6 种，而语料会按 Profile 打标签、谎报覆盖。
        /// </summary>
        static Solid BuildSquareTube(SolidSpec spec)
        {
            var thickness = Math.Max(Math.Min(spec.SizeX, spec.SizeY) / 10.0, 1e-3);
            var innerX = spec.SizeX - 2 * thickness;
            var innerY = spec.SizeY - 2 * thickness;

            var outer = CsgEvaluator.Evaluate(
                CsgNodes.Extrude(Profiles.Rectangle(spec.SizeX, spec.SizeY), spec.Height));

            if (innerX <= 0 || innerY <= 0)
                return outer;   // 壁厚吃满：退化成实心块，仍是有效实体

            // 内轮廓沿 +Z 两端各出头一点，避免与外面在 Z=0/Height 共面导致布尔退化
            var overshoot = Math.Max(spec.Height * 0.01, 1e-6);
            var inner = CsgEvaluator.Evaluate(
                    CsgNodes.Extrude(Profiles.Rectangle(innerX, innerY), spec.Height + 2 * overshoot))
                .Translate(0, 0, -overshoot);

            return outer.Subtract(inner);
        }

        static Profile2D BuildProfile(ProfileKind kind, SolidSpec spec)
        {
            switch (kind)
            {
                case ProfileKind.Rectangle: return Profiles.Rectangle(spec.SizeX, spec.SizeY);
                case ProfileKind.HBeam: return Profiles.HBeam(spec.SizeY, spec.SizeX,
                    Math.Max(spec.SizeX / 10, 1e-3), Math.Max(spec.SizeY / 10, 1e-3));
                case ProfileKind.Channel: return Profiles.Channel(spec.SizeY, spec.SizeX,
                    Math.Max(spec.SizeX / 10, 1e-3), Math.Max(spec.SizeY / 10, 1e-3));
                // SquareTube 只产出外轮廓（Thickness 被忽略）；真正空心方管走 BuildSquareTube
                case ProfileKind.SquareTube: return Profiles.SquareTube(spec.SizeX, spec.SizeY,
                    Math.Max(Math.Min(spec.SizeX, spec.SizeY) / 10, 1e-3));
                case ProfileKind.Trapezoid: return Profiles.Trapezoid(spec.SizeX / 2, spec.SizeX, spec.SizeY);
                case ProfileKind.Capsule: return Profiles.Capsule(spec.SizeX, Math.Max(spec.SizeY / 2, 1e-3));
                case ProfileKind.LShape: return Profiles.LShape(spec.SizeY, spec.SizeX,
                    Math.Max(Math.Min(spec.SizeX, spec.SizeY) / 4, 1e-3));
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知截面种类");
            }
        }
    }
}
