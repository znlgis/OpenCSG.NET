using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// 强制 tests/DEFECTS.md 规则 3 声明的双向索引（册 ↔ 测试源码）：
    /// <list type="bullet">
    /// <item>D1（册 → 测试）：册里每个表格行编号必须能在 tests/**/*.cs 里 grep 到（注释与断言消息也算）。</item>
    /// <item>D2（测试 → 册）：测试源码里每个 Explicit 引用编号必须在册里有表格行。</item>
    /// </list>
    /// 三个解析纯函数（ParseRegisteredIds / ParseExplicitReferences / EnumerateTestSources）
    /// 由本夹具用内联字符串单测，防止正则退化后双向检查静默失效（永远绿）。
    /// 自重应用：本文件也在扫描范围内 —— 所有内联样例里的引号都写成转义形态，
    /// 因此本文件源码不含「Explicit + 裸引号 + DEFECT + 数字」的字面串，不会产生 D2 引用。
    /// </summary>
    [TestFixture]
    public class DefectRegistryTest
    {
        /// <summary>双向索引违规时的统一提示（规则 3 原文）。</summary>
        private const string RuleHint = "规则见 tests/DEFECTS.md 规则 3";

        /// <summary>表格行（行首竖线 + 编号单元）；未修复表与已修复表同构，故一条正则通吃。</summary>
        private static readonly Regex RegisteredRowPattern = new Regex(
            @"^\|\s*DEFECT-(\d+)\s*\|", RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>D2 引用形态：兼容 [Explicit( 与 data.Explicit( 两种前缀（正则只认「Explicit + 裸引号 + DEFECT- + 数字」）。</summary>
        private static readonly Regex ExplicitReferencePattern = new Regex(
            "Explicit\\(\"DEFECT-(\\d+)", RegexOptions.Compiled);

        /// <summary>D1 出现形态：源码里任意位置的 DEFECT-nnn 记号（含注释与断言消息）。</summary>
        private static readonly Regex AnyDefectIdPattern = new Regex(
            @"DEFECT-(\d+)", RegexOptions.Compiled);

        /// <summary>内联样例册：未修复表 2 行 + 已修复表 1 行 + 一处行内提及（不算登记）。</summary>
        private const string SampleRegistry =
            "# 缺陷登记表\n"
            + "\n"
            + "| ID | 模块 | 严重度 |\n"
            + "| --- | --- | --- |\n"
            + "| DEFECT-601 | 未修复样例甲 | 低 |\n"
            + "| DEFECT-602 | 未修复样例乙 | 高 |\n"
            + "\n"
            + "## 已修复\n"
            + "\n"
            + "| ID | 模块 | 修复方式 |\n"
            + "| --- | --- | --- |\n"
            + "| DEFECT-603 | 已修复样例 | 修好了 |\n"
            + "\n"
            + "正文里提到 DEFECT-604，但不在表格行首，不算登记。\n";

        // ------------------------------------------------------------------
        // 纯函数
        // ------------------------------------------------------------------

        /// <summary>解析册里所有表格行编号（未修复表与已修复表都算）。</summary>
        internal static IReadOnlySet<int> ParseRegisteredIds(string markdown)
        {
            var ids = new HashSet<int>();
            AddIds(RegisteredRowPattern, markdown, ids);
            return ids;
        }

        /// <summary>提取所有 Explicit 引用编号（两种前缀形态共用一条正则）。</summary>
        internal static IReadOnlySet<int> ParseExplicitReferences(IEnumerable<string> sources)
        {
            var ids = new HashSet<int>();
            if (sources == null)
                return ids;

            foreach (var text in sources)
                AddIds(ExplicitReferencePattern, text, ids);

            return ids;
        }

        /// <summary>提取源码文本里任意位置的编号（D1 的「grep 得到」口径：注释亦算）。</summary>
        internal static IReadOnlySet<int> ExtractDefectIds(IEnumerable<string> sources)
        {
            var ids = new HashSet<int>();
            if (sources == null)
                return ids;

            foreach (var text in sources)
                AddIds(AnyDefectIdPattern, text, ids);

            return ids;
        }

        /// <summary>枚举 tests/**/*.cs（排除 bin/obj 与非 .cs），返回按 Ordinal 排序的完整路径列表。</summary>
        internal static IReadOnlyList<string> EnumerateTestSources(string repoRoot)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(repoRoot))
                return result;

            var testsRoot = Path.Combine(repoRoot, "tests");
            if (!Directory.Exists(testsRoot))
                return result;

            foreach (var file in Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (HasBuildOutputSegment(testsRoot, file))
                    continue;

                result.Add(Path.GetFullPath(file));
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>从 startDirectory 逐级向上找含 OpenCSG.NET.slnx 的目录；找不到返回 null。</summary>
        internal static string FindRepoRoot(string startDirectory)
        {
            var dir = string.IsNullOrEmpty(startDirectory) ? null : new DirectoryInfo(startDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "OpenCSG.NET.slnx")))
                    return dir.FullName;

                dir = dir.Parent;
            }

            return null;
        }

        private static void AddIds(Regex pattern, string text, ISet<int> sink)
        {
            if (string.IsNullOrEmpty(text))
                return;

            foreach (Match match in pattern.Matches(text))
                sink.Add(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        }

        private static bool HasBuildOutputSegment(string testsRoot, string filePath)
        {
            var relative = filePath.Substring(testsRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
            foreach (var segment in relative.Split(separators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                    || segment.Equals("obj", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        // ------------------------------------------------------------------
        // 活体断言（D1 / D2）
        // ------------------------------------------------------------------

        [Test]
        public void D1_EveryRegisteredDefectIdIsReferencedInTestSources()
        {
            var repoRoot = RequireRepoRoot();
            var registered = ParseRegisteredIds(File.ReadAllText(DefectRegistryPath(repoRoot)));

            // 空册守卫：册解析退化或表格被清空时 D1 会形同虚设，必须判失败而不是静默通过。
            Assert.That(registered, Is.Not.Empty,
                "DEFECTS.md 表格行解析为空（正则退化或册被清空）—— D1 会形同虚设。" + RuleHint);

            var sources = EnumerateTestSources(repoRoot);
            Assert.That(sources, Is.Not.Empty, "tests/**/*.cs 枚举为空 —— D1 会形同虚设。" + RuleHint);

            var referenced = ExtractDefectIds(ReadAll(sources));

            var missing = registered.Except(referenced).OrderBy(id => id).ToList();
            if (missing.Count > 0)
            {
                var detail = missing.Select(id => $"DEFECT-{id:D3}（0 处）");
                Assert.Fail(
                    $"DEFECTS.md 规则 3 违反（D1 册→测试）：{missing.Count} 个注册编号在 tests/**/*.cs 里找不到任何出现："
                    + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", detail)
                    + Environment.NewLine
                    + $"扫描范围：{sources.Count} 个 .cs 文件（已排除 bin/obj；注释亦算出现）。"
                    + "缺失通常意味着引用被删/改编号而册未同步；修复缺陷时按规则 5 同步两边"
                    + "（[Explicit] 摘掉后仍须在回归用例里注明编号，因为规则 3 对「已修复」表同样生效）。"
                    + RuleHint);
            }
        }

        [Test]
        public void D2_EveryExplicitDefectReferenceIsRegistered()
        {
            var repoRoot = RequireRepoRoot();
            var registered = ParseRegisteredIds(File.ReadAllText(DefectRegistryPath(repoRoot)));

            var sources = EnumerateTestSources(repoRoot);
            Assert.That(sources, Is.Not.Empty, "tests/**/*.cs 枚举为空 —— D2 会形同虚设。" + RuleHint);

            var texts = ReadAll(sources);
            var referenced = ParseExplicitReferences(texts);

            var unregistered = referenced.Except(registered).OrderBy(id => id).ToList();
            if (unregistered.Count == 0)
                return;

            var lines = new List<string>();
            foreach (var id in unregistered)
            {
                var perFile = new List<string>();
                var total = 0;
                for (var i = 0; i < texts.Count; i++)
                {
                    var count = 0;
                    foreach (Match match in ExplicitReferencePattern.Matches(texts[i]))
                    {
                        if (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) == id)
                            count++;
                    }

                    if (count > 0)
                    {
                        perFile.Add($"{RelativeTo(repoRoot, sources[i])} ×{count}");
                        total += count;
                    }
                }

                lines.Add($"DEFECT-{id:D3}（{total} 处：{string.Join("、", perFile)}）");
            }

            Assert.Fail(
                $"DEFECTS.md 规则 3 违反（D2 测试→册）：{unregistered.Count} 个 Explicit 引用编号没有册行："
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", lines)
                + Environment.NewLine
                + "含义：引用了不存在的缺陷编号（笔误或未登记）。应先在 DEFECTS.md 建行，或改正测试里的编号。"
                + RuleHint);
        }

        // ------------------------------------------------------------------
        // 纯函数单测（内联字符串）
        // ------------------------------------------------------------------

        [Test]
        public void ParseRegisteredIds_ReadsBothTables_AndOnlyTableRows()
        {
            var registered = ParseRegisteredIds(SampleRegistry);

            Assert.That(registered, Is.EquivalentTo(new[] { 601, 602, 603 }),
                "未修复表与已修复表都要算；行内提及的 DEFECT-604 与正文占位 DEFECT-nnn 不算");
        }

        [Test]
        public void ParseRegisteredIds_EmptyRegistry_IsEmpty()
        {
            Assert.That(ParseRegisteredIds(null), Is.Empty);
            Assert.That(ParseRegisteredIds(string.Empty), Is.Empty);
            Assert.That(
                ParseRegisteredIds("# 标题\n\n| ID | 模块 |\n| --- | --- |\n规则文本：每个 DEFECT-nnn 编号…\n"),
                Is.Empty,
                "表头与规则占位行不是编号行");
        }

        [Test]
        public void ParseFunctions_NormalPair_AgreeInBothDirections()
        {
            var registered = ParseRegisteredIds(SampleRegistry);
            var referenced = ParseExplicitReferences(new[]
            {
                "[Explicit(\"DEFECT-601: 特性形态引用\")]",
                "data.Explicit(\"DEFECT-602: 数据形态引用\")",
                "[Explicit(\"DEFECT-603: 已修复样例的引用\")]",
            });

            Assert.That(registered, Is.EquivalentTo(new[] { 601, 602, 603 }));
            Assert.That(registered.Except(referenced), Is.Empty, "册里不应有未引用编号");
            Assert.That(referenced.Except(registered), Is.Empty, "源码里不应有未登记编号");
        }

        [Test]
        public void ParseFunctions_MissingReference_IsRegistryMinusSources()
        {
            var registered = ParseRegisteredIds(SampleRegistry);
            var referenced = ParseExplicitReferences(new[]
            {
                "[Explicit(\"DEFECT-601: 引用甲\")]",
                "data.Explicit(\"DEFECT-603: 引用丙\")",
            });

            Assert.That(registered.Except(referenced), Is.EquivalentTo(new[] { 602 }),
                "缺一：D1 方向应单点报出 602");
        }

        [Test]
        public void ParseFunctions_ExtraReference_IsSourcesMinusRegistry()
        {
            var registered = ParseRegisteredIds(SampleRegistry);
            var referenced = ParseExplicitReferences(new[]
            {
                "[Explicit(\"DEFECT-602: 引用乙\")]",
                "data.Explicit(\"DEFECT-999: 未登记编号\")",
            });

            Assert.That(referenced.Except(registered), Is.EquivalentTo(new[] { 999 }),
                "多一：D2 方向应单点报出 999");
        }

        [Test]
        public void ParseExplicitReferences_EmptySources_IsEmpty()
        {
            Assert.That(ParseExplicitReferences(null), Is.Empty);
            Assert.That(ParseExplicitReferences(Array.Empty<string>()), Is.Empty);
            Assert.That(ParseExplicitReferences(new[] { string.Empty, "// 没有引用的注释" }), Is.Empty);
        }

        [Test]
        public void ParseExplicitReferences_MultipleIdsOnOneLine_AllCaptured()
        {
            var referenced = ParseExplicitReferences(new[]
            {
                "// 同一行两个引用： [Explicit(\"DEFECT-601: 甲\")] 与 data.Explicit(\"DEFECT-602: 乙\")",
            });

            Assert.That(referenced, Is.EquivalentTo(new[] { 601, 602 }));
        }

        [Test]
        public void ParseExplicitReferences_BothAttributeForms_AreCaptured()
        {
            var referenced = ParseExplicitReferences(new[]
            {
                "        [Explicit(\"DEFECT-601: 特性形态\")]",
                "            data.Explicit(\"DEFECT-602: 数据形态\")",
            });

            Assert.That(referenced, Is.EquivalentTo(new[] { 601, 602 }),
                "[Explicit( 与 data.Explicit( 两种形态都要捕获");
        }

        [Test]
        public void ParseExplicitReferences_IgnoresMentionsWithoutExplicitCall()
        {
            var referenced = ParseExplicitReferences(new[]
            {
                "// 归因统一由 DEFECT-601 承接，注释里的编号不算 Explicit 引用",
                "Assert.Fail(\"DEFECT-602：断言消息里的编号同样不算\");",
                "[Explicit(\"DEFECT-nnn: 占位不是数字\")]",
            });

            Assert.That(referenced, Is.Empty, "只有第 9 行那种「Explicit + 裸引号」才算 D2 引用");
        }

        [Test]
        public void ExtractDefectIds_CountsAnyOccurrenceIncludingComments()
        {
            var found = ExtractDefectIds(new[]
            {
                "// 首轮实测全部归因于 DEFECT-601（注释也算出现）",
                "Assert.Fail(\"DEFECT-602：断言消息里的编号也算出现\");",
                "[Explicit(\"DEFECT-601: 同一编号出现多次只算一次\")]",
            });

            Assert.That(found, Is.EquivalentTo(new[] { 601, 602 }));

            Assert.That(ExtractDefectIds(Array.Empty<string>()), Is.Empty);
            Assert.That(ExtractDefectIds(new[] { "// 只有占位 DEFECT-nnn，没有数字编号" }), Is.Empty);
        }

        [Test]
        public void EnumerateTestSources_SyntheticTree_ExcludesBinAndObj()
        {
            var tempRoot = NewTempDirectory();
            var testsRoot = Path.Combine(tempRoot, "tests");
            try
            {
                Directory.CreateDirectory(Path.Combine(testsRoot, "a"));
                Directory.CreateDirectory(Path.Combine(testsRoot, "bin"));
                Directory.CreateDirectory(Path.Combine(testsRoot, "sub", "obj"));

                File.WriteAllText(Path.Combine(testsRoot, "z.cs"), "// z");
                File.WriteAllText(Path.Combine(testsRoot, "a", "m.cs"), "// m");
                File.WriteAllText(Path.Combine(testsRoot, "bin", "skip.cs"), "// skip");
                File.WriteAllText(Path.Combine(testsRoot, "sub", "obj", "skip.cs"), "// skip");
                File.WriteAllText(Path.Combine(testsRoot, "a", "notes.txt"), "not c#");

                var sources = EnumerateTestSources(tempRoot);

                Assert.That(sources, Is.EqualTo(new[]
                {
                    Path.Combine(testsRoot, "a", "m.cs"),
                    Path.Combine(testsRoot, "z.cs"),
                }), "应按 Ordinal 排序返回绝对路径，且排除 bin/obj 与非 .cs 文件");

                Assert.That(EnumerateTestSources(Path.Combine(tempRoot, "missing")), Is.Empty,
                    "tests/ 不存在时应返回空列表");
            }
            finally
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Test]
        public void EnumerateTestSources_RealRepository_IsSortedAndCrossProject()
        {
            var repoRoot = RequireRepoRoot();
            var sources = EnumerateTestSources(repoRoot);

            Assert.That(sources, Is.Not.Empty);
            Assert.That(sources, Is.EqualTo(sources.OrderBy(path => path, StringComparer.Ordinal).ToList()),
                "必须按 StringComparer.Ordinal 排序返回");

            var normalized = sources.Select(path => path.Replace('\\', '/')).ToList();
            foreach (var project in new[] { "OpenCSG.NET.Tests", "OpenCSG.NET.SystemTests", "OpenCSG.NET.TestKit" })
            {
                Assert.That(normalized.Any(path => path.Contains("/tests/" + project + "/")), Is.True,
                    $"枚举应覆盖 {project} 的 .cs 源码");
            }

            foreach (var path in normalized)
            {
                Assert.That(path, Does.Not.Contain("/bin/"), $"{path} 位于 bin/ 内");
                Assert.That(path, Does.Not.Contain("/obj/"), $"{path} 位于 obj/ 内");
                Assert.That(path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase), Is.True);
            }
        }

        [Test]
        public void FindRepoRoot_FromNestedDirectory_FindsNearestMarker()
        {
            var tempRoot = NewTempDirectory();
            var nested = Path.Combine(tempRoot, "x", "y");
            try
            {
                Directory.CreateDirectory(nested);
                File.WriteAllText(Path.Combine(tempRoot, "OpenCSG.NET.slnx"), "<Solution />");

                Assert.That(FindRepoRoot(nested), Is.EqualTo(tempRoot));
            }
            finally
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Test]
        public void FindRepoRoot_WithoutAnyMarker_ReturnsNull()
        {
            var tempRoot = NewTempDirectory();
            try
            {
                Assert.That(FindRepoRoot(tempRoot), Is.Null,
                    "向上没有任何 OpenCSG.NET.slnx 时必须返回 null（调用方据此 Assert.Fail）");
            }
            finally
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Test]
        public void FindRepoRoot_FromTestOutputDirectory_FindsRepositoryRoot()
        {
            var root = FindRepoRoot(AppContext.BaseDirectory);

            Assert.That(root, Is.Not.Null, $"从 {AppContext.BaseDirectory} 向上应能找到 OpenCSG.NET.slnx");
            Assert.That(File.Exists(Path.Combine(root, "tests", "DEFECTS.md")), Is.True,
                $"仓库根 {root} 下应存在 tests/DEFECTS.md");
        }

        // ------------------------------------------------------------------
        // 夹具辅助
        // ------------------------------------------------------------------

        private static string RequireRepoRoot()
        {
            var root = FindRepoRoot(AppContext.BaseDirectory);
            if (root == null)
            {
                Assert.Fail(
                    $"无法从 AppContext.BaseDirectory = {AppContext.BaseDirectory} 向上找到含 OpenCSG.NET.slnx 的仓库根。"
                    + "DEFECTS.md 与 tests/ 源码都相对仓库根定位，定位失败时双向索引检查会形同虚设，故判失败而不是静默通过。");
            }

            return root;
        }

        private static string DefectRegistryPath(string repoRoot)
        {
            return Path.Combine(repoRoot, "tests", "DEFECTS.md");
        }

        private static List<string> ReadAll(IReadOnlyList<string> paths)
        {
            var texts = new List<string>(paths.Count);
            foreach (var path in paths)
                texts.Add(File.ReadAllText(path));

            return texts;
        }

        private static string RelativeTo(string repoRoot, string path)
        {
            var relative = path.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase)
                ? path.Substring(repoRoot.Length)
                : path;

            return relative.TrimStart('\\', '/').Replace('\\', '/');
        }

        private static string NewTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "csg-defect-registry-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
