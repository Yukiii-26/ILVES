using System;
using System.Globalization;

namespace Ilves.Core
{
    /// <summary>
    /// 效果 DSL（写在 JSON 的 effects 里）：
    ///   exposure+5 / exposure-3   暴露度（0..100）
    ///   time+15                   消耗分钟
    ///   intel+1                   情报完整度（0..6）
    ///   trust+1                   队伍信任（-5..5）
    ///   flag.beacon_down=true     置标志
    ///   member.igor=受伤          队员状态
    /// </summary>
    public static class EffectParser
    {
        public static void ApplyAll(GameState s, string[] effects)
        {
            if (effects == null || s == null) return;
            for (int i = 0; i < effects.Length; i++) Apply(s, effects[i]);
        }

        public static void Apply(GameState s, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            var e = raw.Trim();

            if (e.StartsWith("flag.", StringComparison.Ordinal))
            {
                var body = e.Substring(5);
                int eq = body.IndexOf('=');
                if (eq > 0)
                {
                    var name = body.Substring(0, eq).Trim();
                    var val = body.Substring(eq + 1).Trim();
                    s.SetFlag(name, val == "true" || val == "1");
                }
                return;
            }

            if (e.StartsWith("member.", StringComparison.Ordinal))
            {
                var body = e.Substring(7);
                int eq = body.IndexOf('=');
                if (eq > 0)
                {
                    var id = body.Substring(0, eq).Trim();
                    var st = body.Substring(eq + 1).Trim();
                    s.SetMemberStatus(id, st);
                }
                return;
            }

            // 数值型：key + signed int
            int sign = 1, split = -1;
            for (int i = 1; i < e.Length; i++)
            {
                if (e[i] == '+' || e[i] == '-') { split = i; sign = e[i] == '-' ? -1 : 1; break; }
            }
            if (split <= 0) return;
            var key = e.Substring(0, split).Trim();
            int value;
            if (!int.TryParse(e.Substring(split + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return;
            value *= sign;

            switch (key)
            {
                case "exposure": s.AddExposure(value); break;
                case "time": s.AddTime(value); break;
                case "intel": s.AddIntel(value); break;
                case "trust": s.AddTrust(value); break;
            }
        }
    }
}
