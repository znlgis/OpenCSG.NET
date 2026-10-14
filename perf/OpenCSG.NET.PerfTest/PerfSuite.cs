using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Csg;
using Csg.Test;

namespace Csg.PerfTest
{
    /// <summary>
    /// Plan E / L6 停表基准套件（确定性输入、N 次重复；门禁统计量 = min，中位数仅人读）。
    /// <para>
    /// <b>为什么不用 BenchmarkDotNet 做门禁</b>：BDN 单次运行数分钟级（多次预热 + 统计迭代），
    /// 不适合 nightly 门禁与复杂度标度的重复运行；本套件全部输入固定、N=5 次采样（min/中位数），
    /// 整轮 ~10s。BDN（<see cref="PerfTest"/>）保留供人工深挖统计分布。
    /// </para>
    /// <para><b>用法（nightly / 手工）</b></para>
    /// <list type="bullet">
    /// <item><c>--suite</c>：只跑套件并打印中位数/最小值表（人工观察用，永远 exit 0）。</item>
    /// <item><c>--write-baseline &lt;path&gt;</c>：跑套件并把基线 JSON 写到 path（提交件 = <c>perf/baseline.json</c>）。</item>
    /// <item><c>--gate &lt;baseline.json&gt;</c>：重跑并与基线逐项比较，任一项
    /// <c>new.min &gt; old.min × 1.25</c> ⇒ 打印明细并 exit 1（超阈项自动单次重测取更小 min；
    /// 基线 min &lt; 20ms 的信息项只报告 —— 见下条「快操作地板与单项重测」）。</item>
    /// <item><c>--complexity</c>：复杂度标度（Clean 5k/20k、Project 5k/20k 三角形）预算 + 比值守卫。</item>
    /// </list>
    /// <para>
    /// <b>门禁统计量（为什么用 min 而不是中位数）</b>：本机实测（AMD Ryzen AI 9 HX 370，24 逻辑核）
    /// 存在整轮级的「机器状态」漂移 —— 同一条命令连续 5 次运行，Union/Project/Clean 的**中位数**
    /// 可见 1.4–2× 的两态跳变（电源/热/调度），而 min 基本落回同一水平。若不区分，
    /// 整轮状态会被当成回归：曾出现 <c>--gate</c> 对**刚写出的基线**判 Project ×1.43 失败。
    /// 故：JSON 同时记录 <c>medianMs</c>（人读）与 <c>minMs</c>（门禁判据）；中位数超阈但 min 未超时
    /// 打印「噪声判定」而不失败 —— 一次真实回归会一并抬高 min（所有重复都变慢），逃不掉。
    /// </para>
    /// <para>
    /// <b>快操作地板与单项重测（修复轮 1，防噪声假阳性）</b>：评审/复跑实测（同一未改动代码）：
    /// 15 样本 min 相对基线可见 ~30% 的下摆；WriteBinary（基线 min 97.86ms）一次读到 160.8ms
    /// （×1.64）—— 直接判失败即假阳性。两条抗噪机制：
    /// ① <b>单项重测</b>：首测超阈的项自动重测一次（再 15 样本、同协议），取两次中更小的 min 再判
    /// —— 一次「慢状态抽签」不会被钉死；真实回归两次都超阈（只补一次，不掩盖 2/2 的真信号）。
    /// ② <b>快操作地板</b>：基线 min &lt; <see cref="GateInformationalBelowMs"/>（20ms）的项标记为
    /// 「信息项」只报告不判失败 —— 该量级的自身离散度已超过 25% 窗口（提交基线内 ToJson 的
    /// min 16.748 vs 中位 41.216 = 同一测量内 2.46× 离散；TrianglesOf 5.165 / FromJson 16.116 同理
    /// 处在亚毫秒级调度/GC 抖动带）。曾考虑的绝对地板 max(25%, 2ms) 被同一组证据否决：ToJson 的
    /// 中位已达 min 的 2.46×，2ms 地板（阈值 ≈18.7ms）挡不住该离散。信息项在输出中逐条列出
    /// （比值照印），由人视读。
    /// </para>
    /// <para>
    /// <b>门禁口径（务必写清楚，防误用）</b>：25% 门禁是**机器噪声地板之上的**回归信号，
    /// 只在同一台机器（同一 nightly runner）上有意义；跨机器/虚拟化/降频/后台负载造成的
    /// &gt;25% 差异**不阻塞 PR**—— 该情形应记录环境、在 runner 上重跑，或按需重生成基线
    /// （重生成必须提交 diff 并在 PR 里说明原因）。门禁服务于「夜间回归发现」，不是 PR 硬门。
    /// </para>
    /// </summary>
    public static class PerfSuite
    {
        /// <summary>--suite 的每项重复次数（取中位数/最小值；预热 3 次不计）。</summary>
        public const int DefaultRepeats = 5;

