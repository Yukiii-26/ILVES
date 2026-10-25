using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ilves.Core;
using UnityEditor;
using UnityEngine;

namespace Ilves.EditorTools
{
    /// <summary>
    /// 无头内容校验：解析所有章节、检查跳转与可达性、检查选项规范 / 旗标引用 / 队员档案，
    /// 跑一遍完整流程，最后输出每章体量统计。
    /// 注意：章节 JSON 时常被并行重写，这里的检查一律通用，不针对具体节点 id 或字数写死。
    /// </summary>
    public static class ContentCheck
    {
        static readonly string[] Files = { "prologue.json", "chapter01.json", "chapter02.json" };
        static readonly string[] NoteKinds = { "谨慎", "激进", "欺骗", "拖延" };

        const int MaxTextChars = 420;      // 单节点正文超过此值只提示（正文区可滚动，但太长读着累）
        const int MaxChoices = 4;          // UI 只画 4 个选项
        const float CharsPerMinute = 300f; // 阅读速度估算（字/分钟）

        public static void Run()
        {
            int errors = 0;
            try
            {
                var chapters = new List<StoryChapter>();
                foreach (var file in Files) chapters.Add(Load(file));

                // visibleIf 引用的旗标必须在任意章节 effects 里被设置为 true 过（先把全体旗标收集齐）
                var setFlags = new HashSet<string>();
                foreach (var ch in chapters) CollectSetFlags(ch, setFlags);

                for (int i = 0; i < chapters.Count; i++)
                {
                    if (chapters[i] == null) { errors++; continue; }
                    errors += CheckChapter(Files[i], chapters[i]);
                    errors += CheckChoices(Files[i], chapters[i]);
                    errors += CheckVisibleIf(Files[i], chapters[i], setFlags);
                    CheckTextLength(Files[i], chapters[i]);
                }

                errors += CheckMembers();
                errors += CheckTextPrimitives();
                errors += Playthrough(chapters);
                errors += SaveRoundTrip();
                PrintStats(chapters);
            }
            catch (Exception e)
            {
                Debug.LogError("[ILVES-CHECK] EXCEPTION " + e);
                errors++;
            }
            Debug.Log(errors == 0 ? "[ILVES-CHECK] PASS" : "[ILVES-CHECK] FAIL errors=" + errors);
            EditorApplication.Exit(errors == 0 ? 0 : 1);
        }

        static StoryChapter Load(string file)
        {
            var asset = Resources.Load<TextAsset>("content/" + Path.GetFileNameWithoutExtension(file));
            if (asset != null) return JsonUtility.FromJson<StoryChapter>(asset.text);

            var path = Path.Combine(Application.streamingAssetsPath, "content", file);
            if (!File.Exists(path)) { Debug.LogError("[ILVES-CHECK] missing " + path); return null; }
            return JsonUtility.FromJson<StoryChapter>(File.ReadAllText(path, Encoding.UTF8));
        }

        // ---------- 章节结构：重复 id / 跳转存在 / 结束节点 / 可达性 ----------
        static int CheckChapter(string file, StoryChapter ch)
        {
            int errors = 0;
            if (ch.nodes == null || ch.nodes.Length == 0) { Debug.LogError("[ILVES-CHECK] " + file + " 没有节点"); return 1; }

            var ids = new HashSet<string>();
            foreach (var n in ch.nodes)
            {
                if (n == null) { Debug.LogError("[ILVES-CHECK] " + file + " 有 null 节点"); errors++; continue; }
                if (string.IsNullOrEmpty(n.id)) { Debug.LogError("[ILVES-CHECK] " + file + " 空节点 id"); errors++; continue; }
                if (!ids.Add(n.id)) { Debug.LogError("[ILVES-CHECK] " + file + " 重复 id: " + n.id); errors++; }
            }
            if (!ids.Contains(ch.start)) { Debug.LogError("[ILVES-CHECK] " + file + " start 不存在: " + ch.start); errors++; }

            int ends = 0;
            foreach (var n in ch.nodes)
            {
                if (n == null) continue;
                if (n.end) ends++;
                if (n.choices != null)
                {
                    foreach (var c in n.choices)
                    {
                        if (c == null) { Debug.LogError("[ILVES-CHECK] " + file + " " + n.id + " 有 null 选项"); errors++; continue; }
                        if (string.IsNullOrEmpty(c.text)) { Debug.LogError("[ILVES-CHECK] " + file + " " + n.id + " 有空选项"); errors++; }
                        if (string.IsNullOrEmpty(c.next) || !ids.Contains(c.next))
                        {
                            Debug.LogError("[ILVES-CHECK] " + file + " " + n.id + " 选项跳转不存在: " + c.next); errors++;
                        }
                    }
                }
                else if (!n.end)
                {
                    if (string.IsNullOrEmpty(n.next) || !ids.Contains(n.next))
                    {
                        Debug.LogError("[ILVES-CHECK] " + file + " " + n.id + " next 不存在: " + n.next); errors++;
                    }
                }
            }
            if (ends == 0) { Debug.LogError("[ILVES-CHECK] " + file + " 没有结束节点"); errors++; }

            // 可达性
            var seen = new HashSet<string>();
            var stack = new Stack<string>();
            stack.Push(ch.start);
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (!seen.Add(id)) continue;
                StoryNode n = null;
                foreach (var x in ch.nodes) if (x != null && x.id == id) { n = x; break; }
                if (n == null) continue;
                if (n.choices != null) foreach (var c in n.choices) if (c != null && !string.IsNullOrEmpty(c.next)) stack.Push(c.next);
                if (!string.IsNullOrEmpty(n.next)) stack.Push(n.next);
            }
            foreach (var n in ch.nodes)
            {
                if (n == null) continue;
                if (!seen.Contains(n.id)) { Debug.LogWarning("[ILVES-CHECK] " + file + " 不可达节点: " + n.id); }
            }
            Debug.Log("[ILVES-CHECK] " + file + " OK nodes=" + ch.nodes.Length + " reachable=" + seen.Count + " ends=" + ends);
            return errors;
        }

