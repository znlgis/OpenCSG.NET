namespace Csg
{

using Csg;
using System;
using System.Collections.Generic;

/// <summary>
/// CSG 建模辅助工具：语法树层面的通用几何变换组合。
/// </summary>
public static class CsgHelpers
{
    /// <summary>
    /// 位姿烘焙：先绕原点旋转、再平移。
    /// TransformNode 的语义是「先平移再绕**全局原点**旋转」，若直接 Transform(translation, rotation, body)
    /// 会让规范体绕全局原点公转而非原位转向。规范体以原点为中心 → 绕原点旋转即原位旋转，
    /// 故拆成两层：内层只旋转（平移为零），外层只平移（旋转为零）。
    /// </summary>
    public static CsgNode Place(CsgNode body, Vector3D translation, Vector3D rotation)
    {
        var zero = new Vector3D(0, 0, 0);
        return CsgNodes.Transform(translation, zero, CsgNodes.Transform(zero, rotation, body));
    }

    /// <summary>
    /// 绕指定中心点原位旋转 CSG 子树。
    /// TransformNode 先平移、后绕全局原点旋转，绕任意点的旋转需组合为：
    /// T(center) · R · T(−center)，即先移到原点、旋转、再移回。
    /// </summary>
    /// <param name="node">待旋转的 CSG 子树</param>
    /// <param name="center">旋转中心（全局坐标）</param>
    /// <param name="rotation">绕各轴旋转角（度，X-Y-Z 顺序）</param>
    public static CsgNode RotateAboutCenter(CsgNode node, Vector3D center, Vector3D rotation)
        => CsgNodes.Transform(
            center,
            new Vector3D(0, 0, 0),
            CsgNodes.Transform(new Vector3D(-center.X, -center.Y, -center.Z), rotation, node));

    /// <summary>轴沿 Z 的圆柱：Cylinder 默认高沿 Y，绕中心转 90° 使轴线沿 Z（贯穿板厚方向）。</summary>
    /// <summary>轴线沿 X 的圆柱
    /// 其圆头与吊孔为沿 X 的圆柱）。实现 = 沿 Z 圆柱绕自身中心旋 90° 到 X 轴。</summary>
    /// <summary>
    /// 带凸耳的板类轮廓（Y-Z 平面、厚沿 X；多个零件共用）：
    /// **左直立边 + 顶部 R 圆头 + 与圆头相切的斜边落至右下角 + 底边**，Ø 孔与圆头同轴。
    ///
    /// 相切（图上圆弧与直线无明显过渡）：切线角由几何反解 —— 过底边外端、与"圆心 (zLeft+R, yCenter)、
    /// 半径 R"的圆相切：A·c²+B·c+C=0（c = cot 夹角），取较小正根：
    ///   某零件 (1275/2850/R150) c=0.3392 ⇒ **71.27°**；某零件 (375/340/R125) ⇒ **70.90°**；
    ///   某零件 (323/190/R125) ⇒ **70.92°** —— 三件均 ≈70°，与图纸名义标注吻合。
    ///
    /// 构造：盒（板底 → 切点标高）− 切线半平面 − **肩部（孔心以上 ∩ 圆盘以外）** ∪ 圆头 − 孔。
    /// **肩部必须用"圆盘补集"减法**：被减项 = Subtract(boxUpper, head) ⇒ 圆盘以内自动补回。
    /// 不能用 HalfPlaneCut 切弦：其切割体是正方形盒（边长 √2·halfExtent），垂直切深 = √2·halfExtent，
    /// 而"细长弦"要求 halfExtent ≥ 弦长/0.7071 ⇒ 切深 300mm 切进板体、多切 25 倍（补充文档 27 节实测）。
    /// 切线半平面则无碍：其切深方向整体落在零件外部。
    ///
    /// **镜像策略**：只按 |zLeft| 构造 +Z 版，−Z 侧对**成品**绕 Y 轴（过原点）旋转 180° ⇒ 关于 Z=0 的镜像；
    /// 刚体变换保体积/形状 ⇒ ±Z 实例必然全等（护栏 SixThreeInstances 实测要求）。
    /// </summary>
    /// <summary>
    /// (X,Y) 截面多边形沿 **Z** 拉伸的精确楔块（用于角部/斜面切割，替代 HalfPlaneCut）。
    /// HalfPlaneCut 的切割体是边长 √2·halfExtent 的方块且法向易错，调用方工程已三次踩坑
    /// （某零件 覆盖不足、某零件 啃料 141、某零件/4 方向符号）⇒ 统一改用本精确法。
    /// **Extrude 实际落在局部 z ∈ [0, h]**，
    /// 故平移须为 zCenter − h/2 才能以 zCenter 居中（此前写成 + h/2 ⇒ 每个用户沿拉伸方向偏 h/2 ✗）。
    /// </summary>
    /// <param name="points">(X,Y) 截面顶点（绕向由内核自动规整为逆时针）。</param>
    /// <param name="zCenter">拉伸体的 Z 中心。</param>
    /// <param name="thickness">沿 Z 的拉伸长度（含余量即可）。</param>
    public static CsgNode PrismXY(IReadOnlyList<(double X, double Y)> points, double zCenter, double thickness)
    {
        var pts = new List<Vector2D>();
        foreach (var (x, y) in points) pts.Add(new Vector2D(x, y));
        var prism = CsgNodes.Extrude(new PolygonProfile(pts), thickness);
        return CsgNodes.Transform(new Vector3D(0, 0, zCenter - thickness / 2), new Vector3D(0, 0, 0), prism);
    }

    /// <summary>
    /// **(X,Z) 断面沿 Y 拉伸的棱柱** —— 用于「断面在**径向-轴向**平面、沿**周向**拉伸」的弧形件
    /// （某分图 行4 挡槽 = 沿弧弯曲的 L 型：断面 (X 径向, Z 轴向)、累加方向 = 该处周向 Y）。
    /// 实现 = <see cref="PrismXY"/> 后绕 X 轴 +90°：**(x,y,z) → (x,−z,y)** ⇒ 断面 y(轴向) → Z、
    /// 拉伸 z → 周向 Y；再沿 Y 平移到 yCenter（−Y 方向拉伸故取 +length/2 居中）。
    /// 坑：直接用 PrismXY 会得到「断面在 X-Y、沿 Z 拉伸」的错误姿态 ✗；两次 Transform 的组合语义见
    /// <see cref="Place"/>/<see cref="PlaceRotateThenTranslate"/>。
    /// </summary>
    /// <param name="points">断面顶点 (X 径向, Z 轴向)，绕向由内核自动规整为逆时针。</param>
    /// <param name="yCenter">拉伸体的 Y 中心（周向站位）。</param>
    /// <param name="length">沿 Y 的拉伸长度。</param>
    public static CsgNode PrismXZ(IReadOnlyList<(double X, double Z)> points, double yCenter, double length)
    {
        var pts = new List<Vector2D>(points.Count);
        foreach (var (x, z) in points) pts.Add(new Vector2D(x, z));
        var prism = CsgNodes.Extrude(new PolygonProfile(pts), length);   // Extrude 覆盖局部 z ∈ [0, length]
        var upright = CsgNodes.Transform(new Vector3D(0, 0, 0), new Vector3D(90, 0, 0), prism);
        return CsgNodes.Transform(new Vector3D(0, yCenter + length / 2.0, 0), new Vector3D(0, 0, 0), upright);
    }

    /// <summary>
    /// **把一段圆弧追加到断面点列**（圆心 (cx, cy)、半径 r、角度自 aFrom → aTo，单位：度；跳过起点、含终点）。
    /// 用途：在二维断面里做**相切圆角**（含 **凹角**）—— 圆弧顶点与相邻直边**共用端点** ⇒ 无台阶、**无外挂体**。
    /// 坑：① 圆心必须沿「料内」方向内移 r 才能与两面相切（符号写反 ⇒ 材料长到件外 ✗）；
    /// ② **端点不得与已有点重复**（零面积侧面会让内核 BSP 退化 ✗，某零件 曾据此崩溃）；
    /// ③ 用圆柱布尔做圆角会在渲染里露出**圆柱本体** ✗（某零件 实测「两根圆柱」），必须走本方法。
    /// </summary>
    public static void AddArc(List<(double X, double Y)> points, double cx, double cy, double r,
        double aFromDeg, double aToDeg, int steps)
    {
        for (int i = 1; i <= steps; i++)
        {
            double t = (aFromDeg + (aToDeg - aFromDeg) * i / steps) * Math.PI / 180.0;
            points.Add((cx + r * Math.Cos(t), cy + r * Math.Sin(t)));
        }
    }

    /// <summary>直角三角形楔块（角部 C 倒角常用）：角点 (x,y)，两腿分别沿 ±X、±Y。</summary>
    public static CsgNode CornerWedge(double x, double y, double dx, double dy, double leg,
        double zCenter, double thickness)
        => PrismXY(new[] { (x, y), (x + dx * leg, y), (x, y + dy * leg) }, zCenter, thickness);
    /// <summary>
    /// 凸耳板**斜边角**（度，与水平夹角）：与 <see cref="TabbedPlate"/> 内斜边**同源**（同一相切二次方程）✓。
    /// 某零件 的斜边必须用此角 ⇒ 与某零件 斜边**数学上精确平行/重合** ✓（此前用常量近似 ⇒ 亚毫米级发散 ⇒ 缝/相交 ✗）。
    /// </summary>
    public static double TabbedPlateSlantAngleDeg(double width, double yBottom, double yCenter, double headRadius)
    {
        double yc = yCenter - yBottom;
        double A = yc * yc - headRadius * headRadius;
        double B = -2 * (width - headRadius) * yc;
        double C = (width - headRadius) * (width - headRadius) - headRadius * headRadius;
        double dsc = B * B - 4 * A * C;
        double r1 = (-B + System.Math.Sqrt(dsc)) / (2 * A);
        double r2 = (-B - System.Math.Sqrt(dsc)) / (2 * A);
        double c = System.Math.Min(r1, r2);
        if (c <= 0) c = System.Math.Max(r1, r2);
        double len = System.Math.Sqrt(1 + c * c);
        double yT = yc + headRadius * c / len;
        double run = c * yT;
        return System.Math.Atan2(yT, run) * 180.0 / System.Math.PI;
    }

    public static CsgNode TabbedPlate(double xCenter, double thickness, double zLeft, double width,
        double yBottom, double yCenter, double headRadius, double holeRadius)
    {
        double sz = System.Math.Sign(zLeft);
        double zi = System.Math.Abs(zLeft);
        double w = width;
        double yc = yCenter - yBottom;
        double A = yc * yc - headRadius * headRadius;
        double B = -2 * (w - headRadius) * yc;
        double C = (w - headRadius) * (w - headRadius) - headRadius * headRadius;
        double dsc = B * B - 4 * A * C;
        double r1 = (-B + System.Math.Sqrt(dsc)) / (2 * A);
        double r2 = (-B - System.Math.Sqrt(dsc)) / (2 * A);
        double c = System.Math.Min(r1, r2);
        if (c <= 0) c = System.Math.Max(r1, r2);

        double len = System.Math.Sqrt(1 + c * c);
        double zT = headRadius / len;                        // 切点相对孔心的水平距离
        double yT = yc + headRadius * c / len;               // 切点相对板底的竖直距离
        double zOut = zi + w;
        double run = c * yT;
        double over = thickness + 10;
        // HalfPlaneCut 沿线覆盖仅 ±0.7071·halfExtent ⇒ 按切割线长度留 20% 余量（补充文档 24 节）。
        double halfSlant = 1.2 * System.Math.Sqrt(2) * System.Math.Sqrt(run * run + yT * yT);

        var box = CsgNodes.Box(xCenter, yBottom + yT / 2, zi + w / 2, thickness, yT, w);
        var slantCut = HalfPlaneCut.Build(xCenter, yBottom, zOut, yT, -run, halfSlant, over, +1);
        var head = CylinderAlongX(new Vector3D(xCenter, yCenter, zi + headRadius), headRadius, thickness);

        // 肩部 = 孔心标高以上、Z ∈ [立边, 立边 + 跨度] 的矩形 ∩ 圆盘以外（被减项自含圆盘 ⇒ 弧内区域补回）。
        // 跨度必须覆盖台面标高（y = yT）处圆盘外的左段、即到 z = 立边 + R − R·sinθ：
        //   · θ ≥ 30°（2·zT ≥ R）：切点跨度 zT = R·sinθ 已 ≥ R − R·sinθ，维持 zT（既有 θ≥30° 基线逐字节不变）；
        //   · θ < 30°（2·zT < R）：只盖到切点会在 [切点, 孔心] 间残留水平台面 + 竖壁（DEFECT-008），
        //     扩到孔心（跨度 R）——该域无既有 golden，与评审复算的 corrected 构造一致。
        //   两域在 θ = 30° 交界处 R·sinθ = R − R·sinθ（覆盖恰好连续，输出无跳变）。
        double shoulderSpan = (zT * 2 < headRadius) ? headRadius : zT;
        double upTop = yBottom + yT + 10;
        var boxUpper = CsgNodes.Box(xCenter, (yCenter + upTop) / 2, zi + shoulderSpan / 2,
            over, upTop - yCenter, shoulderSpan);
        var shoulder = CsgNodes.Subtract(boxUpper, head);

        var hole = CylinderAlongX(new Vector3D(xCenter, yCenter, zi + headRadius), holeRadius, over);
        var node = CsgNodes.Subtract(
            CsgNodes.Union(CsgNodes.Subtract(box, slantCut, shoulder), head), hole);

        return sz > 0 ? node
            : RotateAboutCenter(node, new Vector3D(0, 0, 0), new Vector3D(0, 180, 0));
    }

    public static CsgNode CylinderAlongX(Vector3D center, double radius, double height)
        => RotateAboutCenter(CylinderAlongZ(center, radius, height), center, new Vector3D(0, 90, 0));

    public static CsgNode CylinderAlongZ(Vector3D center, double radius, double height)
        => RotateAboutCenter(CsgNodes.Cylinder(center, radius, height), center, new Vector3D(90, 0, 0));

    /// <summary>
    /// **回转体（车削体）**：把「(半径, 高度) 站点」折线绕**竖直轴（Y）**回转成实体。
    /// 相邻站点之间用 <see cref="CsgNodes.Cylinder"/>（等半径）或 <see cref="CsgNodes.Cone"/>（变半径；
    /// OpenCSG 的 Cone 即**圆台**，与 <c>CylinderNode</c> 同为「以 center 为中心、跨 Y 轴」）精确拼接：
    /// 直线段**逐段精确**，圆弧段由调用方给出离散站点（误差随离散密度收敛）。
    ///
    /// 用途：**定位销**类零件 = 底段圆柱 + 锥面 + 尖端圆角
    /// （图纸详图「13 / 1:2」，见 <c>LowerShellGeometry.LocatingPinProfile</c>）。
    ///
    /// 站点须按 Y **严格升序**、半径 ≥ 0 且每段至少一端 &gt; 0（否则无法成体）。
    /// </summary>
    public static CsgNode RevolvedStack(
        IReadOnlyList<(double Radius, double Y)> stations, double centerX, double baseY, double centerZ)
    {
        if (stations is null || stations.Count < 2)
            throw new ArgumentException("回转体至少需要两个站点。", nameof(stations));

        var nodes = new List<CsgNode>(stations.Count - 1);
        for (int i = 0; i + 1 < stations.Count; i++)
        {
            var (r0, y0) = stations[i];
            var (r1, y1) = stations[i + 1];
            double h = y1 - y0;
            if (h <= 0)
                throw new ArgumentException($"站点高度须严格升序（第 {i} 站 {y0} → {y1}）。", nameof(stations));
            if (r0 < 0 || r1 < 0)
                throw new ArgumentException($"站点半径不得为负（第 {i} 段 {r0} → {r1}）。", nameof(stations));
            if (r0 <= 0 && r1 <= 0)
                throw new ArgumentException($"第 {i} 段两端半径均为 0，无法成体。", nameof(stations));

            var center = new Vector3D(centerX, baseY + y0 + h / 2.0, centerZ);
            nodes.Add(Math.Abs(r1 - r0) < 1e-9
                ? CsgNodes.Cylinder(center, r0, h)
                : CsgNodes.Cone(center, r1, r0, h));
        }

        return nodes.Count == 1 ? nodes[0] : CsgNodes.Union(nodes.ToArray());
    }

    /// <summary>
    /// size×size 角切（X-Y 面、厚沿 Z）：绕 Z 旋转 45° 的方盒（边长 size·√2），
    /// 内缘与角点围成 size×size 等腰直角三角。仅覆盖角部三角，不触及板边其余部分。
    /// size 无默认值：加劲肋族 5 块的"上下切 50×50 三角"由常量
    /// <c>StiffenerCornerCut</c> → <c>SupportDimensions.Part4CornerCut / StiffenerCornerCutLower</c> 驱动，
    /// 默认参数会让 JSON 覆盖静默失效。
    /// </summary>
    public static CsgNode CornerCut(double x0, double y0, double zC, double over, double size)
    {
        double sideLen = size * Math.Sqrt(2);
        var box = CsgNodes.Box(x0, y0, zC, sideLen, sideLen, over);
        return RotateAboutCenter(box, new Vector3D(x0, y0, zC), new Vector3D(0, 0, 45));
    }

    // ── ──

    /// <summary>
    /// **凸角圆角**：角点 (cx, cy)、**料在 (inX, inY) 方向**（各取 ±1）⇒
    /// 先 `Subtract` **贴角方块**（只切角尖），再 `Union` 与两面**相切**的圆盘（圆心 = 角点沿有料方向内移 r）✓。
    /// 实测：只切角尖 + 相切补圆 ⇒ **包络保持不变**（某零件 验证：倒角前后体积逐位相同 476 000.000 ✓）。
    /// 坑：方块中心偏离角点 r/2 ⇒ 多切 ⇒ 包络缩短 ✗；圆心符号写反 ⇒ 材料加到件外 ✗。
    /// </summary>
    public static CsgNode RoundConvexCorner(CsgNode body, double cx, double cy, double r, double length, double inX, double inY)
    {
        var sq = CsgNodes.Box(cx + inX * r / 2.0, cy + inY * r / 2.0, 0, r, r, length);   // 符号：inX/inY = 料所在方向 ⇒ 取 +
        var dc = CylinderAlongZ(new Vector3D(cx + inX * r, cy + inY * r, 0), r, length);
        return CsgNodes.Union(CsgNodes.Subtract(body, sq), dc);
    }

    /// <summary>
    /// **凹（再入）角圆角**：(sx, sy) 指向**空料**方向 ⇒ 加一个与两面**相切**的圆盘（圆心 = 角点 + (sx·r, sy·r)，轴沿 Z）✓。
    /// 坑：直接 Union 整个圆盘 = 加凸包而非圆角 ✗（某零件 曾因此体积 +37 800 mm³）；
    /// 半径受断面限制（圆盘 X 上限 = 角点 + 2r ⇒ 不得越出断面/弧面 ✗，某零件 因此取 r ≤ 2.5）。
    /// </summary>
    public static CsgNode FilletConcave(CsgNode body, double cx, double cy, double r, double length, double sx, double sy)
        => CsgNodes.Union(body, CylinderAlongZ(new Vector3D(cx + sx * r, cy + sy * r, 0), r, length));

    /// <summary>
    /// 圆弧扫掠的**段端点钳制**：每段角范围先按 ±overlap·step 外扩（免段间缝），再**钳制在 [a0, a0+sweep]** ✓。
    /// 返回该段的 (角中点, 段长)。坑：不钳制 ⇒ 首末段端点外扩 ⇒ 实际包角偏大 ✗
    /// （某零件 曾外扩 0.3512 度 ≈ 5.9 mm/端 ⇒ 吃掉 5 mm 预留间隙、与某零件 互穿 2892.99 mm³）。
    /// </summary>
    public static (double AMid, double SegLen) ArcSegment(double a0, double sweep, int segments, int i, double radius, double overlap = 0.175)
    {
        double step = sweep / segments;
        double lo = Math.Max(a0 + i * step - overlap * step, a0);
        double hi = Math.Min(a0 + (i + 1) * step + overlap * step, a0 + sweep);
        return ((lo + hi) / 2.0, radius * (hi - lo));
    }

    /// <summary>
    /// **T∘R**：先绕**局部原点**旋转 `rotationDeg`，**再**平移 ⇒ 与 `Place` 的语义差异见注释。
    /// 坑：`CsgNodes.Transform(t, r, node)` 实测是「**先平移、后绕全局原点旋转**」⇒ 直接用会把零件甩飞 ✗
    /// （某零件 曾因此落位偏 ≈1000 mm；本方法内部拆两次 `Transform` 规避）。
    /// </summary>
    public static CsgNode PlaceRotateThenTranslate(CsgNode body, Vector3D translation, Vector3D rotationDeg)
    {
        var rotated = CsgNodes.Transform(new Vector3D(0, 0, 0), rotationDeg, body);
        return CsgNodes.Transform(translation, new Vector3D(0, 0, 0), rotated);
    }
}
}






