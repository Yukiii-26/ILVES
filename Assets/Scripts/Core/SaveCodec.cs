using System;
using System.Collections.Generic;

namespace Ilves.Core
{
    [Serializable]
    public class SaveData
    {
        public int version = 1;
        public string savedAt;
        public string chapterTitle;
        public int chapterIndex;
        public string nodeId;
        public int exposure;
        public int minutes;
        public int intel;
        public int trust;
        public string[] flagKeys;
        public bool[] flagValues;
        public string[] memberIds;
        public string[] memberStatus;
        public string[] notes;
    }

    /// <summary>存档编解码（不碰文件系统：路径与读写由表现层负责，保持 Core 可测试）。</summary>
    public static class SaveCodec
    {
        public static SaveData Capture(int chapterIndex, string nodeId, string chapterTitle, GameState s)
        {
            var d = new SaveData
            {
                chapterIndex = chapterIndex,
                nodeId = nodeId,
                chapterTitle = chapterTitle,
                savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                exposure = s.Exposure,
                minutes = s.Minutes,
                intel = s.Intel,
                trust = s.Trust
            };
            d.flagKeys = new string[s.Flags.Count];
            d.flagValues = new bool[s.Flags.Count];
            int i = 0;
            foreach (var kv in s.Flags) { d.flagKeys[i] = kv.Key; d.flagValues[i] = kv.Value; i++; }

            d.memberIds = new string[GameState.MemberIds.Length];
            d.memberStatus = new string[GameState.MemberIds.Length];
            for (int m = 0; m < GameState.MemberIds.Length; m++)
            {
                d.memberIds[m] = GameState.MemberIds[m];
                d.memberStatus[m] = s.MemberStatus(GameState.MemberIds[m]);
            }
            d.notes = s.Notes.ToArray();
            return d;
        }

        public static void Apply(SaveData d, GameState s)
        {
            s.Reset();
            s.Exposure = d.exposure; s.Minutes = d.minutes; s.Intel = d.intel; s.Trust = d.trust;
            if (d.flagKeys != null && d.flagValues != null)
                for (int i = 0; i < d.flagKeys.Length && i < d.flagValues.Length; i++)
                    s.Flags[d.flagKeys[i]] = d.flagValues[i];
            if (d.memberIds != null && d.memberStatus != null)
                for (int i = 0; i < d.memberIds.Length && i < d.memberStatus.Length; i++)
                    s.SetMemberStatus(d.memberIds[i], d.memberStatus[i]);
            if (d.notes != null) s.Notes.AddRange(d.notes);
        }
    }
}
