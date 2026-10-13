using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// Tier 5：遮挡夹具的解析区域包含 + 回归钉（与 <c>Tier5OcclusionOracleTest</c> 的逐点射线 oracle 互补）。
    /// <para>
    /// 本文件每个视图三条断言：①每条 <c>IsVisible == false</c> 线段的 1/4、1/2、3/4 内部采样点
    /// 必须落在解析推导出的遮挡矩形（外扩 0.05）内 —— 抓「假隐藏」（可见棱边被判成隐藏、且落在
    /// 遮挡体轮廓之外）；②隐藏总长 <c>sum(H.Length)</c> 的确定性回归钉（±0.05）；③V/H/总线段数一致。
    /// </para>
    /// <para>
    /// 矩形一律以**屏幕 (h, v)** 表述（<c>h = p·Right</c>、<c>v = p·Up</c>，取轴见
    /// <c>ViewProjectorContracts.cs:85-104</c>），避开 <c>ViewBridge.ToViewTriangles</c> 的
    /// 网格↔CSG Y↔Z 反射（B1 已核实并冻结）——各视图的具体映射写在用例注释里。
    /// </para>
    /// <para>
    /// 钉值 = 任务书表（控制器实测）经本任务探针 <c>artifacts/b2task3probe</c>（gitignored）逐条复核后的
    /// 当前代码实测值；属确定性输出，运行不匹配时按失败分类处理、不得直接改数字。
    /// </para>
    /// </summary>
    [TestFixture]
    public class Tier5OcclusionFixtureTest
    {
        /// <summary>区域包含的外扩量（矩形本身就是轮廓的精确值，留合并/取整余量）。</summary>
        const double RegionPad = 0.05;

        /// <summary>隐藏总长钉的容差（长度量级微小的格量化差异不影响判定）。</summary>
        const double LengthTolerance = 0.05;

        [Test]
        public void BoxesShifted_LeftView_HiddenInsideFrontBoxSilhouette()
        {
            var view = ViewNamed(OcclusionFixtures.BoxesShifted(), "左视");

            // 区域推导：前块 = 10³ 盒 CSG Translate(3,0,20)（后块同尺寸居中）。左视取轴
            // h=−mesh_x、v=mesh_z（ViewProjectorContracts:98，mesh↔CSG 为 mesh=(x,z,y)）
            // ⇒ 前块轮廓：mesh x∈[−2,8] → h∈[−8,2]；mesh z(=CSG y)∈[−5,5] → v∈[−5,5]。
            // 后块被前块遮住的远侧棱边必须落在前块轮廓内（实测两条：(−5,4.986)→(−5,−5)、
            // (1.988,−5)→(−5,−5)，端点 4.986 / 1.988 均在内）。
            AssertHiddenWithin(view, -8.0, 2.0, -5.0, 5.0, "前块轮廓");
            AssertHiddenLength(view, expected: 16.974); // 实测 16.974063
            AssertCounting(view, expectedVisible: 6, expectedHidden: 2);
        }

        [Test]
        public void LShape_TopView_HiddenInsideSolidSilhouette()
        {
            var view = ViewNamed(OcclusionFixtures.LShape(), "俯视");

            // 区域推导：俯视取轴 h=−mesh_y、v=mesh_x（ViewProjectorContracts:91-94）
            // ⇒ h 轴 = CSG z（台阶抬升方向）、v 轴 = CSG x（20 长轴）。缺口是沿 CSG z 切掉的
            // 一半：缺口象限 = (h∈[−5,0] ∧ v∈[0,10])，俯视轮廓 = 全矩形减去该象限（L 形）。
            // 视线沿网格 z = CSG y（棱柱轴）⇒ 远侧(CSG y=−5)棱边被近侧同一截面的材料遮挡，
            // 实测两条隐藏段都在实体自身轮廓上/内：(4.978,−10)→(−5,−10) 是 v=−10 的远侧棱边；
            // (0,9.978)→(0,−10) 是 CSG z=0 面上的远侧缝（v∈[0,10] 段为缺口底棱、v∈[−10,0] 段为
            // 远侧面内三角剖分缝），跨满 v 全宽。
            // 注：任务书给的矩形 [−5,5]×[−10,0]（按「缺口沿 v 轴切、v≤0 半边永不缺料」推导）装不下
            // 上面第二条 —— 其起点 (0,9.978) 已在矩形外；实际缺口象限 = (h<0 ∧ v>0)（缺口沿 h 轴切）。
            // 故此处取实体自身轮廓包络 [−5,5]×[−10,10]：棱柱轴视图下矩形能取的最紧界，仍能抓
            // 「隐藏段越出实体轮廓」的假隐藏（缺口象限内无材料，理论上不可能出现隐藏段）。详见 Task 3 报告。
            AssertHiddenWithin(view, -5.0, 5.0, -10.0, 10.0, "实体自身轮廓包络");
            AssertHiddenLength(view, expected: 29.956); // 实测 29.955703
            AssertCounting(view, expectedVisible: 6, expectedHidden: 2);
        }

        [Test]
        public void LShape_LeftView_OnlyHiddenLengthPin()
        {
            var view = ViewNamed(OcclusionFixtures.LShape(), "左视");

            // 不做区域包含：左视取轴 h=−mesh_x、v=mesh_z（ViewProjectorContracts:98），视线沿网格 y
            // = CSG z（台阶抬升轴，实体沿该轴有台阶变化）—— 近侧材料（台阶顶面 h∈[0,10] 与缺口底面
            // h∈[−10,0]，v∈[−5,5]）的投影已覆盖整个包络 = 实体整体轮廓，矩形包含断言退化为恒真，
            // 故只钉长度 + 计数。
            // 实测两条隐藏段：(0,4.978)→(0,−5)（CSG x=0 面远侧竖边）、(0,−5)→(10,−5)（CSG y=−5 远侧端棱）。
            AssertHiddenLength(view, expected: 19.978); // 实测 19.977827
            AssertCounting(view, expectedVisible: 5, expectedHidden: 2);
        }

        [Test]
        public void DrilledPlate_FrontView_HiddenInsideNearPlateFaceSilhouette()
        {
            var view = ViewNamed(OcclusionFixtures.DrilledPlate(), "主视");

            // 区域推导：板 = 20×20×4 CSG 盒、R=3 球（穿出 z=±2 板面）。主视取轴
            // h=−mesh_y、v=mesh_z（ViewProjectorContracts:88-89）；mesh↔CSG 为 mesh=(x,z,y)
            // ⇒ 板截面 = mesh y(=CSG z)∈[−2,2] → h∈[−2,2]；mesh z(=CSG y)∈[−10,10] → v∈[−10,10]。
            // 空腔远侧轮廓（实测 5 条隐藏段：(−2,9.978)→(−2,−10) 是 CSG z=2 面上 CSG x=+10 远侧的长棱，
            // 另 4 条为远侧孔缘碎段，|h| ≤ 2）全在这块「近侧板面轮廓」内。
            AssertHiddenWithin(view, -2.0, 2.0, -10.0, 10.0, "近侧板面轮廓");
            AssertHiddenLength(view, expected: 22.016); // 实测 22.016323
            AssertCounting(view, expectedVisible: 4, expectedHidden: 5);
        }

        [Test]
        public void DrilledPlate_TopView_HiddenInsidePlateAndCavitySilhouette()
        {
            var view = ViewNamed(OcclusionFixtures.DrilledPlate(), "俯视");

            // 区域推导：俯视取轴 h=−mesh_y、v=mesh_x（ViewProjectorContracts:91-94）⇒ 板在俯视的
            // 截面 = 厚 4 方向 h∈[−2,2]、20 长轴 v∈[−10,10]；孔（R=3）投影为半径 3 的圆盘，
            // h∈[−3,3] 被板厚裁到 |h| ≤ 2。矩形保守取 h∈[−5,5]（⊇ 板截面 ∪ 孔腔投影 [−3,3]，
            // 见任务书脚注 ②）、v∈[−10,10]：实测 3 条隐藏段为两条远侧孔缘碎段
            // (1.148,2.772)→(2,2.221) 与 (−2,10)→(−2,−10)（CSG z=2 棱另侧 CSG y=−10 远侧的长棱，
            // h 恒 = −2），violations=0。
            AssertHiddenWithin(view, -5.0, 5.0, -10.0, 10.0, "板面∪孔腔轮廓（保守）");
            AssertHiddenLength(view, expected: 21.019); // 实测 21.01926
            AssertCounting(view, expectedVisible: 4, expectedHidden: 3);
        }

        /// <summary>取三视图投影里名为 <paramref name="name"/> 的视图；缺失即失败（防夹具/方向改名后的静默空转）。</summary>
        static ViewProjection ViewNamed(Solid solid, string name)
        {
            foreach (var view in ViewBridge.ProjectViews(solid, ViewDirections.OrthographicThree))
                if (view.Direction.Name == name)
                    return view;

            Assert.Fail($"视图 {name} 不存在（三视图只有 主视/俯视/左视）");
            return null!;
        }

        /// <summary>
        /// 每条隐藏段取 1/4、1/2、3/4 三个内部采样点，断言落在 [minH−0.05, maxH+0.05] × [minV−0.05, maxV+0.05] 内。
        /// </summary>
        static void AssertHiddenWithin(
            ViewProjection view, double minH, double maxH, double minV, double maxV, string label)
        {
            var index = 0;
            var hidden = 0;
            foreach (var segment in view.Segments)
            {
                index++;
                if (segment.IsVisible) continue;
                hidden++;
                for (var k = 1; k <= 3; k++)
                {
                    var t = k / 4.0;
                    var h = segment.Start.X + (segment.End.X - segment.Start.X) * t;
                    var v = segment.Start.Y + (segment.End.Y - segment.Start.Y) * t;
                    var where = $"{view.Direction.Name}/{label}：隐藏段 #{index} 的 t={t:0.##} 采样点 "
                        + $"({h:0.####},{v:0.####}) 越界（段 "
                        + $"({segment.Start.X:0.####},{segment.Start.Y:0.####})→"
                        + $"({segment.End.X:0.####},{segment.End.Y:0.####})，"
                        + $"矩形 h[{minH},{maxH}] v[{minV},{maxV}] ±{RegionPad}）";
                    Assert.That(h, Is.InRange(minH - RegionPad, maxH + RegionPad), where);
                    Assert.That(v, Is.InRange(minV - RegionPad, maxV + RegionPad), where);
                }
            }

            // 防空洞：没有隐藏段时区域包含会静默空转（夹具几何或管线行为已变），单独报出。
            Assert.That(hidden, Is.GreaterThan(0),
                $"{view.Direction.Name}/{label}：该视图零隐藏段，区域包含断言空转");
        }

        /// <summary>隐藏总长钉：sum(H.Length) == expected ± 0.05（确定性实测值）。</summary>
        static void AssertHiddenLength(ViewProjection view, double expected)
        {
            var hiddenLen = 0.0;
            var hidden = 0;
            foreach (var segment in view.Segments)
            {
                if (segment.IsVisible) continue;
                hiddenLen += segment.Length;
                hidden++;
            }

            Assert.That(hidden, Is.GreaterThan(0), $"{view.Direction.Name}：该视图零隐藏段，长度钉无意义");
            Assert.That(hiddenLen, Is.EqualTo(expected).Within(LengthTolerance),
                $"{view.Direction.Name}：隐藏总长实测 {hiddenLen:0.######}（{hidden} 段），"
                + $"钉值 {expected}±{LengthTolerance}");
        }

        /// <summary>计数一致性：V、H 各自对齐钉值，且 V + H == Segments.Count。</summary>
        static void AssertCounting(ViewProjection view, int expectedVisible, int expectedHidden)
        {
            Assert.That(view.VisibleCount, Is.EqualTo(expectedVisible),
                $"{view.Direction.Name}：VisibleCount 实测 {view.VisibleCount}");
            Assert.That(view.HiddenCount, Is.EqualTo(expectedHidden),
                $"{view.Direction.Name}：HiddenCount 实测 {view.HiddenCount}");
            Assert.That(view.Segments.Count, Is.EqualTo(expectedVisible + expectedHidden),
                $"{view.Direction.Name}：Segments.Count 实测 {view.Segments.Count}，应 = V+H "
                + $"= {expectedVisible + expectedHidden}");
        }
    }
}
