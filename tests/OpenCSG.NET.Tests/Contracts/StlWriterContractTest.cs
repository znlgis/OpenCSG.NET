using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// StlWriter 的 L2 契约（Plan C / Task 7 Step 1 + Step 4）：<see cref="StlWriter.Write"/> 的
    /// 路径/三角形校验与 <see cref="StlWriteResult"/> 读数、目录自动创建；头部的非 ASCII 输入
    /// （按实际契约：ASCII 编码 ⇒ '?' 序列）与不含换行的制表符保留。
    /// （默认头精确字节、空串头、换行净化、长度边界已由 A4StlHeaderTest 覆盖。）
    /// </summary>
    [TestFixture]
    public class StlWriterContractTest
    {
        static IReadOnlyList<MeshTriangle> Triangles()
            => CsgMesh.TrianglesOf(Solids.Cube(10, center: true));

        static string TempPath(string name) => Path.Combine(Path.GetTempPath(), $"opencsg-t7-{Guid.NewGuid():N}-{name}");

        static byte[] ReadHeader(string path) => File.ReadAllBytes(path).Take(StlWriter.HeaderLength).ToArray();

        [Test]
        public void Write_NullOrWhitespacePath_ThrowsArgumentException()
        {
            // 读实现：路径判据是 string.IsNullOrWhiteSpace ⇒ null/空/全空白一律 ArgumentException（非 NullReferenceException）。
            foreach (var path in new[] { null, "", "   " })
            {
                var ex = Assert.Throws<ArgumentException>(() => StlWriter.Write(path!, Triangles()));
                Assert.That(ex!.ParamName, Is.EqualTo("path"));
                Assert.That(ex.Message, Does.Contain("路径不能为空"));
            }
        }

        [Test]
        public void Write_NullTriangles_ThrowsArgumentNullException()
        {
            var path = TempPath("null-tris.stl");
            var ex = Assert.Throws<ArgumentNullException>(() => StlWriter.Write(path, null!));
            Assert.That(ex!.ParamName, Is.EqualTo("triangles"));
            Assert.That(File.Exists(path), Is.False, "参数校验失败不得落半成品文件");
        }

        [Test]
        public void Write_InvalidPathCharacter_ThrowsArgumentException()
        {
            // 空白校验之后路径交给 BCL：NUL 字符由 Path.GetFullPath 拒绝（ArgumentException，非 IO 异常）。
            var ex = Assert.Throws<ArgumentException>(() => StlWriter.Write("bad\0path.stl", Triangles()));
            Assert.That(ex!.Message, Does.Contain("Null character"));
        }

        [Test]
        public void Write_Binary_CreatesDirectory_AndReportsResult()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"opencsg-t7-dir-{Guid.NewGuid():N}");
            var path = Path.Combine(directory, "nested", "cube.stl");
            try
            {
                var result = StlWriter.Write(path, Triangles());

                Assert.That(Directory.Exists(Path.GetDirectoryName(path)), Is.True, "目录不存在时应自动创建");
                Assert.That(result.Path, Is.EqualTo(path));
                Assert.That(result.TriangleCount, Is.EqualTo(12));
                Assert.That(result.Bytes, Is.EqualTo(84L + 50L * 12L), "二进制体积 = 80 头 + 4 面数 + 50×面数");
                Assert.That(new FileInfo(path).Length, Is.EqualTo(result.Bytes), "Bytes 读数须等于文件真实长度");
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public void Write_AsciiFormat_WritesAscii_WithDefaultHeader()
        {
            var path = TempPath("ascii.stl");
            try
            {
                var result = StlWriter.Write(path, Triangles(), StlFormat.Ascii);

                Assert.That(result.TriangleCount, Is.EqualTo(12));
                Assert.That(new FileInfo(path).Length, Is.EqualTo(result.Bytes));
                var firstLine = File.ReadAllText(path).Split('\n')[0].TrimEnd('\r');
                Assert.That(firstLine, Is.EqualTo("solid STL export"), "ASCII 路径缺省头与二进制同值");
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void BinaryHeader_NonAsciiInput_BecomesQuestionMarks_Deterministically()
        {
            // 读实现：二进制头经 Encoding.ASCII.GetBytes ⇒ 每个非 ASCII 字符编成 0x3F '?'（确定性行为，钉住）。
            var path = TempPath("cn.stl");
            try
            {
                StlWriter.WriteBinary(path, Triangles(), "中文");
                var expected = new byte[StlWriter.HeaderLength];
                expected[0] = (byte)'?';
                expected[1] = (byte)'?';
                Assert.That(ReadHeader(path), Is.EqualTo(expected),
                    "两个汉字 ⇒ 头首两字节 0x3F、其余补零；文件头域绝不含高比特字节");
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void Header_TabIsPreserved_BothWriters()
        {
            // 净化只针对 \r\n：不含换行的 '\t' 应原样保留（两条路径）。
            var binPath = TempPath("tab.stl");
            var asciiPath = TempPath("tab-ascii.stl");
            try
            {
                StlWriter.WriteBinary(binPath, Triangles(), "a\tb");
                var head = ReadHeader(binPath);
                Assert.That(head[0], Is.EqualTo((byte)'a'));
                Assert.That(head[1], Is.EqualTo((byte)'\t'));
                Assert.That(head[2], Is.EqualTo((byte)'b'));
                Assert.That(head[3], Is.EqualTo((byte)0), "第三字符之后补零");

                StlWriter.WriteAscii(asciiPath, Triangles(), "a\tb");
                var firstLine = File.ReadAllText(asciiPath).Split('\n')[0].TrimEnd('\r');
                Assert.That(firstLine, Is.EqualTo("solid a\tb"), "ASCII 首行应完整保留制表符");
            }
            finally { File.Delete(binPath); File.Delete(asciiPath); }
        }
    }
}
