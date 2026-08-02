using System;
using System.Collections.Generic;
using System.Linq;

namespace Csg
{
    /// <summary>CSG node tree evaluation exception</summary>
    public class CsgEvaluationException : Exception
    {
        public CsgEvaluationException(string message) : base(message) { }
        public CsgEvaluationException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Evaluates a CsgNode tree into a Solid.</summary>
    public static class CsgEvaluator
    {
        public static Solid Evaluate(CsgNode node)
        {
            return node switch
            {
                BoxNode n => Solids.Cube(n.Size, n.Center),
                SphereNode n => Solids.Sphere(n.Radius, n.Center),
                CylinderNode n => Solids.Cylinder(n.Radius, n.Height, true),
                ConeNode n => throw new CsgEvaluationException("Cone not yet supported (requires Solid API investigation)"),
                UnionNode n => EvaluateBool(n.Children, (a, b) => a.Union(b)),
                SubtractNode n => EvaluateBool(n.Children, (a, b) => a.Subtract(b)),
                IntersectNode n => EvaluateBool(n.Children, (a, b) => a.Intersect(b)),
                TransformNode n => ApplyTransform(Evaluate(n.Child), n.Translation, n.Rotation),
                ExtrudeNode n => EvaluateExtrude(n),
                WedgeNode n => EvaluateWedge(n),
                _ => throw new CsgEvaluationException($"Unknown node type: {node.GetType().Name}")
            };
        }

        public static IReadOnlyList<Solid> EvaluateAll(IEnumerable<CsgNode> nodes)
        {
            return nodes.Select(Evaluate).ToList();
        }

        private static Solid EvaluateBool(List<CsgNode> children, Func<Solid, Solid, Solid> op)
        {
            if (children == null || children.Count == 0)
                throw new CsgEvaluationException("Boolean node requires at least one child");

            var solids = children.Select(Evaluate).ToList();
            var result = solids[0];
            for (int i = 1; i < solids.Count; i++)
                result = op(result, solids[i]);
            return result;
        }

        private static Solid ApplyTransform(Solid solid, Vector3D translation, Vector3D rotation)
        {
            var result = solid;
            if (translation.X != 0 || translation.Y != 0 || translation.Z != 0)
                result = result.Translate(translation);
            if (rotation.X != 0)
                result = result.RotateX(rotation.X);
            if (rotation.Y != 0)
                result = result.RotateY(rotation.Y);
            if (rotation.Z != 0)
                result = result.RotateZ(rotation.Z);
            return result;
        }

        static Vertex V(double x, double y, double z) => new Vertex(new Vector3D(x, y, z), new Vector2D(0, 0));

