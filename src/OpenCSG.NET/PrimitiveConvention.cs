namespace Csg
{

using System;

/// <summary>
/// 宿主基本体的「原点约定」：记录单位尺寸基本体生成后最小角相对原点的偏移比例。
/// 0 = 自原点沿正轴生成（代码长期假设的口径）；−0.5 = 以原点为中心（部分宿主/版本的实际口径）。
/// 由宿主侧实体构造层启动时探测一次并缓存，位移按此补偿，
/// 使 CSG「中心 + 尺寸」语义在任何宿主上都能得到同一实体位置。
/// </summary>
public sealed class PrimitiveConvention
{
    /// <summary>单位盒生成后最小角的 X 偏移比例。</summary>
    public double BoxX { get; }

    /// <summary>单位盒生成后最小角的 Y 偏移比例。</summary>
    public double BoxY { get; }

    /// <summary>单位盒生成后最小角的 Z 偏移比例。</summary>
    public double BoxZ { get; }

    /// <summary>单位高圆台（圆柱）生成后最小角的 Z 偏移比例。</summary>
    public double FrustumZ { get; }

    /// <summary>单位楔形体生成后最小角的 X 偏移比例。</summary>
    public double WedgeX { get; }

    /// <summary>单位楔形体生成后最小角的 Y 偏移比例。</summary>
    public double WedgeY { get; }

    /// <summary>单位楔形体生成后最小角的 Z 偏移比例。</summary>
    public double WedgeZ { get; }

    public PrimitiveConvention(
        double boxX, double boxY, double boxZ,
        double frustumZ,
        double wedgeX, double wedgeY, double wedgeZ)
    {
        BoxX = boxX; BoxY = boxY; BoxZ = boxZ;
        FrustumZ = frustumZ;
        WedgeX = wedgeX; WedgeY = wedgeY; WedgeZ = wedgeZ;
    }

    /// <summary>自原点沿正轴生成（无补偿）的约定。</summary>
    public static PrimitiveConvention OriginBased { get; } = new(0, 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// <c>Solid3d.CreateFrustum</c> 的**圆台实参映射**：底面椭圆取**下（底）半径**、顶面取**上半径**。
    ///
    /// 真实签名（**：
    /// <c>CreateFrustum(Double height, Double radiusAlongX, Double radiusAlongY, Double topRadius)</c>
    /// —— 即「高 + 底面椭圆两半径 + 顶面半径」；某 CAD 宿主（某 CAD 内核）为同源 API，
    /// 仓库内 某 CAD 内核库 的 DotNetARX 锥体助手 <c>CreateFrustum(h, r, r, 0)</c>（顶点朝上）与之口径一致。
    ///
    /// **历史缺陷（本轮定位并修复）**：宿主侧实体构造层 原先按「(h, topRadius, bottomRadius, bottomRadius)」传参，
    /// 得到的是**倒置圆台**（底 = 上半径、顶 = 下半径）—— 由于 
    /// 该分支长期未被触发；一旦用到锥面，宿主里的锥体会渲染成**漏斗形**（细端朝下、粗端朝上）。
    /// </summary>
    public static (double RadiusAlongX, double RadiusAlongY, double TopRadius) FrustumRadii(
        double bottomRadius, double topRadius) => (bottomRadius, bottomRadius, topRadius);

    /// <summary>已知约定（0 / −0.5）的判定容差：容纳宿主读取包围盒时的浮点噪声。</summary>
    private const double CanonicalTolerance = 1e-6;

    /// <summary>
    /// 校验单个探测因子：非有限值或超出 [−1, 1] 说明探测无效（宿主未就绪、返回退化实体、
    /// 或包围盒读到了别的东西）。此时按该因子补偿只会静默产出整体错位的几何 —— 宁可落图失败
    /// 也不能落错图，故抛错并把宿主调用与轴向写进消息。
    /// 区间内的非标准因子（既非 0 也非 −0.5）仍按线性公式补偿（公式对任意因子成立），由调用方告警。
    /// </summary>
    public static double ValidateProbeFactor(string hostCall, string axis, double factor)
    {
        if (double.IsNaN(factor) || double.IsInfinity(factor) || Math.Abs(factor) > 1 + CanonicalTolerance)
        {
            throw new InvalidOperationException(
                $"宿主基本体原点约定探测失败：{hostCall} 的 {axis} 轴最小角偏移因子 = {factor}（应为 [−1, 1] 内的有限值）。"
                + "请核验宿主基本体生成口径后再落图（本插件拒绝按无效因子补偿，以免几何整体错位）。");
        }
        return factor;
    }

    /// <summary>探测因子是否属于已知约定（0 = 自原点生成、−0.5 = 以原点为中心）。</summary>
    public static bool IsCanonicalFactor(double factor)
        => Math.Abs(factor) <= CanonicalTolerance || Math.Abs(factor + 0.5) <= CanonicalTolerance;

    /// <summary>
    /// 「中心 + 尺寸」→ 宿主生成体的平移分量：目标最小角（center − size/2）减去生成体实际最小角（factor·size）。
    /// 自原点约定（factor = 0）得 center − size/2；居中约定（factor = −0.5）得 center。
    /// </summary>
    public static double Offset(double center, double size, double probeMinFactor)
        => center - size / 2 - probeMinFactor * size;
}
}