        /// <summary>
        /// --write-baseline / --gate 的每项重复次数：翻三倍 ⇒ min 是 15 个样本的下界包络，
        /// 单次采样碰上慢状态也能被其余采样拉回（实测：5 样本的 min 在整轮慢状态下仍会被抬高
        /// 1.4–2×，15 样本显著更稳）。成本 ≈ 13s/次，nightly 可接受。
        /// </summary>
        public const int GateRepeats = 15;

        /// <summary>门禁系数：新版 &gt; 基线 × 该值即判回归（超阈项先经单项重测取更小 min；信息项除外）。</summary>
        public const double GateFactor = 1.25;

        /// <summary>
        /// 门禁信息项阈值（ms）：基线 min 低于该值的项只报告、不判失败 —— 该量级下共享开发机的
        /// 调度/GC 抖动与自身离散度已超过 25% 窗口（证据与被否决的绝对地板见类注释）。
        /// </summary>
        public const double GateInformationalBelowMs = 20.0;

        /// <summary>基线 JSON 版本（结构变更必须递增，门禁会校验）。</summary>
        public const int BaselineVersion = 1;

        // ── 一条基准项 ────────────────────────────────────────────────────────

        sealed class SuiteOp
        {
            public SuiteOp(string name, string description, Action action)
            {
                Name = name;
                Description = description;
                Action = action;
            }

            public string Name { get; }
            public string Description { get; }
            public Action Action { get; }
        }

        /// <summary>一项的测量结果。</summary>
        public sealed class ItemResult
        {
            public ItemResult(string op, double medianMs, double minMs, long bytes)
            {
                Op = op;
                MedianMs = medianMs;
                MinMs = minMs;
                Bytes = bytes;
            }

            public string Op { get; }

            /// <summary>N 次采样的中位数（人读用；受整轮机器状态影响）。</summary>
            public double MedianMs { get; }

            /// <summary>N 次采样的最小值（**门禁统计量**：抗整轮机器状态；见类注释）。</summary>
            public double MinMs { get; }

            public long Bytes { get; }
        }

        // ── 临时工件（WriteAscii/WriteBinary 的输出；跑完在 finally 里删除） ──────

        static readonly string TempDirectory = Path.Combine(Path.GetTempPath(), "opencsg-perftest");
        static readonly string AsciiStlPath = Path.Combine(TempDirectory, "suite-ascii.stl");
        static readonly string BinaryStlPath = Path.Combine(TempDirectory, "suite-binary.stl");

