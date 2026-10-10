namespace Csg
{

using System;
using System.Collections.Generic;

/// <summary>
/// 三角网格（三维模型导出用）：**Z-up、单位与模型一致（mm）**，与 GStarCAD 实体口径相同。
/// </summary>
public readonly struct MeshTriangle
{
    public MeshTriangle(MeshPoint a, MeshPoint b, MeshPoint c)
    {
        A = a;
        B = b;
        C = c;
    }

    public MeshPoint A { get; }
    public MeshPoint B { get; }
    public MeshPoint C { get; }
}

/// <summary>三维点（Z-up）。</summary>
public readonly struct MeshPoint
{
    public MeshPoint(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }
    public double Y { get; }
    public double Z { get; }
}

/// <summary>
/// CSG 实体 → 三角网格（Z-up）。
/// <para>
/// <b>坐标系</b>：OpenCSG 为 Y-up，GStarCAD 实体为 Z-up，二者按「Y↔Z 交换」桥接
/// （与 <c>CadCsgEvaluator</c> 及 <c>ViewExportBridge</c> 同一口径）——导出的 STL 与
/// 在 CAD 里落图的实体是同一姿态，不会「导出后零件躺着」。
/// </para>
/// <para>
/// <b>绕序必须再翻转一次</b>：Y↔Z 交换是**反射**（3×3 行列式 −1），会把 CSG 原本「逆时针朝外」的绕序
/// 整体翻成「顺时针朝外」⇒ 由绕序重算的法向全部指向实体内部（STL 内外翻转）。
/// 实测（边长 100 立方体）：CSG 原始带符号体积 +1,000,000 mm³（朝外），仅做交换后 −1,000,000，
/// 再交换三角形后两个顶点即恢复 +1,000,000。故 <see cref="TrianglesOf(Csg.Solid)"/> 在三角化时
/// **同时反转顶点序**，使法向朝外、与 STL 约定一致。
/// </para>
/// </summary>
public static class CsgMesh
{
    /// <summary>OpenCSG 顶点（Y-up）→ 网格顶点（Z-up）。</summary>
    public static MeshPoint FromCsg(Csg.Vector3D p) => new(p.X, p.Z, p.Y);

    /// <summary>
    /// 把一个 CSG 实体的凸多边形扇形三角化（n 边形 → n−2 个三角形），
    /// 并按 <see cref="CsgMesh"/> 类注释所述**反转绕序**抵消反射带来的朝向翻转。
    /// </summary>
    public static List<MeshTriangle> TrianglesOf(Csg.Solid solid)
    {
        if (solid == null) throw new ArgumentNullException(nameof(solid));

        var triangles = new List<MeshTriangle>();
        foreach (var polygon in solid.Polygons)
        {
            var vertices = polygon.Vertices;
            if (vertices.Count < 3)
                continue;

            var a = FromCsg(vertices[0].Pos);
            for (var i = 1; i + 1 < vertices.Count; i++)
            {
                // 反转：先 i+1 后 i（反射补偿，使法向朝外）。
                triangles.Add(new MeshTriangle(a, FromCsg(vertices[i + 1].Pos), FromCsg(vertices[i].Pos)));
            }
        }
        return triangles;
    }

    /// <summary>合并多个实体（同一部分的全部零件）。</summary>
    public static List<MeshTriangle> TrianglesOf(IEnumerable<Csg.Solid> solids)
    {
        var triangles = new List<MeshTriangle>();
        foreach (var solid in solids)
            triangles.AddRange(TrianglesOf(solid));
        return triangles;
    }
}
}

