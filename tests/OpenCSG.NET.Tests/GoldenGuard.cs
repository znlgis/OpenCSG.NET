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
    /// 本 SetUpFixture 在整套件结束时扫描 Results/，任何 *_ .stl 残留都判失败。
    /// 必须放在测试程序集内：NUnit 不扫描被引用程序集里的 [SetUpFixture]。
    /// </summary>
    [SetUpFixture]
    public class GoldenGuard
    {
        /// <summary>回收未升级的 golden 结果文件名（以 "_.stl" 结尾）。</summary>
        public static IReadOnlyList<string> FindPendingGoldens(string resultsDirectory)
        {
            if (string.IsNullOrEmpty(resultsDirectory) || !Directory.Exists(resultsDirectory))
                return Array.Empty<string>();

            var pending = new List<string>();
            // 不用 Directory.GetFiles(dir, "*_.stl")：Windows 的 8.3 短名通配匹配有过歧义历史，
            // 显式后缀过滤更可靠。
            foreach (var file in Directory.GetFiles(resultsDirectory, "*.stl"))
                if (file.EndsWith("_.stl", StringComparison.Ordinal))
                    pending.Add(file);
            pending.Sort(StringComparer.Ordinal);
            return pending;
        }

        [OneTimeTearDown]
        public void FailOnUnacceptedGoldens()
        {
            var results = GoldenPaths.ResultsDirectory(Assembly.GetExecutingAssembly().Location);
            var pending = FindPendingGoldens(results);

            if (pending.Count == 0)
                return;

            var names = new List<string>();
            foreach (var path in pending)
                names.Add(Path.GetFileName(path));

            Assert.Fail(
                $"有 {pending.Count} 个 golden 结果未升级：\n  {string.Join("\n  ", names)}\n"
                + "含义：新增用例从未被断言（零保护），或既有用例输出已变。\n"
                + $"确认输出正确后运行 tools/promote-goldens.sh 升级；目录：{results}");
        }
    }
}
