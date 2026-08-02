using System.Collections.Generic;

// Polyfill: IsExternalInit required for C# 9 records on netstandard2.0
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}

namespace Csg
{
    // ---- Forward declaration: subclasses in Profiles.cs ----
    /// <summary>2D 截面形状基类。每种截面存储参数配方，求值时展开为多边形顶点。</summary>
    public abstract record Profile2D;

    // ---- CsgNode 树节点类型 ----

    /// <summary>CsgNode 树节点基类。</summary>
    public abstract record CsgNode;

    // ========== 图元节点 ==========

    /// <param name="Center">中心点坐标 (X, Y, Z)</param>
    /// <param name="Size">轴对齐尺寸 (X, Y, Z)</param>
    public record BoxNode(Vector3D Center, Vector3D Size) : CsgNode;

    /// <param name="Center">中心点坐标</param>
    /// <param name="Radius">半径</param>
    public record SphereNode(Vector3D Center, double Radius) : CsgNode;

    /// <param name="Center">中心点坐标</param>
    /// <param name="Radius">半径</param>
    /// <param name="Height">高度（沿 Y 轴）</param>
    public record CylinderNode(Vector3D Center, double Radius, double Height) : CsgNode;

    /// <param name="Center">中心点坐标</param>
    /// <param name="TopRadius">顶半径</param>
    /// <param name="BottomRadius">底半径</param>
    /// <param name="Height">高度（沿 Y 轴）</param>
    public record ConeNode(Vector3D Center, double TopRadius, double BottomRadius, double Height) : CsgNode;

    /// <param name="Profile">2D 截面配方（子类在 Profiles.cs 中）</param>
    /// <param name="Height">拉伸高度（Z 轴正方向）</param>
    public record ExtrudeNode(Profile2D Profile, double Height) : CsgNode;

    /// <param name="Corner">底面矩形中心坐标</param>
    /// <param name="Width">X 方向宽度</param>
    /// <param name="Depth">Y 方向深度</param>
    /// <param name="Height">Z 方向高度</param>
    public record WedgeNode(Vector3D Corner, double Width, double Depth, double Height) : CsgNode;

    // ========== 布尔运算节点 ==========

    public record UnionNode(List<CsgNode> Children) : CsgNode;

    public record SubtractNode(List<CsgNode> Children) : CsgNode;

    public record IntersectNode(List<CsgNode> Children) : CsgNode;

    // ========== 变换节点 ==========

    /// <param name="Translation">平移向量</param>
    /// <param name="Rotation">Euler 旋转角度 (Rx, Ry, Rz) 单位：度</param>
    /// <param name="Child">待变换的子树</param>
    public record TransformNode(Vector3D Translation, Vector3D Rotation, CsgNode Child) : CsgNode;

    // ========== 便捷工厂 ==========

    public static class CsgNodes
    {
        public static BoxNode Box(Vector3D center, Vector3D size) => new BoxNode(center, size);
        public static BoxNode Box(double cx, double cy, double cz, double sx, double sy, double sz)
            => new BoxNode(new Vector3D(cx, cy, cz), new Vector3D(sx, sy, sz));

        public static SphereNode Sphere(Vector3D center, double radius) => new SphereNode(center, radius);

        public static CylinderNode Cylinder(Vector3D center, double radius, double height)
            => new CylinderNode(center, radius, height);

        public static ConeNode Cone(Vector3D center, double topR, double bottomR, double height)
            => new ConeNode(center, topR, bottomR, height);

        public static ExtrudeNode Extrude(Profile2D profile, double height) => new ExtrudeNode(profile, height);

        public static WedgeNode Wedge(Vector3D corner, double width, double depth, double height)
            => new WedgeNode(corner, width, depth, height);

        public static UnionNode Union(params CsgNode[] children) => new UnionNode(new List<CsgNode>(children));
        public static SubtractNode Subtract(params CsgNode[] children) => new SubtractNode(new List<CsgNode>(children));
        public static IntersectNode Intersect(params CsgNode[] children) => new IntersectNode(new List<CsgNode>(children));

        public static TransformNode Transform(Vector3D translation, Vector3D rotation, CsgNode child)
            => new TransformNode(translation, rotation, child);
    }
}