        /// <summary>Expand a Profile2D into a list of 2D polygon vertices (counter-clockwise).</summary>
        static List<Vector2D> ExpandProfile(Profile2D profile)
        {
            switch (profile)
            {
                case RectangleProfile p:
                    var hw = p.Width / 2;
                    var hh = p.Height / 2;
                    return new List<Vector2D> {
                        new Vector2D(-hw, -hh), new Vector2D( hw, -hh),
                        new Vector2D( hw,  hh), new Vector2D(-hw,  hh)
                    };

                case HBeamProfile p:
                {
                    var hwB = p.FlangeWidth / 2;
                    var hhB = (p.WebHeight + 2 * p.FlangeThickness) / 2;
                    var iw = p.WebThickness / 2;
                    var ih = p.WebHeight / 2;
                    return new List<Vector2D> {
                        new Vector2D(-hwB, -hhB), new Vector2D( hwB, -hhB),
                        new Vector2D( hwB, -ih), new Vector2D( iw, -ih),
                        new Vector2D( iw,  ih), new Vector2D( hwB,  ih),
                        new Vector2D( hwB,  hhB), new Vector2D(-hwB,  hhB),
                        new Vector2D(-hwB,  ih), new Vector2D(-iw,  ih),
                        new Vector2D(-iw, -ih), new Vector2D(-hwB, -ih)
                    };
                }

                case ChannelProfile p:
                {
                    var hwC = p.FlangeWidth;
                    var hhC = (p.WebHeight + 2 * p.FlangeThickness) / 2;
                    var ft = p.FlangeThickness;
                    var wt = p.WebThickness;
                    return new List<Vector2D> {
                        new Vector2D(0,    -hhC),       new Vector2D(hwC, -hhC),
                        new Vector2D(hwC,   -hhC + ft),  new Vector2D(wt, -hhC + ft),
                        new Vector2D(wt,   -hhC + ft + p.WebHeight),
                        new Vector2D(hwC,   -hhC + ft + p.WebHeight),
                        new Vector2D(hwC,    hhC),       new Vector2D(0,   hhC)
                    };
                }

                case SquareTubeProfile p:
                {
                    var hwS = p.Width / 2;
                    var hhS = p.Height / 2;
                    return new List<Vector2D> {
                        new Vector2D(-hwS, -hhS), new Vector2D( hwS, -hhS),
                        new Vector2D( hwS,  hhS), new Vector2D(-hwS,  hhS)
                    };
                }

                case TrapezoidProfile p:
                {
                    var hwTop = p.TopWidth / 2;
                    var hwBot = p.BottomWidth / 2;
                    var h = p.Height;
                    return new List<Vector2D> {
                        new Vector2D(-hwBot, 0), new Vector2D( hwBot, 0),
                        new Vector2D( hwTop, h), new Vector2D(-hwTop, h)
                    };
                }

                case CapsuleProfile p:
                {
                    var pts = new List<Vector2D>();
                    int segs = 16;
                    double r = p.Radius;
                    double halfW = p.RectWidth / 2;
                    for (int i = 0; i <= segs; i++)
                    {
                        double angle = -Math.PI / 2 + Math.PI * i / segs;
                        pts.Add(new Vector2D(halfW + r * Math.Cos(angle), r * Math.Sin(angle)));
                    }
                    for (int i = 0; i <= segs; i++)
                    {
                        double angle = Math.PI / 2 + Math.PI * i / segs;
                        pts.Add(new Vector2D(-halfW + r * Math.Cos(angle), r * Math.Sin(angle)));
                    }
                    return pts;
                }

                case LShapeProfile p:
                {
                    return new List<Vector2D> {
                        new Vector2D(0, 0), new Vector2D(p.Horizontal, 0),
                        new Vector2D(p.Horizontal, p.Thickness),
                        new Vector2D(p.Thickness, p.Thickness),
                        new Vector2D(p.Thickness, p.Vertical),
                        new Vector2D(0, p.Vertical)
                    };
                }

                default:
                    throw new InvalidOperationException(
                        $"Unknown Profile2D type: {profile.GetType().Name}");
            }
        }

        /// <summary>Triangulate a simple polygon using ear-clipping. Vertices must be CCW.</summary>
        static List<(int, int, int)> Triangulate(List<Vector2D> polygon)
        {
            var indices = new List<int>();
            for (int i = 0; i < polygon.Count; i++) indices.Add(i);
            var tris = new List<(int, int, int)>();
            int safety = polygon.Count * 3;

            while (indices.Count > 3 && safety-- > 0)
            {
                bool earFound = false;
                for (int i = 0; i < indices.Count; i++)
                {
                    int prev = indices[(i - 1 + indices.Count) % indices.Count];
                    int curr = indices[i];
                    int next = indices[(i + 1) % indices.Count];

                    if (IsConvex(polygon[prev], polygon[curr], polygon[next]) &&
                        !HasPointInside(polygon, indices, prev, curr, next))
                    {
                        tris.Add((prev, curr, next));
                        indices.RemoveAt(i);
                        earFound = true;
                        break;
                    }
                }
                if (!earFound) break;
            }
            if (indices.Count == 3)
                tris.Add((indices[0], indices[1], indices[2]));

            return tris;
        }

        static bool IsConvex(Vector2D a, Vector2D b, Vector2D c)
            => Cross2D(b - a, c - b) >= 0;

        static double Cross2D(Vector2D a, Vector2D b) => a.X * b.Y - a.Y * b.X;

        static bool HasPointInside(List<Vector2D> poly, List<int> indices, int prev, int curr, int next)
        {
            var a = poly[prev];
            var b = poly[curr];
            var c = poly[next];
            foreach (var i in indices)
            {
                if (i == prev || i == curr || i == next) continue;
                if (PointInTriangle(poly[i], a, b, c)) return true;
            }
            return false;
        }

