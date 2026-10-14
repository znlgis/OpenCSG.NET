#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""OpenCSG.NET 覆盖率门禁（Plan F / Task 2；spec 2026-10-13-comprehensive-testing-design §8.1）。

口径（阈值为 spec §8.1 写死值）：
  - src/ 整体行覆盖 >= 85%（OVERALL_THRESHOLD）；
  - 五个分组各 >= 90%（GROUPS，规则见下）；
  - 统计口径与 Plan C 收口脚本一致：两个测试项目的 cobertura 报告取**并集**
    （任一套件命中即算覆盖；逐行取 max hits），但聚合方式为**行计数加权**
    （covered/total 行数），而非 Plan C 的「逐类行率再对类取平均」——两种口径
    的对照说明见 Plan F Task 2 报告。

     分组 = 文件名（去 .cs）前缀 **或** 类简单名（去命名空间/嵌套 +）前缀命中即入选
     （规则硬编码于 GROUPS；spec §8.1 给的就是前缀式组名）。
  - SystemTests 采集时排除 [Category("Perf")] 六个墙钟预算用例（coverlet 插桩使其
     预算口径失效；实测归因与干预边界见 SUITES 注释）——覆盖率门禁不兼任计时门禁。

CI 以此命令为准（ci.yml 覆盖率步骤，单命令形态）：
    python tools/coverage-gate.py

等价拆分形态（先采集、后门禁；供工件上传与本地调试，语义与单命令形态相同；
注意 SystemTests 排除 [Category("Perf")] 墙钟预算用例的原因见 SUITES 注释）：
    dotnet test tests/OpenCSG.NET.Tests -c Release -f net8.0 --settings tests/coverage.runsettings --collect:"XPlat Code Coverage" --results-directory artifacts/coverage-gate/Tests
    dotnet test tests/OpenCSG.NET.SystemTests -c Release -f net8.0 --settings tests/coverage.runsettings --collect:"XPlat Code Coverage" --results-directory artifacts/coverage-gate/SystemTests --filter "TestCategory!=Perf"
    python tools/coverage-gate.py --reports artifacts/coverage-gate

判别力自证（不碰真数据：把某组阈值抬到不可能的 0.999，必须 exit 1）：
    python tools/coverage-gate.py --reports artifacts/coverage-gate --min-group View=0.999
    python tools/coverage-gate.py --reports artifacts/coverage-gate --min-overall 0.999

退出码：0 = 通过；1 = 阈值不达标；2 = 运行/采集/解析错误（fail-closed，绝不静默通过）。

TFM 选择（CI 口径说明）：两个测试项目为 net8.0;net10.0 双目标，不指定 -f 会依次跑
两个 TFM ⇒ 覆盖率采集双份（同一份源码、行集合相同，仅增加时长与报告噪声）。CI 门禁
统一 **net8.0**：src/ 是 netstandard2.0 单一产物形态，两 TFM 的行集合一致；coverlet
6.0.4 在 net8.0 为本地/CI 均验证过的口径；时长减半。若将来 CI 两个 TFM 都采集，本
脚本的并集语义（逐行 max hits）天然去重，无需改动。

