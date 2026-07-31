using Csg;
using static Csg.Solids;
using System.Diagnostics;

const bool debug = false;

var web = 152.0;
var flange = 64.0;
var lip = 18.5;
var thickness = 2.4;

var holeSize1 = 18.0;
var holeSize2 = 22.0;

var webCenter = 60.0;
var punchingsWeb = new List<double>() { 35 };
var punchingsFlange = new List<double>() { 70 };
var punchingsCenter = new List<double>() { 105 };

var height = 1000.0;

var r = 5.0;

var h1 = debug ? height * 1.2 : height;
var h2 = debug ? height * 2.0 : height;
var h3 = debug ? height * 2.2 : height;
var h4 = debug ? height * 3.0 : height;

var sweb = Cube(
    center: V3D(-flange / 2 + thickness / 2, 0, 0),
    size: V3D(thickness, web - r * 2, h1));

foreach (var p in punchingsWeb)
{
    var holeTop = Punching(p, true)
        .Translate(-flange / 2, +webCenter / 2, 0);
    var holeBottom = Punching(p, true)
        .Translate(-flange / 2, -webCenter / 2, 0);
    sweb = sweb.Subtract(holeTop, holeBottom);
}

foreach (var p in punchingsCenter)
{
    var holeCenter = Punching(p, true)
        .Translate(-flange / 2, 0, 0);
    sweb = sweb.Subtract(holeCenter);
}

var sflangetop = Cube(
    center: V3D(0, web / 2 - thickness / 2, 0),
    size: V3D(flange - r * 2, thickness, h1));

var sflangebottom = Cube(
    center: V3D(0, -web / 2 + thickness / 2, 0),
    size: V3D(flange - r * 2, thickness, h1));

foreach (var p in punchingsFlange)
{
    var holeTop = Punching(p)
        .Translate(0, +web / 2, 0);
    sflangetop = sflangetop.Subtract(holeTop);

    var holeBottom = Punching(p)
        .Translate(0, -web / 2, 0);
    sflangebottom = sflangebottom.Subtract(holeBottom);
}

var sliptop = Cube(
    center: V3D(flange / 2 - thickness / 2, web / 2 - r / 2 - lip / 2, 0),
    size: V3D(thickness, lip - r, h1));

var slipbottom = Cube(
    center: V3D(flange / 2 - thickness / 2, -web / 2 + r / 2 + lip / 2, 0),
    size: V3D(thickness, lip - r, h1));

var notch = Cube(web, web, web, true)
    .Translate(0, 0 - web + lip * 1.5, height / 2);

var s = Union(sweb, sflangetop, sflangebottom, sliptop, slipbottom, Corner(-1, +1), Corner(+1, +1), Corner(-1, -1), Corner(+1, -1))
    .RotateX(90)
    .Translate(0, height / 2, 0)
    .RotateX(90);

s = s.Subtract(notch);

var outputName = "CPurlin";
var outputTextPath = outputName + "-text.stl";
var outputBinPath = outputName + "-bin.stl";

using (var fs = File.Create(outputTextPath))
using (var sw = new StreamWriter(fs))
{
    s.WriteStl(outputName, sw);
}

using (var fs = File.Create(outputBinPath))
using (var bw = new BinaryWriter(fs))
{
    s.WriteStl(outputName, bw);
}

Process.Start(new ProcessStartInfo()
{
    FileName = outputTextPath,
    UseShellExecute = true
});

Solid Corner(int x, int y)
{
    return Cylinder(r, h2, true)
        .Subtract(Cylinder(r - thickness, h3, true))
        .RotateX(90)
        .Intersect(Cube(
            center: V3D(x * r / 2, y * r / 2, 0),
            size: V3D(r, r, h4)))
        .Translate(x * flange / 2 + -x * r, y * web / 2 + -y * r, 0);
}

Solid Punching(double z, bool onWeb = false)
{
    var punching = Cylinder(holeSize1 / 2, thickness * 4, true);

    if (holeSize2 > holeSize1)
    {
        // Pill-shaped hole instead

        var diff = holeSize2 - holeSize1;

        punching = Union(
            punching.Translate(z: -diff / 2),
            punching.Translate(z: +diff / 2),
            Cube(V3D(holeSize1, thickness * 4, diff), center: true)
        );
    }

    if (onWeb)
    {
        punching = punching.RotateZ(90);
    }

    return punching.Translate(0, 0, height / 2 - z);
}

static Vector3D V3D(double x, double y, double z) => new(x, y, z);
