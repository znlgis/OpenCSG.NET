namespace Csg
{

using Csg;

/// <summary>
/// 半平面切割体：板面在 Y-Z 平面、厚度沿 X 的板上，用绕 X 旋转的大方盒
/// 切掉切割线一侧（半平面），配合板自身边界得到精确的三角/梯形切角。
/// 切割线过 ref 点、方向 (dirY, dirZ)；法线 n=(−dirZ, dirY)/|dir|；
/// sideSign=+1 切 n 正侧、−1 切 n 负侧。盒边长 s=√2·halfExtent，其余边落在板外不产生过切。
/// </summary>
public static class HalfPlaneCut
{
    /// <summary>
    /// 半平面切割体（Y-Z 平面、厚度沿 X）：切割线过 (refY, refZ)、方向 (dirY, dirZ)。
    /// </summary>
    /// <param name="centerX">盒中心 X（与板中心对齐，overshoot 贯穿板厚）</param>
    /// <param name="refY">切割线经过点 Y</param>
    /// <param name="refZ">切割线经过点 Z</param>
    /// <param name="dirY">切割线方向 Y 分量</param>
    /// <param name="dirZ">切割线方向 Z 分量</param>
    /// <param name="halfExtent">切割线两侧覆盖半径（盒边长 = √2·halfExtent）</param>
    /// <param name="overshoot">沿板厚方向的超出量（防共面欠切）</param>
    /// <param name="sideSign">+1 切法线正侧、−1 切法线负侧</param>
    public static CsgNode Build(
        double centerX, double refY, double refZ, double dirY, double dirZ,
        double halfExtent, double overshoot, double sideSign)
    {
        double len = System.Math.Sqrt(dirY * dirY + dirZ * dirZ);
        if (len == 0)
            throw new System.ArgumentException("切割方向向量为零", nameof(dirY));
        double nY = -dirZ / len;
        double nZ = dirY / len;
        double s = System.Math.Sqrt(2) * halfExtent;
        double off = sideSign * s / 2;

        var center = new Vector3D(centerX, refY + off * nY, refZ + off * nZ);
        var box = CsgNodes.Box(center, new Vector3D(overshoot, s, s));
        double phi = System.Math.Atan2(dirZ, dirY) * 180.0 / System.Math.PI;
        return CsgHelpers.RotateAboutCenter(box, center, new Vector3D(phi, 0, 0));
    }

    /// <summary>
    /// 半平面切割体（X-Y 平面、厚度沿 Z）：切割线过 (refX, refY)、方向 (dirX, dirY)；
    /// 法线 n=(−dirY, dirX)/|dir|；sideSign=+1 切 n 正侧、−1 切 n 负侧。
    /// 盒 X/Y 边长 s=√2·halfExtent，Z 方向 overshoot 落在板外不产生过切。
    /// </summary>
    /// <param name="refX">切割线经过点 X</param>
    /// <param name="refY">切割线经过点 Y</param>
    /// <param name="dirX">切割线方向 X 分量</param>
    /// <param name="dirY">切割线方向 Y 分量</param>
    /// <param name="halfExtent">切割线两侧覆盖半径（盒边长 = √2·halfExtent）</param>
    /// <param name="overshoot">沿板厚方向的超出量（防共面欠切）</param>
    /// <param name="sideSign">+1 切法线正侧、−1 切法线负侧</param>
    /// <param name="centerZ">盒中心 Z（与板中心对齐，overshoot 贯穿板厚）</param>
    public static CsgNode BuildXY(
        double refX, double refY, double dirX, double dirY,
        double halfExtent, double overshoot, double sideSign, double centerZ)
    {
        double len = System.Math.Sqrt(dirX * dirX + dirY * dirY);
        if (len == 0)
            throw new System.ArgumentException("切割方向向量为零", nameof(dirX));
        double nX = -dirY / len;
        double nY = dirX / len;
        double s = System.Math.Sqrt(2) * halfExtent;
        double off = sideSign * s / 2;

        var center = new Vector3D(refX + off * nX, refY + off * nY, centerZ);
        var box = CsgNodes.Box(center, new Vector3D(s, s, overshoot));
        double phi = System.Math.Atan2(dirY, dirX) * 180.0 / System.Math.PI;
        return CsgHelpers.RotateAboutCenter(box, center, new Vector3D(0, 0, phi));
    }
}
}

