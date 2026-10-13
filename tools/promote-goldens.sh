#!/usr/bin/env bash
# 把 Results/ 下未升级的 golden 残留（<Fixture>.<Test>_.stl）升级为正式基线。
# 升级即"接受当前输出为正确"——请先逐个确认 *_ .stl 的内容确实正确。
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
results="$repo_root/tests/OpenCSG.NET.Tests/Results"

if [[ ! -d "$results" ]]; then
  echo "找不到 Results 目录：$results" >&2
  exit 1
fi

shopt -s nullglob
pending=("$results"/*_.stl)
shopt -u nullglob

if [[ ${#pending[@]} -eq 0 ]]; then
  echo "没有待升级的 golden 残留。"
  exit 0
fi

echo "待升级 ${#pending[@]} 个："
for f in "${pending[@]}"; do
  echo "  $(basename "$f")"
done

if [[ "${1:-}" != "--yes" ]]; then
  echo
  read -r -p "确认这些输出正确并升级为基线？[y/N] " reply
  [[ "$reply" == "y" || "$reply" == "Y" ]] || { echo "已取消。"; exit 1; }
fi

for f in "${pending[@]}"; do
  target="${f%_.stl}.stl"
  mv -- "$f" "$target"
  echo "升级 $(basename "$f") -> $(basename "$target")"
done

echo "完成。接着跑一次全套件确认门禁转绿："
echo "  dotnet test tests/OpenCSG.NET.Tests/OpenCSG.NET.Tests.csproj -c Release"
