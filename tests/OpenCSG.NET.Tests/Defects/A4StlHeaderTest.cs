using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// A4 回归（spec §9 批准的最小修复；出自 spec 缺陷勘察表，从未分配 DEFECT 编号）：
    /// <list type="number">
    /// <item>二进制默认头曾是 <c>"调用方 STL export"</c>，经 <see cref="System.Text.Encoding.ASCII"/>
    /// 编码后中文全变 <c>?</c>（0x3F）——文件头即乱码。</item>
    /// <item>两条写出路径的 header 均未净化 <c>\r</c>/<c>\n</c>：二进制 80 字节头会带裸换行，
    /// ASCII 路径的 <c>solid &lt;head&gt;</c> 结构会被换行破坏（名字被拆成两行）。</item>
    /// </list>
    /// 修复：两条路径统一使用 ASCII 安全默认值 <c>"STL export"</c>，并把 header 里的 <c>\r</c>/<c>\n</c>
    /// 先替换为空格再写（截断到 79 字节的既有逻辑不动，由长度边界用例钉住）。
    /// </summary>
    [TestFixture]
    public class A4StlHeaderTest
    {
        static IReadOnlyList<MeshTriangle> Triangles()
            => CsgMesh.TrianglesOf(Solids.Cube(10, center: true));

        static string TempPath(string name) => Path.Combine(Path.GetTempPath(), $"opencsg-a4-{Guid.NewGuid():N}-{name}");

        [Test]
        public void BinaryDefaultHeader_IsAsciiAndNotGarbled()
        {
            var path = TempPath("default.stl");
            try
            {
                StlWriter.WriteBinary(path, Triangles());
                var head = File.ReadAllBytes(path).Take(80).ToArray();

                // 修复前：默认头 "调用方 STL export" → ASCII 编码产出 '?'（0x3F）乱码。
                Assert.That(head.Any(b => b == (byte)'?'), Is.False, "默认头不应含 '?'（中文经 ASCII 编码的痕迹）");
                Assert.That(head.TakeWhile(b => b != 0).All(b => b is > 0x1F and < 0x7F), Is.True, "默认头应为可见 ASCII");
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void Header_LineBreaksAreSanitized_BothWriters()
        {
            var binPath = TempPath("nl.stl");
            var asciiPath = TempPath("nl-ascii.stl");
            try
            {
                StlWriter.WriteBinary(binPath, Triangles(), "a\r\nb");
                var head = File.ReadAllBytes(binPath).Take(80).ToArray();
                Assert.That(head.Contains((byte)'\n'), Is.False, "二进制头不得含换行（ASCII STL 结构破坏源）");
                Assert.That(head.Contains((byte)'\r'), Is.False);

                StlWriter.WriteAscii(asciiPath, Triangles(), "a\r\nb");
                // 按原始字节切首行，只剥掉文件自身行终止符的 '\r'（WriteLine 的平台换行）；
                // 头内的裸 CR/LF 会留在首行中被下面的断言抓住。
                var firstLine = File.ReadAllText(asciiPath).Split('\n')[0].TrimEnd('\r');
                Assert.That(firstLine, Does.Not.Contain('\n').And.Not.Contain('\r'), "ASCII 头不得含裸 CR/LF");
                Assert.That(firstLine.StartsWith("solid "), Is.True);
                Assert.That(firstLine, Is.EqualTo("solid a  b"),
                    "头应保留在同一行（\\r 与 \\n 各替换为一个空格；修复前被拆成 'solid a' + 'b' 两行）");
            }
            finally { File.Delete(binPath); File.Delete(asciiPath); }
        }

        [Test]
        public void Header_LengthBoundaries_ArePreserved()
        {
            // 79/80/81 字节：头域恒为 80 字节；>79 截断；余位补零（既有行为，防回归）。
            foreach (var (len, label) in new[] { (79, "79"), (80, "80"), (81, "81") })
            {
                var path = TempPath($"len{len}.stl");
                try
                {
                    var header = new string('H', len);
                    StlWriter.WriteBinary(path, Triangles(), header);
                    var head = File.ReadAllBytes(path).Take(80).ToArray();
                    Assert.That(head.Length, Is.EqualTo(80));
                    Assert.That(head.Take(Math.Min(len, 79)).All(b => b == (byte)'H'), Is.True, $"len={label}");
                    Assert.That(head.Skip(len >= 80 ? 79 : len).All(b => b == 0), Is.True, $"len={label} 余位应补零");
                }
                finally { File.Delete(path); }
            }
        }

        [Test]
        public void AsciiRoundTrip_IsParseable_BinaryToo()
        {
            var asciiPath = TempPath("rt-ascii.stl");
            var binPath = TempPath("rt-bin.stl");
            try
            {
                var triangles = Triangles();
                StlWriter.WriteAscii(asciiPath, triangles);
                StlWriter.WriteBinary(binPath, triangles);
                Assert.That(ViewMeshReader.ReadStlFile(asciiPath).Count, Is.EqualTo(triangles.Count));
                Assert.That(ViewMeshReader.ReadStlFile(binPath).Count, Is.EqualTo(triangles.Count));
            }
            finally { File.Delete(asciiPath); File.Delete(binPath); }
        }
    }
}
