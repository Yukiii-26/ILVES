using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ilves.Core;
using UnityEngine;

namespace Ilves.UI
{
    /// <summary>
    /// ILVES 文字原型（v0.1 demo）。
    /// 全部界面用 IMGUI 运行时绘制：不需要预制体、不需要美术资源、不依赖 TextMeshPro 的中文字库。
    /// 目的：先验证“文字 + 决策 + 博弈”的体验，再谈表现升级。
    /// </summary>
    public class DemoBootstrap : MonoBehaviour
    {
        // ---------- 设计分辨率 ----------
        const float W = 1280f, H = 720f;

        // ---------- 配色（沿用队徽语言） ----------
        static readonly Color Bg = new Color(0.043f, 0.047f, 0.031f);
        static readonly Color Panel = new Color(0.070f, 0.075f, 0.055f);
        static readonly Color Olive = new Color(0.463f, 0.475f, 0.329f);
        static readonly Color OliveDim = new Color(0.306f, 0.314f, 0.216f);
        static readonly Color Bone = new Color(0.847f, 0.851f, 0.800f);
        static readonly Color Dim = new Color(0.541f, 0.549f, 0.478f);
        static readonly Color Danger = new Color(0.639f, 0.290f, 0.227f);

        enum Phase { Title, Story, Debrief }

        Phase _screen = Phase.Title;
        GameState _state = new GameState();
        StoryEngine _engine;

        int _chapterIndex;              // 0 = 序章, 1 = 第一章
        string[] _chapterFiles = { "prologue.json", "chapter01.json" };

        string _chapterTitle = "";
        string _chapterSubtitle = "";

        // 文本打字机
        string _fullText = "";
        float _reveal;
        const float CharsPerSecond = 42f;
        bool _textDone;

        // 选择
        List<StoryChoice> _choices = new List<StoryChoice>();
        int _hoverChoice = -1;

        // 数值反馈
        readonly List<string> _toasts = new List<string>();
        float _toastTimer;

        // 样式
        Font _font;
        GUIStyle _body, _bodySmall, _scene, _speaker, _title, _subtitle, _section, _side, _sideDim, _choiceBig, _verdict;
        bool _stylesReady;

        // =====================================================================
        void Awake()
        {
            Application.targetFrameRate = 60;
            LoadFont();
        }

        System.Collections.IEnumerator ForceWindow()
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            if (!Application.isBatchMode) Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
        }

        void Start()
        {
            if (HasArg("-autoplay-demo")) { AutoPlay(); return; }
            if (HasArg("-debrief")) { StartCoroutine(ForceWindow()); FastForwardToDebrief(); return; }
            StartCoroutine(ForceWindow());

            string jump = ArgValue("-node");
            if (!string.IsNullOrEmpty(jump))
            {
                _state.Reset();
                _chapterIndex = jump.StartsWith("p") ? 0 : 1;
                LoadCurrentChapter();
                if (_engine != null && _engine.Get(jump) != null) _engine.GoTo(jump);
                EnterNode();
                _screen = Phase.Story;
                return;
            }
            _screen = Phase.Title;
        }

        /// <summary>QA 用：一路选第一个选项跑到复盘页，用来检查复盘排版与统计。</summary>
        void FastForwardToDebrief()
        {
            BeginStory();
            int guard = 0;
            while (_screen == Phase.Story && guard++ < 800)
            {
                if (!_textDone) { _reveal = _fullText.Length; _textDone = true; continue; }
                if (_choices != null && _choices.Count > 0) { Select(0); continue; }
                Advance();
            }
            _screen = Phase.Debrief;
        }

