// CPurlin 样例入口：构建几何 → 在当前工作目录写出文本/二进制 STL →（可选）打开查看器。
using System.Diagnostics;
using Csg;
using Runner.CPurlin;

var outputName = "CPurlin";
var outputTextPath = outputName + "-text.stl";
var outputBinPath = outputName + "-bin.stl";

var s = CpurlinModel.Build();

using (var fs = File.Create(outputTextPath))
using (var sw = new StreamWriter(fs))
{
    s.WriteStl(outputName, sw);
}

using (var fs = File.Create(outputBinPath))
using (var bw = new BinaryWriter(fs))
{
    s.WriteStl(outputName, bw);
}

// 环境守卫：默认不打开查看器（CI/无头环境不弹窗），
// 仅当显式设置 OPENCSG_OPEN_VIEWER=1 时用系统默认程序打开文本 STL。
if (Environment.GetEnvironmentVariable("OPENCSG_OPEN_VIEWER") == "1")
{
    Process.Start(new ProcessStartInfo()
    {
        FileName = outputTextPath,
        UseShellExecute = true
    });
}
else
{
    Console.WriteLine($"已生成 {outputTextPath} / {outputBinPath}；设置 OPENCSG_OPEN_VIEWER=1 可打开查看器。");
}
