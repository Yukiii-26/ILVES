using System;

namespace Ilves.Core
{
    /// <summary>一章的故事数据（从 StreamingAssets/content/*.json 反序列化）。</summary>
    [Serializable]
    public class StoryChapter
    {
        public string chapterId;
        public string title;
        public string subtitle;
        public string start;
        public StoryNode[] nodes;
    }

    /// <summary>一个决策节点：一段文本 + 若干选择，或一个 next（“继续”）。</summary>
    [Serializable]
    public class StoryNode
    {
        public string id;
        public string scene;     // 场景抬头，如「运输车 · 02:40」
        public string speaker;   // 说话人；空 = 旁白
        public string text;
        public StoryChoice[] choices;
        public string next;      // 无选择时点击继续
        public bool end;         // 本章结束
    }

    [Serializable]
    public class StoryChoice
    {
        public string text;
        public string next;
        public string[] effects;  // 见 EffectParser
        public string note;       // 战术倾向标签：谨慎 / 激进 / 欺骗 / 拖延
        public string[] visibleIf; // 条件：flag.x / !flag.x（全部满足才显示）
    }
}
