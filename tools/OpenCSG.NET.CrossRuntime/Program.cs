using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Csg;
using Csg.Test;

// 跨运行时确定性工具（Plan E / Task 2.5）。
//
// 目的：同一份 src 程序集（netstandard2.0，两个 TFM 共用同一编译产物）在两个 .NET runtime
// 上跑固定的确定性输入集，输出**组合 SHA256**；两 TFM 的哈希必须一致，不一致即为跨运行时
// 非确定性（登记缺陷，首个差异用逐阶段哈希定位）。
//
// 输入集（全部由固定种子/固定语料下标导出，见 tests/OpenCSG.NET.TestKit）：
//   ① 语料子集求值：corpus.json 的 8 个固定下标（覆盖 Normal/Survey 两档），SolidGen.Materialize
//      后按多边形顶点/平面坐标**位级**哈希（不含 Tag —— tag 是进程级计数器，与几何无关）；
//   ② 三视图投影：其中两个实体经 ViewBridge.ProjectViews（正交三视）后哈希段/弧/包络；
//   ③ 序列化：PerfWorkloads.DeepTrees() 的 8 棵固定深树 ToJson + FromJson→ToJson 往返；
//   ④ STL 写出：语料实体写 ASCII 与二进制 STL 到临时文件，按**字节**哈希后删除。
//
// 运行命令（仓库根，nightly 可复跑）：
//   dotnet run --project tools/OpenCSG.NET.CrossRuntime -c Release -f net8.0
//   dotnet run --project tools/OpenCSG.NET.CrossRuntime -c Release -f net10.0
// 两次输出末行 `combined=sha256:<hex>` 必须相同；逐阶段 `[stage]` 行用于定位首个差异。
//
// 注意：哈希含 ASCII STL 的字节（含平台换行）⇒ 本工具比对的是**同一平台**上的跨运行时
// 确定性；跨平台比对须先归一化换行（输出里打印了 OS 供归因）。

var corpusIndices = new[] { 0, 41, 106, 173, 256, 320, 400, 511 };
var viewSlots = new[] { 0, 1 };   // 投影取子集内第 0/1 个实体（语料下标 0/41，均为 Normal 档）

Console.WriteLine($"[runtime] {RuntimeInformation.FrameworkDescription}; " +
                  $"{RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}; " +
                  $"procs={Environment.ProcessorCount}");

var stages = new List<(string Name, byte[] Digest, double Ms)>();

// ── ① 语料子集求值 ────────────────────────────────────────────────────────────
Solid[] solids;
{
    var sw = Stopwatch.StartNew();
    var hash = new Digest();
    var specs = Corpus.CorpusSpecs();
    solids = new Solid[corpusIndices.Length];
    var anchors = new List<string>();
    for (var i = 0; i < corpusIndices.Length; i++)
    {
        var index = corpusIndices[i];
        var solid = SolidGen.Materialize(specs[index]);
        solids[i] = solid;

        hash.Add(index);
        hash.Add(solid.Polygons.Count);
        foreach (var polygon in solid.Polygons)
        {
            hash.Add(polygon.Vertices.Count);
            hash.Add(polygon.Plane.Normal.X);
            hash.Add(polygon.Plane.Normal.Y);
            hash.Add(polygon.Plane.Normal.Z);
            hash.Add(polygon.Plane.W);
            foreach (var vertex in polygon.Vertices)
            {
                hash.Add(vertex.Pos.X);
                hash.Add(vertex.Pos.Y);
                hash.Add(vertex.Pos.Z);
            }
        }

        var anchor = $"#{index}:polys={solid.Polygons.Count}";
        if (solid.Polygons.Count > 0 && solid.Polygons[0].Vertices.Count > 0)
        {
            var v = solid.Polygons[0].Vertices[0].Pos;
            anchor += $" v0={Bits(v.X):X16}";
        }
        anchors.Add(anchor);
    }
    sw.Stop();
    Console.WriteLine($"[corpus] {string.Join(" ", anchors)}");
    stages.Add(("corpus-eval", hash.Finish(), sw.Elapsed.TotalMilliseconds));
}