        static bool PointInTriangle(Vector2D p, Vector2D a, Vector2D b, Vector2D c)
        {
            double d1 = Sign2D(p, a, b);
            double d2 = Sign2D(p, b, c);
            double d3 = Sign2D(p, c, a);
            bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
            return !(hasNeg && hasPos);
        }

        static double Sign2D(Vector2D p1, Vector2D p2, Vector2D p3)
            => (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y);

        private static Solid EvaluateExtrude(ExtrudeNode n)
        {
            if (n.Height <= 0)
                throw new CsgEvaluationException($"Extrude height must be positive (Height={n.Height})");

            var pts2D = ExpandProfile(n.Profile);
            var tris = Triangulate(pts2D);
            if (tris.Count == 0)
                throw new CsgEvaluationException("Extrude: triangulation produced no triangles");

            var polygons = new List<Polygon>();
            double zBottom = 0;
            double zTop = n.Height;

            // Bottom triangles (normal toward -Z)
            foreach (var (i0, i1, i2) in tris)
            {
                var v0 = V(pts2D[i0].X, pts2D[i0].Y, zBottom);
                var v1 = V(pts2D[i1].X, pts2D[i1].Y, zBottom);
                var v2 = V(pts2D[i2].X, pts2D[i2].Y, zBottom);
                polygons.Add(new Polygon(new List<Vertex> { v2, v1, v0 }));
            }

            // Top triangles (normal toward +Z)
            foreach (var (i0, i1, i2) in tris)
            {
                var v0 = V(pts2D[i0].X, pts2D[i0].Y, zTop);
                var v1 = V(pts2D[i1].X, pts2D[i1].Y, zTop);
                var v2 = V(pts2D[i2].X, pts2D[i2].Y, zTop);
                polygons.Add(new Polygon(new List<Vertex> { v0, v1, v2 }));
            }

            // Side quadrilaterals along outer contour edges
            int count = pts2D.Count;
            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                var b0 = V(pts2D[i].X, pts2D[i].Y, zBottom);
                var b1 = V(pts2D[j].X, pts2D[j].Y, zBottom);
                var t0 = V(pts2D[i].X, pts2D[i].Y, zTop);
                var t1 = V(pts2D[j].X, pts2D[j].Y, zTop);
                polygons.Add(new Polygon(new List<Vertex> { b0, b1, t1, t0 }));
            }

            return Solid.FromPolygons(polygons);
        }

        private static Solid EvaluateWedge(WedgeNode n)
        {
            if (n.Width <= 0 || n.Depth <= 0 || n.Height <= 0)
                throw new CsgEvaluationException(
                    $"Wedge dimensions must be positive (Width={n.Width}, Depth={n.Depth}, Height={n.Height})");

            double hx = n.Width / 2;
            double hy = n.Depth / 2;
            double h = n.Height;

            var cx = n.Corner.X;
            var cy = n.Corner.Y;
            var cz = n.Corner.Z;

            var b0 = V(cx - hx, cy - hy, cz);
            var b1 = V(cx + hx, cy - hy, cz);
            var b2 = V(cx + hx, cy + hy, cz);
            var b3 = V(cx - hx, cy + hy, cz);

            var t0 = V(cx - hx, cy, cz + h);
            var t1 = V(cx + hx, cy, cz + h);

            var polygons = new List<Polygon>();

            // Bottom face (normal points -Z)
            polygons.Add(new Polygon(new List<Vertex> { b3, b2, b1, b0 }));

            // Front slope face (b0-b1-t1-t0)
            polygons.Add(new Polygon(new List<Vertex> { b0, b1, t1, t0 }));

            // Back slope face (b2-b3-t0-t1)
            polygons.Add(new Polygon(new List<Vertex> { b2, b3, t0, t1 }));

            // Left triangle (b3-b0-t0)
            polygons.Add(new Polygon(new List<Vertex> { b3, b0, t0 }));

            // Right triangle (b1-b2-t1)
            polygons.Add(new Polygon(new List<Vertex> { b1, b2, t1 }));

            return Solid.FromPolygons(polygons);
        }
    }
}