        /// <summary>删除套件的临时 STL 工件（<see cref="Measure"/> 与 <see cref="RunGate"/> 的 finally 都调用；删除失败不影响结果）。</summary>
        static void DeleteTempArtifacts()
        {
            foreach (var path in new[] { AsciiStlPath, BinaryStlPath })
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // 临时目录下删不掉不影响测量/门禁结果，交还 OS 回收。
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        // ── 套件：11 项操作 ───────────────────────────────────────────────────

        static IReadOnlyList<SuiteOp> BuildOps()
        {
            var (solidA, solidB) = PerfWorkloads.UnionPair();
            var trees = PerfWorkloads.DeepTrees();
            var json = PerfWorkloads.DeepTreeJson();
            var solidMesh = CsgMesh.TrianglesOf(solidA);
            var sphereTriangles = ViewBridge.ToViewTriangles(solidA);
            var fitSegments = PerfWorkloads.Segments(16000);
            var cleanSegments = PerfWorkloads.Segments(5000);
            var cleanBounds = ViewSegGen.BoundsOf(cleanSegments);

            var tempDirectory = TempDirectory;
            Directory.CreateDirectory(tempDirectory);
            var asciiPath = AsciiStlPath;
            var binaryPath = BinaryStlPath;

            void RequireNonEmpty(Solid solid, string what)
            {
                if (solid.Polygons.Count == 0)
                    throw new InvalidOperationException($"{what} 结果为空 —— 输入口径已漂移，基线不可比");
            }

            return new List<SuiteOp>
            {
                new("Union",
                    $"corpus #{PerfWorkloads.UnionIndexA} ∪ #{PerfWorkloads.UnionIndexB}(+{PerfWorkloads.UnionShiftX}mm X)：1152 面球面部分相交",
                    () => RequireNonEmpty(solidA.Union(solidB), "Union")),
                new("Intersect",
                    $"corpus #{PerfWorkloads.UnionIndexA} ∩ #{PerfWorkloads.UnionIndexB}(+{PerfWorkloads.UnionShiftX}mm X)",
                    () => RequireNonEmpty(solidA.Intersect(solidB), "Intersect")),
                new("Evaluate",
                    $"NodeGen d{PerfWorkloads.DeepTreeMaxDepth} × {PerfWorkloads.DeepTreeSeeds.Count} 种子（CsgEvaluator.Evaluate）",
                    () => { foreach (var tree in trees) CsgEvaluator.Evaluate(tree); }),
                new("ToJson",
                    $"d{PerfWorkloads.DeepTreeMaxDepth} × {PerfWorkloads.DeepTreeSeeds.Count} 树 × 20 轮（紧凑）",
                    () =>
                    {
                        for (var round = 0; round < 20; round++)
                            foreach (var tree in trees)
                                CsgSerialization.ToJson(tree, indented: false);
                    }),
                new("FromJson",
                    $"同上 JSON × 20 轮（CsgSerialization.FromJson）",
                    () =>
                    {
                        for (var round = 0; round < 20; round++)
                            foreach (var text in json)
                                CsgSerialization.FromJson(text);
                    }),
                new("TrianglesOf",
                    $"CsgMesh.TrianglesOf(corpus #{PerfWorkloads.UnionIndexA}) × 50 轮（{solidMesh.Count} 三角形）",
                    () =>
                    {
                        for (var round = 0; round < 50; round++)
                            CsgMesh.TrianglesOf(solidA);
                    }),
                new("WriteAscii",
                    $"StlWriter.WriteAscii 临时文件 × 5 轮（{solidMesh.Count} 面）",
                    () =>
                    {
                        for (var round = 0; round < 5; round++)
                            StlWriter.WriteAscii(asciiPath, solidMesh);
                    }),
                new("WriteBinary",
                    $"StlWriter.WriteBinary 临时文件 × 20 轮（{solidMesh.Count} 面）",
                    () =>
                    {
                        for (var round = 0; round < 20; round++)
                            StlWriter.WriteBinary(binaryPath, solidMesh);
                    }),
                new("Project",
                    $"ViewProjector.Project 三视图 × 2 轮（corpus #{PerfWorkloads.UnionIndexA} 三角化；含拓扑焊接）",
                    () =>
                    {
                        for (var round = 0; round < 2; round++)
                            new ViewProjector(sphereTriangles).Project();
                    }),
                new("Fit",
                    $"ViewArcFitter.Fit × 5 轮（PerfWorkloads.Segments(16000)）",
                    () =>
                    {
                        for (var round = 0; round < 5; round++)
                            ViewArcFitter.Fit(fitSegments);
                    }),
                new("Clean",
                    $"ViewSegmentCleanup.Clean × 3 轮（PerfWorkloads.Segments(5000)）",
                    () =>
                    {
                        for (var round = 0; round < 3; round++)
                            ViewSegmentCleanup.Clean(cleanSegments, cleanBounds);
                    }),
            };
        }

        /// <summary>
        /// 跑一遍套件：逐项 <see cref="MeasureOp(SuiteOp, int)"/>（详见其注释）；结束时在 finally 里
        /// 删除临时 STL 工件（正常/异常路径都清理）。
        /// </summary>
        public static IReadOnlyList<ItemResult> Measure(int repeats)
        {
            try
            {
                var results = new List<ItemResult>();
                foreach (var op in BuildOps())
                    results.Add(MeasureOp(op, repeats));
                return results;
            }
            finally
            {
                DeleteTempArtifacts();
            }
        }

        /// <summary>
        /// 单项测量：预热 3 次（吸收 JIT；分层编译开启时也给 tier-1/PGO 提升留余量，不进统计）
        /// + N 次采样（毫秒与线程分配字节数；中位数/最小值）。
        /// <para>
        /// 每次计时前做一次强制全量 GC（不计时；BDN 的默认测量卫生做法）：套件里前面的项
        /// 会留下最多数 GB 的垃圾（Union 单项就分配 ~133MB/次），若不清理，后面项（Clean/Project）
        /// 测到的 GC 频率被前序堆状态污染 —— 实测 Clean 项在套件末尾为 105–330ms，
        /// 而同机同代码在「全新堆先测」（--complexity）稳定 36ms。门禁对超阈项的单次重测也走本方法
        /// （同一协议，取更小 min；见 <see cref="RunGate"/>）。
        /// </para>
        /// </summary>
        static ItemResult MeasureOp(SuiteOp op, int repeats)
        {
            for (var warmup = 0; warmup < 3; warmup++)
                op.Action();

            var times = new double[repeats];
            var bytes = new long[repeats];
            for (var r = 0; r < repeats; r++)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                GC.WaitForPendingFinalizers();

                var before = GC.GetAllocatedBytesForCurrentThread();
                var sw = Stopwatch.StartNew();
                op.Action();
                sw.Stop();
                times[r] = sw.Elapsed.TotalMilliseconds;
                bytes[r] = GC.GetAllocatedBytesForCurrentThread() - before;
            }

            Array.Sort(times);
            Array.Sort(bytes);
            return new ItemResult(op.Name, times[times.Length / 2], times[0], bytes[bytes.Length / 2]);
        }

