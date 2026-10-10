namespace Csg
{

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>STL 输出格式。</summary>
public enum StlFormat
{
    /// <summary>二进制 STL（默认）：紧凑、几乎所有 CAD/切片软件都支持。</summary>
    Binary,

    /// <summary>ASCII STL：可文本 diff/人工查看，体积约为二进制的 3~5 倍。</summary>
    Ascii,
}

/// <summary>STL 写出结果：文件路径 + 三角面数 + 文件字节数。</summary>
public sealed class StlWriteResult
{
    public StlWriteResult(string path, int triangleCount, long bytes)
    {
        Path = path;
        TriangleCount = triangleCount;
        Bytes = bytes;
    }

    public string Path { get; }
    public int TriangleCount { get; }
    public long Bytes { get; }
}

/// <summary>
/// STL 写出器（纯 IO，不依赖 CAD 运行时，可离线单测）。
/// <para>
/// 二进制布局（Little Endian）：80 字节头 + uint32 面数 + 每面 50 字节
/// （法向 3×float + 三顶点 9×float + uint16 属性字节数）。
/// 法向由**绕序重算并归一化**（不信任调用方传入的法向）；退化三角形（面积≈0）写零法向，
/// 这是 STL 约定，读入方可自行忽略。
/// </para>
/// </summary>
public static class StlWriter
{
    /// <summary>二进制 STL 头部长度（字节）。</summary>
    public const int HeaderLength = 80;

    /// <summary>单面字节数。</summary>
    public const int FacetLength = 50;

    /// <summary>写入 STL 文件；目录不存在时自动创建。</summary>
    public static StlWriteResult Write(
        string path, IReadOnlyList<MeshTriangle> triangles, StlFormat format = StlFormat.Binary,
        string? header = null)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("路径不能为空", nameof(path));
        if (triangles == null) throw new ArgumentNullException(nameof(triangles));

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var bytes = format == StlFormat.Binary
            ? WriteBinary(path, triangles, header)
            : WriteAscii(path, triangles, header);

        return new StlWriteResult(path, triangles.Count, bytes);
    }

    /// <summary>二进制 STL（写文件后返回字节数）。</summary>
    public static long WriteBinary(string path, IReadOnlyList<MeshTriangle> triangles, string? header = null)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        var head = new byte[HeaderLength];
        var text = header ?? "TurbineCrateCAD STL export";
        var textBytes = Encoding.ASCII.GetBytes(text);
        Array.Copy(textBytes, head, Math.Min(textBytes.Length, HeaderLength - 1));
        writer.Write(head);

        writer.Write((uint)triangles.Count);
        foreach (var triangle in triangles)
        {
            var normal = NormalOf(triangle);
            WriteVector(writer, normal);
            WritePoint(writer, triangle.A);
            WritePoint(writer, triangle.B);
            WritePoint(writer, triangle.C);
            writer.Write((ushort)0); // 属性字节数
        }

        writer.Flush();
        return stream.Length;
    }

    /// <summary>ASCII STL（写文件后返回字节数）。</summary>
    public static long WriteAscii(string path, IReadOnlyList<MeshTriangle> triangles, string? header = null)
    {
        var number = CultureInfo.InvariantCulture;
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));

        writer.WriteLine("solid " + (header ?? "TurbineCrateCAD"));
        foreach (var triangle in triangles)
        {
            var normal = NormalOf(triangle);
            writer.WriteLine(string.Format(number, "  facet normal {0:0.######} {1:0.######} {2:0.######}",
                normal.X, normal.Y, normal.Z));
            writer.WriteLine("    outer loop");
            WriteAsciiPoint(writer, triangle.A, number);
            WriteAsciiPoint(writer, triangle.B, number);
            WriteAsciiPoint(writer, triangle.C, number);
            writer.WriteLine("    endloop");
            writer.WriteLine("  endfacet");
        }
        writer.WriteLine("endsolid " + (header ?? "TurbineCrateCAD"));
        writer.Flush();
        return stream.Length;
    }

    /// <summary>由绕序（右手法则）重算单位法向；退化三角形返回零向量。</summary>
    public static MeshPoint NormalOf(MeshTriangle triangle)
    {
        var ux = triangle.B.X - triangle.A.X;
        var uy = triangle.B.Y - triangle.A.Y;
        var uz = triangle.B.Z - triangle.A.Z;
        var vx = triangle.C.X - triangle.A.X;
        var vy = triangle.C.Y - triangle.A.Y;
        var vz = triangle.C.Z - triangle.A.Z;

        var nx = uy * vz - uz * vy;
        var ny = uz * vx - ux * vz;
        var nz = ux * vy - uy * vx;
        var length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        return length < 1e-12
            ? default
            : new MeshPoint(nx / length, ny / length, nz / length);
    }

    private static void WriteVector(BinaryWriter writer, MeshPoint vector)
    {
        writer.Write((float)vector.X);
        writer.Write((float)vector.Y);
        writer.Write((float)vector.Z);
    }

    private static void WritePoint(BinaryWriter writer, MeshPoint point) => WriteVector(writer, point);

    private static void WriteAsciiPoint(StreamWriter writer, MeshPoint point, CultureInfo number)
        => writer.WriteLine(string.Format(number, "      vertex {0:0.######} {1:0.######} {2:0.######}",
            point.X, point.Y, point.Z));
}
}

