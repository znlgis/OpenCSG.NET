namespace Csg
{

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>
/// 三角网格（本模块的几何输入）：右手系、单位与模型一致（mm）。
/// <para>
/// <b>来源</b>：本项目由 <c>OpenCSG.NET</c> 的 <c>Solid.Polygons</c>（凸多边形）扇形三角化而来
/// —— 与参考实现（znlgis/GStarCad.Net.Demo，MIT，MESHVIEWEXPORT）从 STL 读取三角形相比，
/// 少了一次「EXPORT 落 STL → 再解析」的往返，而后续消隐算法只依赖三角形，二者等价。
/// </para>
/// </summary>
public readonly struct ViewTriangle
{
    public ViewTriangle(ViewPoint a, ViewPoint b, ViewPoint c)
    {
        A = a;
        B = b;
        C = c;
    }

    public ViewPoint A { get; }
    public ViewPoint B { get; }
    public ViewPoint C { get; }

    /// <summary>三角面法向（右手法则，已归一化；退化三角形返回零向量）。</summary>
    public ViewVector Normal
    {
        get
        {
            var u = B - A;
            var v = C - A;
            var n = ViewVector.Cross(u, v);
            var len = n.Length;
            return len < 1e-12 ? default : n * (1.0 / len);
        }
    }

    public ViewPoint this[int index] => index switch
    {
        0 => A,
        1 => B,
        2 => C,
        _ => throw new ArgumentOutOfRangeException(nameof(index), index, "三角形只有 0/1/2 三个顶点。"),
    };
}

/// <summary>
/// STL 读取（本模块的**辅助**输入通道）：主链路走 CSG 实体直接三角化（旧 `ViewExportBridge` 已随
/// 2026-10-10 命令面简化删除，三角化口径见 `CadCsgEvaluator`）
/// （CSG 实体直接三角化，无文件往返）；STL 入口用于离线核对与外部模型
/// —— 与参考实现（znlgis/GStarCad.Net.Demo，MIT，MESHVIEWEXPORT）的输入口径对齐。
/// </summary>
public static class ViewMeshReader
{
    /// <summary>
    /// 读取 STL（二进制按「84 + 50×面数 == 文件长度」判定，避免以 "solid" 开头的二进制文件被误判为 ASCII；
    /// 与参考实现同一判据）。
    /// </summary>
    public static IReadOnlyList<ViewTriangle> ReadStlFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(stream);
        return IsBinaryStl(reader) ? ParseBinaryStl(reader) : ParseAsciiStl(reader);
    }

    private static bool IsBinaryStl(BinaryReader reader)
    {
        if (reader.BaseStream.Length < 84)
            return false;

        reader.BaseStream.Seek(80, SeekOrigin.Begin);
        var count = reader.ReadUInt32();
        reader.BaseStream.Seek(0, SeekOrigin.Begin);
        return 84L + (long)count * 50L == reader.BaseStream.Length;
    }

    private static List<ViewTriangle> ParseBinaryStl(BinaryReader reader)
    {
        reader.BaseStream.Seek(80, SeekOrigin.Begin);
        var count = reader.ReadUInt32();
        var triangles = new List<ViewTriangle>((int)count);

        for (uint i = 0; i < count; i++)
        {
            reader.ReadSingle(); reader.ReadSingle(); reader.ReadSingle(); // 面法向（由顶点重算，忽略）
            var a = ReadPoint(reader);
            var b = ReadPoint(reader);
            var c = ReadPoint(reader);
            reader.ReadUInt16(); // 属性字节数
            triangles.Add(new ViewTriangle(a, b, c));
        }
        return triangles;
    }

    private static List<ViewTriangle> ParseAsciiStl(BinaryReader reader)
    {
        var triangles = new List<ViewTriangle>();
        using var text = new StreamReader(reader.BaseStream, Encoding.ASCII);

        var points = new List<ViewPoint>(3);
        string? line;
        while ((line = text.ReadLine()) != null)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("vertex", StringComparison.Ordinal))
                continue;

            var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
                continue;

            points.Add(new ViewPoint(
                ParseDouble(parts[1]), ParseDouble(parts[2]), ParseDouble(parts[3])));

            if (points.Count == 3)
            {
                triangles.Add(new ViewTriangle(points[0], points[1], points[2]));
                points.Clear();
            }
        }
        return triangles;
    }

    private static double ParseDouble(string text)
        => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static ViewPoint ReadPoint(BinaryReader reader)
        => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
}
}