        static Dictionary<string, SuiteOp> OpDescriptions()
        {
            var map = new Dictionary<string, SuiteOp>(StringComparer.Ordinal);
            foreach (var op in BuildOps())
                map[op.Name] = op;
            return map;
        }

        public static void PrintSuite(IReadOnlyList<ItemResult> results)
        {
            var descriptions = OpDescriptions();
            Console.WriteLine($"{"操作",-14}{"中位数(ms)",12}{"最小(ms)",11}{"分配(KB)",11}  说明");
            foreach (var item in results)
            {
                descriptions.TryGetValue(item.Op, out var op);
                Console.WriteLine(
                    $"{item.Op,-14}{item.MedianMs,12:F3}{item.MinMs,11:F3}{item.Bytes / 1024.0,11:F1}  {op?.Description}");
            }
        }

        // ── 基线 JSON ─────────────────────────────────────────────────────────

        sealed class BaselineEnvironment
        {
            public string Machine { get; set; } = string.Empty;
            public string Os { get; set; } = string.Empty;
            public string Framework { get; set; } = string.Empty;
            public int Processors { get; set; }
            public string GeneratedAt { get; set; } = string.Empty;
        }

        sealed class BaselineItem
        {
            public string Op { get; set; } = string.Empty;
            public double MedianMs { get; set; }
            public double MinMs { get; set; }
            public long Bytes { get; set; }
            public int Repeats { get; set; }
        }

        sealed class BaselineDocument
        {
            public int Version { get; set; } = BaselineVersion;
            public BaselineEnvironment Env { get; set; } = new();
            public List<BaselineItem> Items { get; set; } = new();
        }

