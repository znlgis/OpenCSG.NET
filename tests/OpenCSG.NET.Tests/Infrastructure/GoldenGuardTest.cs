using System;
using System.IO;
using System.Reflection;
using Csg.Test;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// 验证 golden 门禁本身有效：门禁检查逻辑必须能发现残留的 *_ .stl，
    /// 且 GoldenPaths 必须解析到真实的 Results 目录（否则门禁形同虚设）。
    /// </summary>
    [TestFixture]
    public class GoldenGuardTest
    {
        [Test]
        public void GoldenPaths_ResolvesToExistingResultsDirectory()
        {
            var dir = GoldenPaths.ResultsDirectory(Assembly.GetExecutingAssembly().Location);

            Assert.That(dir, Is.Not.Null, "GoldenPaths 未能从测试程序集位置向上找到 OpenCSG.NET.Tests.csproj");
            Assert.That(Directory.Exists(dir), Is.True, $"解析出的 Results 目录不存在：{dir}");
        }

        [Test]
        public void GoldenPaths_ResolvesToSameDirectoryAsSolidTestAssumes()
        {
            // SolidTest 用 4 层 GetDirectoryName；两者必须指向同一处，
            // 否则门禁守的是另一个目录。
            var asmPath = Assembly.GetExecutingAssembly().Location;
            var repoPath = Path.GetDirectoryName(Path.GetDirectoryName(
                Path.GetDirectoryName(Path.GetDirectoryName(asmPath))));
            var solidTestPath = Path.Combine(repoPath, "Results");

            var guardPath = GoldenPaths.ResultsDirectory(asmPath);

            Assert.That(guardPath, Is.EqualTo(solidTestPath));
        }

        [Test]
        public void FindPendingGoldens_DetectsRejectedFile()
        {
            var results = GoldenPaths.ResultsDirectory(Assembly.GetExecutingAssembly().Location);
            var probe = Path.Combine(results, "__GoldenGuardProbe___.stl");
            File.WriteAllText(probe, "solid probe\nendsolid probe\n");

            try
            {
                var pending = GoldenGuard.FindPendingGoldens(results);

                Assert.That(pending, Does.Contain(probe));
            }
            finally
            {
                File.Delete(probe);
            }
        }

        [Test]
        public void FindPendingGoldens_IgnoresAcceptedFiles()
        {
            var results = GoldenPaths.ResultsDirectory(Assembly.GetExecutingAssembly().Location);
            // 注意：文件名末尾不能是 "_"。以 "_.stl" 结尾是残留判定，而 "__GoldenGuardProbe__.stl"
            // 的末五位恰好就是 "_.stl"，会被判成残留，故此处的已接受探针不带尾部下划线。
            var probe = Path.Combine(results, "__GoldenGuardProbe.stl");
            File.WriteAllText(probe, "solid probe\nendsolid probe\n");

            try
            {
                var pending = GoldenGuard.FindPendingGoldens(results);

                Assert.That(pending, Does.Not.Contain(probe));
            }
            finally
            {
                File.Delete(probe);
            }
        }

        [Test]
        public void FindPendingGoldens_OnCleanTree_IsEmpty()
        {
            var results = GoldenPaths.ResultsDirectory(Assembly.GetExecutingAssembly().Location);

            var pending = GoldenGuard.FindPendingGoldens(results);

            Assert.That(pending, Is.Empty,
                "存在未升级的 golden 残留；先运行 tools/promote-goldens.sh 人工确认后升级");
        }

        [Test]
        public void GoldenPaths_MarkerIsParameterizable()
        {
            var asmPath = System.Reflection.Assembly.GetExecutingAssembly().Location;

            // 用当前工程的标记文件，结果必须与默认调用一致
            var viaDefault = GoldenPaths.ResultsDirectory(asmPath);
            var viaExplicit = GoldenPaths.ResultsDirectory(asmPath, "OpenCSG.NET.Tests.csproj");

            Assert.That(viaExplicit, Is.EqualTo(viaDefault));

            // 用一个不存在的标记文件，必须返回 null 而不是抛异常
            var missing = GoldenPaths.ResultsDirectory(asmPath, "__NoSuchMarker__.csproj");

            Assert.That(missing, Is.Null);
        }
    }
}
