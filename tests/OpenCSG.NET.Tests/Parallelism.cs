using NUnit.Framework;

// Plan E / Task 2（L7）：并行整套件（显式 opt-in，见 tests/ParallelRun.runsettings）。
//
// 机制实测（2026-10-14，探针 artifacts/planE/probeParallel，8 个各睡 1.5s 的夹具）：
//   ① 只加 runsettings 是 no-op —— NUnit 的并行必须由代码 opt-in：没有程序集级 Parallelizable
//      属性时 NumberOfTestWorkers 不产生任何并行（实测 12s = 串行）；
//   ② [assembly: Parallelizable(ParallelScope.Fixtures)] 使各夹具之间可并行；
//   ③ [assembly: LevelOfParallelism(1)] 把**默认**运行（不带 --settings）钉成单 worker
//      （实测 12s，等价串行；计数与历史串行基线逐项可比）；runsettings 的
//      NumberOfTestWorkers=4 覆盖该值（实测 3s = 4 worker）⇒ 并行只在传入 runsettings 时发生。
//
// 运行命令（仓库根）：
//   dotnet test tests/OpenCSG.NET.Tests -c Release --settings tests/ParallelRun.runsettings
// 不进入 CI 默认（Plan F 决定是否纳入）。
[assembly: Parallelizable(ParallelScope.Fixtures)]
[assembly: LevelOfParallelism(1)]
