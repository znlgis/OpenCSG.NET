using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// STL 家族内容级（Plan C / Task 8）：<see cref="StlWriter"/> 的写内容（二进制逐字节布局 / ASCII 逐行结构 /
    /// NormalOf 手推法向）与 <see cref="ViewMeshReader"/> 的读内容（手工 ASCII 已知三角形 / 二进制往返 /
    /// IsBinaryStl 的 84+50N 长度判定与回退）。头部净化矩阵与路径校验已由 A4StlHeaderTest / StlWriterContractTest
    /// 覆盖，此处不重复。
    /// </summary>
    [TestFixture]
    public class StlMeshIoTest
    {
        static string TempPath(string name)
            => Path.Combine(Path.GetTempPath(), $"opencsg-t8-{Guid.NewGuid():N}-{name}");

        /// <summary>两个已知三角形：t1 法向 +Z、t2 法向 +X（浮点值全部可精确表示）。</summary>
        static List<MeshTriangle> TwoTriangles() => new()
        {
            new MeshTriangle(new MeshPoint(0, 0, 0), new MeshPoint(1, 0, 0), new MeshPoint(0, 1, 0)),
            new MeshTriangle(new MeshPoint(0, 0, 0), new MeshPoint(0, 2, 0), new MeshPoint(0, 0, 2)),
        };

        // ══════════════════════════════════════════════════════════════════════════
        //  StlWriter：二进制内容级
        // ══════════════════════════════════════════════════════════════════════════

        [Test]
        public void Write_Binary_ExactRecordLayout_LengthFormula_AndResultReadings()
        {
            var path = TempPath("layout.stl");
            try
            {
                var triangles = TwoTriangles();
                var result = StlWriter.Write(path, triangles, StlFormat.Binary);

                // 文件长度公式：80 头 + 4 面数 + 50×N。
                Assert.That(new FileInfo(path).Length, Is.EqualTo(84L + 50L * 2), "文件长度 = 84 + 50N");
                Assert.That(result.TriangleCount, Is.EqualTo(2), "StlWriteResult.TriangleCount");
                Assert.That(result.Bytes, Is.EqualTo(184L), "StlWriteResult.Bytes");
                Assert.That(result.Path, Is.EqualTo(path), "StlWriteResult.Path");

                using var reader = new BinaryReader(File.OpenRead(path));
                var header = reader.ReadBytes(80);
                Assert.That(Encoding.ASCII.GetString(header, 0, 10), Is.EqualTo("STL export"),
                    "默认头 ASCII 前缀");
                Assert.That(reader.ReadUInt32(), Is.EqualTo(2u), "偏移 80 处的面数（uint32 LE）");

                AssertRecord(reader, (0, 0, 1), (0, 0, 0), (1, 0, 0), (0, 1, 0), "t1");
                AssertRecord(reader, (1, 0, 0), (0, 0, 0), (0, 2, 0), (0, 0, 2), "t2");
                Assert.That(reader.BaseStream.Position, Is.EqualTo(184L), "两条记录恰好写满 50×2 字节");
            }
            finally { File.Delete(path); }
        }

        static void AssertRecord(BinaryReader reader,
            (double X, double Y, double Z) normal,
            (double X, double Y, double Z) a, (double X, double Y, double Z) b, (double X, double Y, double Z) c,
            string name)
        {
            Assert.That(reader.ReadSingle(), Is.EqualTo((float)normal.X), $"{name} 法向 X（绕序重算并归一化）");
            Assert.That(reader.ReadSingle(), Is.EqualTo((float)normal.Y), $"{name} 法向 Y");
            Assert.That(reader.ReadSingle(), Is.EqualTo((float)normal.Z), $"{name} 法向 Z");
            Assert.That(reader.ReadSingle(), Is.EqualTo((float)a.X), $"{name} 顶点 A.X");
            Assert.That(reader.ReadSingle(), Is.EqualTo((float)a.Y), $"{name} 顶点 A.Y");
            Assert.That(reader.ReadSingle(), Is.EqualTo((float)a.Z), $"{name} 顶点 A.Z");
            Assert.That(reader.ReadSingle(), Is.EqualTo((float)b.X), $"{name} 顶点 B.X");
            Assert.That(reader.ReadSingle(), Is.EqualTo((float)b.Y), $"{name} 顶点 B.Y");
            Assert.That(reader.ReadSingle(), Is.EqualTo((float)b.Z), $"{name} 顶点 B.Z");
            Assert.That(reader.ReadSingle(), Is.EqualTo((float)c.X), $"{name} 顶点 C.X");
            Assert.That(reader.ReadSingle(), Is.EqualTo((float)c.Y), $"{name} 顶点 C.Y");
            Assert.That(reader.ReadSingle(), Is.EqualTo((float)c.Z), $"{name} 顶点 C.Z");
            Assert.That(reader.ReadUInt16(), Is.EqualTo((ushort)0), $"{name} 属性字节数 = 0");
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  StlWriter：ASCII 内容级
        // ══════════════════════════════════════════════════════════════════════════

        [Test]
        public void Write_Ascii_ExactLineStructure_DefaultHeader()
        {
            var path = TempPath("ascii-lines.stl");
            try
            {
                StlWriter.Write(path, TwoTriangles(), StlFormat.Ascii);

                var text = File.ReadAllText(path).Replace("\r\n", "\n");
                var expected = string.Join("\n", new[]
                {
                    "solid STL export",
                    "  facet normal 0 0 1",
                    "    outer loop",
                    "      vertex 0 0 0",
                    "      vertex 1 0 0",
                    "      vertex 0 1 0",
                    "    endloop",
                    "  endfacet",
                    "  facet normal 1 0 0",
                    "    outer loop",
                    "      vertex 0 0 0",
                    "      vertex 0 2 0",
                    "      vertex 0 0 2",
                    "    endloop",
                    "  endfacet",
                    "endsolid STL export",
                    "",
                });
                Assert.That(text, Is.EqualTo(expected), "ASCII 逐行结构（solid/facet/outer loop/vertex/endloop/endfacet/endsolid）");
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void Write_Ascii_FractionalCoordinates_SixDecimalsInvariant()
        {
            var path = TempPath("ascii-frac.stl");
            try
            {
                var triangle = new MeshTriangle(
                    new MeshPoint(0.5, -1.25, 1.0 / 3.0),
                    new MeshPoint(1.5, -1.25, 1.0 / 3.0),
                    new MeshPoint(0.5, -0.25, 1.0 / 3.0));
                StlWriter.WriteAscii(path, new[] { triangle }, "frac");

                var text = File.ReadAllText(path).Replace("\r\n", "\n");
                Assert.That(text, Does.Contain("      vertex 0.5 -1.25 0.333333"),
                    "{0:0.######} 六位小数、InvariantCulture 小数点");
                Assert.That(text, Does.Contain("  facet normal 0 0 1"), "面法向同格式");
                Assert.That(text, Does.StartWith("solid frac\n"), "自定义头完整写出");
                Assert.That(text, Does.EndWith("endsolid frac\n"), "结束行回写同名");
            }
            finally { File.Delete(path); }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  StlWriter.NormalOf：手推法向
        // ══════════════════════════════════════════════════════════════════════════

        [Test]
        public void NormalOf_NonAxisAlignedTriangle_HandComputed_AndWindingSign()
        {
            // u = B−A = (1,1,0)、v = C−A = (0,1,1) ⇒ u×v = (1,−1,1)，|n| = √3。
            var a = new MeshPoint(0, 0, 0);
            var b = new MeshPoint(1, 1, 0);
            var c = new MeshPoint(0, 1, 1);

            var normal = StlWriter.NormalOf(new MeshTriangle(a, b, c));
            Assert.That(normal.X, Is.EqualTo(1.0 / Math.Sqrt(3)).Within(1e-12), "NormalOf X = +1/√3");
            Assert.That(normal.Y, Is.EqualTo(-1.0 / Math.Sqrt(3)).Within(1e-12), "NormalOf Y = −1/√3");
            Assert.That(normal.Z, Is.EqualTo(1.0 / Math.Sqrt(3)).Within(1e-12), "NormalOf Z = +1/√3");

            // 交换 B/C ⇒ 绕序反转 ⇒ 法向取反。
            var flipped = StlWriter.NormalOf(new MeshTriangle(a, c, b));
            Assert.That(flipped.X, Is.EqualTo(-1.0 / Math.Sqrt(3)).Within(1e-12), "绕序反转后法向取反 X");
            Assert.That(flipped.Y, Is.EqualTo(1.0 / Math.Sqrt(3)).Within(1e-12), "绕序反转后法向取反 Y");
            Assert.That(flipped.Z, Is.EqualTo(-1.0 / Math.Sqrt(3)).Within(1e-12), "绕序反转后法向取反 Z");
        }

        [Test]
        public void NormalOf_DegenerateTriangle_ReturnsZeroVector()
        {
            // 三点共线（u ∥ v）⇒ 叉积 ≈ 0 ⇒ 按 STL 约定返回零法向（读入方可自行忽略）。
            var degenerate = new MeshTriangle(new MeshPoint(0, 0, 0), new MeshPoint(1, 1, 1), new MeshPoint(2, 2, 2));
            var normal = StlWriter.NormalOf(degenerate);

            Assert.That(normal.X, Is.EqualTo(0.0), "退化三角形法向 X = 0");
            Assert.That(normal.Y, Is.EqualTo(0.0), "退化三角形法向 Y = 0");
            Assert.That(normal.Z, Is.EqualTo(0.0), "退化三角形法向 Z = 0");
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  ViewMeshReader：ASCII
        // ══════════════════════════════════════════════════════════════════════════

        [Test]
        public void ReadStlFile_Ascii_KnownTriangle_VertexValues_AndNormalRecomputedFromWinding()
        {
            // 文本里故意写错误的 facet normal (0,0,−1)；顶点绕序为 CCW ⇒ 读回法向必须重算为 (0,0,1)。
            var path = TempPath("read-ascii.stl");
            try
            {
                File.WriteAllText(path, string.Join("\n", new[]
                {
                    "solid hand",
                    "  facet normal 0 0 -1",
                    "    outer loop",
                    "      vertex 1 1 1",
                    "      vertex 3 1 1",
                    "      vertex 1 3 1",
                    "    endloop",
                    "  endfacet",
                    "endsolid hand",
                    "",
                }));

                var triangles = ViewMeshReader.ReadStlFile(path);

                Assert.That(triangles.Count, Is.EqualTo(1), "手工 ASCII 应读回 1 个三角形");
                var t = triangles[0];
                Assert.That(t.A.X, Is.EqualTo(1.0), "A.X");
                Assert.That(t.A.Y, Is.EqualTo(1.0), "A.Y");
                Assert.That(t.A.Z, Is.EqualTo(1.0), "A.Z");
                Assert.That(t.B.X, Is.EqualTo(3.0), "B.X");
                Assert.That(t.C.Y, Is.EqualTo(3.0), "C.Y");

                var n = t.Normal;
                Assert.That(n.X, Is.EqualTo(0.0).Within(1e-12), "读回法向按顶点绕序重算（忽略文件法向）");
                Assert.That(n.Y, Is.EqualTo(0.0).Within(1e-12), "读回法向 Y");
                Assert.That(n.Z, Is.EqualTo(1.0).Within(1e-12), "读回法向 +Z（文件写的 −1 未被采信）");
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void ReadStlFile_Ascii_TwoFacets_PlusDanglingVertex_ProducesTwoTriangles()
        {
            var path = TempPath("read-ascii-2.stl");
            try
            {
                File.WriteAllText(path, string.Join("\n", new[]
                {
                    "solid pairs",
                    "facet normal 0 0 1",
                    "outer loop",
                    "vertex 0 0 0",
                    "vertex 4 0 0",
                    "vertex 0 4 0",
                    "endloop",
                    "endfacet",
                    "facet normal 0 0 1",
                    "outer loop",
                    "vertex 10 0 0",
                    "vertex 12 0 0",
                    "vertex 10 2 0",
                    "endloop",
                    "endfacet",
                    "vertex 99 99 99",   // 悬空第 4 点：凑不满 3 点，必须被忽略
                    "endsolid pairs",
                    "",
                }));

                var triangles = ViewMeshReader.ReadStlFile(path);

                Assert.That(triangles.Count, Is.EqualTo(2), "2 个完整 facet + 1 个悬空顶点 ⇒ 恰 2 个三角形");
                Assert.That(triangles[1].B.X, Is.EqualTo(12.0), "第 2 个三角形取到正确的顶点组");
                Assert.That(triangles[1].C.Y, Is.EqualTo(2.0), "第 2 个三角形取到正确的顶点组");
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void ReadStlFile_Ascii_NumericFormats_ScientificSignsTabsWhitespace()
        {
            var path = TempPath("read-ascii-num.stl");
            try
            {
                File.WriteAllText(path, string.Join("\n", new[]
                {
                    "solid numbers",
                    "\tvertex\t1e-3  -2.5 +3.5",       // 科学计数法 / 前导 +− / 制表符 / 多空格
                    "  vertex    2.5E2 3 4",           // 大写指数 / 行首空白
                    "vertex -0.125 6.25 -7.5",         // 负小数
                    "endsolid numbers",
                    "",
                }));

                var triangles = ViewMeshReader.ReadStlFile(path);

                Assert.That(triangles.Count, Is.EqualTo(1), "三行 vertex 组成 1 个三角形");
                var t = triangles[0];
                Assert.That(t.A.X, Is.EqualTo(1e-3), "科学计数法 1e-3");
                Assert.That(t.A.Y, Is.EqualTo(-2.5), "负号 -2.5");
                Assert.That(t.A.Z, Is.EqualTo(3.5), "前导 +3.5");
                Assert.That(t.B.X, Is.EqualTo(250.0), "大写指数 2.5E2");
                Assert.That(t.C.X, Is.EqualTo(-0.125), "负小数 -0.125");
                Assert.That(t.C.Z, Is.EqualTo(-7.5), "负小数 -7.5");
            }
            finally { File.Delete(path); }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  ViewMeshReader：二进制 + IsBinaryStl 判定
        // ══════════════════════════════════════════════════════════════════════════

        [Test]
        public void ReadStlFile_Binary_RoundTrip_ExactVertexValues()
        {
            var path = TempPath("roundtrip.stl");
            try
            {
                var triangles = new[]
                {
                    new MeshTriangle(new MeshPoint(0.5, 0, 0), new MeshPoint(1.5, 0, 0), new MeshPoint(0.5, 1, 0)),
                    new MeshTriangle(new MeshPoint(0, 0, 0), new MeshPoint(0, 2.25, 0), new MeshPoint(0, 0, 2)),
                };
                StlWriter.WriteBinary(path, triangles);

                var read = ViewMeshReader.ReadStlFile(path);

                Assert.That(read.Count, Is.EqualTo(2), "二进制往返三角形数");
                Assert.That(read[0].A.X, Is.EqualTo(0.5), "顶点 A.X 精确往返（0.5 可被 float 精确表示）");
                Assert.That(read[0].B.X, Is.EqualTo(1.5), "顶点 B.X 精确往返");
                Assert.That(read[1].B.Y, Is.EqualTo(2.25), "顶点 B.Y 精确往返");
                Assert.That(read[1].C.Z, Is.EqualTo(2.0), "顶点 C.Z 精确往返");

                var n = read[0].Normal;
                Assert.That(n.Z, Is.EqualTo(1.0).Within(1e-6), "读回后法向由绕序重算 = +Z");
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void ReadStlFile_BinaryHeaderStartingWithSolid_StillDetectedAsBinary()
        {
            // 头以 "solid" 起头是本文件长度的**反例诱饵**：判定必须按 84 + 50×面数 == 文件长度 走二进制，
            // 若误入 ASCII 分支则读不到 vertex 行 ⇒ 0 个三角形。
            var path = TempPath("solid-header.stl");
            try
            {
                StlWriter.WriteBinary(path, TwoTriangles(), "solid fake header");

                var read = ViewMeshReader.ReadStlFile(path);

                Assert.That(new FileInfo(path).Length, Is.EqualTo(184L), "前置：长度满足 84 + 50×2");
                Assert.That(read.Count, Is.EqualTo(2), "\"solid\" 开头但长度吻合二进制 ⇒ 仍按二进制解析");
                Assert.That(read[0].C.Y, Is.EqualTo(1.0), "二进制顶点读回");
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void ReadStlFile_EightyFourBytesWithZeroCount_IsBinaryWithNoTriangles()
        {
            var path = TempPath("empty-binary.stl");
            try
            {
                File.WriteAllBytes(path, new byte[84]); // 80 头(零) + uint32 0

                var triangles = ViewMeshReader.ReadStlFile(path);

                Assert.That(triangles.Count, Is.EqualTo(0), "84 字节且面数 0 ⇒ 合法空二进制");
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void ReadStlFile_LengthMismatchWithBinaryContent_FallsBackToAscii_NoTriangles()
        {
            // 合法二进制（134 字节）后再附 1 字节 ⇒ 135 ≠ 84+50×1 ⇒ 按判定回退 ASCII 解析，
            // 二进制字节流里没有 vertex 行 ⇒ 0 个三角形（长度判定是唯一分流依据）。
            var path = TempPath("mismatch.stl");
            try
            {
                StlWriter.WriteBinary(path, new[] { TwoTriangles()[0] });
                using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write))
                    stream.WriteByte(0x00);

                Assert.That(new FileInfo(path).Length, Is.EqualTo(135L), "前置：长度被破坏");
                var triangles = ViewMeshReader.ReadStlFile(path);

                Assert.That(triangles.Count, Is.EqualTo(0), "长度不符 ⇒ 回退 ASCII，无 vertex 行 ⇒ 0 三角形");
            }
            finally { File.Delete(path); }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  Formats：另一条 STL 写出通道（TextWriter / BinaryWriter）
        //  与 StlWriter 的口径差异（记录在案，勿混用断言）：无缩进、法向取多边形平面
        //  （不重算）、坐标用 {0} 默认精度（无 0.###### 截断）、二进制头遇 "solid" 前缀改写 "stlbin"。
        // ══════════════════════════════════════════════════════════════════════════

        [Test]
        public void Formats_WriteStl_TextWriter_CubeStructure_PlaneNormalsMatchVertices()
        {
            var cube = Solids.Cube(2, center: true); // 半边长 1，六个面到原点距离 1

            var writer = new StringWriter();
            cube.WriteStl("unit", writer);
            var text = writer.ToString().Replace("\r\n", "\n");

            Assert.That(text, Does.StartWith("solid unit\n"), "首行 = solid <name>");
            Assert.That(text, Does.EndWith("endsolid unit\n"), "末行 = endsolid <name>");

            var facets = ParseAsciiFacets(text);
            Assert.That(facets.Count, Is.EqualTo(12), "6 个四边形面 × (4−2) 扇形三角 = 12 facet");

            // 逐 facet：写出的法向 = 该多边形平面法向（不重算），且三顶点都在该面上（dot(v,n) = 1）。
            foreach (var (normal, vertices) in facets)
            {
                Assert.That(vertices.Count, Is.EqualTo(3), "每 facet 恰 3 个 vertex 行");
                foreach (var v in vertices)
                {
                    Assert.That(v.Dot(normal), Is.EqualTo(1.0).Within(1e-9),
                        $"顶点 {v} 不在法向 {normal} 的面上（Formats 用法向 = 平面法向）");
                }
            }

            // 六个 ±轴向各出现两次（每面 2 个三角形共用同一平面法向）。
            foreach (var axis in new[]
            {
                new Vector3D(1, 0, 0), new Vector3D(-1, 0, 0),
                new Vector3D(0, 1, 0), new Vector3D(0, -1, 0),
                new Vector3D(0, 0, 1), new Vector3D(0, 0, -1),
            })
            {
                int count = facets.Count(f => f.Normal.Dot(axis) > 1 - 1e-12);
                Assert.That(count, Is.EqualTo(2), $"轴向 {axis} 的 facet 数应为 2");
            }
        }

        [Test]
        public void Formats_ToStlStringAndVectorFormatting_NoIndentDocumentedDifference()
        {
            // 扩展方法 ToStlString 的坐标格式：InvariantCulture 默认精度 "{0} {1} {2}"。
            Assert.That(new Vector3D(1.5, -2, 0).ToStlString(), Is.EqualTo("1.5 -2 0"), "Vector3D.ToStlString");
            Assert.That(new Vertex(new Vector3D(1.5, -2, 0), new Vector2D(0, 0)).ToStlString(),
                Is.EqualTo("vertex 1.5 -2 0"), "Vertex.ToStlString");

            // 与 StlWriter 的 ASCII 口径差异：Formats 的 facet 行**无缩进**（且不经 0.###### 截断）。
            var text = Solids.Cube(2, center: true).ToStlString("unit").Replace("\r\n", "\n");
            Assert.That(text, Does.StartWith("solid unit\n"), "ToStlString = WriteStl 到 StringWriter");
            Assert.That(text, Does.Contain("\nfacet normal "), "Formats facet 行无前导空格");
            Assert.That(text, Does.Not.Contain("  facet normal"), "无 StlWriter 式两空格缩进");
        }

        [Test]
        public void Formats_WriteStl_BinaryWriter_HeaderCountRecords_PerRecordPlaneNormal()
        {
            var path = TempPath("formats-bin.stl");
            try
            {
                var cube = Solids.Cube(2, center: true);
                using (var writer = new BinaryWriter(File.Create(path)))
                {
                    cube.WriteStl("part", writer);
                }

                Assert.That(new FileInfo(path).Length, Is.EqualTo(84L + 50L * 12L),
                    "二进制长度与 StlWriter 同公式：80 头 + 4 面数 + 50×12");

                using var reader = new BinaryReader(File.OpenRead(path));
                var header = reader.ReadBytes(80);
                Assert.That(Encoding.ASCII.GetString(header, 0, 4), Is.EqualTo("part"), "头非 solid 前缀时原样写名");
                Assert.That(header[4], Is.EqualTo((byte)0), "其余头字节补零");
                Assert.That(reader.ReadUInt32(), Is.EqualTo(12u), "面数 12");

                for (int i = 0; i < 12; i++)
                {
                    var n = new Vector3D(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    var a = new Vector3D(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    var b = new Vector3D(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    var c = new Vector3D(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    Assert.That(reader.ReadUInt16(), Is.EqualTo((ushort)0), $"第 {i} 条属性字节数");

                    Assert.That(a.Dot(n), Is.EqualTo(1.0).Within(1e-6), $"第 {i} 条顶点 A 在平面法向面上");
                    Assert.That(b.Dot(n), Is.EqualTo(1.0).Within(1e-6), $"第 {i} 条顶点 B 在平面法向面上");
                    Assert.That(c.Dot(n), Is.EqualTo(1.0).Within(1e-6), $"第 {i} 条顶点 C 在平面法向面上");
                }
                Assert.That(reader.BaseStream.Position, Is.EqualTo(684L), "恰好写满文件");
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void Formats_WriteStl_BinaryWriter_SolidPrefixedName_GetsStlbinPrefix()
        {
            // 头以 "solid" 起头会让部分软件误判 ASCII ⇒ Formats 改写为 "stlbin" + 原名。
            var path = TempPath("formats-stlbin.stl");
            try
            {
                using (var writer = new BinaryWriter(File.Create(path)))
                {
                    Solids.Cube(2, center: true).WriteStl("solid thing", writer);
                }

                var header = new byte[80];
                using (var stream = File.OpenRead(path))
                    stream.ReadExactly(header, 0, 80);

                Assert.That(Encoding.ASCII.GetString(header, 0, 6), Is.EqualTo("stlbin"), "solid 前缀改写 stlbin");
                Assert.That(Encoding.ASCII.GetString(header, 6, 11), Is.EqualTo("solid thing"), "原名完整保留在 stlbin 之后");
                Assert.That(header[17], Is.EqualTo((byte)0), "其余补零");
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void Formats_WriteStl_BinaryWriter_ViewMeshReaderRoundTrip_OutwardNormals()
        {
            var path = TempPath("formats-roundtrip.stl");
            try
            {
                using (var writer = new BinaryWriter(File.Create(path)))
                {
                    Solids.Cube(2, center: true).WriteStl("part", writer);
                }

                var triangles = ViewMeshReader.ReadStlFile(path);

                Assert.That(triangles.Count, Is.EqualTo(12), "Formats 写出的二进制可被 ViewMeshReader 读回");
                double minX = double.MaxValue, maxX = double.MinValue;
                foreach (var t in triangles)
                {
                    foreach (var p in new[] { t.A, t.B, t.C })
                    {
                        minX = Math.Min(minX, p.X);
                        maxX = Math.Max(maxX, p.X);
                    }

                    // 单位立方体：三角形质心 · 外法向 > 0（绕序朝外）。
                    var centroid = new ViewPoint((t.A.X + t.B.X + t.C.X) / 3, (t.A.Y + t.B.Y + t.C.Y) / 3, (t.A.Z + t.B.Z + t.C.Z) / 3);
                    Assert.That(centroid.Dot(t.Normal), Is.GreaterThan(0.0), "读回三角形外法向朝外");
                }
                Assert.That(minX, Is.EqualTo(-1.0).Within(1e-6), "读回包络 −1");
                Assert.That(maxX, Is.EqualTo(1.0).Within(1e-6), "读回包络 +1");
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void Formats_Polygon_WriteStl_TooFewVertices_WritesNothing()
        {
            // 顶点数 < 3 的多边形写出 0 个 facet（不抛错、不写半行）。
            var polygon = new Polygon(
                new List<Vertex>
                {
                    new(new Vector3D(0, 0, 0), new Vector2D(0, 0)),
                    new(new Vector3D(1, 0, 0), new Vector2D(0, 0)),
                },
                shared: null,
                plane: new Plane(new Vector3D(0, 0, 1), 0));

            var writer = new StringWriter();
            polygon.WriteStl(writer);

            Assert.That(writer.ToString(), Is.Empty, "退化多边形（2 顶点）不写任何 facet 行");
        }

        /// <summary>解析 Formats ASCII 输出的 facet 段（法向 + 三个顶点），坐标系按 InvariantCulture。</summary>
        static List<(Vector3D Normal, List<Vector3D> Vertices)> ParseAsciiFacets(string text)
        {
            var facets = new List<(Vector3D, List<Vector3D>)>();
            Vector3D? normal = null;
            var vertices = new List<Vector3D>();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("facet normal ", StringComparison.Ordinal))
                {
                    var parts = line.Substring("facet normal ".Length)
                        .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    normal = new Vector3D(Parse(parts[0]), Parse(parts[1]), Parse(parts[2]));
                    vertices = new List<Vector3D>();
                }
                else if (line.StartsWith("vertex ", StringComparison.Ordinal))
                {
                    var parts = line.Substring("vertex ".Length)
                        .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    vertices.Add(new Vector3D(Parse(parts[0]), Parse(parts[1]), Parse(parts[2])));
                }
                else if (line == "endfacet" && normal is Vector3D n)
                {
                    facets.Add((n, vertices));
                    normal = null;
                }
            }
            return facets;
        }

        static double Parse(string text) => double.Parse(text, CultureInfo.InvariantCulture);
    }
}
