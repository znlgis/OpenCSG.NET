using System.Collections.Generic;
using Csg;

namespace Csg.Test
{
    /// <summary>
    /// Solid → HLR 管线的适配器。
    /// 库内没有这座桥：CsgMesh 产出 MeshPoint 坐标（已完成 Y↔Z 交换与绕序补偿），
    /// ViewProjector 消费 ViewPoint 坐标，两者之间没有任何转换 helper，
    /// 导致 HLR 子系统在库内不可达。这里补上，并把约定链固化。
    /// </summary>
    public static class ViewBridge
    {
        public static IReadOnlyList<ViewTriangle> ToViewTriangles(Solid solid)
        {
            var mesh = CsgMesh.TrianglesOf(solid);
            var triangles = new ViewTriangle[mesh.Count];
            for (int i = 0; i < mesh.Count; i++)
            {
                triangles[i] = new ViewTriangle(
                    ToViewPoint(mesh[i].A),
                    ToViewPoint(mesh[i].B),
                    ToViewPoint(mesh[i].C));
            }
            return triangles;
        }

        public static IReadOnlyList<ViewProjection> ProjectViews(
            Solid solid, IReadOnlyList<ViewDirection>? directions = null)
        {
            var projector = new ViewProjector(ToViewTriangles(solid));
            return projector.Project(directions);
        }

        static ViewPoint ToViewPoint(MeshPoint p) => new ViewPoint(p.X, p.Y, p.Z);
    }
}
