using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
    /// ① **两个**测试程序集对称守卫（审查 M2：SystemTests 自身 + 反射加载的 Tests 兄弟产物），
    /// 各自声明 <see cref="ParallelScope.Fixtures"/> + LevelOfParallelism(1)；
    /// ② `tests/ParallelRun.runsettings` 存在且把 worker 数固定为 4。
    /// </para>
    /// </summary>
    [TestFixture]
    public class ParallelRunMechanismTest
    {
        [Test]
        public void Assembly_DeclaresFixtureParallelismWithSingleWorkerDefault()
        {
            // 对称守卫（审查 M2）：两个测试程序集都必须显式 opt-in —— 任一缺属性或被改成
            // 默认多 worker，「默认串行」/「runsettings=4 worker」的既定配置都会静默漂移。
            // 用例数不变：本方法由一条扩为两个程序集的检查，不新增 [Test]。
            AssertAssemblyPins(typeof(ParallelRunMechanismTest).Assembly, "SystemTests 程序集");
            AssertAssemblyPins(LoadSiblingTestAssembly(), "Tests 程序集");
        }

        static void AssertAssemblyPins(Assembly assembly, string label)
        {
            // 用 CustomAttributeData 读构造参数（不依赖 NUnit 特性的属性名——3.13.3 的
            // ParallelizableAttribute/LevelOfParallelismAttribute 都没有公开读回方法）。
            var attributes = assembly.GetCustomAttributesData();

            var parallelizable = attributes
                .Where(a => a.AttributeType == typeof(ParallelizableAttribute))
                .ToArray();
            Assert.That(parallelizable, Has.Length.EqualTo(1),
                $"{label}：程序集级 Parallelizable 必须恰好一条（并行是显式 opt-in，见 Parallelism.cs）");
            Assert.That(parallelizable[0].ConstructorArguments[0].Value, Is.EqualTo((int)ParallelScope.Fixtures),
                $"{label}：并行粒度 = 夹具级（测试级并行会让同夹具内共享状态的用例串扰）");

            var levels = attributes
                .Where(a => a.AttributeType == typeof(LevelOfParallelismAttribute))
                .ToArray();
            Assert.That(levels, Has.Length.EqualTo(1),
                $"{label}：程序集级 LevelOfParallelism 必须恰好一条");
            Assert.That(levels[0].ConstructorArguments[0].Value, Is.EqualTo(1),
                $"{label}：默认运行必须单 worker（等价串行、与历史计数基线可比）；"
                + "并行由 tests/ParallelRun.runsettings 的 NumberOfTestWorkers 覆盖");
        }

        /// <summary>
        /// 反射加载兄弟测试程序集 OpenCSG.NET.Tests 的**编译产物**（与 attrprobe 同一「钉编译产物」
        /// 口径——只查源码文本会漏掉 MSBuild/生成代码对属性的改写）。产物目录从本程序集位置推导：
        /// 仅把路径中的项目目录段换成兄弟项目名，配置/TFM/自定义产物布局自动跟随。
        /// </summary>
        static Assembly LoadSiblingTestAssembly()
        {
            var selfDll = typeof(ParallelRunMechanismTest).Assembly.Location;
            var separator = Path.DirectorySeparatorChar;
            var selfDir = Path.GetDirectoryName(selfDll);
            Assert.That(selfDir, Is.Not.Null, $"无法从 {selfDll} 取目录");

            var testsDir = selfDir!.Replace(
                $"{separator}OpenCSG.NET.SystemTests{separator}",
                $"{separator}OpenCSG.NET.Tests{separator}");
            Assert.That(testsDir, Is.Not.EqualTo(selfDir),
                $"无法从 {selfDll} 推导 Tests 产物目录（布局约定：tests/OpenCSG.NET.Tests/bin/<Config>/<TFM>）");

            var testsDll = Path.Combine(testsDir, "OpenCSG.NET.Tests.dll");
            Assert.That(File.Exists(testsDll), Is.True,
                $"找不到 Tests 程序集产物 {testsDll} —— 先构建解决方案（dotnet build OpenCSG.NET.slnx）");

            return Assembly.LoadFrom(testsDll);
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
