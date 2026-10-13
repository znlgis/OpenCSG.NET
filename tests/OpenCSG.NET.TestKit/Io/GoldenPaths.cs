using System;
using System.IO;

namespace Csg.Test
{
    /// <summary>
    /// 定位测试 golden 目录。
    /// 既有 SolidTest 用 4 层 Path.GetDirectoryName 从程序集位置反推，在单文件发布、
    /// 自定义输出路径下会静默失效；这里改为自底向上找 csproj 标记文件。
    /// </summary>
    public static class GoldenPaths
    {
        /// <summary>
        /// 从 anchorPath（程序集文件路径或目录路径均可）向上找包含 markerFileName 的目录，
        /// 返回其下 Results/；找不到返回 null。
        /// marker 可参数化，使 SystemTests 等其它测试程序集也能复用。
        /// </summary>
        public static string? ResultsDirectory(string anchorPath, string markerFileName = "OpenCSG.NET.Tests.csproj")
        {
            if (string.IsNullOrEmpty(anchorPath) || string.IsNullOrEmpty(markerFileName))
                return null;

            var start = Directory.Exists(anchorPath) ? anchorPath : Path.GetDirectoryName(anchorPath);
            if (string.IsNullOrEmpty(start))
                return null;

            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, markerFileName)))
                    return Path.Combine(dir.FullName, "Results");
                dir = dir.Parent;
            }
            return null;
        }
    }
}
