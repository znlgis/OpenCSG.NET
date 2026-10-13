#!/usr/bin/env bash
# 把 Results/（含 Views/ 等子目录）下未升级的 golden 残留
# （<Fixture>.<Test>_.stl 或 <Fixture>.<Test>_.json）
# 升级为正式基线（去掉文件名末尾的下划线："Foo_.stl" -> "Foo.stl"；
# 嵌套路径保留子目录："Views/Foo_.json" -> "Views/Foo.json"）。
# 升级即"接受当前输出为正确"——请先对摘要与文件内容逐项核对。
#
# 用法：
#   tools/promote-goldens.sh           列出每个待升级文件的字节数 + 内容摘要，随后交互确认；
#                                      输入 y/Y 才开始升级，其它输入（或 EOF）取消。
#   tools/promote-goldens.sh --yes     跳过交互确认直接升级。仅限已核对过摘要的场合
#                                      （如摘要已写入报告/日志的自动化流程）。
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
results="$repo_root/tests/OpenCSG.NET.Tests/Results"

if [[ ! -d "$results" ]]; then
  echo "找不到 Results 目录：$results" >&2
  exit 1
fi

# 递归收集（Results 顶层 + 所有子目录，如 Tier 4 的 Results/Views/）。
# find -print0 + read -d '' 对含空格的文件名安全；sort -z 与旧 glob 的字典序展开一致，
# 保证预览顺序稳定。globstar 在旧 bash 上有行为差异，故不用。
pending=()
while IFS= read -r -d '' f; do
  pending+=("$f")
done < <(find "$results" -type f \( -name '*_.stl' -o -name '*_.json' \) -print0 | sort -z)

if [[ ${#pending[@]} -eq 0 ]]; then
  echo "没有待升级的 golden 残留。"
  exit 0
fi

# 升级前摘要：字节数 + 内容预览，供人工核对。文件名显示为相对 Results 的路径
# （顶层文件即文件名；嵌套文件含子目录，避免同名歧义）。
#   JSON：首行前 120 字符（多字节 UTF-8 可能在截断处断开，仅作预览）。
#   STL： 前 8 字节 hex（可辨认 ASCII "solid" 或二进制头）+ 三角形数：
#         ASCII 数 "facet normal" 行；binary 按 (字节数 - 84) / 50 推算。
# 只用 wc/head/od/grep，不依赖 python。
summarize() {
  local file="$1" size first_line kind triangles display
  size=$(wc -c < "$file")
  display="${file#"$results"/}"
  case "$file" in
    *.json)
      first_line=$(head -n 1 "$file")
      printf '  %s\n    %s 字节 | 首行前 120 字符: %s\n' "$display" "$size" "${first_line:0:120}"
      ;;
    *.stl)
      local magic
      magic=$(head -c 8 "$file" | od -An -v -tx1 | tr -d ' \n')
      # "736f6c6964" 即 ASCII "solid"：二进制 STL 前 5 字节一般不是可打印文本，
      # 但不能用 "$(head -c 5 ...)" 直接比较——bash 命令替换遇到 NUL 字节会告警。
      if [[ "${magic:0:10}" == "736f6c6964" ]]; then
        kind="ASCII"
        triangles=$(grep -c 'facet normal' "$file" || true)
      else
        kind="binary"
        if (( size >= 84 )); then
          triangles=$(( (size - 84) / 50 ))
        else
          triangles=0
        fi
      fi
      printf '  %s\n    %s 字节 | %s STL | 三角形 %s | 前 8 字节 %s\n' \
        "$display" "$size" "$kind" "$triangles" "$magic"
      ;;
  esac
}

echo "待升级 ${#pending[@]} 个（升级即接受当前输出为正确基线，请逐项核对摘要）："
for f in "${pending[@]}"; do
  summarize "$f"
done

if [[ "${1:-}" != "--yes" ]]; then
  echo
  # read 在 EOF（如 </dev/null）时返回非零，按取消处理而不是让 set -e 中断。
  read -r -p "确认这些输出正确并升级为基线？[y/N] " reply || true
  [[ "$reply" == "y" || "$reply" == "Y" ]] || { echo "已取消。"; exit 1; }
fi

for f in "${pending[@]}"; do
  # ${f%_.stl} / ${f%_.json} 只剥掉全路径末尾的 "_.<ext>"，子目录部分原样保留。
  case "$f" in
    *_.stl)  target="${f%_.stl}.stl" ;;
    *_.json) target="${f%_.json}.json" ;;
  esac
  mv -- "$f" "$target"
  echo "升级 ${f#"$results"/} -> ${target#"$results"/}"
done

echo "完成。接着跑一次全套件确认门禁转绿："
echo "  dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release"
