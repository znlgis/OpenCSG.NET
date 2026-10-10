namespace Csg
{

using System;
using System.Collections.Generic;

/// <summary>
/// 三维点（本模块自有类型，**不依赖 CAD 运行时**，便于离线单测）。
/// 坐标系与模型一致（某 CAD 宿主 Z-up，mm）。
/// </summary>
public readonly struct ViewPoint
{
    public ViewPoint(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public static ViewPoint operator -(ViewPoint a, ViewPoint b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static ViewPoint operator +(ViewPoint a, ViewPoint b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    /// <summary>与方向向量的点积（位置向量语义）。</summary>
    public double Dot(ViewVector v) => X * v.X + Y * v.Y + Z * v.Z;

    /// <summary>逐轴缩放（用于镜像投影方向）。</summary>
    public ViewPoint Scale(double sx, double sy, double sz) => new(X * sx, Y * sy, Z * sz);

    public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
}

/// <summary>三维方向向量；与 <see cref="ViewPoint"/> 同构但语义不同，故分型。</summary>
public readonly struct ViewVector
{
    public ViewVector(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    /// <summary>叉积（右手系）：由三角形两条边求面法向。</summary>
    public static ViewVector Cross(ViewPoint a, ViewPoint b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    /// <summary>叉积（右手系，向量版）：用于校验视图取轴的右手性（Right × Up == −Look）。</summary>
    public static ViewVector Cross(ViewVector a, ViewVector b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    public double Dot(ViewVector other) => X * other.X + Y * other.Y + Z * other.Z;

    /// <summary>归一化（零向量返回零向量，由调用方判退化）。</summary>
    public ViewVector Normalized()
    {
        var len = Length;
        return len < 1e-12 ? this : new ViewVector(X / len, Y / len, Z / len);
    }

    public static ViewVector operator *(ViewVector v, double k) => new(v.X * k, v.Y * k, v.Z * k);

    public override string ToString() => $"[{X:0.###}, {Y:0.###}, {Z:0.###}]";
}

/// <summary>
/// 图纸二维点（模型 2D 或图纸坐标，mm）。与 <c>Features/Drawing</c> 的 <c>Point2D</c> 同形但互不依赖
/// —— 本模块要求完全不依赖 CAD 与既有出图链路，便于离线单测与独立演进。
/// </summary>
public readonly struct ViewPoint2
{
    public ViewPoint2(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; }
    public double Y { get; }

    public override string ToString() => $"({X:0.###}, {Y:0.###})";
}

/// <summary>二维轴对齐包络（模型坐标或图纸坐标，由调用语境决定）。</summary>
public readonly struct ViewBounds2
{
    public ViewBounds2(double minX, double minY, double maxX, double maxY)
    {
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }

    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;

    /// <summary>空包络（没有几何时使用）。</summary>
    public static ViewBounds2 Empty => new(0, 0, 0, 0);

    /// <summary>由点集求包络（空集返回 <see cref="Empty"/>）。</summary>
    public static ViewBounds2 Of(IEnumerable<ViewPoint2> points)
    {
        var any = false;
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var p in points)
        {
            any = true;
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }

        return any ? new ViewBounds2(minX, minY, maxX, maxY) : Empty;
    }

    /// <summary>平移后的包络。</summary>
    public ViewBounds2 Offset(double dx, double dy) => new(MinX + dx, MinY + dy, MaxX + dx, MaxY + dy);

    /// <summary>两包络的并集。</summary>
    public ViewBounds2 Union(ViewBounds2 other) => new(
        Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY),
        Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));

    public override string ToString() => $"({MinX:0.###},{MinY:0.###})–({MaxX:0.###},{MaxY:0.###})";
}
}


