using System;
using System.Collections.Generic;

namespace Ilves.Core
{
    public class GameState
    {
        public const int StartHour = 2;
        public const int StartMinute = 40;

        public int Exposure;
        public int Minutes;
        public int Intel;
        public int Trust;
        public readonly Dictionary<string, bool> Flags = new Dictionary<string, bool>();
        public readonly Dictionary<string, string> Members = new Dictionary<string, string>();
        public readonly List<string> Notes = new List<string>();

        public static readonly string[] MemberIds =
        {
            "vasili", "igor", "roman", "artem", "semen", "kirill", "dmitri", "nikolai", "pavel"
        };

        public static readonly string[] MemberNames =
        {
            "瓦西里", "伊戈尔", "罗曼", "阿尔乔姆", "谢苗", "基里尔", "德米特里", "尼古拉", "帕维尔"
        };

        public GameState() { Reset(); }

        public void Reset()
        {
            Exposure = 0; Minutes = 0; Intel = 0; Trust = 0;
            Flags.Clear(); Notes.Clear();
            Members.Clear();
            for (int i = 0; i < MemberIds.Length; i++) Members[MemberIds[i]] = "待命";
        }

        public GameState Clone()
        {
            var s = new GameState();
            s.Exposure = Exposure; s.Minutes = Minutes; s.Intel = Intel; s.Trust = Trust;
            foreach (var kv in Flags) s.Flags[kv.Key] = kv.Value;
            foreach (var kv in Members) s.Members[kv.Key] = kv.Value;
            s.Notes.AddRange(Notes);
            return s;
        }

        public void AddExposure(int d) { Exposure = Clamp(Exposure + d, 0, 100); }
        public void AddTime(int d) { Minutes = Math.Max(0, Minutes + d); }
        public void AddIntel(int d) { Intel = Clamp(Intel + d, 0, 6); }
        public void AddTrust(int d) { Trust = Clamp(Trust + d, -5, 5); }
        public void SetFlag(string name, bool v) { Flags[name] = v; }
        public bool GetFlag(string name) { bool v; return Flags.TryGetValue(name, out v) && v; }
        public void SetMemberStatus(string id, string status) { if (Members.ContainsKey(id)) Members[id] = status; }
        public string MemberStatus(string id) { string v; return Members.TryGetValue(id, out v) ? v : "待命"; }

        public void RecordChoice(string note)
        {
            if (!string.IsNullOrEmpty(note)) Notes.Add(note);
        }

        public string ClockText()
        {
            int total = StartHour * 60 + StartMinute + Minutes;
            int h = (total / 60) % 24;
            int m = total % 60;
            return string.Format("{0:00}:{1:00}", h, m);
        }

        public string ExposureLabel()
        {
            if (Exposure < 20) return "低";
            if (Exposure < 45) return "中";
            if (Exposure < 70) return "高";
            return "危急";
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }
}
