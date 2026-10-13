using System.Runtime.CompilerServices;

// Task 7 的 Tier 1 属性测试（tests/OpenCSG.NET.SystemTests/Property/Tier1BooleanIdentityTest.cs）
// 需要复用 Task 3 规范里为 internal 的 SplitMix64（FsCheck 整数 → spec 的确定性映射）。
// TestKit.csproj 冻结不可改（无法用 <InternalsVisibleTo> MSBuild 项声明），
// 故在此以装配体特性最小化授权：只放给 SystemTests，不把任何 internal 类型改成 public。
[assembly: InternalsVisibleTo("OpenCSG.NET.SystemTests")]
