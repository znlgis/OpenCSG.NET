// Cpurlin 样例几何：自 Program.cs 抽取，供样例 CLI 与测试（SystemTests）共用。
// 抽取前后几何构造顺序与参数保持一致，样例产出的 STL 逐字节不变。
using Csg;
using static Csg.Solids;

namespace Runner.CPurlin;

public static class CpurlinModel
{
    // --- 截面尺寸 ---
    public const double Web = 152.0;
    public const double Flange = 64.0;
    public const double Lip = 18.5;
    public const double Thickness = 2.4;
    public const double Radius = 5.0;

    // --- 冲孔尺寸（pill 形：两端圆 holeSize1 + 中间矩形差 holeSize2 - holeSize1） ---
    public const double HoleSize1 = 18.0;
    public const double HoleSize2 = 22.0;

    // --- 冲孔位置（z 值；WebCenter 为腹板孔对相对中线的偏移） ---
    public const double WebCenter = 60.0;
    public const double PunchingWebZ = 35.0;
    public const double PunchingFlangeZ = 70.0;
    public const double PunchingCenterZ = 105.0;

    // --- 柱高 ---
    public const double Height = 1000.0;

    // 原样例的调试开关：true 时各段柱高按倍率放大（便于目视检查接缝）。
    // 抽取后保留原语义，默认值（false）与抽取前一致。
    private const bool DebugMode = false;

    private static readonly double[] PunchingsWeb = { PunchingWebZ };
    private static readonly double[] PunchingsFlange = { PunchingFlangeZ };
    private static readonly double[] PunchingsCenter = { PunchingCenterZ };

    public static Solid Build()
    {
        var h1 = DebugMode ? Height * 1.2 : Height;
        var h2 = DebugMode ? Height * 2.0 : Height;
        var h3 = DebugMode ? Height * 2.2 : Height;
        var h4 = DebugMode ? Height * 3.0 : Height;

        var sweb = Cube(
            center: V3D(-Flange / 2 + Thickness / 2, 0, 0),
            size: V3D(Thickness, Web - Radius * 2, h1));

        foreach (var p in PunchingsWeb)
        {
            var holeTop = Punching(p, true)
                .Translate(-Flange / 2, +WebCenter / 2, 0);
            var holeBottom = Punching(p, true)
                .Translate(-Flange / 2, -WebCenter / 2, 0);
            sweb = sweb.Subtract(holeTop, holeBottom);
        }

        foreach (var p in PunchingsCenter)
        {
            var holeCenter = Punching(p, true)
                .Translate(-Flange / 2, 0, 0);
            sweb = sweb.Subtract(holeCenter);
        }

        var sflangetop = Cube(
            center: V3D(0, Web / 2 - Thickness / 2, 0),
            size: V3D(Flange - Radius * 2, Thickness, h1));

        var sflangebottom = Cube(
            center: V3D(0, -Web / 2 + Thickness / 2, 0),
            size: V3D(Flange - Radius * 2, Thickness, h1));

        foreach (var p in PunchingsFlange)
        {
            var holeTop = Punching(p)
                .Translate(0, +Web / 2, 0);
            sflangetop = sflangetop.Subtract(holeTop);

            var holeBottom = Punching(p)
                .Translate(0, -Web / 2, 0);
            sflangebottom = sflangebottom.Subtract(holeBottom);
        }

        var sliptop = Cube(
            center: V3D(Flange / 2 - Thickness / 2, Web / 2 - Radius / 2 - Lip / 2, 0),
            size: V3D(Thickness, Lip - Radius, h1));

        var slipbottom = Cube(
            center: V3D(Flange / 2 - Thickness / 2, -Web / 2 + Radius / 2 + Lip / 2, 0),
            size: V3D(Thickness, Lip - Radius, h1));

        var notch = Cube(Web, Web, Web, true)
            .Translate(0, 0 - Web + Lip * 1.5, Height / 2);

        var s = Union(sweb, sflangetop, sflangebottom, sliptop, slipbottom, Corner(-1, +1, h2, h3, h4), Corner(+1, +1, h2, h3, h4), Corner(-1, -1, h2, h3, h4), Corner(+1, -1, h2, h3, h4))
            .RotateX(90)
            .Translate(0, Height / 2, 0)
            .RotateX(90);

        s = s.Subtract(notch);

        return s;
    }

    private static Solid Corner(int x, int y, double h2, double h3, double h4)
    {
        return Cylinder(Radius, h2, true)
            .Subtract(Cylinder(Radius - Thickness, h3, true))
            .RotateX(90)
            .Intersect(Cube(
                center: V3D(x * Radius / 2, y * Radius / 2, 0),
                size: V3D(Radius, Radius, h4)))
            .Translate(x * Flange / 2 + -x * Radius, y * Web / 2 + -y * Radius, 0);
    }

    private static Solid Punching(double z, bool onWeb = false)
    {
        var punching = Cylinder(HoleSize1 / 2, Thickness * 4, true);

        if (HoleSize2 > HoleSize1)
        {
            // Pill-shaped hole instead

            var diff = HoleSize2 - HoleSize1;

            punching = Union(
                punching.Translate(z: -diff / 2),
                punching.Translate(z: +diff / 2),
                Cube(V3D(HoleSize1, Thickness * 4, diff), center: true)
            );
        }

        if (onWeb)
        {
            punching = punching.RotateZ(90);
        }

        return punching.Translate(0, 0, Height / 2 - z);
    }

    private static Vector3D V3D(double x, double y, double z) => new(x, y, z);
}
