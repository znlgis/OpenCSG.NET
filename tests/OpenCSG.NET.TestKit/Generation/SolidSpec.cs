namespace Csg.Test
{
    /// <summary>基本体种类。与生产 API 的对应关系见 <see cref="SolidGen.Materialize"/>。</summary>
    public enum PrimitiveKind
    {
        Cube = 0,
        Sphere = 1,
        Cylinder = 2,
        Cone = 3,
        Extrude = 4,
        Wedge = 5,
    }

    /// <summary>拉伸截面种类（对应 Profiles 工厂的 7 个形状）。</summary>
    public enum ProfileKind
    {
        Rectangle = 0,
        HBeam = 1,
        Channel = 2,
        SquareTube = 3,
        Trapezoid = 4,
        Capsule = 5,
        LShape = 6,
    }

    /// <summary>
    /// 坐标档位。Survey 档模拟测绘坐标（±1e6）—— 内核布尔合并是**绝对** 1e-5 容差，
    /// 大坐标是历史缺陷高发区（见 Solid.cs 的 int→long 模糊键修复）。
    /// </summary>
    public enum CoordTier
    {
        /// <summary>常规档：偏移 0，尺寸 log-uniform [1e-2, 1e3]。</summary>
        Normal = 0,

        /// <summary>测绘档：偏移 ±1e6，尺寸 [1, 1e3]（亚毫米尺寸在此档不可分辨，故不采样）。</summary>
        Survey = 1,
    }

    /// <summary>
    /// 实体参数（spec-then-materialize 的「spec」）。
    /// 刻意只用基元类型：FsCheck 可直接生成与**收缩**它，收缩结果是「最小的参数反例」，
    /// 人能读懂、能一行复现 —— 而不是一个无法解释的随机网格。
    /// </summary>
    public sealed record SolidSpec
    {
        public PrimitiveKind Kind { get; init; }

        // 基本尺寸（按 Kind 取用）
        public double SizeX { get; init; }
        public double SizeY { get; init; }
        public double SizeZ { get; init; }
        public double Radius { get; init; }
        public double RadiusEnd { get; init; }
        public double Height { get; init; }

        // 曲面细分
        public int Resolution { get; init; }

        /// <summary>扇形角（度）。随机采样固定为 360；其余取值是 Plan C 承接的已知缺陷。</summary>
        public double SectorAngle { get; init; }

        // 放置
        public double OffsetX { get; init; }
        public double OffsetY { get; init; }
        public double OffsetZ { get; init; }

        /// <summary>绕全局原点、X→Y→Z 顺序（度）。</summary>
        public double RotateX { get; init; }
        public double RotateY { get; init; }
        public double RotateZ { get; init; }

        /// <summary>均匀缩放因子（恒 &gt; 0）。</summary>
        public double Scale { get; init; }

        /// <summary>是否对 X 轴取负（负行列式镜像，触发绕序与法线翻转路径）。</summary>
        public bool Mirror { get; init; }

        /// <summary>Extrude 专用截面。</summary>
        public ProfileKind Profile { get; init; }
    }
}
