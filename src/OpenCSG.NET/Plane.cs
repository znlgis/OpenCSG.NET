using System;
using System.Collections.Generic;

namespace Csg
{
    public class Plane : IEquatable<Plane>
    {
        const double EPSILON = 1e-5;

        public readonly Vector3D Normal;
        public readonly double W;
        int tag = 0;
        public int Tag
        {
            get
            {
                if (tag == 0)
                {
                    tag = Solid.GetTag();
                }
                return tag;
            }
        }
        public Plane(Vector3D normal, double w)
        {
            Normal = normal;
            W = w;
        }
        public bool Equals(Plane n)
        {
#pragma warning disable RECS0018 // Comparison of floating point numbers with equality operator
            return Normal.Equals(n.Normal) && W == n.W;
#pragma warning restore RECS0018 // Comparison of floating point numbers with equality operator
        }
        public Plane Flipped()
        {
            return new Plane(Normal.Negated, -W);
        }
        public void SplitPolygon(Polygon polygon, out SplitPolygonResult result)
        {
            result = new SplitPolygonResult();
            var planenormal = this.Normal;
            var vertices = polygon.Vertices;
            var numvertices = vertices.Count;
            if (polygon.Plane.Equals(this))
            {
                result.Type = 0;
            }
            else
            {
                var EPS = Plane.EPSILON;
                var thisw = this.W;
                var hasfront = false;
                var hasback = false;
                var vertexIsBack = new bool[numvertices];
                var MINEPS = -EPS;
                for (var i = 0; i < numvertices; i++)
                {
                    var t = planenormal.Dot(vertices[i].Pos) - thisw;
                    var isback = (t < 0);
                    vertexIsBack[i] = isback;
                    if (t > EPS) hasfront = true;
                    if (t < MINEPS) hasback = true;
                }
                if ((!hasfront) && (!hasback))
                {
                    // all points coplanar
                    var t = planenormal.Dot(polygon.Plane.Normal);
                    result.Type = (t >= 0) ? 0 : 1;
                }
                else if (!hasback)
                {
                    result.Type = 2;
                }
                else if (!hasfront)
                {
                    result.Type = 3;
                }
                else
                {
                    // spanning
                    result.Type = 4;
                    var frontvertices = new List<Vertex>();
                    var backvertices = new List<Vertex>();
                    var isback = vertexIsBack[0];
                    for (var vertexindex = 0; vertexindex < numvertices; vertexindex++)
                    {
                        var vertex = vertices[vertexindex];
                        var nextvertexindex = vertexindex + 1;
                        if (nextvertexindex >= numvertices) nextvertexindex = 0;
                        var nextisback = vertexIsBack[nextvertexindex];
                        if (isback == nextisback)
                        {
                            // line segment is on one side of the plane:
                            if (isback)
                            {
                                backvertices.Add(vertex);
                            }
                            else
                            {
                                frontvertices.Add(vertex);
                            }
                        }
                        else
                        {
                            // line segment intersects plane:
                            var intersectionvertex = SplitLineBetweenVertices(vertex, vertices[nextvertexindex]);
                            if (isback)
                            {
                                backvertices.Add(vertex);
                                backvertices.Add(intersectionvertex);
                                frontvertices.Add(intersectionvertex);
                            }
                            else
                            {
                                frontvertices.Add(vertex);
                                frontvertices.Add(intersectionvertex);
                                backvertices.Add(intersectionvertex);
                            }
                        }
                        isback = nextisback;
                    } // for vertexindex
                      // remove duplicate vertices:
                    var EPS_SQUARED = Plane.EPSILON * Plane.EPSILON;
                    if (backvertices.Count >= 3)
                    {
                        var prevvertex = backvertices[backvertices.Count - 1];
                        for (var vertexindex = 0; vertexindex < backvertices.Count; vertexindex++)
                        {
                            var vertex = backvertices[vertexindex];
                            if (vertex.Pos.DistanceToSquared(prevvertex.Pos) < EPS_SQUARED)
                            {
                                backvertices.RemoveAt(vertexindex);
                                vertexindex--;
                            }
                            prevvertex = vertex;
                        }
                    }
                    if (frontvertices.Count >= 3)
                    {
                        var prevvertex = frontvertices[frontvertices.Count - 1];
                        for (var vertexindex = 0; vertexindex < frontvertices.Count; vertexindex++)
                        {
                            var vertex = frontvertices[vertexindex];
                            if (vertex.Pos.DistanceToSquared(prevvertex.Pos) < EPS_SQUARED)
                            {
                                frontvertices.RemoveAt(vertexindex);
                                vertexindex--;
                            }
                            prevvertex = vertex;
                        }
                    }
                    if (frontvertices.Count >= 3)
                    {
                        result.Front = new Polygon(frontvertices, polygon.Shared, polygon.Plane);
                    }
                    if (backvertices.Count >= 3)
                    {
                        result.Back = new Polygon(backvertices, polygon.Shared, polygon.Plane);
                    }
                }
            }
        }
        Vertex SplitLineBetweenVertices(Vertex v1, Vertex v2)
        {
            var p1 = v1.Pos;
            var p2 = v2.Pos;
            var direction = p2 - (p1);
            var u = (W - Normal.Dot(p1)) / Normal.Dot(direction);
            if (double.IsNaN(u)) u = 0;
            if (u > 1) u = 1;
            if (u < 0) u = 0;
            var result = p1 + (direction * (u));
            var tresult = v1.Tex + (v2.Tex - v1.Tex) * u;
            return new Vertex(result, tresult);
        }
		public static Plane FromVector3Ds (Vector3D[] vectors)
		{
			var a = vectors[0];
			var b = vectors[1];
			var c = vectors[2];
			var n = (b - a).Cross (c - a);

			// ensure that we choose points that are not colinear:
			var increment = 1;
			while (n.Length == 0) {
				if (2 + increment >= vectors.Length)
					throw new ArgumentException ("All vectors are colinear", nameof(vectors));
				c = vectors[2 + increment];
				n = (b - a).Cross (c - a);
				increment++;
			}
			n = n.Unit;
			return new Plane (n, n.Dot (a));
		}
        public static Plane FromVector3Ds(Vector3D a, Vector3D b, Vector3D c)
        {
            var n = (b - a).Cross(c - a).Unit;
            return new Plane(n, n.Dot(a));
        }
        public Plane Transform(Matrix4x4 matrix4x4)
        {
            // **精确仿射映射**（DEFECT-003 修复）：平面 (n, W) 在 v' = A·v + t 下的像是
            // (A⁻ᵀ·n, W + (A⁻ᵀ·n)·t)（归一化后）——A⁻ᵀ·n 用余子式矩阵（= 伴随矩阵转置）除行列式算。
            // 旧实现在「平面上取三点 → 逐点变换 → 用变换后三点重建平面」：平移量 ~1e6 时
            // (p + t) 相减发生灾难性抵消，切向量的相对误差被放大（~1e-10），法向误差再经
            // |变换后点| ≈ 1e6 放大成 W 漂移——实测 1e6 平移往返后 W 漂移达 5e-5 > Plane.EPSILON
            // (1e-5)，SplitPolygon 的共面判定失效（Survey 档 A∪A 产出 11/12 个面的重复/丢失）。
            // 精确公式对旋转/镜像/缩放/平移都是一次乘加，误差 ~ulp；镜像不再需要额外 Flipped
            // （负行列式经 A⁻ᵀ 自然给出正确的朝向）。
            var e = matrix4x4.Elements;
            var a00 = e[0]; var a01 = e[4]; var a02 = e[8];
            var a10 = e[1]; var a11 = e[5]; var a12 = e[9];
            var a20 = e[2]; var a21 = e[6]; var a22 = e[10];

            var c00 = a11 * a22 - a12 * a21;
            var c01 = a12 * a20 - a10 * a22;
            var c02 = a10 * a21 - a11 * a20;
            var det = a00 * c00 + a01 * c01 + a02 * c02;

            if (det != 0)
            {
                var c10 = a02 * a21 - a01 * a22;
                var c11 = a00 * a22 - a02 * a20;
                var c12 = a01 * a20 - a00 * a21;
                var c20 = a01 * a12 - a02 * a11;
                var c21 = a02 * a10 - a00 * a12;
                var c22 = a00 * a11 - a01 * a10;

                var n = Normal;
                var mx = (c00 * n.X + c01 * n.Y + c02 * n.Z) / det;
                var my = (c10 * n.X + c11 * n.Y + c12 * n.Z) / det;
                var mz = (c20 * n.X + c21 * n.Y + c22 * n.Z) / det;

                var length = Math.Sqrt(mx * mx + my * my + mz * mz);
                if (length > 0)
                {
                    var w = (W + mx * e[12] + my * e[13] + mz * e[14]) / length;
                    return new Plane(new Vector3D(mx / length, my / length, mz / length), w);
                }
            }

            // 退化矩阵（行列式为 0 / 法向塌缩）：回退三点重建路径（保持旧行为）。
            {
                var ismirror = matrix4x4.IsMirroring;
                // get two vectors in the plane:
                var r = this.Normal.RandomNonParallelVector();
                var u = this.Normal.Cross(r);
                var v = this.Normal.Cross(u);
                // get 3 points in the plane:
                var point1 = this.Normal * (this.W);
                var point2 = point1 + (u);
                var point3 = point1 + (v);
                // transform the points:
                point1 = point1 * (matrix4x4);
                point2 = point2 * (matrix4x4);
                point3 = point3 * (matrix4x4);
                // and create a new plane from the transformed points:
                var newplane = Plane.FromVector3Ds(point1, point2, point3);
                if (ismirror)
                {
                    // the transform is mirroring
                    // We should mirror the plane:
                    newplane = newplane.Flipped();
                }
                return newplane;
            }
        }
    }

	public struct SplitPolygonResult
	{
		public int Type;
		public Polygon Front;
		public Polygon Back;
	}
}
