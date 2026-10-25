using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ilves.Core;
using UnityEditor;
using UnityEngine;

namespace Ilves.EditorTools
{
    /// <summary>无头内容校验：解析所有章节、检查跳转与可达性，并跑一遍完整流程。</summary>
    public static class ContentCheck
    {
        static readonly string[] Files = { "prologue.json", "chapter01.json", "chapter02.json" };

        public static void Run()
        {
            int errors = 0;
            try
            {
                foreach (var file in Files) errors += CheckChapter(file);
                errors += Playthrough();
                errors += SaveRoundTrip();
            }
            catch (Exception e)
            {
                Debug.LogError("[ILVES-CHECK] EXCEPTION " + e);
                errors++;
            }
            Debug.Log(errors == 0 ? "[ILVES-CHECK] PASS" : "[ILVES-CHECK] FAIL errors=" + errors);
            EditorApplication.Exit(errors == 0 ? 0 : 1);
        }

        static string ContentDir { get { return Path.Combine(Application.streamingAssetsPath, "content"); } }

        static StoryChapter Load(string file)
        {
            var path = Path.Combine(ContentDir, file);
            if (!File.Exists(path)) { Debug.LogError("[ILVES-CHECK] missing " + path); return null; }
            return JsonUtility.FromJson<StoryChapter>(File.ReadAllText(path, Encoding.UTF8));
        }

        static int CheckChapter(string file)
        {
            int errors = 0;
            var ch = Load(file);
            if (ch == null) return 1;
            if (ch.nodes == null || ch.nodes.Length == 0) { Debug.LogError("[ILVES-CHECK] " + file + " 没有节点"); return 1; }

            var ids = new HashSet<string>();
            foreach (var n in ch.nodes)
            {
                if (string.IsNullOrEmpty(n.id)) { Debug.LogError("[ILVES-CHECK] 空节点 id"); errors++; continue; }
                if (!ids.Add(n.id)) { Debug.LogError("[ILVES-CHECK] 重复 id: " + n.id); errors++; }
            }
            if (!ids.Contains(ch.start)) { Debug.LogError("[ILVES-CHECK] start 不存在: " + ch.start); errors++; }

            int ends = 0;
            foreach (var n in ch.nodes)
            {
                if (n.end) ends++;
                if (n.choices != null)
                {
                    foreach (var c in n.choices)
                    {
                        if (string.IsNullOrEmpty(c.text)) { Debug.LogError("[ILVES-CHECK] " + n.id + " 有空选项"); errors++; }
                        if (string.IsNullOrEmpty(c.next) || !ids.Contains(c.next))
                        {
                            Debug.LogError("[ILVES-CHECK] " + n.id + " 选项跳转不存在: " + c.next); errors++;
                        }
                    }
                }
                else if (!n.end)
                {
                    if (string.IsNullOrEmpty(n.next) || !ids.Contains(n.next))
                    {
                        Debug.LogError("[ILVES-CHECK] " + n.id + " next 不存在: " + n.next); errors++;
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
                foreach (var x in ch.nodes) if (x.id == id) { n = x; break; }
                if (n == null) continue;
                if (n.choices != null) foreach (var c in n.choices) if (!string.IsNullOrEmpty(c.next)) stack.Push(c.next);
                if (!string.IsNullOrEmpty(n.next)) stack.Push(n.next);
            }
            foreach (var n in ch.nodes)
            {
                if (!seen.Contains(n.id)) { Debug.LogWarning("[ILVES-CHECK] " + file + " 不可达节点: " + n.id); }
            }
            Debug.Log("[ILVES-CHECK] " + file + " OK nodes=" + ch.nodes.Length + " reachable=" + seen.Count + " ends=" + ends);
            return errors;
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

        static int Playthrough()
        {
            var state = new GameState();
            foreach (var file in Files)
            {
                var ch = Load(file);
                if (ch == null) return 1;
                var engine = new StoryEngine(ch);
                int steps = 0;
                while (engine.Current != null && !engine.Current.end)
                {
                    var choices = engine.VisibleChoices(state);
                    if (choices.Count > 0) engine.Choose(choices[0], state);
                    else engine.GoTo(engine.Current.next);
                    if (++steps > 200) { Debug.LogError("[ILVES-CHECK] 死循环 @" + file); return 1; }
                }
                Debug.Log("[ILVES-CHECK] playthrough " + ch.chapterId + " steps=" + steps + " exposure=" + state.Exposure + " trust=" + state.Trust + " intel=" + state.Intel);
            }
            return 0;
        }
    }
}
