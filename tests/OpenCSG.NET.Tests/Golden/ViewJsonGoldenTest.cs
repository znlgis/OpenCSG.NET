using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Csg;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// HLR 三视图的**测试侧稳定 JSON 序列化器**（Plan D / Task 3；只存在于测试工程，`src/` 零改动）。
    ///
    /// <para><b>为什么存在</b>：L4 需要把 <see cref="ViewBridge.ProjectViews"/> 的投影产物
    /// （线段表 + 圆弧表 + 包络）钉成逐字节可比的基线。JSON 序列化若不固定口径，跨运行就会出现
    /// 数字格式/键序/行尾漂移，基线形同虚设。本类把口径全部钉死：</para>
    /// <list type="bullet">
    /// <item><b>字段顺序固定</b>：<c>view → bounds → segments → arcs</c>；
    /// 线段 <c>a → b → visible</c>；圆弧 <c>cx → cy → r → start → sweep → visible → closed</c>。</item>
    /// <item><b>数字</b>：一律 <c>double.ToString("R", CultureInfo.InvariantCulture)</c>（最短往返格式，
    /// <c>double.Parse</c> 可逐位还原；不受宿主区域设置影响）。NaN/∞ 不是合法 JSON 数字 ⇒ 显式抛
    /// <see cref="InvalidOperationException"/>，不允许把非法 JSON 静默写进 golden。</item>
    /// <item><b>行尾</b>：`\n` 显式写出（**不得**用 <see cref="Environment.NewLine"/>——Windows 下是 CRLF，
    /// 跨平台 golden 会漂移）。落盘侧统一 <c>UTF8Encoding(false)</c>（无 BOM）。</item>
    /// <item><b>顺序</b>：视图按入参顺序；线段/圆弧按 <see cref="ViewProjection"/> 的既有输出顺序
    /// （管线确定性已由 Plan B/C 背书）——本类不排序、不去重、不做任何"规范化"。</item>
    /// <item><b>单位</b>：坐标 = 模型 2D mm；<c>start</c>/<c>sweep</c> = 弧度（与 <see cref="ViewArc"/>
    /// 字段口径一致；<c>closed=true</c> 时 <c>sweep</c> 恒为 2π，与 <c>StartAngle</c>/<c>EndAngle</c>
    /// 的差值无关）。</item>
    /// </list>
    ///
    /// <para><b>布局</b>（每个视图对象纵向展开；空表写 <c>[]</c>；文件末尾有换行）：</para>
    /// <code>
    /// [
    ///   {"view":"主视","bounds":[minX,minY,maxX,maxY],
    ///    "segments":[
    ///     {"a":[x,y],"b":[x,y],"visible":true}
    ///    ],
    ///    "arcs":[
    ///     {"cx":..,"cy":..,"r":..,"start":..,"sweep":..,"visible":..,"closed":..}
    ///    ]}
    /// ]
    /// </code>
    /// </summary>
    public static class ViewJson
    {
        /// <summary>序列化三视图列表（口径见类注释；输出以 `\n` 收尾）。</summary>
        public static string Serialize(IReadOnlyList<ViewProjection> views)
        {
            if (views == null) throw new ArgumentNullException(nameof(views));

            var sb = new StringBuilder(8 * 1024);
            sb.Append("[\n");
            for (var v = 0; v < views.Count; v++)
            {
                var view = views[v];
                var bounds = view.Bounds;

                sb.Append("  {\"view\":");
                AppendString(sb, view.Direction.Name);
                sb.Append(",\"bounds\":[");
                AppendNumber(sb, bounds.MinX);
                sb.Append(',');
                AppendNumber(sb, bounds.MinY);
                sb.Append(',');
                AppendNumber(sb, bounds.MaxX);
                sb.Append(',');
                AppendNumber(sb, bounds.MaxY);
                sb.Append("],\n");

                AppendSegments(sb, view.Segments);
                AppendArcs(sb, view.Arcs);

                sb.Append(v + 1 < views.Count ? "  },\n" : "  }\n");
            }
            sb.Append("]\n");
            return sb.ToString();
        }

        static void AppendSegments(StringBuilder sb, IReadOnlyList<ViewSegment> segments)
        {
            if (segments.Count == 0)
            {
                sb.Append("   \"segments\":[],\n");
                return;
            }

            sb.Append("   \"segments\":[\n");
            for (var i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                sb.Append("    {\"a\":[");
                AppendNumber(sb, segment.Start.X);
                sb.Append(',');
                AppendNumber(sb, segment.Start.Y);
                sb.Append("],\"b\":[");
                AppendNumber(sb, segment.End.X);
                sb.Append(',');
                AppendNumber(sb, segment.End.Y);
                sb.Append("],\"visible\":");
                sb.Append(segment.IsVisible ? "true" : "false");
                sb.Append('}');
                sb.Append(i + 1 < segments.Count ? ",\n" : "\n");
            }
            sb.Append("   ],\n");
        }

        static void AppendArcs(StringBuilder sb, IReadOnlyList<ViewArc> arcs)
        {
            if (arcs.Count == 0)
            {
                sb.Append("   \"arcs\":[]\n");
                return;
            }

            sb.Append("   \"arcs\":[\n");
            for (var i = 0; i < arcs.Count; i++)
            {
                var arc = arcs[i];
                sb.Append("    {\"cx\":");
                AppendNumber(sb, arc.Center.X);
                sb.Append(",\"cy\":");
                AppendNumber(sb, arc.Center.Y);
                sb.Append(",\"r\":");
                AppendNumber(sb, arc.Radius);
                sb.Append(",\"start\":");
                AppendNumber(sb, arc.StartAngle);
                sb.Append(",\"sweep\":");
                AppendNumber(sb, SweepRadians(arc));
                sb.Append(",\"visible\":");
                sb.Append(arc.IsVisible ? "true" : "false");
                sb.Append(",\"closed\":");
                sb.Append(arc.IsClosed ? "true" : "false");
                sb.Append('}');
                sb.Append(i + 1 < arcs.Count ? ",\n" : "\n");
            }
            sb.Append("   ]\n");
        }

        /// <summary>
        /// 圆弧扫掠角（弧度，0~2π 闭区间）：口径与 <see cref="ViewArc.SweepDegrees"/> 一致
        /// （整圆恒 2π；否则把 End−Start 归一到 (0, 2π)），只是不绕道度数。
        /// </summary>
        static double SweepRadians(ViewArc arc)
        {
            if (arc.IsClosed)
                return 2 * Math.PI;

            var sweep = arc.EndAngle - arc.StartAngle;
            while (sweep < 0) sweep += 2 * Math.PI;
            while (sweep >= 2 * Math.PI) sweep -= 2 * Math.PI;
            return sweep;
        }

        static void AppendNumber(StringBuilder sb, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new InvalidOperationException(
                    $"JSON 无法表达非有限数（{value.ToString(CultureInfo.InvariantCulture)}）；序列化前须先修数据。");
            sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>写一个 JSON 字符串字面量：转义 <c>"</c>/<c>\</c> 与全部控制字符；非 ASCII 原样写（UTF-8）。</summary>
        static void AppendString(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < ' ')
                            sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    /// <summary>
    /// L4 HLR 三视图 JSON 基线（Plan D / Task 3）：为 `Solid → CsgMesh → ViewProjector`
    /// 投影产物钉 ~15 个**逐字节** JSON 基线，文件落在 <c>Results/Views/&lt;Fixture&gt;.&lt;TestName&gt;.json</c>
    /// （D1 已把门禁 <see cref="GoldenGuard.FindPendingGoldens"/> 与 tools/promote-goldens.sh
    /// 递归覆盖到该子目录，.gitignore 亦已含 <c>tests/**/Results/**/*_.json</c>）。
    ///
    /// <para><b>golden 约定</b>（沿用 <c>ModuleBaselineGoldenTest</c> 的 byte-exact 辅助模式）：
    /// 内容 = <c>ViewJson.Serialize(ViewBridge.ProjectViews(...))</c> 的 UTF-8 无 BOM 字节；
    /// 基线缺失 ⇒ 写 <c>&lt;Fixture&gt;.&lt;TestName&gt;_.json</c> + <c>Assert.Inconclusive</c>（门禁随后判红），
    /// 人工用 <c>tools/promote-goldens.sh</c> 核对升级；基线存在 ⇒ <c>File.ReadAllBytes</c> 逐字节比对，
    /// 不一致即失败。每个夹具同时断言 basics：视图数、每视图**非空**、序列化输出可被
    /// <c>JsonDocument.Parse</c> 解析（非空口径 = <c>Segments.Count + Arcs.Count &gt; 0</c>：
    /// 简报原文写 <c>Segments.Count &gt; 0</c>，但解析几何体的**轴向视图**例外——圆柱俯视的整圈
    /// 边缘会被 <see cref="ViewArcFitter"/> 整体吸收为一条 ARC（Segments=0、Arcs=1，实测见
    /// <c>Cylinder_ThreeViews</c> 注释），故按「非空」断言并额外要求夹具合计仍含折线段）。</para>
    ///
    /// <para><b>V/H 分类按现状冻结（回归指纹；DEFECT-005 注释）</b>：golden 记录的是管线**当前输出**，
    /// 不判断对错。<see cref="ViewSegment.IsVisible"/> 与 <see cref="ViewArc.IsVisible"/> 受
    /// DEFECT-005（深度栅格量化 ⇒ 可见边被判 H 产生虚线碎段）及 DEFECT-004 族（掠射轮廓 V/H 交替）
    /// 影响，本套件**不逐条复核分类正确性**，只把现状冻结为回归指纹：任何管线改动（含将来对
    /// DEFECT-005 的修复）都会让相关 golden 集体变更 —— 那是**预期的重基线化**，
    /// 必须经 promote 脚本人工确认（不得静默覆盖）。</para>
    ///
    /// <para><b>夹具口径</b>：简报指定的 Cube / Box10x20x30 / LShape / DrilledPlate（主+俯，避左视掠射）/
    /// Sphere / Cylinder / Cone（主+俯）/ SectorCylinder90（A2 语义）/ CPurlin（简化版，见该用例注释），
    /// 另补 SectorCylinder270、Wedge、HexPrism、HiddenBoxes、HollowBox、TabbedPlate 凑足 15 条
    /// （覆盖隐藏线、斜掠面、圆弧、A2 两分支与真实零件各一类）。</para>
    /// </summary>
    [TestFixture]
    public class ViewJsonGoldenTest
    {
        const string Fixture = "ViewJsonGoldenTest";

        // ─────────────── 简报指定夹具 ───────────────

        [Test]
        public void Cube_ThreeViews()
        {
            // 10³ 立方体居中：HLR 的最基本件（每视图只余近端面 4 条轮廓边）。
            var cube = Solids.Cube(10, center: true);
            AssertGoldenJson(cube, ViewDirections.OrthographicThree);
        }

        [Test]
        public void Box10x20x30_ThreeViews()
        {
            // 各向异性盒（CSG 尺寸 X=10 / Y=20 / Z=30）居中：三视图宽高按取轴映射形成
            // 30×20、30×10、10×20 三种比例（取轴口径已由 ViewBridgeTest 钉死，此处钉 HLR 内容）。
            var box = Solids.Cube(new Vector3D(10, 20, 30), center: true);
            AssertGoldenJson(box, ViewDirections.OrthographicThree);
        }

        [Test]
        public void LShape_ThreeViews()
        {
            // TestKit 遮挡夹具：20×10×10 方块减去右上角 10³——俯视/左视含隐藏段
            // （B2 探针实测 H=2），是 V/H 分类的回归指纹件。
            var lshape = OcclusionFixtures.LShape();
            AssertGoldenJson(lshape, ViewDirections.OrthographicThree);
        }

        [Test]
        public void DrilledPlate_FrontTop()
        {
            // 通孔板（20×20×4 板减去 R=3、分辨率 32 的球，TestKit 遮挡夹具）。
            // 只取主视 + 俯视：左视孔缘掠射属 DEFECT-004 同族的不稳定分类（简报指定回避）。
            var plate = OcclusionFixtures.DrilledPlate();
            AssertGoldenJson(plate, new[] { ViewDirections.Front, ViewDirections.Top });
        }

        [Test]
        public void Sphere_ThreeViews()
        {
            // R10 球、默认分辨率（Solid.DefaultResolution3D = 12）：镶嵌轮廓的三视图指纹。
            var sphere = Solids.Sphere(10, center: true);
            AssertGoldenJson(sphere, ViewDirections.OrthographicThree);
        }

        [Test]
        public void Cylinder_ThreeViews()
        {
            // r=8、高 24 的圆柱（轴沿 CSG Y ⇒ 视图空间竖轴；默认 12 段）：主/左视为矩形轮廓；
            // 俯视是**轴向视图被弧拟合整体吸收**的典型（探针 d3probe 实测）：12 边形顶点的最小二乘圆
            // 精确成立 ⇒ Segments=0、Arcs=1（330° 开口弧，缺 30° > MaxClosingGapDegrees=25° 未补整圆），
            // 且 ViewProjection.Bounds 只由线段导出 ⇒ (0,0,0,0)——全部按现状冻结进 golden。
            // 该口径即夹具非空断言用「线段+圆弧」而非「线段」的原因（类注释）。
            var cylinder = Solids.Cylinder(8, 24, center: true);
            AssertGoldenJson(cylinder, ViewDirections.OrthographicThree);
        }

        [Test]
        public void Cone_FrontTop()
        {
            // 正圆锥（CsgNodes.Cone：中心原点、TopRadius=0、BottomRadius=10、高 30，轴沿 CSG Y）。
            // 只取主视 + 俯视：锥面斜掠投影与 DEFECT-005/004 同族，分类原样冻结为指纹。
            var cone = CsgEvaluator.Evaluate(CsgNodes.Cone(new Vector3D(0, 0, 0), 0, 10, 30));
            AssertGoldenJson(cone, new[] { ViewDirections.Front, ViewDirections.Top });
        }

        [Test]
        public void SectorCylinder90_ThreeViews()
        {
            // A2 修复后的扇形柱语义（Plan C）：Y 轴长 24、R=8、SectorAngle=90（四分之一楔，
            // 两片径向封壁全封闭）——与 D2 的 SectorCylinder_90 STL 基线同参数。
            var solid = Solids.Cylinder(new CylinderOptions
            {
                Start = new Vector3D(0, -12, 0),
                End = new Vector3D(0, 12, 0),
                RadiusStart = 8,
                RadiusEnd = 8,
                SectorAngle = 90,
            });
            AssertGoldenJson(solid, ViewDirections.OrthographicThree);
        }

        [Test]
        public void CPurlin_FrontTop()
        {
            // **简化版** C 型檩条（简报允许：真实样本 Runner.CPurlin 的 CpurlinModel 抽取在 Plan D / Task 4，
            // 本任务先不引 samples —— 需要 ProjectReference，属 D4 口径）。
            // 尺寸取真实样本（腹板 152 / 翼缘 64 / 卷边 18.5 / 厚 2.4 / 长 1000），但**无冲孔、无角部圆角、
            // 无端部切口**；姿态与真实模型同口径：长沿 CSG Y（±500）、腹板沿 CSG Z、翼缘沿 CSG X。
            // 构造 = 外廓 64×1000×152 整块按截面做 3 次轴对齐减料（开口槽 1 + 上下悬挑槽 2）。
            // 视图读数：主视 = 152×1000（腹板立面）、俯视 = 152×64（C 形断面轮廓）——与 Task 5 对真实模型的预期一致。
            const double web = 152, flange = 64, lip = 18.5, thickness = 2.4, length = 1000, margin = 2;
            const double flangeInnerZ = web / 2 - thickness; // 73.6，翼缘内表面（CSG Z）
            const double lipInnerZ = flangeInnerZ - lip;     // 55.1，卷边内端
            const double lipInnerX = flange - thickness;     // 61.6，卷边内侧（= 翼缘尖端内表面）

            var outer = Solids.Cube(new Vector3D(flange, length, web), new Vector3D(flange / 2, 0, 0));

            // ① 开口槽：从 +X 面挖入到腹板内表面（X ∈ [厚度, 外廓+余量]），Z 留出两侧「翼缘+悬挑」条带。
            var mouth = Solids.Cube(
                new Vector3D(flange - thickness + margin, length + 2 * margin, 2 * lipInnerZ),
                new Vector3D((thickness + flange + margin) / 2, 0, 0));

            // ② 悬挑槽（上下各一）：掏空「卷边与翼缘之间」的悬挑区（翼缘本体 Z ≥ 73.6 保留）。
            var lipCutTop = Solids.Cube(
                new Vector3D(lipInnerX - thickness, length + 2 * margin, lip),
                new Vector3D((thickness + lipInnerX) / 2, 0, (lipInnerZ + flangeInnerZ) / 2));
            var lipCutBottom = Solids.Cube(
                new Vector3D(lipInnerX - thickness, length + 2 * margin, lip),
                new Vector3D((thickness + lipInnerX) / 2, 0, -(lipInnerZ + flangeInnerZ) / 2));

            var section = outer.Subtract(mouth, lipCutTop, lipCutBottom);
            AssertGoldenJson(section, new[] { ViewDirections.Front, ViewDirections.Top });
        }

        // ─────────────── 补充夹具（凑足 15 条覆盖） ───────────────

        [Test]
        public void SectorCylinder270_ThreeViews()
        {
            // 同上参数、SectorAngle=270（A2 归一化路径的四分之三扇形分支）。
            var solid = Solids.Cylinder(new CylinderOptions
            {
                Start = new Vector3D(0, -12, 0),
                End = new Vector3D(0, 12, 0),
                RadiusStart = 8,
                RadiusEnd = 8,
                SectorAngle = 270,
            });
            AssertGoldenJson(solid, ViewDirections.OrthographicThree);
        }

        [Test]
        public void Wedge_ThreeViews()
        {
            // 直角楔块（CsgHelpers.CornerWedge：角点 (0,0)、两腿沿 +X/+Y 各 8、厚 5、Z=0 居中）：
            // 斜面在三个视图均投影为斜轮廓，是 DEFECT-005 斜掠面 V/H 分类的天然指纹件。
            var wedge = CsgEvaluator.Evaluate(
                CsgHelpers.CornerWedge(0, 0, dx: 1, dy: 1, leg: 8, zCenter: 0, thickness: 5));
            AssertGoldenJson(wedge, ViewDirections.OrthographicThree);
        }

        [Test]
        public void HexPrism_ThreeViews()
        {
            // 正六边形棱柱（CsgHelpers.PrismXY：R=20 外接圆、六等分取点、沿 Z 拉伸 6、Z=0 居中）：
            // 俯视为六边形斜轮廓、主/左视为矩形轮廓。
            const double radius = 20, thickness = 6;
            var points = new (double X, double Y)[6];
            for (var i = 0; i < 6; i++)
            {
                var angle = Math.PI / 3.0 * i;
                points[i] = (radius * Math.Cos(angle), radius * Math.Sin(angle));
            }

            var prism = CsgEvaluator.Evaluate(CsgHelpers.PrismXY(points, zCenter: 0, thickness));
            AssertGoldenJson(prism, ViewDirections.OrthographicThree);
        }

        [Test]
        public void HiddenBoxes_ThreeViews()
        {
            // TestKit 遮挡夹具：两同尺寸 10³ 立方体，前块沿 +Z 平移 20 再沿 +X 平移 3（部分遮挡）——
            // 左视是 B2 探针实测的隐藏段件（H=2）；主视/俯视无遮挡（H=0）。
            var boxes = OcclusionFixtures.BoxesShifted();
            AssertGoldenJson(boxes, ViewDirections.OrthographicThree);
        }

        [Test]
        public void HollowBox_ThreeViews()
        {
            // 20³ 居中立方体减去 10³ 居中空腔（完整内腔）：三个视图都含「真空腔」的隐藏轮廓。
            var hollow = Solids.Cube(20, center: true).Subtract(Solids.Cube(10, center: true));
            AssertGoldenJson(hollow, ViewDirections.OrthographicThree);
        }

        [Test]
        public void TabbedPlate_ThreeViews()
        {
            // 与 D2 STL 基线同参数的凸耳板（宽 400 / 孔心高 300 / 圆头 R100 / Ø80 孔 / 厚 20 /
            // 立边 zLeft=50；斜边由相切二次方程反解 ≈58.63°）——在 JSON 侧再钉一份 HLR 指纹。
            const double xCenter = 0, thickness = 20, zLeft = 50, width = 400;
            const double yBottom = 0, yCenter = 300, headRadius = 100, holeRadius = 40;

            var plate = CsgEvaluator.Evaluate(CsgHelpers.TabbedPlate(
                xCenter, thickness, zLeft, width, yBottom, yCenter, headRadius, holeRadius));
            AssertGoldenJson(plate, ViewDirections.OrthographicThree);
        }

        // ─────────────── 序列化器自测（不落盘、不参与 promote） ───────────────

        [Test]
        public void Serializer_DoubleSerialization_IsByteIdentical()
        {
            var solid = Solids.Cube(new Vector3D(10, 20, 30), center: true);
            var views = ViewBridge.ProjectViews(solid, ViewDirections.OrthographicThree);

            var first = Encoding.UTF8.GetBytes(ViewJson.Serialize(views));
            var second = Encoding.UTF8.GetBytes(ViewJson.Serialize(views));
            Assert.That(second, Is.EqualTo(first), "同一输入两次序列化必须逐字节一致");

            // 更强口径：同一几何重新投影后再序列化也必须逐字节一致
            // （跨进程稳定由 golden 基线比对在每次运行时持续证明）。
            var reprojected = Encoding.UTF8.GetBytes(
                ViewJson.Serialize(ViewBridge.ProjectViews(solid, ViewDirections.OrthographicThree)));
            Assert.That(reprojected, Is.EqualTo(first), "重新投影后再序列化必须逐字节一致");

            var json = ViewJson.Serialize(views);
            Assert.That(json.Contains('\r'), Is.False, "行尾必须是显式 \\n（不得输出 CR）");
            Assert.That(json.EndsWith("\n", StringComparison.Ordinal), Is.True, "输出应以换行收尾");
        }

        [Test]
        public void Serializer_FieldsComplete_AndNumbersRoundTrip()
        {
            // 用真实管线输出核对字段齐全与数字保真：通孔板主/俯视（含隐藏段；若含圆弧一并校验）。
            var views = ViewBridge.ProjectViews(
                OcclusionFixtures.DrilledPlate(), new[] { ViewDirections.Front, ViewDirections.Top });
            var json = ViewJson.Serialize(views);

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            Assert.That(root.ValueKind, Is.EqualTo(JsonValueKind.Array), "顶层必须是 JSON 数组");
            Assert.That(root.GetArrayLength(), Is.EqualTo(views.Count));

            for (var v = 0; v < views.Count; v++)
            {
                var view = views[v];
                var node = root[v];
                Assert.That(PropertyNames(node), Is.EqualTo(new[] { "view", "bounds", "segments", "arcs" }),
                    $"视图 {v} 的字段名与顺序");
                Assert.That(node.GetProperty("view").GetString(), Is.EqualTo(view.Direction.Name));

                var bounds = node.GetProperty("bounds");
                Assert.That(bounds.GetArrayLength(), Is.EqualTo(4), "bounds 必须是 [minX,minY,maxX,maxY]");
                AssertRoundTrip(bounds[0], view.Bounds.MinX);
                AssertRoundTrip(bounds[1], view.Bounds.MinY);
                AssertRoundTrip(bounds[2], view.Bounds.MaxX);
                AssertRoundTrip(bounds[3], view.Bounds.MaxY);

                var segments = node.GetProperty("segments");
                Assert.That(segments.GetArrayLength(), Is.EqualTo(view.Segments.Count));
                for (var i = 0; i < view.Segments.Count; i++)
                {
                    var segment = segments[i];
                    Assert.That(PropertyNames(segment), Is.EqualTo(new[] { "a", "b", "visible" }),
                        $"视图 {v} 线段 {i} 的字段名与顺序");
                    var a = segment.GetProperty("a");
                    var b = segment.GetProperty("b");
                    Assert.That(a.GetArrayLength(), Is.EqualTo(2));
                    Assert.That(b.GetArrayLength(), Is.EqualTo(2));
                    AssertRoundTrip(a[0], view.Segments[i].Start.X);
                    AssertRoundTrip(a[1], view.Segments[i].Start.Y);
                    AssertRoundTrip(b[0], view.Segments[i].End.X);
                    AssertRoundTrip(b[1], view.Segments[i].End.Y);
                    Assert.That(segment.GetProperty("visible").GetBoolean(), Is.EqualTo(view.Segments[i].IsVisible));
                }

                var arcs = node.GetProperty("arcs");
                Assert.That(arcs.GetArrayLength(), Is.EqualTo(view.Arcs.Count));
                for (var i = 0; i < view.Arcs.Count; i++)
                {
                    var arc = arcs[i];
                    Assert.That(PropertyNames(arc),
                        Is.EqualTo(new[] { "cx", "cy", "r", "start", "sweep", "visible", "closed" }),
                        $"视图 {v} 圆弧 {i} 的字段名与顺序");
                    AssertRoundTrip(arc.GetProperty("cx"), view.Arcs[i].Center.X);
                    AssertRoundTrip(arc.GetProperty("cy"), view.Arcs[i].Center.Y);
                    AssertRoundTrip(arc.GetProperty("r"), view.Arcs[i].Radius);
                    AssertRoundTrip(arc.GetProperty("start"), view.Arcs[i].StartAngle);
                    Assert.That(arc.GetProperty("visible").GetBoolean(), Is.EqualTo(view.Arcs[i].IsVisible));
                    Assert.That(arc.GetProperty("closed").GetBoolean(), Is.EqualTo(view.Arcs[i].IsClosed));

                    // sweep 为归一化扫掠角（弧度）：开口弧 ∈ (0, 2π)，整圆 = 2π；文本同样须往返保真。
                    var sweepText = arc.GetProperty("sweep").GetRawText();
                    var sweep = double.Parse(sweepText, NumberStyles.Float, CultureInfo.InvariantCulture);
                    Assert.That(sweepText, Is.EqualTo(sweep.ToString("R", CultureInfo.InvariantCulture)),
                        "数字文本必须就是 R 往返格式");
                    Assert.That(sweep, Is.GreaterThan(0.0).And.LessThanOrEqualTo(2 * Math.PI + 1e-12));
                }
            }
        }

        [Test]
        public void Serializer_SyntheticArcs_AreComplete()
        {
            // 合成单视图（1 段折线 + 2 条圆弧）补齐真实夹具可能不含圆弧分支时的覆盖：
            // 七字段齐全；开口弧 sweep = 归一化扫掠角（弧度）、整圆 sweep 恒 2π；visible/closed 保真。
            var arcs = new[]
            {
                new ViewArc(new ViewPoint2(1.5, -2.5), 3.75, 0.5, 0.75, isVisible: true, isClosed: false),
                new ViewArc(new ViewPoint2(-3.0, 1.0), 1.25, -0.5, -0.5 + 2 * Math.PI, isVisible: false, isClosed: true),
            };
            var view = new ViewProjection(
                ViewDirections.Front,
                new[] { new ViewSegment(new ViewPoint2(-1.5, 2.25), new ViewPoint2(3.25, -4.75), isVisible: true) },
                arcs);

            var json = ViewJson.Serialize(new[] { view });
            using var document = JsonDocument.Parse(json);
            var arcNodes = document.RootElement[0].GetProperty("arcs");
            Assert.That(arcNodes.GetArrayLength(), Is.EqualTo(2));

            var open = arcNodes[0];
            Assert.That(PropertyNames(open), Is.EqualTo(new[] { "cx", "cy", "r", "start", "sweep", "visible", "closed" }));
            Assert.That(open.GetProperty("visible").GetBoolean(), Is.True);
            Assert.That(open.GetProperty("closed").GetBoolean(), Is.False);
            Assert.That(double.Parse(open.GetProperty("sweep").GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture),
                Is.EqualTo(0.25), "开口弧 sweep = End−Start（此夹具精确可表示）");

            var closed = arcNodes[1];
            Assert.That(closed.GetProperty("visible").GetBoolean(), Is.False);
            Assert.That(closed.GetProperty("closed").GetBoolean(), Is.True);
            Assert.That(double.Parse(closed.GetProperty("sweep").GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture),
                Is.EqualTo(2 * Math.PI), "整圆 sweep 恒为 2π（与首末角差无关）");
        }

        [Test]
        public void Serializer_NonFiniteNumbers_Throw()
        {
            // NaN/∞ 无法表示为合法 JSON 数字：必须显式抛 InvalidOperationException（而不是静默写出
            // 非法 JSON、让 JsonDocument.Parse 在 golden 流程里才炸）。
            var nanView = new ViewProjection(
                ViewDirections.Front,
                new[] { new ViewSegment(new ViewPoint2(double.NaN, 0), new ViewPoint2(0, 1), true) });
            Assert.Throws<InvalidOperationException>(() => ViewJson.Serialize(new[] { nanView }));

            var infView = new ViewProjection(
                ViewDirections.Front,
                new[] { new ViewSegment(new ViewPoint2(0, 0), new ViewPoint2(1, 1), true) },
                new[] { new ViewArc(new ViewPoint2(0, 0), double.PositiveInfinity, 0, 1, true, false) });
            Assert.Throws<InvalidOperationException>(() => ViewJson.Serialize(new[] { infView }));
        }

        // ─────────────── golden 私有辅助 ───────────────

        /// <summary>
        /// 投影 + basics 断言 + byte-exact golden 比对（约定见类注释）。
        /// 进入即删除陈旧 pending 残留（与 legacy SolidTest / ModuleBaselineGoldenTest 同行为，
        /// 避免中断运行留下的 <c>*_.json</c> 在基线已恢复后让 GoldenGuard 永久转红）。
        /// </summary>
        static void AssertGoldenJson(Solid solid, IReadOnlyList<ViewDirection> directions)
        {
            var views = ViewBridge.ProjectViews(solid, directions);
            Assert.That(views.Count, Is.EqualTo(directions.Count), "视图数应与给定方向数一致");
            var totalSegments = 0;
            for (var i = 0; i < views.Count; i++)
            {
                totalSegments += views[i].Segments.Count;
                Assert.That(views[i].Direction.Name, Is.EqualTo(directions[i].Name),
                    $"第 {i} 个视图应对应方向 {directions[i].Name}");
                // 非空 = 线段 + 圆弧（轴向视图可能整圈被弧拟合吸收，见类注释与 Cylinder_ThreeViews）。
                Assert.That(views[i].Segments.Count + views[i].Arcs.Count, Is.GreaterThan(0),
                    $"{directions[i].Name} 视图为空（既无线段也无圆弧）");
            }
            Assert.That(totalSegments, Is.GreaterThan(0), "夹具合计应有折线段（防止 golden 退化为纯 ARC）");

            var json = ViewJson.Serialize(views);
            using (var document = JsonDocument.Parse(json))
            {
                Assert.That(document.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array),
                    "golden 顶层必须是 JSON 数组");
            }

            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
            var testName = TestContext.CurrentContext.Test.Name;
            var results = GoldenPaths.ResultsDirectory(Assembly.GetExecutingAssembly().Location);
            Assert.That(results, Is.Not.Null.And.Not.Empty, "GoldenPaths 未能定位 Results 目录");
            var viewsDir = Path.Combine(results!, "Views");
            Directory.CreateDirectory(viewsDir);

            var acceptedPath = Path.Combine(viewsDir, $"{Fixture}.{testName}.json");
            var pendingPath = Path.Combine(viewsDir, $"{Fixture}.{testName}_.json");

            if (File.Exists(pendingPath))
                File.Delete(pendingPath);

            if (!File.Exists(acceptedPath))
            {
                File.WriteAllBytes(pendingPath, bytes);
                Assert.Inconclusive(
                    $"golden 基线缺失：已写 Views/{Fixture}.{testName}_.json；核对后用 tools/promote-goldens.sh 升级。");
            }

            Assert.That(File.ReadAllBytes(acceptedPath), Is.EqualTo(bytes),
                $"golden 基线与当前输出不一致：{acceptedPath}");
        }

        /// <summary>JSON 对象的字段名序列（用于断言字段齐全与顺序）。</summary>
        static string[] PropertyNames(JsonElement element)
        {
            var names = new List<string>();
            foreach (var property in element.EnumerateObject())
                names.Add(property.Name);
            return names.ToArray();
        }

        /// <summary>数字往返：原始文本 <c>double.Parse</c>（Invariant）必须与源值**逐位相等**。</summary>
        static void AssertRoundTrip(JsonElement element, double expected)
        {
            var text = element.GetRawText();
            var parsed = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            Assert.That(parsed, Is.EqualTo(expected), $"数字 {text} 未能往返到原值（应为 R 格式保真）");
        }
    }
}