        // ---------- 选项规范：note 取值 / 选项数量 ----------
        static int CheckChoices(string file, StoryChapter ch)
        {
            int errors = 0;
            foreach (var n in ch.nodes)
            {
                if (n == null || n.choices == null) continue;
                if (n.choices.Length > MaxChoices)
                {
                    Debug.LogError("[ILVES-CHECK] " + file + " 节点 " + n.id + " 有 " + n.choices.Length + " 个选项（UI 只画 " + MaxChoices + " 个）");
                    errors++;
                }
                foreach (var c in n.choices)
                {
                    if (c == null) continue;
                    if (!IsNoteKind(c.note))
                    {
                        Debug.LogError("[ILVES-CHECK] " + file + " 节点 " + n.id + " 的选项 note=\"" + (c.note ?? "") + "\" 不是 " + string.Join("/", NoteKinds) + " 之一");
                        errors++;
                    }
                }
            }
            return errors;
        }

        static bool IsNoteKind(string note)
        {
            if (string.IsNullOrEmpty(note)) return false;
            var n = note.Trim();
            for (int i = 0; i < NoteKinds.Length; i++) if (NoteKinds[i] == n) return true;
            return false;
        }

        // ---------- visibleIf 旗标必须被设置过 ----------
        static void CollectSetFlags(StoryChapter ch, HashSet<string> setFlags)
        {
            if (ch == null || ch.nodes == null) return;
            foreach (var n in ch.nodes)
            {
                if (n == null || n.choices == null) continue;
                foreach (var c in n.choices)
                {
                    if (c == null || c.effects == null) continue;
                    foreach (var e in c.effects)
                    {
                        if (string.IsNullOrEmpty(e)) continue;
                        var s = e.Trim();
                        if (!s.StartsWith("flag.", StringComparison.Ordinal)) continue;
                        int eq = s.IndexOf('=');
                        if (eq <= 5) continue;
                        var name = s.Substring(5, eq - 5).Trim();
                        var val = s.Substring(eq + 1).Trim();
                        if (val == "true" || val == "1") setFlags.Add(name);
                    }
                }
            }
        }

        static int CheckVisibleIf(string file, StoryChapter ch, HashSet<string> setFlags)
        {
            int errors = 0;
            foreach (var n in ch.nodes)
            {
                if (n == null || n.choices == null) continue;
                foreach (var c in n.choices)
                {
                    if (c == null || c.visibleIf == null) continue;
                    foreach (var raw in c.visibleIf)
                    {
                        string flag = NormalizeFlag(raw);
                        if (string.IsNullOrEmpty(flag)) continue;
                        if (!setFlags.Contains(flag))
                        {
                            Debug.LogError("[ILVES-CHECK] " + file + " 节点 " + n.id + " 的 visibleIf 引用了从未设置过的旗标: " + flag
                                           + "（需要某处 effects 里出现 flag." + flag + "=true）");
                            errors++;
                        }
                    }
                }
            }
            return errors;
        }

        static string NormalizeFlag(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            var s = raw.Trim();
            if (s.StartsWith("!")) s = s.Substring(1).Trim();
            if (s.StartsWith("flag.", StringComparison.Ordinal)) s = s.Substring(5).Trim();
            return s;
        }

