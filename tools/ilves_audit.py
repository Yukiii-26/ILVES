#!/usr/bin/env python3
"""ILVES 内容审计（不依赖 Unity）：结构/旗标/文风约束/体量。"""
import json, sys, re, os, collections
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

ROOT = r"D:\UnityProjects\ILVES"
CONTENT = os.path.join(ROOT, "Assets", "Resources", "content")
CHAPTERS = [("prologue.json", "序章", 30, 3800, 4400),
            ("chapter01.json", "第一章", 150, 7200, 8200),
            ("chapter02.json", "第二章", 200, 7000, 8000)]
NOTES = {"谨慎", "激进", "欺骗", "拖延"}
MSTATUS = {"待命","受伤","兴奋","专注","沉默","开火","射击","驾驶","救治","紧张","焦虑","失联","离队"}
FLAG_SET = re.compile(r"^flag\.([A-Za-z0-9_]+)=true$")
FLAG_ANY = re.compile(r"^flag\.")
NUM = re.compile(r"^(exposure|time|intel|trust)([+-])(\d+)$")
MEM = re.compile(r"^member\.([a-z]+)=(.+)$")
MEMBER_IDS = ["vasili","igor","roman","artem","semen","kirill","dmitri","nikolai","pavel"]

errors, warns = [], []
def err(m): errors.append(m)
def warn(m): warns.append(m)

data = {}
for fname, label, tbudget, lo, hi in CHAPTERS:
    path = os.path.join(CONTENT, fname)
    try:
        with open(path, encoding="utf-8") as f:
            data[fname] = json.load(f)
    except Exception as e:
        err("%s JSON 解析失败: %s" % (fname, e))

    ch = data.get(fname)
    if not ch:
        continue
    nodes = ch.get("nodes") or []
    ids = [n.get("id") for n in nodes]
    dup = [k for k, v in collections.Counter(ids).items() if v > 1]
    if dup: err("%s 重复 id: %s" % (fname, dup))
    idset = set(ids)
    if ch.get("start") not in idset: err("%s start 不存在: %s" % (fname, ch.get("start")))
    if not ch.get("briefing"): err("%s 缺 briefing" % fname)
    if not ch.get("objectives"): err("%s 缺 objectives" % fname)

    total_chars = 0
    maxnode = 0; maxnode_id = ""
    decision_nodes = 0
    ends = 0
    for n in nodes:
        nid = n.get("id")
        t = n.get("text") or ""
        total_chars += len(t)
        if len(t) > maxnode: maxnode, maxnode_id = len(t), nid
        if len(t) > 420: warn("%s %s 正文 %d 字 > 420（UI 需滚动）" % (fname, nid, len(t)))
        if len(t) < 40: warn("%s %s 正文仅 %d 字（偏薄）" % (fname, nid, len(t)))
        if n.get("end"): ends += 1
        chs = n.get("choices") or []
        if chs:
            decision_nodes += 1
            if len(chs) > 4: err("%s %s 选项 %d 个 > 4（UI 只画 4 个）" % (fname, nid, len(chs)))
            if len(chs) < 2: warn("%s %s 只有 %d 个选项" % (fname, nid, len(chs)))
            for c in chs:
                ct = c.get("text") or ""
                total_chars += len(ct)
                if not ct.strip(): err("%s %s 空选项" % (fname, nid))
                note = c.get("note")
                if note not in NOTES: err("%s %s note=%r 不在四词表" % (fname, nid, note))
                nxt = c.get("next")
                if not nxt or nxt not in idset: err("%s %s 选项跳转不存在: %s" % (fname, nid, nxt))
                for e in (c.get("effects") or []):
                    if not (FLAG_ANY.match(e) or NUM.match(e) or MEM.match(e)):
                        err("%s %s 效果语法可疑: %s" % (fname, nid, e))
                m = MEM.match(e) if False else None
                for e in (c.get("effects") or []):
                    mm = MEM.match(e)
                    if mm:
                        if mm.group(1) not in MEMBER_IDS: err("%s %s 未知队员 id: %s" % (fname, nid, mm.group(1)))
                        if mm.group(2) not in MSTATUS: warn("%s %s 队员状态词不在表内: %s" % (fname, nid, mm.group(2)))
                for v in (c.get("visibleIf") or []):
                    s = v[1:] if v.startswith("!") else v
                    if not s.startswith("flag."):
                        err("%s %s visibleIf 缺少 flag. 前缀: %s" % (fname, nid, v))
        else:
            if not n.get("end"):
                nxt = n.get("next")
                if not nxt or nxt not in idset: err("%s %s next 不存在: %s" % (fname, nid, nxt))
    if ends == 0: err("%s 没有 end 节点" % fname)

    # 可达性
    seen = set(); stack = [ch.get("start")]
    byid = {n.get("id"): n for n in nodes}
    while stack:
        i = stack.pop()
        if i in seen or i not in byid: continue
        seen.add(i)
        n = byid[i]
        if n.get("next"): stack.append(n["next"])
        for c in (n.get("choices") or []):
            if c.get("next"): stack.append(c["next"])
    unreach = [i for i in ids if i not in seen]
    if unreach: warn("%s 不可达节点: %s" % (fname, unreach))

    # 单路径最大耗时
    memo = {}
    def maxtime(i, stack_guard):
        if i in memo: return memo[i]
        if i not in byid or i in stack_guard: return 0
        stack_guard = stack_guard | {i}
        n = byid[i]
        best = 0
        outs = []
        if n.get("next"): outs.append((n["next"], []))
        for c in (n.get("choices") or []):
            outs.append((c.get("next"), c.get("effects") or []))
        for nxt, eff in outs:
            d = sum(int(e.split("+")[1]) for e in eff if re.match(r"^time\+\d+$", e or ""))
            neg = sum(int(e.split("-")[1]) for e in eff if re.match(r"^time-\d+$", e or ""))
            val = d - neg + maxtime(nxt, stack_guard)
            best = max(best, val)
        memo[i] = best
        return best
    mt = maxtime(ch.get("start"), frozenset())
    mins = total_chars / 300.0
    flagtxt = "OK" if lo <= total_chars <= hi else "超出目标"
    if not (ch.get("clockStart") or "").strip(): warn("%s 缺 clockStart（侧栏时间会跨章累加）" % fname)
    print("[%s] %s nodes=%d 字数=%d (%s, 目标 %d-%d) 决策点=%d maxNode=%d(%s) 单路径最大耗时=%d分 目标上限=%d分 估读=%0.1f分 ends=%d 可达=%d/%d"
          % (label, fname, len(nodes), total_chars, flagtxt, lo, hi, decision_nodes, maxnode, maxnode_id, mt, tbudget, mins, ends, len(seen), len(ids)))
    if mt > tbudget: warn("%s 单路径耗时 %d 分 > 剧情窗口 %d 分" % (fname, mt, tbudget))

