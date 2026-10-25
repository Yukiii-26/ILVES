import json, os, io
ROOT = r"D:\UnityProjects\ILVES"
C = os.path.join(ROOT, "Assets", "Resources", "content")
FILES = [("prologue.json","序章"),("chapter01.json","第一章"),("chapter02.json","第二章")]
BT = chr(96)
rows = {}
order = []
for fn, label in FILES:
    with open(os.path.join(C, fn), encoding="utf-8") as f:
        ch = json.load(f)
    for n in ch["nodes"]:
        for c in (n.get("choices") or []):
            for e in (c.get("effects") or []):
                if e.startswith("flag.") and "=" in e:
                    name, val = e[5:].split("=", 1)
                    if val.strip() != "true": continue
                    if name not in rows:
                        rows[name] = (label, n["id"], c.get("text","").strip(), c.get("note",""))
                        order.append(name)
L = []
L.append("# ILVES · 剧情旗标登记（自动生成）\n")
L.append("> 用途：强连续。写第三~六章 / 复盘 / 结局判定时先查这张表，" + BT + "不要重复发明语义相近的旗标" + BT + "。\n")
L.append("> 生成方式：扫描 " + BT + "Assets/Resources/content/*.json" + BT + " 里所有 " + BT + "effects" + BT + " 中的 " + BT + "flag.x=true" + BT + "；重跑 " + BT + "tools/gen_flag_ledger.py" + BT + " 刷新。\n")
L.append("\n共 **%d** 个旗标。\n\n" % len(order))
L.append("| # | 旗标 | 首设章节 | 首设节点 | 触发它的选项 | 倾向 |\n|---|---|---|---|---|---|\n")
for i, name in enumerate(order, 1):
    label, nid, text, note = rows[name]
    t = text.replace("|", "／").replace("\n", " ")
    if len(t) > 46: t = t[:46] + "…"
    L.append("| %d | " % i + BT + name + BT + " | %s | %s | %s | %s |\n" % (label, nid, t, note))
out = os.path.join(ROOT, "docs", "剧情旗标登记.md")
with io.open(out, "w", encoding="utf-8", newline="\n") as f:
    f.write("".join(L))
print("wrote", out, "flags=", len(order))