        // ---------- 正文长度提示（warning，不影响退出码） ----------
        static void CheckTextLength(string file, StoryChapter ch)
        {
            foreach (var n in ch.nodes)
            {
                if (n == null) continue;
                int chars = CountChars(n.text);
                if (chars > MaxTextChars)
                {
                    Debug.LogWarning("[ILVES-CHECK] " + file + " 节点 " + n.id + " 正文 " + chars + " 字（> " + MaxTextChars + "，正文区需滚动，注意阅读节奏）");
                }
            }
        }

        static int CountChars(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int n = 0;
            for (int i = 0; i < s.Length; i++) if (!char.IsWhiteSpace(s[i])) n++;
            return n;
        }

        // ---------- members.json ----------
        static string LoadMembersJson()
        {
            var asset = Resources.Load<TextAsset>("content/members");
            if (asset != null) return asset.text;
            string path = Path.Combine(Application.dataPath, "Resources/content/members.json");
            if (File.Exists(path)) return File.ReadAllText(path, Encoding.UTF8);
            path = Path.Combine(Application.streamingAssetsPath, "content/members.json");
            if (File.Exists(path)) return File.ReadAllText(path, Encoding.UTF8);
            return null;
        }

        static int CheckMembers()
        {
            string json = LoadMembersJson();
            if (json == null)
            {
                Debug.LogError("[ILVES-CHECK] 缺少 members.json（应为 Assets/Resources/content/members.json）");
                return 1;
            }

            MemberRoster roster;
            try { roster = JsonUtility.FromJson<MemberRoster>(json); }
            catch (Exception e) { Debug.LogError("[ILVES-CHECK] members.json 解析失败: " + e.Message); return 1; }
            if (roster == null || roster.members == null)
            {
                Debug.LogError("[ILVES-CHECK] members.json 没有 members 数组");
                return 1;
            }

            int errors = 0;
            var ids = new HashSet<string>();
            for (int i = 0; i < roster.members.Length; i++)
            {
                var m = roster.members[i];
                if (m == null) { Debug.LogError("[ILVES-CHECK] members[" + i + "] 是 null"); errors++; continue; }
                if (string.IsNullOrEmpty(m.id)) { Debug.LogError("[ILVES-CHECK] members[" + i + "] 缺 id"); errors++; continue; }
                if (!ids.Add(m.id)) { Debug.LogError("[ILVES-CHECK] members.json 重复 id: " + m.id); errors++; }
                if (Array.IndexOf(GameState.MemberIds, m.id) < 0)
                {
                    Debug.LogError("[ILVES-CHECK] members.json 出现未知 id: " + m.id + "（GameState.MemberIds 里没有）"); errors++;
                }
                if (string.IsNullOrEmpty(m.role)) { Debug.LogError("[ILVES-CHECK] members.json " + m.id + " 缺 role"); errors++; }
                if (string.IsNullOrEmpty(m.roleShort)) { Debug.LogError("[ILVES-CHECK] members.json " + m.id + " 缺 roleShort"); errors++; }
                else if (m.roleShort.Length > 4) Debug.LogWarning("[ILVES-CHECK] members.json " + m.id + " 的 roleShort 超过 4 字: " + m.roleShort);
                if (m.weapons == null || m.weapons.Length == 0) { Debug.LogError("[ILVES-CHECK] members.json " + m.id + " 缺 weapons"); errors++; }
                else
                {
                    for (int w = 0; w < m.weapons.Length; w++)
                    {
                        if (string.IsNullOrEmpty(m.weapons[w])) { Debug.LogError("[ILVES-CHECK] members.json " + m.id + " 的 weapons[" + w + "] 为空"); errors++; }
                    }
                }
                if (string.IsNullOrEmpty(m.bio)) { Debug.LogError("[ILVES-CHECK] members.json " + m.id + " 缺 bio"); errors++; }
            }

            if (roster.members.Length != GameState.MemberIds.Length)
            {
                Debug.LogError("[ILVES-CHECK] members.json 应为 " + GameState.MemberIds.Length + " 条，实际 " + roster.members.Length + " 条");
                errors++;
            }
            else
            {
                // 顺序也要与 GameState.MemberIds 一致（人物栏按同一顺序排）
                for (int i = 0; i < GameState.MemberIds.Length; i++)
                {
                    var m = roster.members[i];
                    if (m == null || m.id != GameState.MemberIds[i])
                    {
                        Debug.LogError("[ILVES-CHECK] members.json 第 " + (i + 1) + " 条应为 " + GameState.MemberIds[i] + "，实际 " + (m != null ? m.id : "null"));
                        errors++;
                    }
                }
            }
            foreach (var expected in GameState.MemberIds)
            {
                if (!ids.Contains(expected)) { Debug.LogError("[ILVES-CHECK] members.json 缺少队员: " + expected); errors++; }
            }

            if (errors == 0) Debug.Log("[ILVES-CHECK] members.json OK members=" + roster.members.Length);
            return errors;
        }

