using NUnit.Framework;

// Plan E / Task 2（L7）：并行整套件（显式 opt-in，见 tests/ParallelRun.runsettings）。
// 与 tests/OpenCSG.NET.Tests/Parallelism.cs 成对（同一机制实测，摘录见彼处注释）：
//   默认运行 = LevelOfParallelism(1)（等价串行，计数与串行基线对照）；
//   runsettings 的 NumberOfTestWorkers=4 覆盖为 4 worker（Parallelizable(Fixtures) 使并行生效）。
//
// 运行命令（仓库根）：
//   dotnet test tests/OpenCSG.NET.SystemTests -c Release --settings tests/ParallelRun.runsettings
// 不进入 CI 默认（Plan F 决定是否纳入）。
[assembly: Parallelizable(ParallelScope.Fixtures)]
[assembly: LevelOfParallelism(1)]
