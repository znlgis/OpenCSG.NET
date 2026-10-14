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

    /// <summary>
    /// 相机朝向（深度轴）。相机位于 <c>−Look</c> 侧；正对相机判定 = <c>n · Look &lt; 0</c>。
    /// <para>
    /// <b>消隐深度口径</b>：深度 = <c>p · Look</c>（<c>ViewProjector</c> / <c>DepthRaster</c> 直接用）。
    /// 相机在 <c>−Look</c> 侧 ⇒ 该值随「远离观察者」递增，与 <c>Look</c> 主轴符号**无关**
    /// （主视 <c>+X</c>、俯视 <c>−Z</c>、左视 <c>−Y</c> 皆成立），正好与
    /// 「<c>DepthRaster</c> 每格存最小深度 + <c>IsVisibleAt</c> 判 <c>depth ≤ nearest + bias</c>」配套。
    /// 2026-10-11 主视由 <c>−X</c> 改 <c>+X</c> 时曾误判「正向主轴会让消隐整体反向」而想引入 ±1 方向因子，
    /// 实测（<c>ViewExportTests</c> 用例 4「小立方体在大立方体后方被遮挡」）证明**无需**取反 —— 故此处不再设方向因子。
    /// </para>
    /// </summary>
    public ViewVector Look { get; }

    /// <summary>映射到 2D 水平轴的模型轴。</summary>
    public ViewVector Right { get; }

    /// <summary>映射到 2D 垂直轴的模型轴。</summary>
    public ViewVector Up { get; }
}

/// <summary>
/// 国标第一角三视图方向（GB/T 4458.1 第一角画法），取轴口径 = **需求方图纸口径 + 2026-10-11 整体朝向口径**：
/// 坐标系为 Z-up：**长轴沿 +Y**、宽沿 X、高沿 Z
/// （与 <c>CadCsgEvaluator</c> 的落图坐标系、<c>ViewExportBridge</c> 的桥接口径一致）。
///
/// <para><b>朝向基准（2026-10-11 用户口径「方案 A」）</b>：整箱几何在总装末端做过一次
/// 绕竖轴 180° 的整体旋转（<c>CrateOrientation</c>）⇒ 现行模型里
/// <b>汽机侧在 +Y、电机侧在 −Y、人孔侧壁朝 −X</b>。三视图取轴据此重新标定：</para>
/// <list type="bullet">
/// <item><b>主视图</b>（正视）：观察者在 <b>−X 侧</b>朝 <b>+X</b> 看 ⇒ 「从人孔侧（−X 侧壁）看向
/// 另一侧」✔；水平轴取 <c>Right = −Y</c> ⇒ 汽机侧（+Y）落在**图左**、电机侧（−Y）落在图右 ✔；
/// 垂直轴 = +Z（箱高）；</item>
/// <item><b>俯视图</b>（画在主视图**正下方**）：观察者在上方朝 −Z 看 ⇒ 水平轴 = <b>−Y</b>（与主视**长对正**：
/// 汽机侧在左、电机侧在右 ✔）、垂直轴 = <b>+X</b>（人孔侧 −X 朝观察者 ⇒ 画在**远离主视**的一侧，即下缘；
/// 符合第一角「靠近主视的一侧 = 物体后方 = +X」）；</item>
/// <item><b>左视图</b>（画在主视图**正右方**）：观察者在 <b>+Y 侧</b>朝 <b>−Y</b> 看（从汽机侧看向电机侧 ✔）⇒
/// 水平轴 = <b>−X</b>（人孔侧 −X 落在图左）、垂直轴 = +Z（与主视**高平齐**）。</item>
/// </list>
///
/// <para>
/// <b>口径变更（202某分图0-09）</b>：此前主视图取 <c>look = +Y</c>（观察者朝箱体**端面**看），
/// 出的是端面立面；而图纸口径的主视图画的是**长边立面**（即上表口径）。
/// </para>
/// <para>
/// <b>口径变更（2026-10-11，本轮）</b>：整箱几何绕竖轴旋转 180°（人孔侧 +X→−X、汽机侧 −Y→+Y）后，
/// 三个视图的观察侧与图面轴相应重标：
/// 正视由「+X 侧朝 −X 看、Right=+Y」改为「<b>−X 侧朝 +X 看、Right=−Y</b>」；
/// 俯视由「Right=+Y、Up=−X」改为「<b>Right=−Y、Up=+X</b>」；
/// 左视由「−Y 侧朝 +Y 看、Right=+X」改为「<b>+Y 侧朝 −Y 看、Right=−X</b>」。
/// 三者仍满足下方不变量与第一角相对位置（俯视在主视正下方、左视在主视正右方）。
/// </para>
/// <para>
/// <b>关键不变量（防镜像）</b>：<c>Right × Up == −Look</c>，即 (Right, Up, −Look) 构成右手正交基、
/// 投影行列式为 +1 ⇒ 图面不镜像。
/// </para>
/// <para>
/// <b>本实现与参考实现的差异（必须是前者）</b>：参考实现（znlgis/GStarCad.Net.Demo 的 <c>OrthoProjector</c>）
/// 对俯视取 (Right,Up) = (+X, −Y)、对左视取 (+Y, +Z)，二者都使 <c>Right × Up == +Look</c>
/// —— 那是**左手基**，等于把俯视/左视整体镜像（非对称零件会画错，且违反「靠近主视图的一侧 = 物体后面」）。
/// </para>
/// </summary>
public static class ViewDirections
{
    /// <summary>主视图（Front）：观察者在 −X 侧（人孔侧）朝 +X 看 ⇒ 水平 = −Y（汽机侧 +Y 在左）、垂直 = +Z。</summary>
    public static ViewDirection Front { get; } = new(
        "主视", new ViewVector(1, 0, 0), new ViewVector(0, -1, 0), new ViewVector(0, 0, 1));

