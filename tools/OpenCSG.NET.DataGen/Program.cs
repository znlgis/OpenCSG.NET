using System.Globalization;
using System.Text;
using Csg;
using Csg.Test;

// 语料生成/物化工具。
//   --write-corpus          重建 tests/OpenCSG.NET.TestKit/Generation/corpus.json（并就地提交）
//   --emit <dir>            把语料物化成 STL + metrics.csv，供人工检视
// 语料本身由确定性种子驱动，所以重建是幂等的。
var corpusPath = args.Length > 0 && args[0] == "--write-corpus"
    ? LocateCorpusPath()
    : null;

if (corpusPath != null)
{
    WriteCorpus(corpusPath);
    return 0;
}

var emitIndex = Array.IndexOf(args, "--emit");
if (emitIndex >= 0 && emitIndex + 1 < args.Length)
{
    Emit(args[emitIndex + 1]);
    return 0;
}

Console.Error.WriteLine("用法：--write-corpus | --emit <目录>");
return 1;

static string LocateCorpusPath()
{
    // 从可执行文件位置向上找 tests/OpenCSG.NET.TestKit 目录
    DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        var candidate = Path.Combine(dir.FullName, "tests", "OpenCSG.NET.TestKit", "Generation");
        if (Directory.Exists(candidate))
            return Path.Combine(candidate, "corpus.json");
        dir = dir.Parent;
    }
    throw new DirectoryNotFoundException("找不到 tests/OpenCSG.NET.TestKit/Generation —— 请在仓库内运行");
}

static void WriteCorpus(string path)
{
    var normal = Corpus.CorpusCaseCount * 5 / 8;
    var builder = new StringBuilder();
    builder.Append("{\n  \"schemaVersion\": 1,\n  \"masterSeed\": 20261013,\n  \"specs\": [\n");

    for (var i = 0; i < Corpus.CorpusCaseCount; i++)
    {
        var tier = i < normal ? CoordTier.Normal : CoordTier.Survey;
        var spec = SolidGen.Random(Corpus.CorpusSeed(i), tier);
        var solid = SolidGen.Materialize(spec);

        builder.Append("    {");
        builder.Append($"\"index\": {i}, ");
        builder.Append($"\"tier\": \"{tier}\", ");
        builder.Append($"\"kind\": {(int)spec.Kind}, ");
        builder.Append($"\"sizeX\": {spec.SizeX.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"sizeY\": {spec.SizeY.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"sizeZ\": {spec.SizeZ.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"radius\": {spec.Radius.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"radiusEnd\": {spec.RadiusEnd.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"height\": {spec.Height.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"resolution\": {spec.Resolution}, ");
        builder.Append($"\"sectorAngle\": {spec.SectorAngle.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"offsetX\": {spec.OffsetX.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"offsetY\": {spec.OffsetY.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"offsetZ\": {spec.OffsetZ.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"rotateX\": {spec.RotateX.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"rotateY\": {spec.RotateY.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"rotateZ\": {spec.RotateZ.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"scale\": {spec.Scale.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"mirror\": {(spec.Mirror ? "true" : "false")}, ");
        builder.Append($"\"profile\": {(int)spec.Profile}, ");

        // 观测指标：记录生成当时的结果，供人工检视与 Plan D 的 golden 层使用。
        // 属性测试**不**依赖这些数字（那会让 oracle 退化成自证）；
        // 它们的作用是让「实现漂移」可见。
        var volume = GeometryAssert.SignedVolume(solid);
        var (min, max) = GeometryAssert.Bounds(solid);
        var mesh = CsgMesh.TrianglesOf(solid);
        var edges = MeshAssert.Analyze(mesh);
        builder.Append($"\"polygons\": {solid.Polygons.Count}, ");
        builder.Append($"\"volume\": {volume.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"minX\": {min.X.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"maxX\": {max.X.ToString("R", CultureInfo.InvariantCulture)}, ");
        builder.Append($"\"meshTriangles\": {edges.TriangleCount}, ");
        builder.Append($"\"meshEuler\": {edges.EulerCharacteristic}");
        builder.Append(i + 1 == Corpus.CorpusCaseCount ? "}\n" : "},\n");
    }

    builder.Append("  ]\n}\n");
    File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    Console.WriteLine($"已写入 {Corpus.CorpusCaseCount} 条 spec 到 {path}");
}

static void Emit(string directory)
{
    Directory.CreateDirectory(directory);
    var csv = new StringBuilder("index,tier,kind,polygons,volume,meshTriangles,meshEuler,stl\n");

    var specs = Corpus.CorpusSpecs();
    for (var index = 0; index < specs.Count; index++)
    {
        var spec = specs[index];
        var solid = SolidGen.Materialize(spec);
        var name = $"{index:D4}-{spec.Kind}.stl";
        var path = Path.Combine(directory, name);

        StlWriter.Write(path, CsgMesh.TrianglesOf(solid));

        var edges = MeshAssert.Analyze(CsgMesh.TrianglesOf(solid));
        csv.Append(CultureInfo.InvariantCulture,
            $"{index},{spec.Kind},{solid.Polygons.Count},"
            + $"{GeometryAssert.SignedVolume(solid):R},{edges.TriangleCount},{edges.EulerCharacteristic},{name}\n");
    }

    File.WriteAllText(Path.Combine(directory, "metrics.csv"), csv.ToString(),
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    Console.WriteLine($"已物化 {Corpus.CorpusSpecs().Count} 个模型到 {directory}");
}
