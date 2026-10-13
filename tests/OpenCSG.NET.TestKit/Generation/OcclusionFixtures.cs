using Csg;

namespace Csg.Test
{
    /// <summary>
    /// Tier 5 遮挡 oracle 的手工夹具（Task 2/3 共用）。
    /// <para>
    /// 几何全部轴对齐、由公开 API 构造（无随机性、无分辨率依赖的斜面），
    /// 使射线投射 oracle 的判定不含掠射歧义；各注释里的 H/实测值来自控制器探针
    /// （<c>artifacts/b2probe</c>）预跑读数，供测试断言对照，不作为断言本身。
    /// </para>
    /// </summary>
    public static class OcclusionFixtures
    {
        /// <summary>
        /// 前后双盒：后块 <c>Cube(10, center)</c>；前块同尺寸、沿 +Z 平移 20 再沿 +X 平移 3（部分遮挡）。
        /// 遮挡视图 = 左视（实测 H=2 / 隐藏总长 ≈16.974）；主视/俯视无遮挡（H=0）。
        /// </summary>
        public static Solid BoxesShifted()
        {
            var back = Solids.Cube(10, center: true);
            var front = Solids.Cube(10, center: true).Translate(0, 0, 20).Translate(3, 0, 0);
            return back.Union(front);
        }

        /// <summary>L 形：20×10×10 方块减去右上角 10³。俯视 H=2、左视 H=2、主视 H=0（实测）。</summary>
        public static Solid LShape()
        {
            var block = Solids.Cube(new Vector3D(20, 10, 10), center: true);
            var notch = Solids.Cube(new Vector3D(10, 10, 10), center: true).Translate(5, 0, 5);
            return block.Subtract(notch);
        }

        /// <summary>
        /// 带贯通空腔的方板：20×20×4 板减 R=3、分辨率 32 的球。主视 H=5、俯视 H=3（实测）。
        /// 左视因孔缘掠射（与 DEFECT-004 机制②同族）不在 oracle 覆盖范围。
        /// </summary>
        public static Solid DrilledPlate()
        {
            var plate = Solids.Cube(new Vector3D(20, 20, 4), center: true);
            var hole = Solids.Sphere(new SphereOptions { Center = new Vector3D(0, 0, 0), Radius = 3.0, Resolution = 32 });
            return plate.Subtract(hole);
        }
    }
}