        // ---------- 存档往返 ----------
        /// <summary>
        /// 源码 lint：所有文字必须走 Label() / LabelClipH()（这两个原语会用 CalcHeight 自动加高，
        /// 所以无论分辨率/字号都不会把文字下沿切掉）。裸 GUI.Label 一律报错。
        /// 原语自己那三行带 [text-primitive] 标记，是唯一豁免。
        /// </summary>
        static int CheckTextPrimitives()
        {
            const string uiPath = "Assets/Scripts/UI/DemoBootstrap.cs";
            if (!File.Exists(uiPath))
            {
                Debug.LogWarning("[ILVES-CHECK] 找不到 " + uiPath + "，跳过文字原语检查");
                return 0;
            }
            int bad = 0;
            var lines = File.ReadAllLines(uiPath, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains("GUI.Label(") && !line.Contains("[text-primitive]"))
                {
                    Debug.LogError("[ILVES-CHECK] 第 " + (i + 1) + " 行是裸 GUI.Label，必须改用 Label()/LabelClipH()（否则文字会被矩形下沿裁掉）: " + line.Trim());
                    bad++;
                }
            }
            if (bad == 0) Debug.Log("[ILVES-CHECK] 文字原语 OK（所有文字都走 Label/LabelClipH 自动加高）");
            return bad;
        }

        static int SaveRoundTrip()
        {
            var s = new GameState();
            s.AddExposure(17); s.AddTime(33); s.AddIntel(2); s.AddTrust(-1);
            s.SetFlag("beacon_down", true);
            s.SetMemberStatus("igor", "受伤");
            s.RecordChoice("谨慎");
            var data = SaveCodec.Capture(1, "n05", "第一章", s);
            var json = JsonUtility.ToJson(data);
            var back = JsonUtility.FromJson<SaveData>(json);
            var s2 = new GameState();
            SaveCodec.Apply(back, s2);
            bool ok = s2.Exposure == s.Exposure && s2.Minutes == s.Minutes && s2.Intel == s.Intel &&
                      s2.Trust == s.Trust && s2.GetFlag("beacon_down") && s2.MemberStatus("igor") == "受伤" &&
                      s2.Notes.Count == s.Notes.Count && back.nodeId == "n05" && back.chapterIndex == 1;
            if (!ok) Debug.LogError("[ILVES-CHECK] 存档往返失败");
            else Debug.Log("[ILVES-CHECK] save round-trip OK");
            return ok ? 0 : 1;
        }

        // ---------- 完整流程 ----------
        static int Playthrough(List<StoryChapter> chapters)
        {
            var state = new GameState();
            for (int i = 0; i < chapters.Count; i++)
            {
                var ch = chapters[i];
                if (ch == null) return 1;
                var engine = new StoryEngine(ch);
                int steps = 0;
                while (engine.Current != null && !engine.Current.end)
                {
                    var choices = engine.VisibleChoices(state);
                    if (choices.Count > 0) engine.Choose(choices[0], state);
                    else engine.GoTo(engine.Current.next);
                    if (++steps > 200) { Debug.LogError("[ILVES-CHECK] 死循环 @" + ch.chapterId); return 1; }
                }
                Debug.Log("[ILVES-CHECK] playthrough " + ch.chapterId + " steps=" + steps + " exposure=" + state.Exposure + " trust=" + state.Trust + " intel=" + state.Intel);
            }
            return 0;
        }

        // ---------- 体量统计（Lead 用来核对章节长度） ----------
        static void PrintStats(List<StoryChapter> chapters)
        {
            int totalNodes = 0, totalChars = 0, totalDecisions = 0;
            for (int i = 0; i < chapters.Count; i++)
            {
                var ch = chapters[i];
                if (ch == null) continue;
                int nodes = 0, chars = 0, decisions = 0, options = 0;
                if (ch.nodes != null)
                {
                    foreach (var n in ch.nodes)
                    {
                        if (n == null) continue;
                        nodes++;
                        chars += CountChars(n.text);
                        if (n.choices != null && n.choices.Length > 0) { decisions++; options += n.choices.Length; }
                    }
                }
                totalNodes += nodes;
                totalChars += chars;
                totalDecisions += decisions;
                string name = string.IsNullOrEmpty(ch.title) ? ch.chapterId : ch.title;
                Debug.Log("[ILVES-CHECK] stats " + Files[i] + " (" + name + ") nodes=" + nodes
                          + " text=" + chars + " 字 ≈ " + (chars / CharsPerMinute).ToString("F1") + " 分钟"
                          + " decisions=" + decisions + " options=" + options);
            }
            Debug.Log("[ILVES-CHECK] stats TOTAL nodes=" + totalNodes + " text=" + totalChars
                      + " 字 ≈ " + (totalChars / CharsPerMinute).ToString("F1") + " 分钟 decisions=" + totalDecisions);
        }
    }
}