        static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            // Plan E 约定的小写字段名：{"version":1,"env":{...},"items":[{"op":…,"medianMs":…,"minMs":…}]}
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // 中文说明/机器名原样输出（默认转义会把可读性毁掉）
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static int WriteBaseline(string path, IReadOnlyList<ItemResult> results, int repeats)
        {
            var document = new BaselineDocument
            {
                Version = BaselineVersion,
                Env = new BaselineEnvironment
                {
                    Machine = Environment.MachineName,
                    Os = RuntimeInformation.OSDescription,
                    Framework = RuntimeInformation.FrameworkDescription,
                    Processors = Environment.ProcessorCount,
                    GeneratedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture),
                },
                Items = results.Select(r => new BaselineItem
                {
                    Op = r.Op,
                    MedianMs = Math.Round(r.MedianMs, 3),
                    MinMs = Math.Round(r.MinMs, 3),
                    Bytes = r.Bytes,
                    Repeats = repeats,
                }).ToList(),
            };

            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions) + "\n", new UTF8Encoding(false));
            Console.WriteLine($"基线已写出：{Path.GetFullPath(path)}（{document.Items.Count} 项，repeats={repeats}）");
            Console.WriteLine($"  机器：{document.Env.Machine} / {document.Env.Os} / {document.Env.Framework}");
            Console.WriteLine("  提醒：基线只对同机（同 nightly runner）可比；跨机器重生成须提交 diff 并说明原因。");
            return 0;
        }

        public static int RunGate(string baselinePath, int repeats)
        {
            if (!File.Exists(baselinePath))
            {
                Console.Error.WriteLine($"门禁失败：基线文件不存在 {baselinePath}");
                return 1;
            }

            BaselineDocument? document;
            try
            {
                document = JsonSerializer.Deserialize<BaselineDocument>(File.ReadAllText(baselinePath), JsonOptions);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"门禁失败：基线 JSON 读取/解析失败（{baselinePath}）：{ex.Message}");
                return 1;
            }

            if (document == null || document.Version != BaselineVersion || document.Env == null || document.Items == null)
            {
                Console.Error.WriteLine(
                    $"门禁失败：基线文件结构与当前版本不匹配（期望 version={BaselineVersion} 且 env/items 完整；"
                    + $"实际 version={document?.Version.ToString() ?? "null"}）");
                return 1;
            }

            Dictionary<string, BaselineItem> baseline;
            try
            {
                baseline = document.Items.ToDictionary(i => i.Op, StringComparer.Ordinal);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"门禁失败：基线 JSON 项列表无效（op 缺失或重复）：{ex.Message}");
                return 1;
            }

            Console.WriteLine($"门禁：基线 {Path.GetFullPath(baselinePath)}（version={document.Version}，"
                + $"机器 {document.Env.Machine}，生成于 {document.Env.GeneratedAt}）");
            Console.WriteLine($"当前环境：{Environment.MachineName} / {RuntimeInformation.OSDescription} / "
                + $"{RuntimeInformation.FrameworkDescription}");

            try
            {
                var ops = new Dictionary<string, SuiteOp>(StringComparer.Ordinal);
                var results = new List<ItemResult>();
                foreach (var op in BuildOps())
                {
                    ops[op.Name] = op;
                    results.Add(MeasureOp(op, repeats));
                }

                // 单项重测（见类注释「快操作地板与单项重测」）：首测超阈的项重测一次，
                // 取两次中更小的 min（整行取更优一侧的读数）。
                var retryNotes = new List<string>();
                foreach (var item in document.Items)
                {
                    if (item.MinMs <= 0 || !ops.TryGetValue(item.Op, out var op))
                        continue;

                    var index = results.FindIndex(r => string.Equals(r.Op, item.Op, StringComparison.Ordinal));
                    if (index < 0 || results[index].MinMs / item.MinMs <= GateFactor)
                        continue;

                    var first = results[index];
                    var retry = MeasureOp(op, repeats);
                    if (retry.MinMs < first.MinMs)
                        results[index] = retry;

                    retryNotes.Add($"{item.Op}：首测 min {first.MinMs:F3}ms（×{first.MinMs / item.MinMs:F2}）超阈，"
                        + $"重测 min {retry.MinMs:F3}ms（×{retry.MinMs / item.MinMs:F2}）⇒ 取"
                        + $"{(retry.MinMs < first.MinMs ? "重测" : "首测")}");
                }

                PrintSuite(results);

                var current = results.ToDictionary(i => i.Op, StringComparer.Ordinal);
                var failures = new List<string>();
                var noiseNotes = new List<string>();
                var informationalNotes = new List<string>();
                var informationalItems = new List<string>();

                Console.WriteLine($"\n逐项比较（判据：min 超过基线 min × {GateFactor:F2} 即失败；超阈项已单次重测取更小 min；"
                    + $"基线 min < {GateInformationalBelowMs:F0}ms 的项为信息项只报告）：");
                Console.WriteLine($"  {"操作",-14}{"基线min",10}{"当前min",10}{"min比",8}{"基线中位",10}{"当前中位",10}{"中位比",8}  判定");
                foreach (var item in document.Items)
                {
                    if (!current.TryGetValue(item.Op, out var now))
                    {
                        failures.Add($"{item.Op}：基线中存在、新运行缺失（套件项被删/改名 ⇒ 基线需重生成）");
                        Console.WriteLine($"  {item.Op,-14} 基线 min {item.MinMs,9:F3}ms → 缺失  [失败]");
                        continue;
                    }

                    if (item.MinMs <= 0)
                    {
                        failures.Add($"{item.Op}：基线 min 值非正（{item.MinMs}），无法比较");
                        continue;
                    }

                    var minRatio = now.MinMs / item.MinMs;
                    var medianRatio = item.MedianMs > 0 ? now.MedianMs / item.MedianMs : double.NaN;
                    var medianRow = double.IsNaN(medianRatio)
                        ? $"{"—",10}{"—",10}{"—",8}"
                        : $"{item.MedianMs,10:F3}{now.MedianMs,10:F3}{medianRatio,8:F2}";

                    var informational = item.MinMs < GateInformationalBelowMs;
                    if (informational)
                        informationalItems.Add(item.Op);

                    string verdict;
                    if (minRatio > GateFactor)
                    {
                        if (informational)
                        {
                            verdict = "[信息]";
                            informationalNotes.Add($"{item.Op}：min {item.MinMs:F3}ms → {now.MinMs:F3}ms（×{minRatio:F2}）超阈，"
                                + $"但基线 min < {GateInformationalBelowMs:F0}ms ⇒ 信息项只报告（快操作自身离散度超过门禁窗口，见类注释）");
                        }
                        else
                        {
                            verdict = "[失败]";
                            failures.Add(
                                $"{item.Op}：min {item.MinMs:F3}ms → {now.MinMs:F3}ms（×{minRatio:F2}，阈值 ×{GateFactor:F2}；"
                                + $"已单次重测取更小 min）；中位数 {item.MedianMs:F3} → {now.MedianMs:F3}（×{medianRatio:F2}）");
                        }
                    }
                    else if (medianRatio > GateFactor)
                    {
                        verdict = "[噪声]";
                        noiseNotes.Add($"{item.Op}：中位数 ×{medianRatio:F2} 超阈但 min ×{minRatio:F2} 未超 —— "
                            + "整轮机器状态被抬高（电源/负载），非代码回归");
                    }
                    else
                    {
                        verdict = minRatio > 0.9 && medianRatio > 0.9 ? "[注意]" : "[通过]";
                    }

                    Console.WriteLine($"  {item.Op,-14}{item.MinMs,10:F3}{now.MinMs,10:F3}{minRatio,8:F2}{medianRow}  {verdict}");
                }

                foreach (var extra in results.Where(r => !baseline.ContainsKey(r.Op)))
                {
                    failures.Add($"{extra.Op}：新运行出现基线中没有的项（基线需重生成）");
                    Console.WriteLine($"  {extra.Op,-14} 新增项  [失败]");
                }

                Console.WriteLine();
                if (informationalItems.Count > 0)
                    Console.WriteLine($"  信息项（基线 min < {GateInformationalBelowMs:F0}ms，只报告不判失败）：{string.Join("、", informationalItems)}");
                foreach (var note in retryNotes)
                    Console.WriteLine($"  重测判定：{note}");
                foreach (var note in informationalNotes)
                    Console.WriteLine($"  信息判定：{note}");
                foreach (var note in noiseNotes)
                    Console.WriteLine($"  噪声判定：{note}");

                if (failures.Count == 0)
                {
                    Console.WriteLine("门禁通过：无项 min 超出 25% 阈值。");
                    Console.WriteLine("口径提醒：>25% 的差异若来自机器噪声/负载（非同日同机对比），不阻塞 PR —— 记录环境后重跑或重生成基线。");
                    return 0;
                }

                Console.WriteLine($"门禁失败：{failures.Count} 项：");
                foreach (var failure in failures)
                    Console.WriteLine($"  - {failure}");
                Console.WriteLine("口径提醒：先确认是否机器噪声（同机重跑 / 检查后台负载）；确属回归再排查代码。");
                return 1;
            }
            finally
            {
                DeleteTempArtifacts();
            }
        }

        // ── 复杂度模式 ────────────────────────────────────────────────────────

        // 预算 = 实测 × 4（Plan E 任务 1：先实测、预算 = 实测 × 倍数并注明）。
        // 实测基准：2026-10-14，AMD Ryzen AI 9 HX 370 / Win10.0.26300 / .NET 8.0.31（本仓开发机）。
        // **统计量 = n 次采样的 min**（复现性实测 ±0.5%，见 PerfSuite 类注释）：Clean 20k 的
        // 中位数会被「同进程连续调用时堆状态劣化」抬高到 ≈1.7×min（566→960ms，协议伪影，
        // 非机器状态），故预算与比值都钉在 min 上。工具侧 ×4、SystemTests 侧 ×5（抵 CI 抖动）。
        // 若在显著更慢的机器上运行 —— 记录环境、重测后重钉，不得直接放宽倍数。

        /// <summary>Clean n=5000 预算（ms）：实测 min 33.0ms × 4。</summary>
        public const double CleanBudget5kMs = 132.0;

        /// <summary>Clean n=20000 预算（ms）：实测 min 567.0ms × 4。</summary>
        public const double CleanBudget20kMs = 2268.0;

        /// <summary>Project 5000 三角形预算（ms）：实测 min 214.7ms × 4。</summary>
        public const double ProjectBudget5kMs = 858.8;

        /// <summary>Project 20000 三角形预算（ms）：实测 min 961.9ms × 4。</summary>
        public const double ProjectBudget20kMs = 3847.6;

        /// <summary>
        /// Clean 的 20k/5k 耗时比上限（比值口径 = min/min）。
        /// 理论 O(n²) ⇒ ≈16×，本输入族实测 17.0–17.6（2026-10-14，三次复跑 17.03/17.62/17.05）；
        /// 关闭动态 PGO 的复测（失真配置）实测 24.54 ⇒ 32 的余量 ≈ 32 ÷ 24.54 ≈ 1.3×。
        /// &lt;32 是**宽界**：抓的是「忽然多项式升级」—— n = 4（20k/5k）下 n^2.5 级 ⇒ 4^2.5 = 32
        /// （恰为该上限，按 &lt; 判定即失败）、n^3 级 ⇒ 4^3 = 64；不是 1.5× 级抖动。
        /// </summary>
        public const double CleanQuadraticRatioLimit = 32.0;

        /// <summary>
        /// 单项测量（复杂度模式）：预热 3 次 + N 次中位数。
        /// <para>
        /// 与 <see cref="Measure"/> 的差别：**不**在每次计时前强制 GC。复杂度模式的进程里
        /// 只有 4 个测量项、堆是干净的，实测（3 次复跑）中位数复现性 ≈±3%；而加强制 GC 后
        /// 20k 档每次都从「刚全量回收的堆」起测，分配代价占比变化，20k/5k 比值从 ≈16.5 漂到 ≈25.6
        /// （把协议伪影算进了标度）。故复杂度模式保持朴素测量，比值口径与理论 O(n²) 直接可比。
        /// （套件模式必须加，见 <see cref="Measure"/>：多项共用进程、前序项留下数 GB 垃圾。）
        /// </para>
        /// </summary>
        static (double Median, double Min, long Bytes) MeasureOp(Action action, int repeats)
        {
            for (var warmup = 0; warmup < 3; warmup++)
                action();

            var times = new double[repeats];
            var bytes = new long[repeats];
            for (var r = 0; r < repeats; r++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                var sw = Stopwatch.StartNew();
                action();
                sw.Stop();
                times[r] = sw.Elapsed.TotalMilliseconds;
                bytes[r] = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Array.Sort(times);
            Array.Sort(bytes);
            return (times[times.Length / 2], times[0], bytes[bytes.Length / 2]);
        }

        public static int RunComplexity(int repeats)
        {
            Console.WriteLine("复杂度标度（每项 n 次采样，判据 = min、中位数并排人读；预算 = 实测 × 4，见常量注释）");
            var failures = new List<string>();

            var clean5k = PerfWorkloads.Segments(5000);
            var clean20k = PerfWorkloads.Segments(20000);
            var clean5kBounds = ViewSegGen.BoundsOf(clean5k);
            var clean20kBounds = ViewSegGen.BoundsOf(clean20k);

            Console.WriteLine("\n[Clean] ViewSegmentCleanup.Clean（ViewSegGen 批拼接，跨度恒定 2400mm）");
            var (clean5, clean5Min, _) = MeasureOp(() => ViewSegmentCleanup.Clean(clean5k, clean5kBounds), repeats);
            var (clean20, clean20Min, _) = MeasureOp(() => ViewSegmentCleanup.Clean(clean20k, clean20kBounds), repeats);
            var cleanRatio = clean5Min > 0 ? clean20Min / clean5Min : double.PositiveInfinity;

            // 预算断言用 min（见 CleanBudget* 注释）；中位数打印供人读，不参与判定。
            Console.WriteLine($"  n=5000 : 中位 {clean5,8:F1}ms（min {clean5Min,8:F1}）  预算 {CleanBudget5kMs,8:F1}ms  "
                + (clean5Min <= CleanBudget5kMs ? "[通过]" : "[失败]"));
            Console.WriteLine($"  n=20000: 中位 {clean20,8:F1}ms（min {clean20Min,8:F1}）  预算 {CleanBudget20kMs,8:F1}ms  "
                + (clean20Min <= CleanBudget20kMs ? "[通过]" : "[失败]"));
            Console.WriteLine($"  20k/5k 比值（min/min）：{cleanRatio:F2}（O(n²) 理论 ≈16；上限 {CleanQuadraticRatioLimit}）  "
                + (cleanRatio < CleanQuadraticRatioLimit ? "[通过]" : "[失败]"));

            if (clean5Min > CleanBudget5kMs)
                failures.Add($"Clean n=5000：min {clean5Min:F1}ms > 预算 {CleanBudget5kMs:F1}ms");
            if (clean20Min > CleanBudget20kMs)
                failures.Add($"Clean n=20000：min {clean20Min:F1}ms > 预算 {CleanBudget20kMs:F1}ms");
            if (!(cleanRatio < CleanQuadraticRatioLimit))
                failures.Add($"Clean 20k/5k 比值 {cleanRatio:F2} ≥ {CleanQuadraticRatioLimit}（疑似多项式升级）");

            Console.WriteLine("\n[Project] ViewProjector.Project 三视图（圆柱 900mm 长/900mm 直径 ⇒ 三视图 ≈900² 栅格）");
            var mesh5k = PerfWorkloads.ProjectMesh(PerfWorkloads.ProjectSmallTriangles);
            var mesh20k = PerfWorkloads.ProjectMesh(PerfWorkloads.ProjectLargeTriangles);
            var (project5, project5Min, _) = MeasureOp(() => new ViewProjector(mesh5k).Project(), repeats);
            var (project20, project20Min, _) = MeasureOp(() => new ViewProjector(mesh20k).Project(), repeats);
            var projectRatio = project5Min > 0 ? project20Min / project5Min : double.PositiveInfinity;

            Console.WriteLine($"  5000 三角形 : 中位 {project5,8:F1}ms（min {project5Min,8:F1}）  预算 {ProjectBudget5kMs,8:F1}ms  "
                + (project5Min <= ProjectBudget5kMs ? "[通过]" : "[失败]"));
            Console.WriteLine($"  20000 三角形: 中位 {project20,8:F1}ms（min {project20Min,8:F1}）  预算 {ProjectBudget20kMs,8:F1}ms  "
                + (project20Min <= ProjectBudget20kMs ? "[通过]" : "[失败]"));
            Console.WriteLine($"  20k/5k 比值（min/min）：{projectRatio:F2}（线性 ≈4，仅记录不断言）");

            if (project5Min > ProjectBudget5kMs)
                failures.Add($"Project 5000 三角形：min {project5Min:F1}ms > 预算 {ProjectBudget5kMs:F1}ms");
            if (project20Min > ProjectBudget20kMs)
                failures.Add($"Project 20000 三角形：min {project20Min:F1}ms > 预算 {ProjectBudget20kMs:F1}ms");

            if (project5 > ProjectBudget5kMs)
                failures.Add($"Project 5000 三角形：{project5:F1}ms > 预算 {ProjectBudget5kMs:F1}ms");
            if (project20 > ProjectBudget20kMs)
                failures.Add($"Project 20000 三角形：{project20:F1}ms > 预算 {ProjectBudget20kMs:F1}ms");

            if (failures.Count == 0)
            {
                Console.WriteLine("\n复杂度模式通过。");
                return 0;
            }

            Console.WriteLine($"\n复杂度模式失败：{failures.Count} 项：");
            foreach (var failure in failures)
                Console.WriteLine($"  - {failure}");
            return 1;
        }
    }
}
