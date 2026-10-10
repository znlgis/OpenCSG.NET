namespace Csg
{

using System;
using System.Collections.Generic;

/// <summary>
/// 投影视图方向：观察方向 <see cref="Look"/> + 2D 取轴 <see cref="Right"/>/<see cref="Up"/>。
/// <b>不变量</b>：<c>Right × Up == −Look</c>（右手正交基 ⇒ 图面不镜像）。
/// </summary>
public sealed class ViewDirection
{
    public ViewDirection(string name, ViewVector look, ViewVector right, ViewVector up)
    {
        Name = name;
        Look = look;
        Right = right;
        Up = up;
    }

    /// <summary>视图名（"主视" / "俯视" / "左视"）。</summary>
    public string Name { get; }

    /// <summary>相机朝向（深度轴；值越小越靠近观察者）。</summary>
    public ViewVector Look { get; }

    /// <summary>映射到 2D 水平轴的模型轴。</summary>
    public ViewVector Right { get; }

    /// <summary>映射到 2D 垂直轴的模型轴。</summary>
    public ViewVector Up { get; }
}

/// <summary>
/// 国标第一角三视图方向（GB/T 4458.1 第一角画法），取轴口径 = **甲方图纸口径**：
/// 落图坐标系为 Z-up，箱体**长轴沿 +Y（汽机侧在 −Y 端）**、宽沿 X、高沿 Z
/// （与 <c>CadCsgEvaluator</c> 的落图坐标系、<c>ViewExportBridge</c> 的桥接口径一致）。
/// <list type="bullet">
/// <item><b>主视图</b>：观察者在 +X 侧朝 **−X** 看 ⇒ 水平轴 = <b>+Y（箱长）</b>、垂直轴 = +Z（箱高）
/// —— 与甲方 6-1 主视图逐位一致（8,950 / 9,340 横向、3,890 竖向，**汽机侧在左**、电机侧在右）；</item>
/// <item><b>俯视图</b>：观察者在上方朝 −Z 看，画在主视图**正下方** ⇒ 水平轴 = +Y（与主视**长对正**）、
/// 垂直轴 = −X（靠近主视图的一侧 = 物体后方 = −X，符合第一角画法）；</item>
/// <item><b>左视图</b>：观察者在 −Y 侧朝 +Y 看，画在主视图**正右方** ⇒ 水平轴 = +X、垂直轴 = +Z（与主视**高平齐**）。</item>
/// </list>
/// <para>
/// <b>口径变更（2026-10-09）</b>：此前主视图取 <c>look = +Y</c>（观察者朝箱体**端面**看），
/// 出的是端面立面（3,887 × 4,141）；甲方 6-1/6-3…图纸的主视图画的是**箱体长边立面**（9,340 × 3,890）。
/// 本轮按图纸口径改为 <c>look = −X</c>，三视图随之整体重排（俯视仍在主视正下方、左视仍在主视正右方）。
/// </para>
/// <para>
/// <b>关键不变量（防镜像）</b>：<c>Right × Up == −Look</c>，即 (Right, Up, −Look) 构成右手正交基、
/// 投影行列式为 +1 ⇒ 图面不镜像。
/// </para>
/// <para>
/// <b>本实现与参考实现的差异（必须是前者）</b>：参考实现（znlgis/GStarCad.Net.Demo 的 <c>OrthoProjector</c>）
/// 对俯视取 (Right,Up) = (+X, −Y)、对左视取 (+Y, +Z)，二者都使 <c>Right × Up == +Look</c>
/// —— 那是**左手基**，等于把俯视/左视整体镜像（非对称零件会画错，且违反「靠近主视图的一侧 = 物体后面」）。
/// 本模块按第一角重定为：俯视 (X, +Y) 水平长对正；左视 (−Y, +Z) 使近端出现物体后面。
/// </para>
/// </summary>
public static class ViewDirections
{
    /// <summary>主视图（Front）：观察者 +X 侧，look = −X ⇒ 水平 = +Y（箱长，汽机侧在左）、垂直 = +Z。</summary>
    public static ViewDirection Front { get; } = new(
        "主视", new ViewVector(-1, 0, 0), new ViewVector(0, 1, 0), new ViewVector(0, 0, 1));

    /// <summary>俯视图（Top，第一角：画在主视图正下方）：观察者在 +Z 上方，look = −Z ⇒ 水平 = +Y、垂直 = −X。</summary>
    public static ViewDirection Top { get; } = new(
        "俯视", new ViewVector(0, 0, -1), new ViewVector(0, 1, 0), new ViewVector(-1, 0, 0));

    /// <summary>左视图（Left，第一角：画在主视图正右方）：观察者 −Y 侧，look = +Y ⇒ 水平 = +X、垂直 = +Z。</summary>
    public static ViewDirection Left { get; } = new(
        "左视", new ViewVector(0, 1, 0), new ViewVector(1, 0, 0), new ViewVector(0, 0, 1));

    /// <summary>三视图（顺序固定：主视 / 俯视 / 左视）。</summary>
    public static IReadOnlyList<ViewDirection> OrthographicThree { get; } = new[] { Front, Top, Left };
}

/// <summary>
/// 投影线段的最小契约：模型坐标段 <see cref="ViewSegment"/> 与"图纸坐标段"（宿主侧实现）
/// 共用 —— 圆弧识别（<see cref="ViewArcFitter"/>）在两侧都要用。
/// </summary>
public interface IProjectedSegment
{
    /// <summary>起点。</summary>
    ViewPoint2 Start { get; }

    /// <summary>终点。</summary>
    ViewPoint2 End { get; }

    /// <summary>可见（实线）还是被遮挡（虚线）。</summary>
    bool IsVisible { get; }
}

/// <summary>一条投影后的 2D 线段（模型 2D 坐标，单位 mm）。</summary>
public readonly struct ViewSegment : IProjectedSegment
{
    public ViewSegment(ViewPoint2 start, ViewPoint2 end, bool isVisible)
    {
        Start = start;
        End = end;
        IsVisible = isVisible;
    }

    public ViewPoint2 Start { get; }
    public ViewPoint2 End { get; }

    /// <summary>可见（实线）还是被遮挡（虚线）。</summary>
    public bool IsVisible { get; }

    /// <summary>线段长度（用于统计与排序，不做合并判据）。</summary>
    public double Length
    {
        get
        {
            var dx = End.X - Start.X;
            var dy = End.Y - Start.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}

/// <summary>单视图的投影产物：方向 + 线段表 + 圆弧表 + 包络（模型 2D 坐标）。</summary>
public sealed class ViewProjection
{
    public ViewProjection(
        ViewDirection direction,
        IReadOnlyList<ViewSegment> segments,
        IReadOnlyList<ViewArc>? arcs = null)
    {
        Direction = direction;
        Segments = segments;
        Arcs = arcs ?? Array.Empty<ViewArc>();

        var points = new List<ViewPoint2>(segments.Count * 2);
        foreach (var segment in segments)
        {
            points.Add(segment.Start);
            points.Add(segment.End);
        }
        Bounds = ViewBounds2.Of(points);
    }

    public ViewDirection Direction { get; }

    /// <summary>可见段 + 隐藏段（顺序无保证：由深度缓冲逐边切分产生）。</summary>
    public IReadOnlyList<ViewSegment> Segments { get; }

    /// <summary>
    /// **识别出的圆弧/整圆**（由 <see cref="ViewArcFitter"/> 在碎片清理**之前**从折线还原）。
    /// 它们不再出现在 <see cref="Segments"/> 里；落图时写成 <c>ARC</c>/<c>CIRCLE</c> 实体。
    /// </summary>
    public IReadOnlyList<ViewArc> Arcs { get; }

    /// <summary>视图包络（模型 2D 坐标）。</summary>
    public ViewBounds2 Bounds { get; }

    /// <summary>可见段数。</summary>
    public int VisibleCount
    {
        get
        {
            var n = 0;
            foreach (var segment in Segments)
                if (segment.IsVisible) n++;
            return n;
        }
    }

    /// <summary>隐藏段数。</summary>
    public int HiddenCount => Segments.Count - VisibleCount;

    /// <summary>圆弧/整圆条数（含可见与隐藏）。</summary>
    public int ArcCount => Arcs.Count;
}

/// <summary>一件（或一个装配）的三视图投影。</summary>
public sealed class OrthographicViews
{
    public OrthographicViews(string name, string label, IReadOnlyList<ViewProjection> views)
    {
        Name = name;
        Label = label;
        Views = views;
    }

    /// <summary>内部名（如 "6-6#1"），用于日志与排序。</summary>
    public string Name { get; }

    /// <summary>图面标签（如 "6-6 件1 支撑顶板"）。</summary>
    public string Label { get; }

    /// <summary>三视图（主/俯/左）。</summary>
    public IReadOnlyList<ViewProjection> Views { get; }

    /// <summary>三视图合并包络（模型 2D 坐标）。</summary>
    public ViewBounds2 Bounds
    {
        get
        {
            var bounds = ViewBounds2.Empty;
            var first = true;
            foreach (var view in Views)
            {
                bounds = first ? view.Bounds : bounds.Union(view.Bounds);
                first = false;
            }
            return bounds;
        }
    }
}
}

