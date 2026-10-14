using System.Collections.Generic;

namespace Csg
{
    /// <summary>
    /// Convex polygons comprised of vertices lying on a plane.
    /// Each polygon also has "Shared" data which is any
    /// metadata (usually a material reference) that you need to
    /// share between sets of polygons.
    /// </summary>
    /// <remarks>
    /// <c>Vertices</c> 为公共可变列表：边界读数（<see cref="BoundingBox"/>/<see cref="BoundingSphere"/>）
    /// 无缓存、始终反映当前顶点（DEFECT-006 修复，A1 同模式）。但 <c>Plane</c> 是构造期快照 ——
    /// 改变几何的原地顶点变更（整实体搬运/顶点替换）会使 Plane 与顶点不一致，依赖 Plane 的
    /// 布尔/BSP 路径不受支持（探针实测该路径可长时间不返回），见 tests/DEFECTS.md。
    /// </remarks>
    public class Polygon
    {
        public readonly List<Vertex> Vertices;
        public readonly Plane Plane;
        public readonly PolygonShared Shared;

        static readonly PolygonShared defaultShared = new PolygonShared(null);

        public Polygon(List<Vertex> vertices, PolygonShared? shared = null, Plane? plane = null)
        {
            Vertices = vertices;
            Shared = shared ?? defaultShared;
            if (plane == null)
            {
                // 仅在调用方未给平面时才复制顶点位置求平面：布尔运算路径
                // （分割/重剖分/翻转）都显式传平面，此处不再为空跑一次的数组分配。
                var verts = new Vector3D[vertices.Count];
                for (int i = 0; i < verts.Length; i++)
                {
                    verts[i] = vertices[i].Pos;
                }
                plane = Plane.FromVector3Ds (verts);
            }
            Plane = plane;
        }

        public Polygon(params Vertex[] vertices)
            : this(new List<Vertex>(vertices))
        {
        }

        public BoundingSphere BoundingSphere
        {
            get
            {
                // 无缓存现算（DEFECT-006 修复）：Vertices 是公共可变列表，惰性缓存无从失效。
                var box = BoundingBox;
                var middle = (box.Min + box.Max) * 0.5;
                var radius3 = box.Max - middle;
                return new BoundingSphere { Center = middle, Radius = radius3.Length };
            }
        }

        public BoundingBox BoundingBox
        {
            get
            {
                // 无缓存现算（DEFECT-006 修复）：调用点每多边形 O(顶点数)，相对 BSP 裁剪可忽略。
                Vector3D minpoint, maxpoint;
                var vertices = this.Vertices;
                var numvertices = vertices.Count;
                if (numvertices == 0)
                {
                    minpoint = new Vector3D(0, 0, 0);
                }
                else
                {
                    minpoint = vertices[0].Pos;
                }
                maxpoint = minpoint;
                for (var i = 1; i < numvertices; i++)
                {
                    var point = vertices[i].Pos;
                    minpoint = minpoint.Min(point);
                    maxpoint = maxpoint.Max(point);
                }
                return new BoundingBox(minpoint, maxpoint);
            }
        }

        public Polygon Flipped()
        {
            var newvertices = new List<Vertex>(Vertices.Count);
            for (int i = 0; i < Vertices.Count; i++)
            {
                newvertices.Add(Vertices[i].Flipped());
            }
            newvertices.Reverse();
            var newplane = Plane.Flipped();
            return new Polygon(newvertices, Shared, newplane);
        }
    }

    public class PolygonShared
    {
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
        public PolygonShared(object? color)
        {
        }
        public string Hash
        {
            get
            {
                return "null";
            }
        }
    }
}