# 全局旗标一致性
set_flags = set(); read_flags = set()
for fname, ch in data.items():
    for n in ch.get("nodes") or []:
        for c in (n.get("choices") or []):
            for e in (c.get("effects") or []):
                m = FLAG_SET.match(e)
                if m: set_flags.add(m.group(1))
            for v in (c.get("visibleIf") or []):
                read_flags.add(v[1:] if v.startswith("!") else v)
missing = sorted(r for r in read_flags if r.startswith("flag.") and r[5:] not in set_flags)
if missing: err("visibleIf 引用了从未设置的旗标: %s" % missing)
print("旗标: 设置 %d 个, visibleIf 引用 %d 个" % (len(set_flags), len(read_flags)))

# members.json
mp = os.path.join(CONTENT, "members.json")
if not os.path.exists(mp):
    warn("members.json 不存在（UI 工程师还在做）")
else:
    try:
        with open(mp, encoding="utf-8") as f: mb = json.load(f)
        ms = mb.get("members") or []
        got = [m.get("id") for m in ms]
        if len(ms) != 9: err("members.json 只有 %d 条" % len(ms))
        if got != MEMBER_IDS: err("members.json id 顺序不符: %s" % got)
        for m in ms:
            for k in ("name","callsign","role","roleShort","rank","age","bio","traits"):
                if not (m.get(k) or "").strip(): err("members.json %s 缺 %s" % (m.get("id"), k))
            if not m.get("weapons"): err("members.json %s 缺 weapons" % m.get("id"))
            if not m.get("gear"): err("members.json %s 缺 gear" % m.get("id"))
            if len(m.get("roleShort") or "") > 4: warn("members.json %s roleShort 过长: %s" % (m.get("id"), m.get("roleShort")))
        print("members.json OK %d 人" % len(ms))
    except Exception as e:
        err("members.json 解析失败: %s" % e)

print()
for w in warns: print("WARN  " + w)
for e in errors: print("ERROR " + e)
print("\n结果: %s  (errors=%d warns=%d)" % ("PASS" if not errors else "FAIL", len(errors), len(warns)))
sys.exit(0 if not errors else 1)
