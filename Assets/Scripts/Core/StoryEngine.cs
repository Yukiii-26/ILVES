using System;
using System.Collections.Generic;

namespace Ilves.Core
{
    /// <summary>纯逻辑的故事推进器：不依赖 UnityEngine，可在编辑器里做无头测试。</summary>
    public class StoryEngine
    {
        readonly Dictionary<string, StoryNode> _map = new Dictionary<string, StoryNode>();
        public StoryChapter Chapter { get; private set; }
        public StoryNode Current { get; private set; }
        public StoryEngine(StoryChapter chapter)
        {
            Chapter = chapter;
            if (chapter == null || chapter.nodes == null) return;
            foreach (var n in chapter.nodes)
            {
                if (n != null && !string.IsNullOrEmpty(n.id)) _map[n.id] = n;
            }
            if (!string.IsNullOrEmpty(chapter.start) && _map.ContainsKey(chapter.start))
                Current = _map[chapter.start];
        }

        public StoryNode Get(string id)
        {
            StoryNode n;
            return !string.IsNullOrEmpty(id) && _map.TryGetValue(id, out n) ? n : null;
        }

        public bool IsEnd { get { return Current == null || Current.end; } }

        public void GoTo(string id)
        {
            var n = Get(id);
            if (n != null) Current = n;
        }

        /// <summary>进入节点时应用节点内联 effect（放在 choices 里则只在选择时应用）。</summary>
        public bool Choose(StoryChoice choice, GameState state)
        {
            if (choice == null) return false;
            EffectParser.ApplyAll(state, choice.effects);
            state.RecordChoice(choice.note);
            if (!string.IsNullOrEmpty(choice.next))
            {
                GoTo(choice.next);
                return true;
            }
            return false;
        }

        public List<StoryChoice> VisibleChoices(GameState state)
        {
            var list = new List<StoryChoice>();
            if (Current == null || Current.choices == null) return list;
            foreach (var c in Current.choices)
            {
                if (c == null || string.IsNullOrEmpty(c.text)) continue;
                if (ConditionPass(c.visibleIf, state)) list.Add(c);
            }
            return list;
        }

        static bool ConditionPass(string[] conds, GameState state)
        {
            if (conds == null || conds.Length == 0) return true;
            foreach (var raw in conds)
            {
                if (string.IsNullOrEmpty(raw)) continue;
                var c = raw.Trim();
                bool neg = c.StartsWith("!");
                if (neg) c = c.Substring(1).Trim();
                // effects 里写 "flag.x=true"，EffectParser 存旗标时剥掉了 "flag." 前缀；
                // visibleIf 兼容两种写法（"flag.x" 与 "x"），否则第一章 n11 之类永远不可见。
                if (c.StartsWith("flag.", StringComparison.Ordinal)) c = c.Substring(5).Trim();
                bool has = state != null && state.GetFlag(c);
                if (neg ? has : !has) return false;
            }
            return true;
        }
    }
}