        static string ArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith(name + "=")) return args[i].Substring(name.Length + 1);
                if (args[i] == name && i + 1 < args.Length) return args[i + 1];
            }
            return null;
        }

        static bool HasArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++) if (args[i] == name) return true;
            return false;
        }

        /// <summary>无头自检：把两个章节按“永远选第一个选项”跑一遍，只验逻辑不画画。</summary>
        void AutoPlay()
        {
            _state.Reset();
            for (int c = 0; c < _chapterFiles.Length; c++)
            {
                var chapter = LoadChapter(_chapterFiles[c]);
                if (chapter == null) { Debug.LogError("[ILVES-AUTOTEST] FAIL: 无法加载 " + _chapterFiles[c]); Application.Quit(1); return; }
                var engine = new StoryEngine(chapter);
                int steps = 0;
                while (engine.Current != null && !engine.Current.end && steps < 500)
                {
                    var ch = engine.VisibleChoices(_state);
                    if (ch.Count > 0) engine.Choose(ch[0], _state);
                    else engine.GoTo(engine.Current.next);
                    steps++;
                }
                Debug.Log("[ILVES-AUTOTEST] chapter=" + chapter.chapterId + " steps=" + steps + " exposure=" + _state.Exposure + " time=" + _state.Minutes + " flags=" + _state.Flags.Count);
                if (steps >= 500) { Debug.LogError("[ILVES-AUTOTEST] FAIL: 节点闭环（死循环）"); Application.Quit(1); return; }
            }
            Debug.Log("[ILVES-AUTOTEST] PASS");
            Application.Quit(0);
        }

        void LoadFont()
        {
            string[] candidates = { "Microsoft YaHei UI", "Microsoft YaHei", "DengXian", "SimHei", "SimSun", "Segoe UI" };
            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    var f = Font.CreateDynamicFontFromOSFont(candidates[i], 20);
                    if (f != null) { _font = f; break; }
                }
                catch { }
            }
            if (_font == null) Debug.LogWarning("[ILVES] 未找到中文字体，回退默认字体。");
        }

        void BuildStyles()
        {
            if (_stylesReady) return;
            _body = new GUIStyle(GUI.skin.label) { font = _font, fontSize = 22, wordWrap = true, richText = true, alignment = TextAnchor.UpperLeft };
            _body.normal.textColor = Bone;
            _bodySmall = new GUIStyle(_body) { fontSize = 16 };
            _bodySmall.normal.textColor = Dim;
            _scene = new GUIStyle(_body) { fontSize = 15 };
            _scene.normal.textColor = OliveDim;
            _speaker = new GUIStyle(_body) { fontSize = 18, wordWrap = false };
            _speaker.normal.textColor = Olive;
            _title = new GUIStyle(_body) { fontSize = 96, wordWrap = false, alignment = TextAnchor.MiddleCenter };
            _title.normal.textColor = Olive;
            _subtitle = new GUIStyle(_body) { fontSize = 22, wordWrap = false, alignment = TextAnchor.MiddleCenter };
            _subtitle.normal.textColor = Dim;
            _section = new GUIStyle(_body) { fontSize = 13, wordWrap = false };
            _section.normal.textColor = OliveDim;
            _side = new GUIStyle(_body) { fontSize = 16, wordWrap = false };
            _side.normal.textColor = Bone;
            _sideDim = new GUIStyle(_side);
            _sideDim.normal.textColor = Dim;
            _choiceBig = new GUIStyle(_body) { fontSize = 20, wordWrap = true };
            _verdict = new GUIStyle(_body) { fontSize = 22 };
            _verdict.normal.textColor = Olive;
            _stylesReady = true;
        }

        // =====================================================================
        StoryChapter LoadChapter(string file)
        {
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, "content", file);
                if (!File.Exists(path)) { Debug.LogError("[ILVES] 缺少内容文件: " + path); return null; }
                string json = File.ReadAllText(path, Encoding.UTF8);
                return JsonUtility.FromJson<StoryChapter>(json);
            }
            catch (Exception e)
            {
                Debug.LogError("[ILVES] 读取章节失败 " + file + ": " + e.Message);
                return null;
            }
        }

        void BeginStory()
        {
            _state.Reset();
            _chapterIndex = 0;
            LoadCurrentChapter();
            _screen = Phase.Story;
        }

        void LoadCurrentChapter()
        {
            var chapter = LoadChapter(_chapterFiles[_chapterIndex]);
            if (chapter == null)
            {
                _screen = Phase.Debrief;
                return;
            }
            _chapterTitle = chapter.title;
            _chapterSubtitle = chapter.subtitle;
            _engine = new StoryEngine(chapter);
            EnterNode();
        }

        void EnterNode()
        {
            if (_engine == null || _engine.Current == null) { _screen = Phase.Debrief; return; }
            _fullText = _engine.Current.text ?? "";
            _reveal = 0f;
            _textDone = string.IsNullOrEmpty(_fullText);
            _choices = _engine.VisibleChoices(_state);
            _hoverChoice = -1;
        }

        void Advance()
        {
            if (!_textDone) { _reveal = _fullText.Length; _textDone = true; return; }
            if (_choices != null && _choices.Count > 0) return;
            if (_engine == null || _engine.Current == null) return;
            if (_engine.Current.end || string.IsNullOrEmpty(_engine.Current.next))
            {
                EndChapter();
                return;
            }
            _engine.GoTo(_engine.Current.next);
            EnterNode();
        }

        void Select(int index)
        {
            if (_choices == null || index < 0 || index >= _choices.Count) return;
            var c = _choices[index];
            var before = Snapshot();
            var ok = _engine.Choose(c, _state);
            AddToasts(before, _state);
            if (!ok) { EndChapter(); return; }
            EnterNode();
        }

        int[] Snapshot() { return new[] { _state.Exposure, _state.Minutes, _state.Intel, _state.Trust }; }

        void AddToasts(int[] before, GameState now)
        {
            _toastTimer = 3f;
            if (now.Exposure != before[0]) _toasts.Add("暴露 " + Delta(now.Exposure - before[0]));
            if (now.Minutes != before[1]) _toasts.Add("时间 " + Delta(now.Minutes - before[1]) + " 分");
            if (now.Intel != before[2]) _toasts.Add("情报 " + Delta(now.Intel - before[2]));
            if (now.Trust != before[3]) _toasts.Add("信任 " + Delta(now.Trust - before[3]));
            while (_toasts.Count > 4) _toasts.RemoveAt(0);
        }

        static string Delta(int d) { return d > 0 ? "+" + d : d.ToString(); }

        void EndChapter()
        {
            if (_chapterIndex < _chapterFiles.Length - 1)
            {
                _chapterIndex++;
                LoadCurrentChapter();
                return;
            }
            _screen = Phase.Debrief;
        }

        // =====================================================================
        void Update()
        {
            if (_screen != Phase.Story) return;

            if (!_textDone)
            {
                _reveal += Time.unscaledDeltaTime * CharsPerSecond;
                if (_reveal >= _fullText.Length) { _reveal = _fullText.Length; _textDone = true; }
            }

            if (_toastTimer > 0f) { _toastTimer -= Time.unscaledDeltaTime; if (_toastTimer <= 0f) _toasts.Clear(); }

            if (Input.GetKeyDown(KeyCode.F11))
            {
                if (Screen.fullScreen) Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
                else Screen.SetResolution(Screen.currentResolution.width, Screen.currentResolution.height, FullScreenMode.FullScreenWindow);
            }
            if (Input.GetKeyDown(KeyCode.Escape)) { Application.Quit(); return; }
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)) Advance();
            for (int i = 0; i < 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                {
                    if (_textDone && _choices != null && i < _choices.Count) Select(i);
                    break;
                }
            }
        }

        // =====================================================================
        void OnGUI()
        {
            BuildStyles();
            float scale = Mathf.Min(Screen.width / W, Screen.height / H);
            float ox = (Screen.width - W * scale) * 0.5f;
            float oy = (Screen.height - H * scale) * 0.5f;

            Fill(new Rect(0, 0, Screen.width, Screen.height), Bg);
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(ox, oy, 0f), Quaternion.identity, new Vector3(scale, scale, 1f));

            if (_screen == Phase.Title) DrawTitle();
            else if (_screen == Phase.Story) DrawStory();
            else DrawDebrief();

            GUI.matrix = old;
        }

        void DrawTitle()
        {
            Fill(new Rect(0, 0, W, H), Bg);
            Label(new Rect(0, 200, W, 130), "ILVES", _title);
            Label(new Rect(0, 330, W, 40), "猞 猁 小 队", _subtitle);
            Fill(new Rect(W * 0.5f - 60, 390, 120, 1), OliveDim);
            var centered = new GUIStyle(_bodySmall) { alignment = TextAnchor.MiddleCenter };
            Label(new Rect(0, 410, W, 30), "卡累利阿 · 一支九人侦察小队的故事", centered);
            Label(new Rect(0, 470, W, 30), "你是指挥官。你不扣扳机，你下命令。", centered);

            float a = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 2f);
            var c = Olive; c.a = a;
            var st = new GUIStyle(_body) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
            st.normal.textColor = c;
            Label(new Rect(0, 560, W, 30), "按任意键 / 点击 开始", st);
            Label(new Rect(0, 660, W, 24), "v0.1 demo  ·  序章 + 第一章  ·  文本原型（无美术资源）", centered);

            if (Event.current.type == EventType.KeyDown || (Event.current.type == EventType.MouseDown && Event.current.button == 0))
            {
                BeginStory();
                Event.current.Use();
            }
        }

        void DrawStory()
        {
            // 顶栏
            Fill(new Rect(0, 0, W, 54), Panel);
            Label(new Rect(40, 14, 200, 28), "ILVES", _speaker);
            var mid = _chapterIndex == 0 ? "序章 · 入队" : (_chapterTitle + " · " + _chapterSubtitle);
            var cst = new GUIStyle(_bodySmall) { alignment = TextAnchor.MiddleCenter };
            Label(new Rect(240, 18, 800, 24), mid, cst);
            var timeSt = new GUIStyle(_bodySmall) { alignment = TextAnchor.MiddleRight };
            Label(new Rect(1040, 18, 200, 24), "时间 " + _state.ClockText(), timeSt);
            Fill(new Rect(0, 54, W, 1), OliveDim);

            // 正文区
            float bx = 56f, by = 86f, bw = 744f;
            var node = _engine.Current;
            if (node == null) return;

            if (!string.IsNullOrEmpty(node.scene)) Label(new Rect(bx, by, bw, 22), node.scene, _scene);
            if (!string.IsNullOrEmpty(node.speaker)) Label(new Rect(bx, by + 26, bw, 26), node.speaker, _speaker);

            string visible = _textDone ? _fullText : _fullText.Substring(0, Mathf.Clamp(Mathf.FloorToInt(_reveal), 0, _fullText.Length));
            float ty = by + (string.IsNullOrEmpty(node.speaker) ? 26f : 58f);
            Label(new Rect(bx, ty, bw, 420), visible, _body);

            // 选择 / 继续
            float cy = H - 250f;
            if (_textDone && _choices != null && _choices.Count > 0)
            {
                for (int i = 0; i < _choices.Count && i < 4; i++)
                {
                    var r = new Rect(bx, cy + i * 54f, bw, 46f);
                    bool hover = r.Contains(Event.current.mousePosition);
                    DrawChoice(r, i, _choices[i], hover);
                }
            }
            else if (_textDone)
            {
                var hint = "▾  空格 / 点击 继续";
                var st = new GUIStyle(_bodySmall);
                st.normal.textColor = OliveDim;
                Label(new Rect(bx, cy + 8, bw, 24), hint, st);
            }
            else
            {
                var st = new GUIStyle(_bodySmall);
                st.normal.textColor = OliveDim;
                Label(new Rect(bx, cy + 8, bw, 24), "点击 / 空格 跳过", st);
            }

            DrawSidebar();

            // 行为推进
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && !_overChoice)
            {
                Advance();
                Event.current.Use();
            }
            _overChoice = false;
        }

        bool _overChoice;

        void DrawChoice(Rect r, int index, StoryChoice choice, bool hover)
        {
            var bg = hover ? new Color(0.145f, 0.153f, 0.110f) : new Color(0.090f, 0.094f, 0.070f);
            Fill(r, bg);
            Fill(new Rect(r.x, r.y, 3f, r.height), hover ? Olive : OliveDim);
            var n = new GUIStyle(_bodySmall) { fontSize = 14 };
            n.normal.textColor = hover ? Olive : OliveDim;
            Label(new Rect(r.x + 14, r.y + 13, 24, 22), (index + 1).ToString(), n);

            var st = new GUIStyle(_choiceBig);
            st.normal.textColor = hover ? new Color(0.95f, 0.95f, 0.90f) : Bone;
            Label(new Rect(r.x + 44, r.y + 10, r.width - 120, r.height - 14), choice.text, st);

            if (hover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Select(index);
                _overChoice = true;
                Event.current.Use();
            }
        }

        void DrawSidebar()
        {
            float x = 840f, y = 86f, w = 384f;
            Fill(new Rect(x, y, w, H - y - 40f), Panel);
            Fill(new Rect(x, y, 1f, H - y - 40f), OliveDim);

            float cy = y + 16f;
            Label(new Rect(x + 20, cy, w - 40, 20), "小队状态  ·  9 人", _section);
            cy += 26f;

            // 暴露度
            Label(new Rect(x + 20, cy, 120, 20), "暴露度", _sideDim);
            var exSt = new GUIStyle(_side) { alignment = TextAnchor.MiddleRight };
            exSt.normal.textColor = _state.Exposure >= 60 ? Danger : Bone;
            Label(new Rect(x + w - 160, cy, 140, 20), _state.Exposure + "  " + _state.ExposureLabel(), exSt);
            cy += 24f;
            Fill(new Rect(x + 20, cy, w - 40, 6), new Color(0.14f, 0.145f, 0.11f));
            float fillW = (w - 40) * Mathf.Clamp01(_state.Exposure / 100f);
            Fill(new Rect(x + 20, cy, fillW, 6), _state.Exposure >= 60 ? Danger : Olive);
            cy += 22f;

            Label(new Rect(x + 20, cy, 120, 20), "情报完整度", _sideDim);
            var intSt = new GUIStyle(_side) { alignment = TextAnchor.MiddleRight };
            Label(new Rect(x + w - 120, cy, 100, 20), _state.Intel + " / 6", intSt);
            cy += 22f;

            Label(new Rect(x + 20, cy, 120, 20), "队伍信任", _sideDim);
            var trSt = new GUIStyle(_side) { alignment = TextAnchor.MiddleRight };
            Label(new Rect(x + w - 120, cy, 100, 20), (_state.Trust >= 0 ? "+" : "") + _state.Trust, trSt);
            cy += 34f;
            Fill(new Rect(x + 20, cy, w - 40, 1), OliveDim);
            cy += 14f;

            for (int i = 0; i < GameState.MemberIds.Length; i++)
            {
                string id = GameState.MemberIds[i];
                string status = _state.MemberStatus(id);
                var nameSt = new GUIStyle(_side);
                nameSt.normal.textColor = (id == "vasili") ? Olive : Bone;
                Label(new Rect(x + 20, cy, 110, 20), GameState.MemberNames[i] + (id == "vasili" ? " (你)" : ""), nameSt);
                var stSt = new GUIStyle(_side) { alignment = TextAnchor.MiddleRight };
                stSt.normal.textColor = StatusColor(status);
                Label(new Rect(x + 130, cy, w - 170, 20), status, stSt);
                cy += 24f;
            }

            // 数值反馈
            if (_toasts.Count > 0)
            {
                float ty = H - 120f;
                for (int i = 0; i < _toasts.Count; i++)
                {
                    var st = new GUIStyle(_bodySmall);
                    st.normal.textColor = Olive;
                    Label(new Rect(x + 20, ty + i * 20f, w - 40, 20), "+ " + _toasts[i], st);
                }
            }
        }

        static Color StatusColor(string status)
        {
            if (status.Contains("伤")) return Danger;
            if (status.Contains("焦虑") || status.Contains("紧张")) return new Color(0.75f, 0.62f, 0.30f);
            if (status.Contains("失联") || status.Contains("离队")) return new Color(0.55f, 0.55f, 0.50f);
            if (status.Contains("正常") || status.Contains("待命")) return Dim;
            return new Color(0.70f, 0.74f, 0.62f);
        }

        // =====================================================================
        void DrawDebrief()
        {
            Fill(new Rect(0, 0, W, H), Bg);
            Label(new Rect(80, 70, W - 160, 40), "复盘", _section);

            var st = new GUIStyle(_title) { fontSize = 54, alignment = TextAnchor.UpperLeft };
            Label(new Rect(80, 96, W - 160, 70), "任务结束", st);

            var lines = new List<string>();
            lines.Add("时间　" + _state.ClockText() + "　（消耗 " + _state.Minutes + " 分钟）");
            lines.Add("暴露　" + _state.Exposure + " / 100　（" + _state.ExposureLabel() + "）");
            lines.Add("情报　" + _state.Intel + " / 6　　信任　" + (_state.Trust >= 0 ? "+" : "") + _state.Trust);

            if (_state.GetFlag("beacon_down")) lines.Add("目标　信标已失效——你把“不该存在的眼睛”按了下去。");
            else if (_state.GetFlag("beacon_located")) lines.Add("目标　信标已定位，但没能拆掉它。它会继续说话。");
            else lines.Add("目标　未达成。信标还在林子里。");

            if (_state.GetFlag("sokol_rescued")) lines.Add("友军　«Сокол»（猎鹰）小队的两名幸存者被你带了回来。");
            else if (_state.GetFlag("sokol_found")) lines.Add("友军　你找到了«Сокол»的痕迹，但没有带走他们。");
            else lines.Add("友军　没有«Сокол»的消息。");

            float y = 210f;
            foreach (var l in lines)
            {
                var ls = new GUIStyle(_body) { fontSize = 21 };
                ls.normal.textColor = Bone;
                Label(new Rect(80, y, W - 200, 30), l, ls);
                y += 34f;
            }

            Fill(new Rect(80, y + 10, W - 160, 1), OliveDim);
            y += 34f;

            string verdict = Verdict();
            Label(new Rect(80, y, W - 160, 40), verdict, _verdict);
            y += 60f;

            // 决策倾向
            int care = 0, aggr = 0, deceive = 0, delay = 0;
            foreach (var n in _state.Notes)
            {
                if (n == "谨慎") care++;
                else if (n == "激进") aggr++;
                else if (n == "欺骗") deceive++;
                else if (n == "拖延") delay++;
            }
            var ps = new GUIStyle(_bodySmall);
            Label(new Rect(80, y, W - 160, 24), "你的决策倾向：谨慎 " + care + "　激进 " + aggr + "　欺骗 " + deceive + "　拖延 " + delay, ps);
            y += 34f;
            Label(new Rect(80, y, W - 160, 24), "「猞猁准则」：潜伏优先、一击脱离、不追不恋。交火不是目的，而是失败之后的补救。", ps);
            y += 46f;
            Label(new Rect(80, y, W - 160, 24), "第一章结束。第二章《另一支队伍》正在写。", ps);
            y += 60f;

            var hint = new GUIStyle(_bodySmall);
            hint.normal.textColor = OliveDim;
            Label(new Rect(80, H - 60, 600, 24), "Esc 退出　·　R 重新开始", hint);

            if (Input.GetKeyDown(KeyCode.R)) { BeginStory(); }
            if (Input.GetKeyDown(KeyCode.Escape)) Application.Quit();
        }

        string Verdict()
        {
            int score = 0;
            if (_state.GetFlag("beacon_down")) score += 3;
            else if (_state.GetFlag("beacon_located")) score += 1;
            if (_state.GetFlag("sokol_rescued")) score += 3;
            else if (_state.GetFlag("sokol_found")) score += 1;
            if (_state.Exposure < 25) score += 2;
            else if (_state.Exposure > 65) score -= 2;
            score += Mathf.Clamp(_state.Trust, -2, 2);

            if (score >= 8) return "评价　А —— 你像一只猞猁：看见了，记住了，走了。";
            if (score >= 5) return "评价　Б —— 任务完成，代价可控。你能接受这个结果。";
            if (score >= 2) return "评价　В —— 你们回来了，但林子记住了你们。";
            return "评价　Г —— 这一夜，你做了不该做的决定。";
        }

        // =====================================================================
        static void Label(Rect r, string text, GUIStyle style)
        {
            if (string.IsNullOrEmpty(text)) return;
            GUI.Label(r, text, style);
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
