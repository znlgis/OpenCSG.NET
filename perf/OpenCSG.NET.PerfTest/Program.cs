using System;
using System.Collections.Generic;

namespace Csg.PerfTest
{
    /// <summary>
    /// Plan E / L6 性能工具入口。
    /// <para>
    /// <b>nightly 用法</b>（从仓库根运行；相对路径按当前目录解析）：
    /// <code>
    /// dotnet run --project perf/OpenCSG.NET.PerfTest -c Release -- --gate perf/baseline.json
    /// </code>
    /// 回归时先重跑确认是否机器噪声，再按需重生成基线：
    /// <code>
    /// dotnet run --project perf/OpenCSG.NET.PerfTest -c Release -- --write-baseline perf/baseline.json
    /// </code>
    /// （基线重生成必须提交 diff 并在 PR 里说明原因。）
    /// </para>
    /// <para>
    /// <b>「&gt;25% 机器噪声不阻塞 PR」口径</b>：门禁阈值 25% 是**同机同日**对比下的回归信号；
    /// 跨机器、虚拟化降频、后台负载、首次 JIT/磁盘缓存差异都会造成 &gt;25% 的假阳性。
    /// 出现失败时：① 在 nightly runner 上原样重跑；② 检查后台负载/电源策略；
    /// ③ 确认是环境噪声后记录环境信息，不阻塞 PR；④ 只有同机复现的稳定劣化才按回归处理。
    /// </para>
    /// </summary>
    public class Program
    {
        public static int Main(string[] args)
        {
            int? repeats = null;
            var positional = new List<string>();
            string? mode = null;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--suite":
                        if (mode != null)
                            return RejectSecondMode(mode, args[i]);
                        mode = "suite";
                        break;
                    case "--write-baseline":
                        if (mode != null)
                            return RejectSecondMode(mode, args[i]);
                        mode = "write-baseline";
                        if (i + 1 >= args.Length)
                        {
                            Console.Error.WriteLine("--write-baseline 需要一个输出路径参数");
                            return 2;
                        }
                        positional.Add(args[++i]);
                        break;
                    case "--gate":
                        if (mode != null)
                            return RejectSecondMode(mode, args[i]);
                        mode = "gate";
                        if (i + 1 >= args.Length)
                        {
                            Console.Error.WriteLine("--gate 需要基线 JSON 路径参数");
                            return 2;
                        }
                        positional.Add(args[++i]);
                        break;
                    case "--complexity":
                        if (mode != null)
                            return RejectSecondMode(mode, args[i]);
                        mode = "complexity";
                        break;
                    case "--repeats":
                        if (i + 1 >= args.Length || !int.TryParse(args[++i], out var parsed) || parsed < 1)
                        {
                            Console.Error.WriteLine("--repeats 需要一个 ≥1 的整数");
                            return 2;
                        }
                        repeats = parsed;
                        break;
                    default:
                        Console.Error.WriteLine($"未知参数：{args[i]}");
                        PrintHelp();
                        return 2;
                }
            }

            // 门禁/基线用 3× 采样（min 抗整轮机器状态），--suite/--complexity 用默认值。
            var effectiveRepeats = repeats
                ?? (mode is "gate" or "write-baseline" ? PerfSuite.GateRepeats : PerfSuite.DefaultRepeats);

            switch (mode)
            {
                case null:
                    // 无参数 = 既有行为：BenchmarkDotNet 全量运行（人工深挖用，分钟级）
                    PerfTest.Run();
                    return 0;

                case "suite":
                    PerfSuite.PrintSuite(PerfSuite.Measure(effectiveRepeats));
                    return 0;

                case "write-baseline":
                    return PerfSuite.WriteBaseline(positional[0], PerfSuite.Measure(effectiveRepeats), effectiveRepeats);

                case "gate":
                    return PerfSuite.RunGate(positional[0], effectiveRepeats);

                case "complexity":
                    return PerfSuite.RunComplexity(effectiveRepeats);

                default:
                    PrintHelp();
                    return 2;
            }
        }

        /// <summary>模式互斥：--suite / --write-baseline / --gate / --complexity 只能选一（重复出现同样拒绝）。</summary>
        static int RejectSecondMode(string first, string second)
        {
            Console.Error.WriteLine($"参数错误：模式互斥 —— 已指定 --{first}，又见 {second}；一次只能运行一个模式。");
            PrintHelp();
            return 2;
        }

        static void PrintHelp()
        {
            Console.WriteLine("OpenCSG.NET Plan E 性能工具");
            Console.WriteLine("  --suite                        跑停表套件并打印中位数表");
            Console.WriteLine("  --write-baseline <path>        跑套件并写出基线 JSON（提交件 perf/baseline.json）");
            Console.WriteLine("  --gate <baseline.json>         与基线逐项比较，任一项 new.min > old.min×1.25 ⇒ exit 1");
            Console.WriteLine("                                 （超阈项自动重测一次取更小 min；基线 min < 20ms 的信息项只报告；");
            Console.WriteLine("                                  中位超阈而 min 未超判为机器状态噪声）");
            Console.WriteLine("  --complexity                   Clean 5k/20k、Project 5k/20k 预算 + 比值守卫");
            Console.WriteLine("  --repeats <n>                  每项重复次数（--suite 默认 5；--gate/--write-baseline 默认 15）");
            Console.WriteLine("  （无参数）                     BenchmarkDotNet 全量运行（人工深挖，分钟级）");
            Console.WriteLine();
            Console.WriteLine("nightly：dotnet run --project perf/OpenCSG.NET.PerfTest -c Release -- --gate perf/baseline.json");
            Console.WriteLine("口径：25% 门禁只对同机同日对比有意义；机器噪声造成的超阈不阻塞 PR（详见 PerfSuite 头注释）。");
        }
    }
}
