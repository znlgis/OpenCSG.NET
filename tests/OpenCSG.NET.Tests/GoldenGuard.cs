using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// golden 升级门禁。
    /// SolidTest.AssertAcceptedStl 在 accept 缺失时只 Assert.Inconclusive——NUnit 的
    /// Inconclusive 不会让 dotnet test 失败，于是"新增用例忘了升级 golden"会静默通过、零保护。
    /// 本 SetUpFixture 在整套件结束时扫描 Results/，任何 *_ .stl / *_ .json 残留都判失败。
    /// 必须放在测试程序集内：NUnit 不扫描被引用程序集里的 [SetUpFixture]。
    /// 已知局限：OneTimeTearDown 失败属 fixture 级错误，不计入 VSTest 用例失败数；
    /// 实测 --filter 排除 GoldenGuardTest 时，即使门禁触发，运行仍报通过、退出码为 0
    /// （全量运行时 GoldenGuardTest 也会失败，故整套件运行仍为非零退出）。
    /// </summary>
    [SetUpFixture]
    public class GoldenGuard
    {
        /// <summary>回收未升级的 golden 结果文件名（以 "_.stl" 或 "_.json" 结尾）。</summary>
        public static IReadOnlyList<string> FindPendingGoldens(string resultsDirectory)
        {
            if (string.IsNullOrEmpty(resultsDirectory) || !Directory.Exists(resultsDirectory))
                return Array.Empty<string>();

            var pending = new List<string>();
            // 不用 Directory.GetFiles(dir, "*_.stl") 之类的通配：Windows 的 8.3 短名通配匹配
            // 有过歧义历史，显式后缀过滤更可靠（*.json 同理，故对两种扩展名分别显式过滤）。
            CollectPending(resultsDirectory, "*.stl", "_.stl", pending);
            CollectPending(resultsDirectory, "*.json", "_.json", pending);
            pending.Sort(StringComparer.Ordinal);
            return pending;
        }

        static void CollectPending(string directory, string searchPattern, string pendingSuffix, List<string> pending)
        {
            foreach (var file in Directory.GetFiles(directory, searchPattern))
            {
                if (file.EndsWith(pendingSuffix, StringComparison.Ordinal))
                    pending.Add(file);
            }
        }

        [OneTimeTearDown]
        public void FailOnUnacceptedGoldens()
        {
            var results = GoldenPaths.ResultsDirectory(Assembly.GetExecutingAssembly().Location);

            if (string.IsNullOrEmpty(results) || !Directory.Exists(results))
            {
                Assert.Fail(
                    "golden 门禁无法定位 Results 目录（GoldenPaths 返回 null，或目录不存在）——"
                    + "此时门禁形同虚设，故判失败而不是静默通过。");
            }

            var pending = FindPendingGoldens(results);

            if (pending.Count == 0)
                return;

            var names = new List<string>();
            foreach (var path in pending)
                names.Add(Path.GetFileName(path));

            Assert.Fail(
                $"有 {pending.Count} 个 golden 结果未升级：\n  {string.Join("\n  ", names)}\n"
                + "含义：新增用例从未被断言（零保护），或既有用例输出已变。\n"
                + "排查提示：若被标记的名字看起来像一条正式基线（即 {Fixture}.{Test}_.stl 或 {Fixture}.{Test}_.json 中的 {Test} 本身以 _ 结尾），\n"
                + "那是测试方法名以 _ 结尾造成的歧义 —— 本门禁按后缀匹配无法区分，请给该用例改名，不要反复 promote。\n"
                + $"确认输出正确后运行 tools/promote-goldens.sh 升级；目录：{results}");
        }
    }
}
