using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;

namespace Csg.Test
{
    /// <summary>
    /// L7 健壮性：并行整套件**机制本身**的守卫（Plan E / Task 2.1）。
    /// <para>
    /// 实测（2026-10-14，探针 artifacts/planE/probeParallel）：只加 runsettings 是 no-op ——
    /// NUnit 的并行必须由程序集级属性 opt-in；而属性一旦缺失/被改成默认多 worker，
    /// 「默认串行」或「runsettings=4 worker」的既定配置就会静默漂移。本文件把两端都钉住：
    /// ① 本程序集（SystemTests）声明 <see cref="ParallelScope.Fixtures"/> + LevelOfParallelism(1)；
    /// ② `tests/ParallelRun.runsettings` 存在且把 worker 数固定为 4。
    /// </para>
    /// </summary>
    [TestFixture]
    public class ParallelRunMechanismTest
    {
        [Test]
        public void Assembly_DeclaresFixtureParallelismWithSingleWorkerDefault()
        {
            // 用 CustomAttributeData 读构造参数（不依赖 NUnit 特性的属性名——3.13.3 的
            // ParallelizableAttribute/LevelOfParallelismAttribute 都没有公开读回方法）。
            var assembly = typeof(ParallelRunMechanismTest).Assembly;
            var attributes = assembly.GetCustomAttributesData();

            var parallelizable = attributes
                .Where(a => a.AttributeType == typeof(ParallelizableAttribute))
                .ToArray();
            Assert.That(parallelizable, Has.Length.EqualTo(1),
                "程序集级 Parallelizable 必须恰好一条（并行是显式 opt-in，见 Parallelism.cs）");
            Assert.That(parallelizable[0].ConstructorArguments[0].Value, Is.EqualTo((int)ParallelScope.Fixtures),
                "并行粒度 = 夹具级（测试级并行会让同夹具内共享状态的用例串扰）");

            var levels = attributes
                .Where(a => a.AttributeType == typeof(LevelOfParallelismAttribute))
                .ToArray();
            Assert.That(levels, Has.Length.EqualTo(1),
                "程序集级 LevelOfParallelism 必须恰好一条");
            Assert.That(levels[0].ConstructorArguments[0].Value, Is.EqualTo(1),
                "默认运行必须单 worker（等价串行、与历史计数基线可比）；"
                + "并行由 tests/ParallelRun.runsettings 的 NumberOfTestWorkers 覆盖");
        }

        [Test]
        public void RunsettingsPinsFourWorkers_ForOptInParallelRuns()
        {
            var runsettingsPath = Path.Combine(FindRepoRoot(), "tests", "ParallelRun.runsettings");
            Assert.That(File.Exists(runsettingsPath), Is.True,
                $"并行运行配置文件必须存在：{runsettingsPath}（缺失 ⇒ 并行整套件无法复跑）");

            var doc = XDocument.Load(runsettingsPath);
            var worker = doc.Root?
                .Element("NUnit")?
                .Element("NumberOfTestWorkers")?
                .Value;

            Assert.That(worker, Is.EqualTo("4"),
                "runsettings 必须把 NUnit.NumberOfTestWorkers 固定为 4（Plan E / Task 2 实测口径）");
        }

        /// <summary>从程序集位置向上找 OpenCSG.NET.slnx 定位仓库根（自定义输出路径下也成立）。</summary>
        static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "OpenCSG.NET.slnx")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            Assert.Fail($"从 {AppContext.BaseDirectory} 向上未找到 OpenCSG.NET.slnx —— 无法定位仓库根");
            throw new InvalidOperationException("unreachable");
        }
    }
}