    /// <summary>俯视图（Top，第一角：画在主视图正下方）：观察者在 +Z 上方朝 −Z 看 ⇒ 水平 = −Y（汽机侧 +Y 在左）、
    /// 垂直 = +X（人孔侧 −X 朝观察者 ⇒ 画在远离主视的下缘）。</summary>
    public static ViewDirection Top { get; } = new(
        "俯视", new ViewVector(0, 0, -1), new ViewVector(0, -1, 0), new ViewVector(1, 0, 0));

    /// <summary>左视图（Left，第一角：画在主视图正右方）：观察者在 +Y 侧（汽机侧）朝 −Y 看（看向电机侧）⇒
    /// 水平 = −X（人孔侧 −X 在左）、垂直 = +Z。</summary>
    public static ViewDirection Left { get; } = new(
        "左视", new ViewVector(0, -1, 0), new ViewVector(-1, 0, 0), new ViewVector(0, 0, 1));

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

        var points = new List<ViewPoint2>(segments.Count * 2 + Arcs.Count * 6);
        foreach (var segment in segments)
        {
            points.Add(segment.Start);
            points.Add(segment.End);
        }

        // 弧极值并入包络（DEFECT-009 修复）：被弧吸收的折线已从 Segments 移除，只看线段会把
        // 全弧视图的包络退化为 (0,0,0,0)（= ViewBounds2.Empty 哨兵，与「无几何」不可区分）。
        // 取**精确弧盒**：整圆 ⇒ 全圆外接盒；开口弧 ⇒ 两端点 + 扫掠范围内的轴极值点（0/90/180/270°）。
        // 不用「保守全圆盒」：拟合出的浅弧半径可远大于几何尺度（实测某类弧 R≈280、扫掠 30°），
        // 全圆盒会把包络撑到几何之外（如 64 宽视图撑到 515），破坏落图定位。
        foreach (var arc in Arcs)
        {
            if (arc.IsClosed)
            {
                points.Add(new ViewPoint2(arc.Center.X - arc.Radius, arc.Center.Y - arc.Radius));
                points.Add(new ViewPoint2(arc.Center.X + arc.Radius, arc.Center.Y + arc.Radius));
                continue;
            }

            // 扫掠角归一化用**取模**而非逐次减 2π：非有限值（±∞/NaN 经公开 ctor 可构造，
            // 序列化守卫会拒绝）在减法下恒不收敛会死循环；超大有限值减法则近似无限次循环。
            // 非有限扫掠按整圆保守并入包络（无效输入不该让包络构造挂起或抛错——该输入的下游
            // 消费者 ViewJson.Serialize 有自己的守卫）。
            var sweep = arc.EndAngle - arc.StartAngle;
            if (double.IsNaN(sweep) || double.IsInfinity(sweep))
            {
                points.Add(new ViewPoint2(arc.Center.X - arc.Radius, arc.Center.Y - arc.Radius));
                points.Add(new ViewPoint2(arc.Center.X + arc.Radius, arc.Center.Y + arc.Radius));
                continue;
            }

            sweep %= 2 * Math.PI;
            if (sweep < 0) sweep += 2 * Math.PI;
            if (sweep <= 0)
            {
                // 起点=终点（或整圈倍数）：按整圆保守处理，宁可外扩也不退化到空。
                points.Add(new ViewPoint2(arc.Center.X - arc.Radius, arc.Center.Y - arc.Radius));
                points.Add(new ViewPoint2(arc.Center.X + arc.Radius, arc.Center.Y + arc.Radius));
                continue;
            }

            points.Add(ArcPoint(arc, arc.StartAngle));
            points.Add(ArcPoint(arc, arc.EndAngle));

            for (var quadrant = 0; quadrant < 4; quadrant++)
            {
                var angle = quadrant * Math.PI / 2;
                var delta = angle - arc.StartAngle;
                if (double.IsNaN(delta) || double.IsInfinity(delta))
                    continue;
                delta %= 2 * Math.PI;
                if (delta < 0) delta += 2 * Math.PI;
                if (delta <= sweep)
                    points.Add(ArcPoint(arc, angle));
            }
        }

        Bounds = ViewBounds2.Of(points);
    }

    static ViewPoint2 ArcPoint(ViewArc arc, double angle)
        => new ViewPoint2(
            arc.Center.X + arc.Radius * Math.Cos(angle),
            arc.Center.Y + arc.Radius * Math.Sin(angle));

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

    /// <summary>内部名（如 "某分图#1"），用于日志与排序。</summary>
    public string Name { get; }

    /// <summary>图面标签（如 "某分图 某零件 支撑顶板"）。</summary>
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






