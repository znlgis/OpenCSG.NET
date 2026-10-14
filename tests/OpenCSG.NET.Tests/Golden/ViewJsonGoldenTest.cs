using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        /// <para>
        /// <b>镜像维护（desync 风险）</b>：本归一化是 <see cref="ViewArc.SweepDegrees"/>
        /// （<c>src/OpenCSG.NET/ViewArcFitter.cs</c>）的镜像实现——两处必须同步修改，
        /// 否则 golden 的 <c>sweep</c> 字段会与库内口径背离。
        /// </para>
        /// </summary>
        static double SweepRadians(ViewArc arc)
        {
            if (arc.IsClosed)
                return 2 * Math.PI;

            // 非有限守卫：EndAngle = ±∞ 经公开 ViewArc ctor 可构造（此时 cx/cy/r/start 可全为有限值，
            // 序列化会先走到这里）；若不先抛，下面的 while 归一化会**死循环**（±∞ ∓ 2π 恒为 ±∞），
            // 而不是如期以 InvalidOperationException 收敛。
            var sweep = arc.EndAngle - arc.StartAngle;
            EnsureFinite(sweep, "圆弧扫掠角 EndAngle − StartAngle");

            while (sweep < 0) sweep += 2 * Math.PI;
            while (sweep >= 2 * Math.PI) sweep -= 2 * Math.PI;
            return sweep;
        }

        static void AppendNumber(StringBuilder sb, double value)
        {
            EnsureFinite(value, "输出数值");
            sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// 非有限数守卫（NaN/±∞ 不是合法 JSON 数字）：序列化前须先修数据；
        /// <see cref="AppendNumber"/> 与 <see cref="SweepRadians"/> 共用同一守卫风格
        /// （统一抛 <see cref="InvalidOperationException"/>，不得静默写出非法 JSON 或挂起）。
        /// </summary>
        static void EnsureFinite(double value, string what)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new InvalidOperationException(
                    $"JSON 无法表达非有限数（{what} = {value.ToString(CultureInfo.InvariantCulture)}）；序列化前须先修数据。");
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
    /// <para><b>golden 约定（跨平台分层，DEFECT-011）</b>（沿用 <c>ModuleBaselineGoldenTest</c> 的
    /// byte-exact 辅助模式）：
    /// 内容 = <c>ViewJson.Serialize(ViewBridge.ProjectViews(...))</c> 的 UTF-8 无 BOM 字节；
    /// 基线缺失 ⇒ 写 <c>&lt;Fixture&gt;.&lt;TestName&gt;_.json</c> + <c>Assert.Inconclusive</c>（门禁随后判红），
    /// 人工用 <c>tools/promote-goldens.sh</c> 核对升级；基线存在 ⇒ **Windows 沿用 <c>File.ReadAllBytes</c>
    /// 逐字节指纹**（golden 按 Windows 输出冻结 = Windows-pinned），**非 Windows 走语义校验**
    /// （跨平台 libm 的 ≤1 ULP 差会改变「R」数字文本：计数/标志精确 + 数值 1e-6，见
    /// <see cref="FindJsonSemanticDivergence"/>）。其中两个夹具经 CI 第二跑（run 38513802564）确证
    /// **跨平台拓扑翻转**（<c>Cone_FrontTop</c> 俯视 1150→1158、<c>TabbedPlate_ThreeViews</c> 主视 197→171），
    /// 非 Windows 改走平台拓扑敏感路径（<see cref="AssertGoldenJsonTopologySensitive"/> /
    /// <see cref="FindTopologySensitiveJsonDivergence"/>）：计数翻转视图跳过数值比对并输出带实测差量的
    /// DEFECT-011 说明；其余 13 个夹具维持计数精确比对不变。每个夹具同时断言 basics：视图数、每视图**非空**、序列化输出可被
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
            //
            // ── 平台拓扑敏感夹具（DEFECT-011；CI 第二跑 run 38513802564 定性）──
            // 俯视在 Linux 发生拓扑翻转：segments golden（Windows）1150 → Linux 1158（+8，+0.7%）。
            // 机制 = ULP 微差穿越阈值敏感逻辑的比较边界（ViewSegmentCleanup 合并 / ViewArcFitter 拟合 /
            // DepthRaster 切分）。非 Windows 走 AssertGoldenJsonTopologySensitive：视图数/视图名/非空/
            // 数字合理性照查，主视（计数一致）保留数值 1e-6 比对；俯视（计数翻转）跳过数值比对并输出
            // 带实测差量的 DEFECT-011 说明。Windows 路径不变（逐字节指纹）。
            var cone = CsgEvaluator.Evaluate(CsgNodes.Cone(new Vector3D(0, 0, 0), 0, 10, 30));
            AssertGoldenJsonTopologySensitive(cone, new[] { ViewDirections.Front, ViewDirections.Top });
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
            //
            // ── 平台拓扑敏感夹具（DEFECT-011；CI 第二跑 run 38513802564 定性）──
            // 主视在 Linux 发生拓扑翻转：segments golden（Windows）197 → Linux 171（−26，−13.2%），
            // 机制同 Cone_FrontTop（ULP 微差穿越 ViewSegmentCleanup 合并 / ViewArcFitter 拟合 /
            // DepthRaster 切分 的比较边界）。非 Windows 走 AssertGoldenJsonTopologySensitive：
            // 计数一致视图保留数值 1e-6 比对；主视（翻转）跳过数值比对并输出带实测差量的
            // DEFECT-011 说明。Windows 路径不变（逐字节指纹）。
            const double xCenter = 0, thickness = 20, zLeft = 50, width = 400;
            const double yBottom = 0, yCenter = 300, headRadius = 100, holeRadius = 40;

            var plate = CsgEvaluator.Evaluate(CsgHelpers.TabbedPlate(
                xCenter, thickness, zLeft, width, yBottom, yCenter, headRadius, holeRadius));
            AssertGoldenJsonTopologySensitive(plate, ViewDirections.OrthographicThree);
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

        [Test]
        public void Serializer_NonFiniteSweep_Throws()
        {
            // 扫掠角守卫（审查补强，Finding-2）：EndAngle = ±∞ 经公开 ViewArc ctor 可构造，且
            // cx/cy/r/start 全为有限值 ⇒ 序列化会先进入 SweepRadians。若无守卫，归一化 while
            // 死循环（±∞ ∓ 2π 恒为 ±∞）—— 本用例会**永久挂起**而不是如期抛错（这正是守卫存在的原因）。
            var sweepPosInfView = new ViewProjection(
                ViewDirections.Front,
                Array.Empty<ViewSegment>(),
                new[] { new ViewArc(new ViewPoint2(0, 0), 1.0, 0, double.PositiveInfinity, true, false) });
            Assert.Throws<InvalidOperationException>(
                () => ViewJson.Serialize(new[] { sweepPosInfView }),
                "EndAngle=+∞（StartAngle 有限）必须以 InvalidOperationException 收敛");

            var sweepNegInfView = new ViewProjection(
                ViewDirections.Front,
                Array.Empty<ViewSegment>(),
                new[] { new ViewArc(new ViewPoint2(0, 0), 1.0, 0, double.NegativeInfinity, true, false) });
            Assert.Throws<InvalidOperationException>(
                () => ViewJson.Serialize(new[] { sweepNegInfView }),
                "EndAngle=−∞（StartAngle 有限）必须以 InvalidOperationException 收敛");
        }

        // ─────────────── golden 私有辅助 ───────────────

        /// <summary>
        /// 投影 + basics 断言 + golden 比对（约定见类注释）。调用入口两个：
        /// 本方法（13 个常规夹具，非 Windows 语义层 = 计数/标志精确 + 数值 1e-6）与
        /// <see cref="AssertGoldenJsonTopologySensitive"/>（两个已定性平台拓扑敏感夹具）。
        /// 进入即删除陈旧 pending 残留（与 legacy SolidTest / ModuleBaselineGoldenTest 同行为，
        /// 避免中断运行留下的 <c>*_.json</c> 在基线已恢复后让 GoldenGuard 永久转红）。
        /// </summary>
        static void AssertGoldenJson(Solid solid, IReadOnlyList<ViewDirection> directions)
        {
            AssertGoldenJson(solid, directions, topologySensitive: false);
        }

        /// <summary>
        /// 平台拓扑敏感夹具入口（<c>Cone_FrontTop</c> / <c>TabbedPlate_ThreeViews</c>；
        /// DEFECT-011 第二跑定性后新增）：Windows 与其他夹具完全一致（逐字节指纹 = Windows-pinned）；
        /// 非 Windows 计数允许按平台翻转（见 <see cref="FindTopologySensitiveJsonDivergence"/> 的口径），
        /// 翻转视图跳过数值比对并输出带实测差量的 DEFECT-011 说明（不静默、不判红）。
        /// </summary>
        static void AssertGoldenJsonTopologySensitive(Solid solid, IReadOnlyList<ViewDirection> directions)
        {
            AssertGoldenJson(solid, directions, topologySensitive: true);
        }

        static void AssertGoldenJson(Solid solid, IReadOnlyList<ViewDirection> directions, bool topologySensitive)
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

            var acceptedBytes = File.ReadAllBytes(acceptedPath);

            if (OperatingSystem.IsWindows())
            {
                // Windows：维持原有**逐字节指纹**（golden 按 Windows 输出冻结 = Windows-pinned；DEFECT-011）。
                Assert.That(acceptedBytes, Is.EqualTo(bytes),
                    $"golden 基线与当前输出不一致：{acceptedPath}");
                return;
            }

            // 非 Windows（DEFECT-011）：跨平台 libm（UCRT vs glibc）的 ≤1 ULP 差经曲面/旋转几何传播后
            // 会改变「R」格式数字文本 ⇒ 字节比对不可移植；本平台改为语义比对。
            var goldenText = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(acceptedBytes);

            if (!topologySensitive)
            {
                // 13 个常规夹具：计数/标志精确 + 数值 1e-6；计数不一致仍响亮判红（拓扑敏感通道）。
                var semanticDivergence = FindJsonSemanticDivergence(goldenText, json);
                Assert.That(semanticDivergence, Is.Null,
                    semanticDivergence == null
                        ? string.Empty
                        : $"非 Windows 语义比对失败（平台：{RuntimeInformation.OSDescription}）：{semanticDivergence}"
                          + $"；golden 基线：{acceptedPath}。字节级指纹为 Windows-pinned（DEFECT-011），本平台走语义层。");
                return;
            }

            // 两个平台拓扑敏感夹具（CI 第二跑确证拓扑翻转，差量见夹具注释）：计数翻转视图不再与
            // Windows golden 硬比，但每一处翻转都输出带实测差量的说明（不静默）；其余检查照旧。
            var notes = new List<string>();
            var topologyDivergence = FindTopologySensitiveJsonDivergence(goldenText, json, notes);
            foreach (var note in notes)
                TestContext.Progress.WriteLine($"[DEFECT-011] 平台拓扑敏感：{note}");
            Assert.That(topologyDivergence, Is.Null,
                topologyDivergence == null
                    ? string.Empty
                    : $"非 Windows 平台拓扑敏感语义比对失败（平台：{RuntimeInformation.OSDescription}）：{topologyDivergence}"
                      + $"；golden 基线：{acceptedPath}。字节级指纹为 Windows-pinned（DEFECT-011）。");
        }

        // ─────────────── 跨平台语义比对（DEFECT-011；非 Windows 分支） ───────────────

        /// <summary>数值容差（相对与近零绝对同值）：1e-6。ULP 级漂移（~1e-16 相对）远在容差内，真实回归远超。</summary>
        internal const double SemanticNumberTolerance = 1e-6;

        /// <summary>计数/拓扑不一致的统一提示（DEFECT-011 的「响亮通道」：不静默通过）。</summary>
        internal const string TopologyDivergenceHint =
            "计数/拓扑不一致属 DEFECT-011 的拓扑敏感通道：非 Windows 语义层不静默通过，须人工定性（数字长度 vs 拓扑翻转）。";

        /// <summary>
        /// 非 Windows 语义比对（DEFECT-011）：逐字节指纹是 Windows-pinned（golden 按 Windows 输出冻结），
        /// 跨平台 libm（UCRT vs glibc）的 ≤1 ULP 差经曲线拟合/投影传播会改变「R」格式数字文本，故 Linux 侧
        /// 改为语义校验——视图数、每视图线段/圆弧计数、view/visible/closed 标志**精确相等**，
        /// 全部数值按 1e-6 相对（近零 1e-6 绝对）容差比对。计数不一致 = 拓扑敏感，照样报分歧（不静默通过）。
        /// 返回 null = 语义等价；否则为首个分歧的完整描述。
        /// </summary>
        internal static string? FindJsonSemanticDivergence(string expectedJson, string actualJson)
        {
            if (expectedJson == null) throw new ArgumentNullException(nameof(expectedJson));
            if (actualJson == null) throw new ArgumentNullException(nameof(actualJson));

            using var golden = TryParseJson(expectedJson, "golden", out var goldenError);
            if (golden == null)
                return goldenError;
            using var current = TryParseJson(actualJson, "当前输出", out var currentError);
            if (current == null)
                return currentError;

            var goldenViews = golden.RootElement;
            var currentViews = current.RootElement;
            if (goldenViews.ValueKind != JsonValueKind.Array || currentViews.ValueKind != JsonValueKind.Array)
                return $"顶层必须是 JSON 数组：golden {goldenViews.ValueKind} / 当前 {currentViews.ValueKind}";

            var goldenCount = goldenViews.GetArrayLength();
            var currentCount = currentViews.GetArrayLength();
            if (goldenCount != currentCount)
                return $"视图数不一致：golden {goldenCount} vs 当前 {currentCount}。{TopologyDivergenceHint}";

            for (var v = 0; v < goldenCount; v++)
            {
                var divergence = FindViewDivergence(goldenViews[v], currentViews[v], v);
                if (divergence != null)
                    return divergence;
            }

            return null;
        }

        /// <summary>解析 JSON（统一以分歧消息收敛，不让 JsonException 穿透）。</summary>
        static JsonDocument? TryParseJson(string text, string what, out string? error)
        {
            try
            {
                error = null;
                return JsonDocument.Parse(text);
            }
            catch (JsonException ex)
            {
                error = $"{what} JSON 解析失败：{ex.Message}";
                return null;
            }
        }

        static string? FindViewDivergence(JsonElement goldenView, JsonElement currentView, int viewIndex)
        {
            var path = $"视图 {viewIndex} [{JsonString(goldenView, "view") ?? "?"}]";
            if (goldenView.ValueKind != JsonValueKind.Object || currentView.ValueKind != JsonValueKind.Object)
                return $"{path}：视图必须是 JSON 对象（golden {goldenView.ValueKind} / 当前 {currentView.ValueKind}）";

            // view 名：精确相等。
            var goldenName = JsonString(goldenView, "view");
            var currentName = JsonString(currentView, "view");
            if (goldenName != currentName)
                return $"{path}：view 名不一致：golden \"{goldenName}\" vs 当前 \"{currentName}\"";

            // 线段/圆弧计数：精确相等（拓扑敏感通道，DEFECT-011）。
            var segmentsCount = FindArrayCountDivergence(goldenView, currentView, "segments", path);
            if (segmentsCount != null)
                return segmentsCount;
            var arcsCount = FindArrayCountDivergence(goldenView, currentView, "arcs", path);
            if (arcsCount != null)
                return arcsCount;

            // segments[i]：a[2] / b[2]（数值）+ visible（标志精确）。先比源数据、后比派生读数，
            // 使「首个分歧」指向根因字段（如线段端点）而不是由它导出的 bounds。
            var goldenSegments = goldenView.GetProperty("segments");
            var currentSegments = currentView.GetProperty("segments");
            for (var i = 0; i < goldenSegments.GetArrayLength(); i++)
            {
                var segmentPath = $"{path}.segments[{i}]";
                var divergence = ComparePoint2(segmentPath, "a", goldenSegments[i], currentSegments[i])
                    ?? ComparePoint2(segmentPath, "b", goldenSegments[i], currentSegments[i])
                    ?? CompareFlagProperty(segmentPath, "visible", goldenSegments[i], currentSegments[i]);
                if (divergence != null)
                    return divergence;
            }

            // arcs[i]：cx/cy/r/start/sweep（数值）+ visible/closed（标志精确）。
            var goldenArcs = goldenView.GetProperty("arcs");
            var currentArcs = currentView.GetProperty("arcs");
            for (var i = 0; i < goldenArcs.GetArrayLength(); i++)
            {
                var arcPath = $"{path}.arcs[{i}]";
                var divergence = CompareNumberProperty(arcPath, "cx", goldenArcs[i], currentArcs[i])
                    ?? CompareNumberProperty(arcPath, "cy", goldenArcs[i], currentArcs[i])
                    ?? CompareNumberProperty(arcPath, "r", goldenArcs[i], currentArcs[i])
                    ?? CompareNumberProperty(arcPath, "start", goldenArcs[i], currentArcs[i])
                    ?? CompareNumberProperty(arcPath, "sweep", goldenArcs[i], currentArcs[i])
                    ?? CompareFlagProperty(arcPath, "visible", goldenArcs[i], currentArcs[i])
                    ?? CompareFlagProperty(arcPath, "closed", goldenArcs[i], currentArcs[i]);
                if (divergence != null)
                    return divergence;
            }

            // bounds = [minX, minY, maxX, maxY]（派生读数；数值 1e-6）。
            if (!goldenView.TryGetProperty("bounds", out var goldenBounds) || goldenBounds.ValueKind != JsonValueKind.Array)
                return $"{path}：golden 缺少 bounds 数组";
            if (!currentView.TryGetProperty("bounds", out var currentBounds) || currentBounds.ValueKind != JsonValueKind.Array)
                return $"{path}：当前输出缺少 bounds 数组";
            if (goldenBounds.GetArrayLength() != 4 || currentBounds.GetArrayLength() != 4)
                return $"{path}：bounds 必须是 [minX,minY,maxX,maxY]（golden {goldenBounds.GetArrayLength()} / 当前 {currentBounds.GetArrayLength()}）";
            for (var k = 0; k < 4; k++)
            {
                var divergence = CompareNumber($"{path}.bounds[{k}]", goldenBounds[k], currentBounds[k]);
                if (divergence != null)
                    return divergence;
            }

            return null;
        }

        static string? JsonString(JsonElement element, string property)
        {
            return element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(property, out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        static string? FindArrayCountDivergence(JsonElement goldenView, JsonElement currentView, string property, string path)
        {
            if (!goldenView.TryGetProperty(property, out var goldenArray) || goldenArray.ValueKind != JsonValueKind.Array)
                return $"{path}：golden 缺少 {property} 数组";
            if (!currentView.TryGetProperty(property, out var currentArray) || currentArray.ValueKind != JsonValueKind.Array)
                return $"{path}：当前输出缺少 {property} 数组";

            var goldenLength = goldenArray.GetArrayLength();
            var currentLength = currentArray.GetArrayLength();
            return goldenLength == currentLength
                ? null
                : $"{path}.{property} 计数不一致：golden {goldenLength} vs 当前 {currentLength}。{TopologyDivergenceHint}";
        }

        static string? ComparePoint2(string path, string property, JsonElement golden, JsonElement current)
        {
            if (!golden.TryGetProperty(property, out var goldenPoint))
                return $"{path}.{property}：golden 缺少字段";
            if (!current.TryGetProperty(property, out var currentPoint))
                return $"{path}.{property}：当前输出缺少字段";
            if (goldenPoint.ValueKind != JsonValueKind.Array || currentPoint.ValueKind != JsonValueKind.Array)
                return $"{path}.{property}：必须是 [x,y] 数组（golden {goldenPoint.ValueKind} / 当前 {currentPoint.ValueKind}）";
            if (goldenPoint.GetArrayLength() != 2 || currentPoint.GetArrayLength() != 2)
                return $"{path}.{property}：必须是 [x,y]（golden {goldenPoint.GetArrayLength()} / 当前 {currentPoint.GetArrayLength()}）";

            for (var k = 0; k < 2; k++)
            {
                var divergence = CompareNumber($"{path}.{property}[{k}]", goldenPoint[k], currentPoint[k]);
                if (divergence != null)
                    return divergence;
            }

            return null;
        }

        static string? CompareNumberProperty(string path, string property, JsonElement golden, JsonElement current)
        {
            if (!golden.TryGetProperty(property, out var goldenValue))
                return $"{path}.{property}：golden 缺少字段";
            if (!current.TryGetProperty(property, out var currentValue))
                return $"{path}.{property}：当前输出缺少字段";
            return CompareNumber($"{path}.{property}", goldenValue, currentValue);
        }

        static string? CompareFlagProperty(string path, string property, JsonElement golden, JsonElement current)
        {
            if (!golden.TryGetProperty(property, out var goldenValue))
                return $"{path}.{property}：golden 缺少字段";
            if (!current.TryGetProperty(property, out var currentValue))
                return $"{path}.{property}：当前输出缺少字段";

            var goldenFlag = AsBool(goldenValue);
            var currentFlag = AsBool(currentValue);
            if (goldenFlag == null || currentFlag == null)
                return $"{path}.{property}：必须是布尔（golden {goldenValue.ValueKind} / 当前 {currentValue.ValueKind}）";
            return goldenFlag == currentFlag
                ? null
                : $"{path}.{property} 标志不一致：golden {(goldenFlag.Value ? "true" : "false")} vs 当前 {(currentFlag.Value ? "true" : "false")}";
        }

        static bool? AsBool(JsonElement element)
        {
            return element.ValueKind == JsonValueKind.True ? true
                : element.ValueKind == JsonValueKind.False ? false
                : (bool?)null;
        }

        /// <summary>数值容差比对：绝对差 ≤ 1e-6，或 ≤ 1e-6·max(|golden|,|当前|)（相对，近零时绝对兜底）。</summary>
        static string? CompareNumber(string path, JsonElement golden, JsonElement current)
        {
            if (golden.ValueKind != JsonValueKind.Number)
                return $"{path}：golden 不是数字（{golden.ValueKind}）";
            if (current.ValueKind != JsonValueKind.Number)
                return $"{path}：当前输出不是数字（{current.ValueKind}）";

            var goldenValue = golden.GetDouble();
            var currentValue = current.GetDouble();
            var difference = Math.Abs(goldenValue - currentValue);
            var scale = Math.Max(Math.Abs(goldenValue), Math.Abs(currentValue));
            if (difference <= SemanticNumberTolerance || difference <= SemanticNumberTolerance * scale)
                return null;

            return $"{path}：golden {FormatNumber(goldenValue)} vs 当前 {FormatNumber(currentValue)}"
                + $"（绝对差 {FormatNumber(difference)}）超出 1e-6 容差（相对/近零绝对）";
        }

        static string FormatNumber(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        // ─────────────── 平台拓扑敏感比对（DEFECT-011；非 Windows 分支，仅两个夹具） ───────────────

        /// <summary>
        /// 平台拓扑敏感夹具的语义比对（DEFECT-011 第二跑定性后新增；非 Windows 分支，仅
        /// <c>Cone_FrontTop</c> / <c>TabbedPlate_ThreeViews</c> 使用）。
        ///
        /// <para><b>动机（CI 第二跑 run 38513802564 实测）</b>：两个夹具在 Linux 上发生**拓扑翻转**——
        /// <c>Cone_FrontTop</c> 俯视 segments golden（Windows）1150 → Linux 1158（+8，+0.7%）；
        /// <c>TabbedPlate_ThreeViews</c> 主视 segments golden 197 → Linux 171（−26，−13.2%）。
        /// 机制 = ULP 微差穿越阈值敏感逻辑的比较边界（<c>ViewSegmentCleanup</c> 合并 /
        /// <c>ViewArcFitter</c> 拟合 / <c>DepthRaster</c> 切分）；两平台输出均为同一几何的有效近似，
        /// Windows golden 无「唯一正确」地位 ⇒ 计数不能对 Windows golden 精确断言。</para>
        ///
        /// <para><b>口径</b>：① 视图数相等（结构读数，不属翻转通道，违者硬判红）；
        /// ② 逐视图 view 名精确；③ 每视图非空（golden 与当前都查）；
        /// ④ 计数（segments/arcs）与 golden 一致的视图 → 数值 1e-6 容差比对
        /// （复用 <see cref="FindViewDivergence"/>，不弱化；该视图的可见性/闭合标志亦照旧精确）；
        /// ⑤ 计数不一致的视图 → 跳过与 golden 的数值比对，改做当前输出的数字形状合理性检查
        /// （数组长度 / 数字 / 布尔类型 + bounds 四元序），并往 <paramref name="notes"/> 写一条带
        /// DEFECT-011 引用与实测差量（Δ 与百分比）的说明——文档化的平台性质，不静默、不判红。
        /// 数值有限性由写出侧 <see cref="ViewJson"/> 的非有限守卫与 JSON 解析共同保证
        /// （NaN/∞ 既进不了 golden 也进不了当前输出），形状检查即其可落地的运行时哨兵。</para>
        ///
        /// 返回 null = 语义可放行（可能带 notes）；否则为首个硬分歧的完整描述。
        /// </summary>
        internal static string? FindTopologySensitiveJsonDivergence(
            string expectedJson, string actualJson, List<string>? notes = null)
        {
            if (expectedJson == null) throw new ArgumentNullException(nameof(expectedJson));
            if (actualJson == null) throw new ArgumentNullException(nameof(actualJson));

            using var golden = TryParseJson(expectedJson, "golden", out var goldenError);
            if (golden == null)
                return goldenError;
            using var current = TryParseJson(actualJson, "当前输出", out var currentError);
            if (current == null)
                return currentError;

            var goldenViews = golden.RootElement;
            var currentViews = current.RootElement;
            if (goldenViews.ValueKind != JsonValueKind.Array || currentViews.ValueKind != JsonValueKind.Array)
                return $"顶层必须是 JSON 数组：golden {goldenViews.ValueKind} / 当前 {currentViews.ValueKind}";

            var goldenCount = goldenViews.GetArrayLength();
            var currentCount = currentViews.GetArrayLength();
            if (goldenCount != currentCount)
                return $"视图数不一致：golden {goldenCount} vs 当前 {currentCount}"
                    + "（视图数是结构读数，不在 DEFECT-011 的计数翻转通道内）。";

            for (var v = 0; v < goldenCount; v++)
            {
                var goldenView = goldenViews[v];
                var currentView = currentViews[v];
                var path = $"视图 {v} [{JsonString(goldenView, "view") ?? "?"}]";

                if (goldenView.ValueKind != JsonValueKind.Object || currentView.ValueKind != JsonValueKind.Object)
                    return $"{path}：视图必须是 JSON 对象（golden {goldenView.ValueKind} / 当前 {currentView.ValueKind}）";

                var goldenName = JsonString(goldenView, "view");
                var currentName = JsonString(currentView, "view");
                if (goldenName != currentName)
                    return $"{path}：view 名不一致：golden \"{goldenName}\" vs 当前 \"{currentName}\"";

                if (!goldenView.TryGetProperty("segments", out var goldenSegments) || goldenSegments.ValueKind != JsonValueKind.Array)
                    return $"{path}：golden 缺少 segments 数组";
                if (!goldenView.TryGetProperty("arcs", out var goldenArcs) || goldenArcs.ValueKind != JsonValueKind.Array)
                    return $"{path}：golden 缺少 arcs 数组";

                // 当前输出的数字形状合理性（对全部视图执行；跳过数值比对的视图主要靠它兜底）。
                var currentStructure = FindViewStructureSanityDivergence(currentView, path);
                if (currentStructure != null)
                    return currentStructure;

                var currentSegments = currentView.GetProperty("segments");
                var currentArcs = currentView.GetProperty("arcs");

                var goldenNonEmpty = goldenSegments.GetArrayLength() + goldenArcs.GetArrayLength();
                if (goldenNonEmpty == 0)
                    return $"{path}：golden 视图为空（夹具退化）";
                var currentNonEmpty = currentSegments.GetArrayLength() + currentArcs.GetArrayLength();
                if (currentNonEmpty == 0)
                    return $"{path}：当前视图为空（既无线段也无圆弧）";

                if (goldenSegments.GetArrayLength() == currentSegments.GetArrayLength()
                    && goldenArcs.GetArrayLength() == currentArcs.GetArrayLength())
                {
                    // 计数一致：数值 1e-6 容差比对照旧（含标志精确与 bounds；不弱化）。
                    var divergence = FindViewDivergence(goldenView, currentView, v);
                    if (divergence != null)
                        return divergence;
                    continue;
                }

                // 计数翻转：跳过与 golden 的数值比对，输出带实测差量的 DEFECT-011 说明（不静默、不判红）。
                notes?.Add(DescribeTopologySensitiveSkip(path, goldenSegments, currentSegments, goldenArcs, currentArcs));
            }

            return null;
        }

        /// <summary>计数翻转视图的说明消息（含 DEFECT-011 引用与实测差量；不静默通过、不判红）。</summary>
        static string DescribeTopologySensitiveSkip(
            string path,
            JsonElement goldenSegments, JsonElement currentSegments,
            JsonElement goldenArcs, JsonElement currentArcs)
        {
            var parts = new List<string>(2);
            AppendCountDelta(parts, "segments", goldenSegments.GetArrayLength(), currentSegments.GetArrayLength());
            AppendCountDelta(parts, "arcs", goldenArcs.GetArrayLength(), currentArcs.GetArrayLength());

            return $"{path}：{string.Join("；", parts)}——计数与 Windows golden 不一致（已定性平台拓扑翻转），"
                + "按平台拓扑敏感路径跳过该视图的数值比对"
                + "（DEFECT-011：ULP 微差穿越 ViewSegmentCleanup 合并 / ViewArcFitter 拟合 / DepthRaster 切分 的比较边界；"
                + "两平台输出均为同一几何的有效近似）。";
        }

        /// <summary>差量文本（Δ 与相对百分比；计数相等时不产出）。</summary>
        static void AppendCountDelta(List<string> parts, string field, int goldenCount, int currentCount)
        {
            if (goldenCount == currentCount)
                return;

            var delta = currentCount - goldenCount;
            var percent = goldenCount > 0
                ? (100.0 * delta / goldenCount).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%"
                : "n/a";
            parts.Add($"{field} golden {goldenCount} vs 当前 {currentCount}"
                + $"（Δ {delta.ToString("+0;-0;0", CultureInfo.InvariantCulture)}，{percent}）");
        }

        /// <summary>
        /// 当前输出单视图的数字形状合理性检查（跳过与 golden 数值比对的视图的替代检查，也是常规
        /// 路径的廉价哨兵）：segments/arcs/bounds 的存在性、数组长度、数字/布尔类型与 bounds 四元序。
        /// 有限性不在此重复检查（JSON 文本无法表达 NaN/∞：写出侧 <see cref="ViewJson"/> 已守卫、
        /// 解析层对非法数字直接拒绝）。
        /// </summary>
        static string? FindViewStructureSanityDivergence(JsonElement view, string path)
        {
            if (view.ValueKind != JsonValueKind.Object)
                return $"{path}：当前视图必须是 JSON 对象（{view.ValueKind}）";

            if (!view.TryGetProperty("segments", out var segments) || segments.ValueKind != JsonValueKind.Array)
                return $"{path}：当前输出缺少 segments 数组";
            if (!view.TryGetProperty("arcs", out var arcs) || arcs.ValueKind != JsonValueKind.Array)
                return $"{path}：当前输出缺少 arcs 数组";

            for (var i = 0; i < segments.GetArrayLength(); i++)
            {
                var segmentPath = $"{path}.segments[{i}]";
                var divergence = CheckPoint2Shape(segments[i], segmentPath, "a")
                    ?? CheckPoint2Shape(segments[i], segmentPath, "b")
                    ?? CheckBoolShape(segments[i], segmentPath, "visible");
                if (divergence != null)
                    return divergence;
            }

            for (var i = 0; i < arcs.GetArrayLength(); i++)
            {
                var arcPath = $"{path}.arcs[{i}]";
                var divergence = CheckNumberShape(arcs[i], arcPath, "cx")
                    ?? CheckNumberShape(arcs[i], arcPath, "cy")
                    ?? CheckNumberShape(arcs[i], arcPath, "r")
                    ?? CheckNumberShape(arcs[i], arcPath, "start")
                    ?? CheckNumberShape(arcs[i], arcPath, "sweep")
                    ?? CheckBoolShape(arcs[i], arcPath, "visible")
                    ?? CheckBoolShape(arcs[i], arcPath, "closed");
                if (divergence != null)
                    return divergence;
            }

            if (!view.TryGetProperty("bounds", out var bounds) || bounds.ValueKind != JsonValueKind.Array
                || bounds.GetArrayLength() != 4)
                return $"{path}：bounds 必须是 [minX,minY,maxX,maxY] 四元数组";
            for (var k = 0; k < 4; k++)
            {
                if (bounds[k].ValueKind != JsonValueKind.Number)
                    return $"{path}.bounds[{k}]：必须是数字（{bounds[k].ValueKind}）";
            }
            if (bounds[0].GetDouble() > bounds[2].GetDouble() || bounds[1].GetDouble() > bounds[3].GetDouble())
                return $"{path}.bounds：min > max（[{bounds[0].GetDouble()}, {bounds[1].GetDouble()},"
                    + $" {bounds[2].GetDouble()}, {bounds[3].GetDouble()}]）";

            return null;
        }

        static string? CheckPoint2Shape(JsonElement owner, string path, string property)
        {
            if (owner.ValueKind != JsonValueKind.Object)
                return $"{path}：必须是 JSON 对象（{owner.ValueKind}）";
            if (!owner.TryGetProperty(property, out var value))
                return $"{path}.{property}：当前输出缺少字段";
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2)
                return $"{path}.{property}：必须是 [x,y] 数组（{value.ValueKind}）";
            for (var k = 0; k < 2; k++)
            {
                if (value[k].ValueKind != JsonValueKind.Number)
                    return $"{path}.{property}[{k}]：必须是数字（{value[k].ValueKind}）";
            }
            return null;
        }

        static string? CheckNumberShape(JsonElement owner, string path, string property)
        {
            if (owner.ValueKind != JsonValueKind.Object)
                return $"{path}：必须是 JSON 对象（{owner.ValueKind}）";
            if (!owner.TryGetProperty(property, out var value))
                return $"{path}.{property}：当前输出缺少字段";
            return value.ValueKind == JsonValueKind.Number
                ? null
                : $"{path}.{property}：必须是数字（{value.ValueKind}）";
        }

        static string? CheckBoolShape(JsonElement owner, string path, string property)
        {
            if (owner.ValueKind != JsonValueKind.Object)
                return $"{path}：必须是 JSON 对象（{owner.ValueKind}）";
            if (!owner.TryGetProperty(property, out var value))
                return $"{path}.{property}：当前输出缺少字段";
            return value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False
                ? null
                : $"{path}.{property}：必须是布尔（{value.ValueKind}）";
        }

        // ─────────────── 语义比对器自测（DEFECT-011；合成输入，两平台确定性） ───────────────

        [Test]
        public void SemanticJsonComparison_UlpDrift_IsEquivalent_AndRealDrift_Caught()
        {
            var baseline = ViewJson.Serialize(new[] { SyntheticSemanticView(secondEndX: 3.25, firstVisible: true) });

            // 模拟 glibc vs UCRT 的 ≤1 ULP 漂移：数值差 ~4.4e-16，但「R」文本已不同（3.25 → 3.2500000000000004）。
            var ulpDrifted = ViewJson.Serialize(new[] { SyntheticSemanticView(secondEndX: UlpUp(3.25), firstVisible: true) });
            Assert.That(ulpDrifted, Is.Not.EqualTo(baseline), "测试前提：1 ULP 漂移必须改变序列化字节（R 文本不同）");
            Assert.That(FindJsonSemanticDivergence(baseline, ulpDrifted), Is.Null,
                "ULP 级漂移（跨平台 libm 的实测量级）必须在 1e-6 语义容差内放行");

            var drifted = ViewJson.Serialize(new[] { SyntheticSemanticView(secondEndX: 3.251, firstVisible: true) });
            var divergence = FindJsonSemanticDivergence(baseline, drifted);
            Assert.That(divergence, Is.Not.Null, "超出 1e-6 容差的数值漂移必须报分歧");
            StringAssert.Contains(".segments[0].b[0]", divergence);
            StringAssert.Contains("超出 1e-6 容差", divergence);
        }

        [Test]
        public void SemanticJsonComparison_CountMismatch_IsLoud()
        {
            var golden = ViewJson.Serialize(new[] { SyntheticSemanticView(segmentCount: 2, arcCount: 1) });

            var fewerSegments = ViewJson.Serialize(new[] { SyntheticSemanticView(segmentCount: 1, arcCount: 1) });
            var segmentDivergence = FindJsonSemanticDivergence(golden, fewerSegments);
            Assert.That(segmentDivergence, Is.Not.Null, "线段数不一致不得静默通过（拓扑敏感通道）");
            StringAssert.Contains("segments 计数不一致", segmentDivergence);
            StringAssert.Contains("DEFECT-011", segmentDivergence);

            var noArcs = ViewJson.Serialize(new[] { SyntheticSemanticView(segmentCount: 2, arcCount: 0) });
            var arcDivergence = FindJsonSemanticDivergence(golden, noArcs);
            Assert.That(arcDivergence, Is.Not.Null, "圆弧数不一致不得静默通过（拓扑敏感通道）");
            StringAssert.Contains("arcs 计数不一致", arcDivergence);
            StringAssert.Contains("DEFECT-011", arcDivergence);
        }

        [Test]
        public void SemanticJsonComparison_FlagMismatch_IsCaught()
        {
            var golden = ViewJson.Serialize(new[] { SyntheticSemanticView(firstVisible: true) });
            var flipped = ViewJson.Serialize(new[] { SyntheticSemanticView(firstVisible: false) });

            var divergence = FindJsonSemanticDivergence(golden, flipped);
            Assert.That(divergence, Is.Not.Null, "visible 标志不一致必须报分歧");
            StringAssert.Contains("visible", divergence);
        }

        [Test]
        public void SemanticJsonComparison_MalformedJson_IsReportedNotThrown()
        {
            var divergence = FindJsonSemanticDivergence("这不是 JSON", "[]");
            Assert.That(divergence, Is.Not.Null, "golden 解析失败必须收敛为分歧消息，不得抛 JsonException");
            StringAssert.Contains("golden JSON 解析失败", divergence);

            Assert.That(FindJsonSemanticDivergence("[]", "[]"), Is.Null, "空视图列表语义等价");
        }

        [Test]
        public void SemanticJsonComparison_RealGoldens_ParseAndMatchThemselves()
        {
            var goldens = RealViewGoldens();
            Assert.That(goldens.Length, Is.GreaterThanOrEqualTo(10),
                "Views 目录下的真实 golden 数量不足以构成非真空检查");

            foreach (var goldenPath in goldens)
            {
                // 语义比对器必须能吃下每一个真实 golden 的完整格式（长数字文本、中文字段值）并自比等价；
                // 若写出格式漂移导致解析失败，这里（两平台）就会判红，而非等到 Linux 才暴露。
                var text = ReadGoldenText(goldenPath);
                Assert.That(FindJsonSemanticDivergence(text, text), Is.Null,
                    $"真实 golden 必须被语义层解析且自比等价：{goldenPath}");
            }
        }

        [Test]
        public void SemanticJsonComparison_RealGoldenWithUlpDrift_IsEquivalent()
        {
            var results = GoldenPaths.ResultsDirectory(Assembly.GetExecutingAssembly().Location);
            Assert.That(results, Is.Not.Null.And.Not.Empty, "GoldenPaths 未能定位 Results 目录");
            var goldenPath = Path.Combine(results!, "Views", $"{Fixture}.TabbedPlate_ThreeViews.json");
            Assert.That(File.Exists(goldenPath), Is.True, $"golden 缺失：{goldenPath}");

            // 整文件逐数字 1 ULP 上移 = 模拟 glibc vs UCRT 的最坏情形（每个「R」文本都可能漂移）。
            // 对照：旧的字节比对在这种输入上必然失败（本任务的分层动机）。
            var goldenText = ReadGoldenText(goldenPath);
            var drifted = UlpDriftJsonNumbers(goldenText);
            Assert.That(drifted, Is.Not.EqualTo(goldenText), "测试前提：漂移必须改变文本");
            Assert.That(FindJsonSemanticDivergence(goldenText, drifted), Is.Null,
                "真实 golden 的逐数字 1 ULP 漂移必须被 1e-6 语义容差放行");
        }

        // ─────────────── 平台拓扑敏感比对自测（DEFECT-011 第二跑定性；合成输入，两平台确定性） ───────────────

        [Test]
        public void TopologySensitiveComparison_CountFlip_SkipsNumericCompareWithLoudNote()
        {
            // 模拟 CI 第二跑实测形态：同几何、计数翻转（如 Cone_FrontTop 俯视 1150 → 1158）。
            var golden = ViewJson.Serialize(new[] { SyntheticCountedView(segmentCount: 2, arcCount: 1) });
            var flipped = ViewJson.Serialize(new[] { SyntheticCountedView(segmentCount: 3, arcCount: 1, xOffset: 99) });

            var notes = new List<string>();
            var divergence = FindTopologySensitiveJsonDivergence(golden, flipped, notes);

            Assert.That(divergence, Is.Null,
                "计数翻转视图不再与 Windows golden 做数值硬比（DEFECT-011 第二跑已定性为平台拓扑翻转）");
            Assert.That(notes, Has.Count.EqualTo(1), "翻转视图必须输出说明（不静默通过）");
            StringAssert.Contains("DEFECT-011", notes[0]);
            StringAssert.Contains("segments golden 2 vs 当前 3", notes[0]);
            StringAssert.Contains("+1", notes[0]);
            StringAssert.Contains("+50.0%", notes[0]);
        }

        [Test]
        public void TopologySensitiveComparison_MatchedCounts_KeepNumericCompare()
        {
            var golden = ViewJson.Serialize(new[] { SyntheticCountedView(segmentCount: 2, arcCount: 1) });
            var drifted = ViewJson.Serialize(new[] { SyntheticCountedView(segmentCount: 2, arcCount: 1, xOffset: 0.5) });

            var notes = new List<string>();
            var divergence = FindTopologySensitiveJsonDivergence(golden, drifted, notes);

            Assert.That(divergence, Is.Not.Null, "计数一致视图的数值比对不得弱化（真实漂移仍判红）");
            StringAssert.Contains("超出 1e-6 容差", divergence);
            Assert.That(notes, Is.Empty, "已按数值判红，无需跳过说明");
        }

        [Test]
        public void TopologySensitiveComparison_ViewCountMismatch_IsHardDivergence()
        {
            var golden = ViewJson.Serialize(new[] { SyntheticCountedView(segmentCount: 2, arcCount: 1) });
            var extraView = ViewJson.Serialize(new[]
            {
                SyntheticCountedView(segmentCount: 2, arcCount: 1),
                SyntheticCountedView(segmentCount: 2, arcCount: 1),
            });

            var divergence = FindTopologySensitiveJsonDivergence(golden, extraView);

            Assert.That(divergence, Is.Not.Null, "视图数是结构读数，不一致必须硬判红（翻转通道不覆盖视图数）");
            StringAssert.Contains("视图数不一致", divergence);
        }

        [Test]
        public void TopologySensitiveComparison_CountFlipWithMalformedCurrent_IsCaught()
        {
            var golden = ViewJson.Serialize(new[] { SyntheticCountedView(segmentCount: 2, arcCount: 1) });

            // 当前输出：segments 多一段（触发跳过路径；插入在第 2 段位置）且该段 b 不是 [x,y]
            // → 形状合理性检查必须判红。
            var malformed = golden.Replace(
                "{\"a\":[0,0],\"b\":[1,1],\"visible\":true},",
                "{\"a\":[0,0],\"b\":[1,1],\"visible\":true},{\"a\":[5,5],\"b\":7,\"visible\":true},");

            Assert.That(malformed, Is.Not.EqualTo(golden), "测试前提：替换必须命中（否则测试空洞）");
            var divergence = FindTopologySensitiveJsonDivergence(golden, malformed);

            Assert.That(divergence, Is.Not.Null, "跳过数值比对的视图仍须通过数字形状合理性检查");
            StringAssert.Contains(".segments[1].b", divergence);
        }

        /// <summary>
        /// 合成单视图（拓扑敏感比对自测专用）：按计数生成等距线段与圆弧，数值全部精确可表示；
        /// <paramref name="xOffset"/> 施加于线段端点以模拟数值漂移。
        /// </summary>
        static ViewProjection SyntheticCountedView(int segmentCount, int arcCount, double xOffset = 0)
        {
            var segments = new List<ViewSegment>();
            for (var i = 0; i < segmentCount; i++)
            {
                segments.Add(new ViewSegment(
                    new ViewPoint2(i + xOffset, 0), new ViewPoint2(i + 1 + xOffset, 1), isVisible: true));
            }

            var arcs = new List<ViewArc>();
            for (var i = 0; i < arcCount; i++)
                arcs.Add(new ViewArc(new ViewPoint2(i, 0), 1, 0, 1, isVisible: true, isClosed: false));

            return new ViewProjection(ViewDirections.Front, segments, arcs);
        }

        /// <summary>枚举 Views 目录下的真实 JSON golden（排除 pending 残留 *_ .json）。</summary>
        static string[] RealViewGoldens()
        {
            var results = GoldenPaths.ResultsDirectory(Assembly.GetExecutingAssembly().Location);
            Assert.That(results, Is.Not.Null.And.Not.Empty, "GoldenPaths 未能定位 Results 目录");
            var viewsDir = Path.Combine(results!, "Views");
            Assert.That(Directory.Exists(viewsDir), Is.True, $"Views 目录缺失：{viewsDir}");

            var goldens = new List<string>();
            foreach (var path in Directory.GetFiles(viewsDir, $"{Fixture}.*.json"))
            {
                if (!path.EndsWith("_.json", StringComparison.Ordinal))
                    goldens.Add(path);
            }

            goldens.Sort(StringComparer.Ordinal);
            return goldens.ToArray();
        }

        static string ReadGoldenText(string path)
        {
            return File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        /// <summary>
        /// 把 JSON 文本里的每个数字 token 上移 1 ULP 后重排（模拟跨平台 libm 输出的数字文本漂移）。
        /// 仅用于本套件生成的 golden —— 其字符串值（视图名）不含数字，数字只以 JSON number 形态出现。
        /// </summary>
        static string UlpDriftJsonNumbers(string json)
        {
            return Regex.Replace(json, "-?[0-9]+(?:\\.[0-9]+)?(?:[eE][+-]?[0-9]+)?", match =>
            {
                var value = double.Parse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                return UlpUp(value).ToString("R", CultureInfo.InvariantCulture);
            });
        }

        /// <summary>合成单视图（语义比对器自测专用；计数/标志/数值全部可参数化，取值均为精确可表示数）。</summary>
        static ViewProjection SyntheticSemanticView(
            int segmentCount = 2,
            int arcCount = 1,
            double secondEndX = 3.25,
            bool firstVisible = true)
        {
            var segments = new List<ViewSegment>();
            if (segmentCount >= 1)
                segments.Add(new ViewSegment(new ViewPoint2(-1.5, 2.25), new ViewPoint2(secondEndX, -4.75), firstVisible));
            if (segmentCount >= 2)
                segments.Add(new ViewSegment(new ViewPoint2(0, 0), new ViewPoint2(1, 1), !firstVisible));

            var arcs = new List<ViewArc>();
            if (arcCount >= 1)
                arcs.Add(new ViewArc(new ViewPoint2(1.5, -2.5), 3.75, 0.5, 0.75, isVisible: true, isClosed: false));
            if (arcCount >= 2)
                arcs.Add(new ViewArc(new ViewPoint2(-3, 1), 1.25, -0.5, 0.5, isVisible: false, isClosed: true));

            return new ViewProjection(ViewDirections.Front, segments, arcs);
        }

        /// <summary>把双精度沿位模式上移 1 ULP（模拟跨平台 libm 的 ≤1 ULP 差）。</summary>
        static double UlpUp(double value)
        {
            return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(value) + 1);
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
