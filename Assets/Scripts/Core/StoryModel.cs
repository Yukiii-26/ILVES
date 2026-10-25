using System;

namespace Ilves.Core
{
    [Serializable]
    public class StoryChapter
    {
        public string chapterId;
        public string title;
        public string subtitle;
        public string briefing;      // 任务目标说明（简报页）
        public string[] objectives;  // 目标清单（简报页逐条显示）
        public string start;
        public StoryNode[] nodes;
    }

    [Serializable]
    public class StoryNode
    {
        public string id;
        public string scene;
        public string speaker;
        public string text;
        public StoryChoice[] choices;
        public string next;
        public bool end;
    }

    [Serializable]
    public class StoryChoice
    {
        public string text;
        public string next;
        public string[] effects;
        public string note;
        public string[] visibleIf;
    }
}
