using System;
using System.IO;
using System.Text;

namespace Csg
{
    public static class Formats
    {
        static readonly IFormatProvider icult = System.Globalization.CultureInfo.InvariantCulture;

        public static string ToStlString(this Solid csg, string name)
        {
            var w = new StringWriter();
            WriteStl(csg, name, w);
            return w.ToString();
        }

        public static void WriteStl(this Solid csg, string name, TextWriter writer)
        {
            writer.Write("solid ");
            writer.WriteLine(name);
            foreach (var p in csg.Polygons)
            {
                WriteStl(p, writer);
            }
            writer.Write("endsolid ");
            writer.WriteLine(name);
        }

        public static void WriteStl(this Polygon polygon, TextWriter writer)
        {
            if (polygon.Vertices.Count >= 3)
            {
                var firstVertexStl = polygon.Vertices[0].ToStlString();
                for (var i = 0; i < polygon.Vertices.Count - 2; i++)
                {
                    writer.WriteLine("facet normal " + polygon.Plane.Normal.ToStlString());
                    writer.WriteLine("outer loop");
                    writer.WriteLine(firstVertexStl);
                    writer.WriteLine(polygon.Vertices[i + 1].ToStlString());
                    writer.WriteLine(polygon.Vertices[i + 2].ToStlString());
                    writer.WriteLine("endloop");
                    writer.WriteLine("endfacet");
                }
            }
        }

        public static string ToStlString(this Vector3D vector)
        {
            return string.Format(icult, "{0} {1} {2}", vector.X, vector.Y, vector.Z);
        }

        public static string ToStlString(this Vertex vertex)
        {
            return string.Format(icult, "vertex {0} {1} {2}", vertex.Pos.X, vertex.Pos.Y, vertex.Pos.Z);
        }

        public static void WriteStl(this Solid csg, string name, BinaryWriter writer)
        {
            // https://en.wikipedia.org/wiki/STL_(file_format)#Binary_STL
            // UINT8[80]    – Header                 -     80 bytes
            // UINT32       – Number of triangles    -      4 bytes
            // foreach triangle                      - 50 bytes:
            //     REAL32[3] – Normal vector             - 12 bytes
            //     REAL32[3] – Vertex 1                  - 12 bytes
            //     REAL32[3] – Vertex 2                  - 12 bytes
            //     REAL32[3] – Vertex 3                  - 12 bytes
            //     UINT16    – Attribute byte count      -  2 bytes
            // end


            // Write header
            var header = new byte[80];
            var hi = 0;

            // https://en.wikipedia.org/wiki/STL_(file_format)#Binary_STL
            // A binary STL file has an 80-character header which is generally ignored,
            // but should never begin with the ASCII representation of the string solid,
            // as that may lead some software to confuse it with an ASCII STL file.
            if (name.StartsWith("solid", StringComparison.OrdinalIgnoreCase))
            {
                // Have header start with "stlbin"
                header[0] = (byte)'s'; header[1] = (byte)'t'; header[2] = (byte)'l';
                header[3] = (byte)'b'; header[4] = (byte)'i'; header[5] = (byte)'n';
                hi += 6;
            }

            Encoding.ASCII.GetBytes(name, 0, Math.Min(name.Length, header.Length - hi), header, hi);
            writer.Write(header);

            var numTris = 0u;
            foreach (var p in csg.Polygons)
            {
                if (p.Vertices.Count >= 3)
                {
                    numTris += (uint)p.Vertices.Count - 2;
                }
            }
            writer.Write(numTris);

            void writeVec(Vector3D v)
            {
                writer.Write((float)v.X);
                writer.Write((float)v.Y);
                writer.Write((float)v.Z);
            }

            foreach (var p in csg.Polygons)
            {
                if (p.Vertices.Count >= 3)
                {
                    for (var i = 0; i < p.Vertices.Count - 2; i++)
                    {
                        writeVec(p.Plane.Normal);
                        writeVec(p.Vertices[0].Pos);
                        writeVec(p.Vertices[i + 1].Pos);
                        writeVec(p.Vertices[i + 2].Pos);
                        writer.Write((ushort)0);
                    }
                }
            }
        }
    }
}
