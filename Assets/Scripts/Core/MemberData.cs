using System;

namespace Ilves.Core
{
    /// <summary>队员档案（纯数据，不依赖 UnityEngine；由 JsonUtility 从 members.json 反序列化）。</summary>
    [Serializable]
    public class MemberData
    {
        public string id;
        public string name;
        public string callsign;      // 俄语代号，如 «Хозяин»
        public string callsignCn;    // 代号中文，如 当家的
        public string role;          // 岗位全称
        public string roleShort;     // 岗位短称（人物栏用，≤4 字）
        public string rank;          // 军衔
        public string age;           // 年龄 · 服役
        public string[] weapons;     // "主武器　型号｜细节" / "副武器　型号｜细节"
        public string[] gear;        // 随身装备，逐条
        public string bio;           // 档案正文
        public string traits;        // 性格／弱点
    }

    [Serializable]
    public class MemberRoster
    {
        public MemberData[] members;
    }
}