// ── ② 三视图投影 ──────────────────────────────────────────────────────────────
{
    var sw = Stopwatch.StartNew();
    var hash = new Digest();
    foreach (var slot in viewSlots)
    {
        var views = ViewBridge.ProjectViews(solids[slot], ViewDirections.OrthographicThree);
        hash.Add(slot);
        hash.Add(views.Count);
        foreach (var view in views)
        {
            hash.Add(view.Direction.Name);
            hash.Add(view.Bounds.MinX);
            hash.Add(view.Bounds.MinY);
            hash.Add(view.Bounds.MaxX);
            hash.Add(view.Bounds.MaxY);
            hash.Add(view.Segments.Count);
            foreach (var segment in view.Segments)
            {
                hash.Add(segment.Start.X);
                hash.Add(segment.Start.Y);
                hash.Add(segment.End.X);
                hash.Add(segment.End.Y);
                hash.Add(segment.IsVisible);
            }
            hash.Add(view.Arcs.Count);
            foreach (var arc in view.Arcs)
            {
                hash.Add(arc.Center.X);
                hash.Add(arc.Center.Y);
                hash.Add(arc.Radius);
                hash.Add(arc.StartAngle);
                hash.Add(arc.EndAngle);
                hash.Add(arc.IsVisible);
                hash.Add(arc.IsClosed);
            }
        }
    }
    sw.Stop();
    stages.Add(("views-three", hash.Finish(), sw.Elapsed.TotalMilliseconds));
}

// ── ③ 序列化（深树 ToJson + FromJson→ToJson 往返） ─────────────────────────────
{
    var sw = Stopwatch.StartNew();
    var hash = new Digest();
    var trees = PerfWorkloads.DeepTrees();
    hash.Add(trees.Count);
    for (var i = 0; i < trees.Count; i++)
    {
        var json = CsgSerialization.ToJson(trees[i], indented: false);
        hash.Add(i);
        hash.Add(json);
        var roundTrip = CsgSerialization.ToJson(CsgSerialization.FromJson(json), indented: false);
        hash.Add(roundTrip);
        if (!string.Equals(json, roundTrip, StringComparison.Ordinal))
            Console.WriteLine($"[warn] 深树 #{i}：FromJson→ToJson 与原文不逐字相等（仅提示，参与哈希）");
    }
    sw.Stop();
    stages.Add(("serialization", hash.Finish(), sw.Elapsed.TotalMilliseconds));
}

// ── ④ STL 写出（ASCII + 二进制，按文件字节哈希） ───────────────────────────────
{
    var sw = Stopwatch.StartNew();
    var hash = new Digest();
    var dir = Path.Combine(Path.GetTempPath(), "opencsg-crossruntime");
    Directory.CreateDirectory(dir);
    var asciiPath = Path.Combine(dir, "crossruntime-ascii.stl");
    var binaryPath = Path.Combine(dir, "crossruntime-binary.stl");
    try
    {
        using (var fs = File.Create(asciiPath))
        using (var writer = new StreamWriter(fs))
        {
            solids[0].WriteStl("CrossRuntime", writer);
        }
        using (var fs = File.Create(binaryPath))
        using (var writer = new BinaryWriter(fs))
        {
            solids[0].WriteStl("CrossRuntime", writer);
        }

        var ascii = File.ReadAllBytes(asciiPath);
        var binary = File.ReadAllBytes(binaryPath);
        hash.Add(ascii.Length);
        hash.Add(ascii);
        hash.Add(binary.Length);
        hash.Add(binary);
        Console.WriteLine($"[stl] ascii={ascii.Length}B binary={binary.Length}B");
    }
    finally
    {
        File.Delete(asciiPath);
        File.Delete(binaryPath);
    }
    sw.Stop();
    stages.Add(("stl-write", hash.Finish(), sw.Elapsed.TotalMilliseconds));
}

// ── 组合哈希 ─────────────────────────────────────────────────────────────────
using var combined = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
foreach (var stage in stages)
{
    Console.WriteLine($"[stage] {stage.Name,-14} sha256={Hex(stage.Digest)}  {stage.Ms:F1}ms");
    combined.AppendData(stage.Digest);
}
Console.WriteLine($"combined=sha256:{Hex(combined.GetHashAndReset())}");
return 0;

static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);

/// <summary>增量 SHA256：所有 Add 先写定长小端前缀（避免不同结构的字节流歧义）。</summary>
sealed class Digest
{
    readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    readonly byte[] _scratch = new byte[8];

    public void Add(bool value) => Add(value ? 1 : 0);
    public void Add(int value) => Add((long)value);
    public void Add(long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(_scratch, value);
        _hash.AppendData(_scratch);
    }
    public void Add(double value) => Add(BitConverter.DoubleToInt64Bits(value));
    public void Add(byte[] bytes)
    {
        Add((long)bytes.Length);
        _hash.AppendData(bytes);
    }
    public void Add(string value) => Add(Encoding.UTF8.GetBytes(value));
    public byte[] Finish() => _hash.GetHashAndReset();
}