输出面板刻意只用 ASCII（含控制台代码页差异的场合同样可读）；注释为中文。
"""

import argparse
import os
import shlex
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# ---------------------------------------------------------------- 常量与口径

REPO_ROOT = Path(__file__).resolve().parent.parent

CONFIGURATION = "Release"
TFM = "net8.0"  # 见 module docstring「TFM 选择」
RUNSETTINGS = REPO_ROOT / "tests" / "coverage.runsettings"
SUITES = (
    # (采集目录名, 工程相对路径, dotnet test 追加 --filter 表达式或 None)。
    # Tests 工程**不加筛选**（spec §8.2 第 8 项：GoldenGuardTest 及其伴随用例必须照常执行；
    # 「不筛选」是该条的字面要求，保留原样）。
    ("Tests", "tests/OpenCSG.NET.Tests", None),
    # SystemTests 排除 [Category("Perf")] 六个墙钟预算用例——理由（实测归因，2026-10-14）：
    #   * 该六个用例断言的是「实测 × 5」的墙上时间预算，其口径以**未插桩**执行为前提；
    #   * coverlet 插桩把热循环路径整体拖慢 ≈35×（全套件 11s → 6m27s），预算随即失效：
    #     实测 Project 5k 于插桩下 8367.5ms > 预算 1073.5ms（≈7.8×预算），同类 4 个预算断言变红；
    #   * 同一命令不带 --collect 时 6/6 全绿（9s）——失败是**插桩×预算口径**的组合，非产品缺陷，
    #     更不得放宽预算（用例声明「不得直接放宽预算」）；
    #   * 墙钟回归把关本就属于 Plan E 的 perf 门禁（nightly、非阻塞 PR），覆盖率门禁不兼任计时门禁；
    #   * `TestCategory!=Perf` 经实测保留所有无类别用例（1103/1103 全绿），不会连带排除 GoldenGuardTest
    #     一类的守卫（本过滤只作用于 SystemTests；Tests 工程完全未筛选）。
    ("SystemTests", "tests/OpenCSG.NET.SystemTests", "TestCategory!=Perf"),
)
RESULTS_ROOT = REPO_ROOT / "artifacts" / "coverage-gate"

# 产品代码识别：coverlet 的 filename 形态随采集配置变化（仓库根相对 / 仅文件名 + <sources>，
# 两种均支持，见 normalize_src_path）。统一只认路径中出现该片段者（绝对路径亦可），
# 由此排除 tests/ 与第三方程序集。
SRC_MARKER = "src/OpenCSG.NET/"

OVERALL_LABEL = "src/ overall"
OVERALL_THRESHOLD = 0.85  # spec §8.1：src/ 整体行覆盖 >= 85%

# spec §8.1 五组：(显示名, 前缀, 阈值)。
# 组 = 文件名（去 .cs）前缀命中 **或** 类简单名（去命名空间与嵌套 '+'）前缀命中。
# 例：StlWriteResult 类本体不以 StlWriter 开头，但落在 StlWriter.cs 中（文件名命中）
# ⇒ 计入 StlWriter 组（与 Plan C「StlWriter 全家」口径一致）。若未来出现跨组重叠，
# 同一行会分别计入两组（当前五组前缀两两不相交）。
GROUPS = (
    ("View*", "View", 0.90),
    ("CsgHelpers", "CsgHelpers", 0.90),
    ("StlWriter", "StlWriter", 0.90),
    ("CsgMesh", "CsgMesh", 0.90),
    ("Tessellation", "Tessellation", 0.90),
)

MIN_REPORTS = len(SUITES)  # 默认要求两份报告（两个套件都跑过），--allow-partial 放宽


# ---------------------------------------------------------------- 基础工具


def die(code, message):
    """Operational error（fail-closed）：写到 stderr 后以 code 退出。"""
    print("ERROR: %s" % message, file=sys.stderr)
    sys.exit(code)


def to_milli(threshold):
    """阈值转为千分位整数，比较用整数运算避免浮点边界误判。"""
    return int(round(threshold * 1000))


def pct(covered, lines):
    return (100.0 * covered / lines) if lines else 0.0


def fmt_threshold(threshold):
    """阈值列显示：整数百分比省略小数（90%），否则保留一位（99.9%）。"""
    text = ("%.1f" % (threshold * 100)).rstrip("0").rstrip(".")
    return text + "%"


def normalize_src_path(filename, sources=()):
    """把 coverlet 的 (filename, <sources>) 归一为 'src/OpenCSG.NET/x.cs'；非产品代码返回 None。

    coverlet 的 filename 形态随采集配置变化（本地实测两种，均须支持）：
      a) 仓库根相对：'src\\OpenCSG.NET\\Solid.cs'（不带 runsettings 的默认采集；
         <source> 同时含 tests/ 源 ⇒ 公共根 = 仓库根）；
      b) 仅文件名 + <sources> 指向产品目录：'Solid.cs' + '...\\src\\OpenCSG.NET\\'
         （带 runsettings 的 Include 过滤 ⇒ 只插桩产品程序集，公共根 = src 目录）。
    统一策略：先试原样，再把 filename 与每个 <source> 拼接后试；只认归一化后含
    'src/OpenCSG.NET/' 片段的候选（绝对 / 相对路径皆可）。
    """
    candidates = [filename] + [os.path.join(source, filename) for source in sources if source]
    for candidate in candidates:
        if not candidate:
            continue
        path = candidate.replace(chr(92), "/")  # chr(92) == '\\'，两种分隔符都接受
        idx = path.find(SRC_MARKER)
        if idx < 0:
            continue
        rel = path[idx:]
        if rel.endswith(".cs") and "/obj/" not in rel and "/bin/" not in rel:
            return rel
    return None


# ---------------------------------------------------------------- 采集


def run_suites():
    """默认模式：依次跑两个测试项目并采集覆盖率，产出到 artifacts/coverage-gate/<套件>/。

    每次运行前清掉该套件的结果目录，避免旧 GUID 目录/旧报告混入并集。
    """
    dotnet = shutil.which("dotnet")
    if dotnet is None:
        die(2, "dotnet not found on PATH")
    for name, project, test_filter in SUITES:
        out_dir = RESULTS_ROOT / name
        shutil.rmtree(out_dir, ignore_errors=True)
        cmd = [
            dotnet,
            "test",
            project,
            "-c",
            CONFIGURATION,
            "-f",
            TFM,
            "--settings",
            str(RUNSETTINGS),
            "--collect:XPlat Code Coverage",
            "--results-directory",
            str(out_dir),
        ]
        if test_filter:
            cmd.extend(["--filter", test_filter])
        print("[run] %s" % shlex.join(cmd))
        rc = subprocess.call(cmd, cwd=str(REPO_ROOT))
        if rc != 0:
            # 套件红/构建失败决不允许门禁给出绿灯（覆盖率 XML 可能仍是旧/半成品）。
            die(2, "dotnet test for %s exited with code %d" % (name, rc))
    return RESULTS_ROOT


def find_reports(roots):
    """递归收集给定目录下的 coverage.cobertura.xml（文件或目录均可）。"""
    files = []
    for root in roots:
        path = Path(root)
        if not path.is_absolute():
            path = REPO_ROOT / path
        if path.is_file():
            if path.name != "coverage.cobertura.xml":
                die(2, "not a cobertura report: %s" % path)
            files.append(path)
        elif path.is_dir():
            files.extend(sorted(path.glob("**/coverage.cobertura.xml")))
        else:
            die(2, "reports path not found: %s" % path)
    return files


# ---------------------------------------------------------------- 解析与聚合


def parse_reports(paths):
    """并集解析：{(file, class): {line: max_hits}}。

    行集合以覆盖到的 XML 为准；同一行出现在多个类/多份报告时取 max hits
    （任一套件命中即算覆盖，与 Plan C 收口脚本的并集口径一致）。
    解析失败 = fail-closed（exit 2），绝不把截断/损坏的报告当成 100%。
    """
    classes = {}
    for path in paths:
        try:
            tree = ET.parse(str(path))
        except ET.ParseError as exc:
            die(2, "cannot parse %s: %s" % (path, exc))
        sources = [s.text for s in tree.iter("source") if s.text]
        for cls in tree.iter("class"):
            rel = normalize_src_path(cls.get("filename"), sources)
            if rel is None:
                continue
            key = (rel, cls.get("name", "?"))
            lines = classes.setdefault(key, {})
            for line in cls.iter("line"):
                number = int(line.get("number"))
                hits = int(line.get("hits", 0))
                if hits > lines.get(number, -1):
                    lines[number] = hits
    return classes


def group_prefixes_for(rel, class_name):
    """返回该 (文件, 类) 命中的分组前缀列表（规则见 GROUPS 注释）。"""
    stem = rel.rsplit("/", 1)[-1]
    if stem.endswith(".cs"):
        stem = stem[:-3]
    simple = class_name.replace("+", ".").rsplit(".", 1)[-1]
    return [
        prefix
        for _label, prefix, _threshold in GROUPS
        if stem.startswith(prefix) or simple.startswith(prefix)
    ]


def aggregate(classes):
    """返回 (overall, group_lines, class_stats)。

    overall/group_lines: {(file,line): max_hits}（去重后的行集合）；
    class_stats: [(file, class, lines, covered)]，供类计数加权的参考行（info 行）使用。
    """
    overall = {}
    group_lines = {prefix: {} for _label, prefix, _t in GROUPS}
    class_stats = []
    for (rel, class_name), lines in classes.items():
        if not lines:
            continue  # 无序列点的类（如纯接口/生成物）不参与任何比率
        prefixes = group_prefixes_for(rel, class_name)
        for number, hits in lines.items():
            key = (rel, number)
            if hits > overall.get(key, -1):
                overall[key] = hits
            for prefix in prefixes:
                bucket = group_lines[prefix]
                if hits > bucket.get(key, -1):
                    bucket[key] = hits
        covered = sum(1 for hits in lines.values() if hits > 0)
        class_stats.append((rel, class_name, len(lines), covered))
    return overall, group_lines, class_stats


# ---------------------------------------------------------------- 报表输出


def emit_table(overall, group_lines, class_stats, overall_threshold, thresholds):
    """打印对照表 + 参考口径信息；返回是否全部达标。"""
    rows = []  # (label, lines, covered, threshold, ok)
    for label, prefix, _default in GROUPS:
        lines = group_lines[prefix]
        total = len(lines)
        if total == 0:
            # 规则漂移/采集落空的静默陷阱：组为空必须视为操作错误。
            die(2, "group %s matched 0 lines (filename/class prefix rule drifted?)" % label)
        covered = sum(1 for hits in lines.values() if hits > 0)
        threshold = thresholds[prefix]
        ok = covered * 1000 >= to_milli(threshold) * total
        rows.append((label, total, covered, threshold, ok))

    total_all = len(overall)
    if total_all == 0:
        die(2, "no src/ lines in report(s): collection empty or path marker drifted")
    covered_all = sum(1 for hits in overall.values() if hits > 0)
    overall_ok = covered_all * 1000 >= to_milli(overall_threshold) * total_all
    rows.append((OVERALL_LABEL, total_all, covered_all, overall_threshold, overall_ok))

    print("group          lines   covered      pct    min  result")
    print("-" * 54)
    for label, total, covered, threshold, ok in rows:
        print(
            "%-12s %6d %9d %7.2f%% %5s  %s"
            % (label, total, covered, pct(covered, total), fmt_threshold(threshold), "PASS" if ok else "FAIL")
        )
    print("-" * 54)

    # 参考信息（不参与判定）：类计数加权的行覆盖均值。
    # 注意与 Plan C 收口脚本的细微差异（两轴，见 Task 2 报告）：
    #   * 本行 = 逐类「逐行并集」行率 → 对 88 个类取平均；
    #   * Plan C 脚本 = 逐类「取单套件 max 行率」（coverlet 属性值）→ 对类取平均。
    # 两者都在同一并集证据上，但本行给出的数字系统性略高（并集 ≥ 单套件 max）。
    if class_stats:
        zero = sum(1 for _f, _c, _t, cov in class_stats if cov == 0)
        avg = sum(pct(cov, total) for _f, _c, total, cov in class_stats) / len(class_stats)
        print(
            "info: src classes %d; 0%%-classes %d; class-avg (per-class union line rate) %.1f%%"
            " [informational only]"
            % (len(class_stats), zero, avg)
        )
    return all(ok for _l, _t, _c, _th, ok in rows)


def resolve_thresholds(args):
    """应用 --min-overall / --min-group 覆盖（判别力自证与 CI 调参用）。"""
    overall_threshold = args.min_overall
    if not 0.0 <= overall_threshold <= 1.0:
        die(2, "--min-overall must be within [0, 1]")
    thresholds = {prefix: default for _label, prefix, default in GROUPS}
    for spec in args.min_group:
        if "=" not in spec:
            die(2, "--min-group expects NAME=VALUE, got %r" % spec)
        name, _sep, value_text = spec.partition("=")
        name = name.strip().rstrip("*")  # 允许写 View* 或 View（后者免 shell glob 展开）
        matches = [prefix for _l, prefix, _d in GROUPS if prefix == name]
        if not matches:
            known = ", ".join(prefix for _l, prefix, _d in GROUPS)
            die(2, "unknown group %r for --min-group (known: %s)" % (name, known))
        try:
            value = float(value_text)
        except ValueError:
            die(2, "--min-group value is not a number: %r" % value_text)
        if not 0.0 <= value <= 1.0:
            die(2, "--min-group value must be within [0, 1]: %r" % value_text)
        thresholds[matches[0]] = value
    return overall_threshold, thresholds


def parse_args(argv):
    parser = argparse.ArgumentParser(
        prog="coverage-gate.py",
        description="OpenCSG.NET coverage gate: src/ overall >= 85%, five module groups >= 90%.",
        epilog=(
            "examples:\n"
            "  python tools/coverage-gate.py\n"
            "  python tools/coverage-gate.py --reports artifacts/coverage-gate\n"
            "  python tools/coverage-gate.py --reports artifacts/coverage-gate --min-group View=0.999\n"
        ),
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument(
        "--reports",
        nargs="+",
        metavar="DIR",
        help="analyze existing coverage.cobertura.xml under DIR(s) instead of running the test suites",
    )
    parser.add_argument(
        "--min-overall",
        type=float,
        default=OVERALL_THRESHOLD,
        metavar="F",
        help="override src/ overall threshold (default %.2f)" % OVERALL_THRESHOLD,
    )
    parser.add_argument(
        "--min-group",
        action="append",
        default=[],
        metavar="NAME=F",
        help="override a group threshold (repeatable; NAME as in the table, e.g. View=0.95)",
    )
    parser.add_argument(
        "--allow-partial",
        action="store_true",
        help="allow --reports with fewer than two reports (local single-suite runs only)",
    )
    return parser.parse_args(argv)


# ---------------------------------------------------------------- 主流程


def main(argv):
    args = parse_args(argv)
    overall_threshold, thresholds = resolve_thresholds(args)

    if args.reports:
        reports = find_reports(args.reports)
        if len(reports) < MIN_REPORTS and not args.allow_partial:
            die(
                2,
                "expected at least %d reports (both suites); found %d -- "
                "pass --allow-partial for single-suite local runs" % (MIN_REPORTS, len(reports)),
            )
    else:
        results_root = run_suites()
        print("[gate] coverage reports under %s" % results_root)
        reports = find_reports([results_root])
        if len(reports) < MIN_REPORTS:
            die(2, "collection produced %d report(s); expected at least %d" % (len(reports), MIN_REPORTS))

    classes = parse_reports(reports)
    overall, group_lines, class_stats = aggregate(classes)

    print("reports: %d (cobertura union; line-weighted; src/ only)" % len(reports))
    ok = emit_table(overall, group_lines, class_stats, overall_threshold, thresholds)
    print("GATE %s (exit %d)" % ("PASS" if ok else "FAIL", 0 if ok else 1))
    return 0 if ok else 1


if __name__ == "__main__":
    # 面板为 ASCII；仅错误路径可能出现非 ASCII（防御性转 utf-8，避免代码页异常）。
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.exit(main(sys.argv[1:]))
