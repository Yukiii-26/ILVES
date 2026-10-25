using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ilves.Core;
using UnityEngine;

namespace Ilves.UI
{
    /// <summary>
    /// ILVES 文字原型 v0.2。
    /// 新增：任务简报、退回两步、设置（分辨率/全屏）、暂停菜单、存档/读档、主菜单。
    /// 界面仍为 IMGUI：不依赖预制体与中文字库，先把“指挥—博弈—复盘”的手感做出来。
    /// </summary>
    public class DemoBootstrap : MonoBehaviour
    {
        const float W = 1280f, H = 720f;

        static readonly Color Bg = new Color(0.043f, 0.047f, 0.031f);
        static readonly Color Panel = new Color(0.070f, 0.075f, 0.055f);
        static readonly Color PanelHi = new Color(0.145f, 0.153f, 0.110f);
        static readonly Color Olive = new Color(0.463f, 0.475f, 0.329f);
        static readonly Color OliveDim = new Color(0.306f, 0.314f, 0.216f);
        static readonly Color Bone = new Color(0.847f, 0.851f, 0.800f);
        static readonly Color Dim = new Color(0.541f, 0.549f, 0.478f);
        static readonly Color Danger = new Color(0.639f, 0.290f, 0.227f);

        enum Phase { Title, Settings, Briefing, Story, Pause, Debrief, Slots }
        enum SlotMode { Save, Load }
        static readonly string[] SlotNames = { "存档 01", "存档 02", "存档 03" };

        static readonly string[] ChapterFiles = { "prologue.json", "chapter01.json", "chapter02.json" };
        static readonly int[,] Resolutions = { { 1280, 720 }, { 1920, 1080 }, { 2560, 1440 } };
        static readonly string[] ResLabels = { "1280 × 720", "1920 × 1080", "2560 × 1440" };

        // ---------- 运行时状态 ----------
        GameState _state = new GameState();
        StoryEngine _engine;
        StoryChapter _chapter;
        int _chapterIndex;

        Phase _phase = Phase.Title;
        Phase _settingsReturn = Phase.Title;

        string _fullText;
        float _reveal;
        bool _textDone;
        List<StoryChoice> _choices = new List<StoryChoice>();
        bool _overChoice;

        // 退回（最多两步）
        class Snapshot
        {
            public int chapter;
            public string node;
            public GameState state;
        }
        readonly List<Snapshot> _history = new List<Snapshot>();
        const int MaxHistory = 2;

        // 数值反馈
        readonly List<string> _toasts = new List<string>();
        float _toastTimer;
        string _transient;

        // 设置
        int _resIndex;
        bool _fullscreen;
        string _autoPath;
        bool _hasSave;
        SlotMode _slotMode = SlotMode.Load;
        bool _noSavePrefs;
        Phase _slotReturn = Phase.Title;
        readonly List<SaveData> _slotMeta = new List<SaveData>();

        // 样式
        Font _font;
        GUIStyle _body, _bodySmall, _scene, _speaker, _title, _subtitle, _section, _side, _sideDim, _choiceBig, _verdict, _button, _hint;
        bool _stylesReady;

        // =====================================================================
        void Awake()
        {
            Application.targetFrameRate = 60;
            LoadFont();
            LoadSettings();
            _autoPath = Path.Combine(Application.persistentDataPath, "save_auto.json");
        }

        void Start()
        {
            string resArg = ArgValue("-res");
            if (!string.IsNullOrEmpty(resArg))
            {
                int rv;
                if (int.TryParse(resArg, out rv)) { _resIndex = Mathf.Clamp(rv, 0, 2); _noSavePrefs = true; }
            }

            if (HasArg("-autoplay-demo")) { AutoPlay(); return; }
            if (HasArg("-savetest")) { SaveTest(); return; }
            if (HasArg("-debrief")) { ApplyDisplay(); FastForwardToDebrief(); return; }

            ApplyDisplay();
            _hasSave = HasAnySave();

            string ph = ArgValue("-phase");
            if (ph == "settings") { _settingsReturn = Phase.Title; _phase = Phase.Settings; return; }
            if (ph == "briefing") { BeginNewGame(); return; }
            if (ph == "pause") { JumpTo("n09", 1, Phase.Pause); return; }
            if (ph == "slots")
            {
                JumpTo("n09", 1, Phase.Story);
                SaveTo(SlotPath(0), true);
                OpenSlots(SlotMode.Load, Phase.Title);
                return;
            }

            string jump = ArgValue("-node");
            if (!string.IsNullOrEmpty(jump))
            {
                int idx = jump.StartsWith("p") ? 0 : (jump.StartsWith("m") ? 2 : 1);
                JumpTo(jump, idx, Phase.Story);
            }
        }

        /// <summary>QA 用：直接跳到某一章的某个节点（不写存档、不经过简报）。</summary>
        void JumpTo(string node, int chapterIndex, Phase phase)
        {
            _state.Reset();
            _history.Clear();
            _chapterIndex = Mathf.Clamp(chapterIndex, 0, ChapterFiles.Length - 1);
            _chapter = LoadChapter(_chapterIndex);
            _engine = new StoryEngine(_chapter);
            if (_engine.Get(node) != null) _engine.GoTo(node);
            EnterNode();
            _phase = phase;
        }

        static bool HasArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++) if (args[i] == name) return true;
            return false;
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

        // =====================================================================
        // 设置与显示
        // =====================================================================
        void LoadSettings()
        {
            _resIndex = Mathf.Clamp(PlayerPrefs.GetInt("ilves.res", 0), 0, 2);
            _fullscreen = PlayerPrefs.GetInt("ilves.fullscreen", 0) == 1;
        }

        void ApplyDisplay()
        {
            int w = Resolutions[_resIndex, 0];
            int h = Resolutions[_resIndex, 1];
            int sw = Screen.currentResolution.width;
            int sh = Screen.currentResolution.height;
            int ww = Mathf.Min(w, Mathf.RoundToInt(sw * 0.9f));
            int wh = Mathf.Min(h, Mathf.RoundToInt(sh * 0.9f));
            Screen.SetResolution(_fullscreen ? sw : ww, _fullscreen ? sh : wh,
                _fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            if (!_noSavePrefs)
            {
                PlayerPrefs.SetInt("ilves.res", _resIndex);
                PlayerPrefs.SetInt("ilves.fullscreen", _fullscreen ? 1 : 0);
                PlayerPrefs.Save();
            }
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
        }

        void BuildStyles()
        {
            if (_stylesReady) return;
            _body = new GUIStyle(GUI.skin.label) { font = _font, fontSize = 22, wordWrap = true, richText = true };
            _body.normal.textColor = Bone;
            _bodySmall = new GUIStyle(_body) { fontSize = 16 };
            _bodySmall.normal.textColor = Dim;
            _scene = new GUIStyle(_body) { fontSize = 15 };
            _scene.normal.textColor = OliveDim;
            _speaker = new GUIStyle(_body) { fontSize = 18 };
            _speaker.normal.textColor = Olive;
            _title = new GUIStyle(_body) { fontSize = 96, wordWrap = false, alignment = TextAnchor.MiddleCenter };
            _title.normal.textColor = Olive;
            _subtitle = new GUIStyle(_body) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            _subtitle.normal.textColor = Dim;
            _section = new GUIStyle(_body) { fontSize = 13 };
            _section.normal.textColor = OliveDim;
            _side = new GUIStyle(_body) { fontSize = 16, wordWrap = false };
            _side.normal.textColor = Bone;
            _sideDim = new GUIStyle(_side);
            _sideDim.normal.textColor = Dim;
            _choiceBig = new GUIStyle(_body) { fontSize = 20 };
            _verdict = new GUIStyle(_body) { fontSize = 22 };
            _verdict.normal.textColor = Olive;
            _button = new GUIStyle(_body) { fontSize = 19, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _button.normal.textColor = Bone;
            _hint = new GUIStyle(_bodySmall);
            _hint.normal.textColor = OliveDim;
            _stylesReady = true;
        }

        int[] Stats() { return new[] { _state.Exposure, _state.Minutes, _state.Intel, _state.Trust }; }

        void AddToasts(int[] before)
        {
            _toastTimer = 3f;
            if (_state.Exposure != before[0]) _toasts.Add("暴露 " + Delta(_state.Exposure - before[0]));
            if (_state.Minutes != before[1]) _toasts.Add("时间 " + Delta(_state.Minutes - before[1]) + " 分");
            if (_state.Intel != before[2]) _toasts.Add("情报 " + Delta(_state.Intel - before[2]));
            if (_state.Trust != before[3]) _toasts.Add("信任 " + Delta(_state.Trust - before[3]));
            while (_toasts.Count > 4) _toasts.RemoveAt(0);
        }

        static string Delta(int d) { return d > 0 ? "+" + d : d.ToString(); }

        // =====================================================================
        // 章节与节点
        // =====================================================================
        StoryChapter LoadChapter(int index)
        {
            try
            {
                string file = ChapterFiles[Mathf.Clamp(index, 0, ChapterFiles.Length - 1)];
                string path = Path.Combine(Application.streamingAssetsPath, "content", file);
                if (!File.Exists(path)) { Debug.LogError("[ILVES] 缺少内容文件: " + path); return null; }
                return JsonUtility.FromJson<StoryChapter>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception e)
            {
                Debug.LogError("[ILVES] 读取章节失败: " + e.Message);
                return null;
            }
        }

        void BeginNewGame()
        {
            _state.Reset();
            _history.Clear();
            _chapterIndex = 0;
            EnterChapterBriefing();
        }

        void EnterChapterBriefing()
        {
            _chapter = LoadChapter(_chapterIndex);
            if (_chapter == null) { _phase = Phase.Debrief; return; }
            _engine = new StoryEngine(_chapter);
            _phase = Phase.Briefing;
            AutoSave();
        }

        void StartChapter()
        {
            if (_chapter == null) return;
            _engine = new StoryEngine(_chapter);
            EnterNode();
            _phase = Phase.Story;
        }

        void EnterNode()
        {
            if (_engine == null || _engine.Current == null) { _phase = Phase.Debrief; return; }
            _fullText = _engine.Current.text ?? "";
            _reveal = 0f;
            _textDone = string.IsNullOrEmpty(_fullText);
            _choices = _engine.VisibleChoices(_state);
            _overChoice = false;
        }

        void PushHistory()
        {
            if (_engine == null || _engine.Current == null) return;
            _history.Add(new Snapshot { chapter = _chapterIndex, node = _engine.Current.id, state = _state.Clone() });
            while (_history.Count > MaxHistory) _history.RemoveAt(0);
        }

        void Undo()
        {
            if (_history.Count == 0) return;
            var snap = _history[_history.Count - 1];
            _history.RemoveAt(_history.Count - 1);
            if (snap.chapter != _chapterIndex)
            {
                _chapterIndex = snap.chapter;
                _chapter = LoadChapter(_chapterIndex);
                _engine = new StoryEngine(_chapter);
            }
            _state = snap.state.Clone();
            _engine.GoTo(snap.node);
            EnterNode();
            _phase = Phase.Story;
            _transient = "已退回一步";
        }

        void Advance()
        {
            if (!_textDone) { _reveal = _fullText.Length; _textDone = true; return; }
            if (_choices != null && _choices.Count > 0) return;
            if (_engine == null || _engine.Current == null) return;
            if (_engine.Current.end || string.IsNullOrEmpty(_engine.Current.next)) { EndChapter(); return; }
            PushHistory();
            _engine.GoTo(_engine.Current.next);
            EnterNode();
        }

        void Select(int index)
        {
            if (_choices == null || index < 0 || index >= _choices.Count) return;
            var before = Stats();
            PushHistory();
            var ok = _engine.Choose(_choices[index], _state);
            AddToasts(before);
            if (!ok) { EndChapter(); return; }
            EnterNode();
        }

        void EndChapter()
        {
            if (_chapterIndex < ChapterFiles.Length - 1)
            {
                _chapterIndex++;
                EnterChapterBriefing();
                return;
            }
            _phase = Phase.Debrief;
        }

        // =====================================================================
        // 存档
        // =====================================================================
        string SlotPath(int index) { return Path.Combine(Application.persistentDataPath, "save_slot" + (index + 1) + ".json"); }

        SaveData ReadSlot(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return JsonUtility.FromJson<SaveData>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch { return null; }
        }

        void RefreshSlotMeta()
        {
            _slotMeta.Clear();
            for (int i = 0; i < 3; i++) _slotMeta.Add(ReadSlot(SlotPath(i)));
        }

        bool HasAnySave()
        {
            if (File.Exists(_autoPath)) return true;
            for (int i = 0; i < 3; i++) if (File.Exists(SlotPath(i))) return true;
            return false;
        }

        string LatestSavePath()
        {
            string best = null, bestTime = null;
            var paths = new List<string> { _autoPath, SlotPath(0), SlotPath(1), SlotPath(2) };
            foreach (var p in paths)
            {
                var d = ReadSlot(p);
                if (d == null || string.IsNullOrEmpty(d.savedAt)) continue;
                if (bestTime == null || string.CompareOrdinal(d.savedAt, bestTime) > 0) { best = p; bestTime = d.savedAt; }
            }
            return best;
        }

        void SaveTo(string path, bool silent)
        {
            try
            {
                if (_engine == null || _engine.Current == null) return;
                var data = SaveCodec.Capture(_chapterIndex, _engine.Current.id, _chapter != null ? _chapter.title : "", _state);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
                _hasSave = true;
                if (!silent) _transient = "已保存（" + data.savedAt + "）";
            }
            catch (Exception e)
            {
                Debug.LogError("[ILVES] 保存失败: " + e.Message);
                _transient = "保存失败：" + e.Message;
            }
        }

        void AutoSave()
        {
            if (_engine == null || _engine.Current == null) return;
            SaveTo(_autoPath, true);
        }

        bool LoadFrom(string path)
        {
            try
            {
                var data = ReadSlot(path);
                if (data == null) return false;
                _chapterIndex = Mathf.Clamp(data.chapterIndex, 0, ChapterFiles.Length - 1);
                _chapter = LoadChapter(_chapterIndex);
                if (_chapter == null) return false;
                SaveCodec.Apply(data, _state);
                _history.Clear();
                _engine = new StoryEngine(_chapter);
                if (_engine.Get(data.nodeId) != null) _engine.GoTo(data.nodeId);
                EnterNode();
                _phase = Phase.Story;
                _transient = "已读取存档（" + data.savedAt + "）";
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[ILVES] 读档失败: " + e.Message);
                _transient = "读档失败：" + e.Message;
                return false;
            }
        }

        void ContinueGame()
        {
            string p = LatestSavePath();
            if (p == null || !LoadFrom(p)) BeginNewGame();
        }

        void OpenSlots(SlotMode mode, Phase ret)
        {
            _slotMode = mode;
            _slotReturn = ret;
            RefreshSlotMeta();
            _phase = Phase.Slots;
        }

        // =====================================================================
        // Update
        // =====================================================================
        void Update()
        {
            if (_toastTimer > 0f)
            {
                _toastTimer -= Time.unscaledDeltaTime;
                if (_toastTimer <= 0f) _toasts.Clear();
            }

            if (_phase == Phase.Story)
            {
                if (!_textDone)
                {
                    _reveal += Time.unscaledDeltaTime * 42f;
                    if (_reveal >= _fullText.Length) { _reveal = _fullText.Length; _textDone = true; }
                }
                if (Input.GetKeyDown(KeyCode.Escape)) { _phase = Phase.Pause; return; }
                if (Input.GetKeyDown(KeyCode.Backspace)) Undo();
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
            else if (_phase == Phase.Pause)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) _phase = Phase.Story;
            }
            else if (_phase == Phase.Briefing)
            {
                if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)) StartChapter();
            }
            else if (_phase == Phase.Settings)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) _phase = _settingsReturn;
            }
            else if (_phase == Phase.Slots)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) _phase = _slotReturn;
            }
        }

        // =====================================================================
        // OnGUI
        // =====================================================================
        void OnGUI()
        {
            BuildStyles();
            Fill(new Rect(0, 0, Screen.width, Screen.height), Bg);

            float scale = Mathf.Min(Screen.width / W, Screen.height / H);
            float ox = (Screen.width - W * scale) * 0.5f;
            float oy = (Screen.height - H * scale) * 0.5f;
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(ox, oy, 0f), Quaternion.identity, new Vector3(scale, scale, 1f));

            switch (_phase)
            {
                case Phase.Title: DrawTitle(); break;
                case Phase.Settings: DrawSettings(); break;
                case Phase.Briefing: DrawBriefing(); break;
                case Phase.Story: DrawStory(true); break;
                case Phase.Pause: DrawStory(false); DrawPause(); break;
                case Phase.Debrief: DrawDebrief(); break;
                case Phase.Slots: DrawSlots(); break;
            }

            GUI.matrix = old;
        }

        // ---------- 主界面 ----------
        void DrawTitle()
        {
            Label(new Rect(0, 120, W, 130), "ILVES", _title);
            Label(new Rect(0, 250, W, 40), "猞 猁 小 队", _subtitle);
            Fill(new Rect(W * 0.5f - 60, 306, 120, 1), OliveDim);
            Label(new Rect(0, 322, W, 30), "卡累利阿 · 一支九人侦察小队的故事", Centered(_bodySmall));
            Label(new Rect(0, 356, W, 30), "你是指挥官。你不扣扳机，你下命令。", Centered(_bodySmall));

            float bx = W * 0.5f - 160f, by = 396f;
            if (Button(new Rect(bx, by, 320, 44), "新 游 戏")) BeginNewGame();
            GUI.enabled = _hasSave;
            if (Button(new Rect(bx, by + 52, 320, 44), "继 续 游 戏")) ContinueGame();
            if (Button(new Rect(bx, by + 104, 320, 44), "读 取 存 档")) OpenSlots(SlotMode.Load, Phase.Title);
            GUI.enabled = true;
            if (Button(new Rect(bx, by + 156, 320, 44), "设 置")) { _settingsReturn = Phase.Title; _phase = Phase.Settings; }
            if (Button(new Rect(bx, by + 208, 320, 44), "退 出")) { Application.Quit(); }

            if (!string.IsNullOrEmpty(_transient))
                Label(new Rect(0, 676, W, 24), _transient, Centered(_hint));
            else
                Label(new Rect(0, 676, W, 24), "v0.2 demo  ·  序章 + 第一章 + 第二章", Centered(_hint));
        }

        // ---------- 设置 ----------
        void DrawSettings()
        {
            Label(new Rect(0, 90, W, 60), "设 置", Centered(new GUIStyle(_title) { fontSize = 54 }));
            Label(new Rect(80, 180, 400, 30), "分 辨 率", _section);
            for (int i = 0; i < 3; i++)
            {
                var r = new Rect(80 + i * 380f, 210, 340, 52);
                bool active = _resIndex == i;
                if (Button(r, ResLabels[i], active)) { _resIndex = i; ApplyDisplay(); }
            }

            Label(new Rect(80, 300, 400, 30), "显 示 模 式", _section);
            if (Button(new Rect(80, 330, 340, 52), "窗 口", !_fullscreen)) { _fullscreen = false; ApplyDisplay(); }
            if (Button(new Rect(80 + 380f, 330, 340, 52), "全 屏", _fullscreen)) { _fullscreen = true; ApplyDisplay(); }

            Label(new Rect(80, 420, W - 160, 24), "提示：界面按 16:9 等比缩放；窗口模式会自动限制在屏幕的 90% 以内，避免窗口超出屏幕。", _hint);
            if (Button(new Rect(W * 0.5f - 160f, 560, 320, 46), "返 回")) _phase = _settingsReturn;
        }

        // ---------- 任务简报 ----------
        void DrawBriefing()
        {
            Label(new Rect(80, 70, W - 160, 30), "任 务 简 报", _section);
            Label(new Rect(80, 100, W - 160, 60), _chapter != null ? _chapter.title : "", new GUIStyle(_title) { fontSize = 52, alignment = TextAnchor.UpperLeft });
            Label(new Rect(80, 168, W - 160, 30), _chapter != null ? _chapter.subtitle : "", new GUIStyle(_subtitle) { alignment = TextAnchor.UpperLeft });

            float y = 230f;
            if (!string.IsNullOrEmpty(_chapter.briefing))
            {
                Label(new Rect(80, y, 760, 200), _chapter.briefing, _body);
                y += 190f;
            }

            if (_chapter.objectives != null && _chapter.objectives.Length > 0)
            {
                Label(new Rect(900, 230, 300, 24), "任 务 目 标", _section);
                float oy = 258f;
                for (int i = 0; i < _chapter.objectives.Length; i++)
                {
                    Label(new Rect(900, oy, 320, 24), "▸ " + _chapter.objectives[i], _side);
                    oy += 30f;
                }
            }

            if (Button(new Rect(W * 0.5f - 160f, 600, 320, 48), "开 始 任 务")) StartChapter();
            Label(new Rect(0, 668, W, 24), "空格 / 回车 也可以开始", Centered(_hint));
            if (Input.GetKeyDown(KeyCode.Escape)) _phase = Phase.Title;
        }

        // ---------- 正文 ----------
        void DrawStory(bool interactive)
        {
            Fill(new Rect(0, 0, W, 54), Panel);
            Label(new Rect(40, 14, 200, 28), "ILVES", _speaker);
            string mid = (_chapter != null ? _chapter.title + " · " + _chapter.subtitle : "");
            Label(new Rect(240, 18, 760, 24), mid, Centered(_bodySmall));
            Label(new Rect(1000, 18, 240, 24), "时间 " + _state.ClockText(), Righted(_bodySmall));
            Fill(new Rect(0, 54, W, 1), OliveDim);

            if (interactive && _history.Count > 0)
            {
                if (Button(new Rect(40, 66, 120, 28), "← 退回 (" + _history.Count + ")", false)) Undo();
            }
            Label(new Rect(1000, 70, 240, 24), "ESC 菜单", Righted(_hint));

            var node = _engine != null ? _engine.Current : null;
            if (node == null) return;

            float bx = 56f, by = 96f, bw = 744f;
            if (!string.IsNullOrEmpty(node.scene)) Label(new Rect(bx, by, bw, 22), node.scene, _scene);
            if (!string.IsNullOrEmpty(node.speaker)) Label(new Rect(bx, by + 26, bw, 26), node.speaker, _speaker);

            string visible = _textDone ? _fullText : _fullText.Substring(0, Mathf.Clamp(Mathf.FloorToInt(_reveal), 0, _fullText.Length));
            float ty = by + (string.IsNullOrEmpty(node.speaker) ? 26f : 58f);
            Label(new Rect(bx, ty, bw, 400), visible, _body);

            float cy = H - 250f;
            if (_textDone && _choices != null && _choices.Count > 0)
            {
                for (int i = 0; i < _choices.Count && i < 4; i++)
                    DrawChoice(new Rect(bx, cy + i * 54f, bw, 46f), i, _choices[i], interactive);
            }
            else if (_textDone)
            {
                Label(new Rect(bx, cy + 8, bw, 24), "▾  空格 / 点击 继续", _hint);
            }
            else
            {
                Label(new Rect(bx, cy + 8, bw, 24), "点击 / 空格 跳过", _hint);
            }

            DrawSidebar();

            if (interactive && Event.current.type == EventType.MouseDown && Event.current.button == 0 && !_overChoice)
            {
                Advance();
                Event.current.Use();
            }
        }

        void DrawChoice(Rect r, int index, StoryChoice choice, bool interactive)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            Fill(r, hover ? PanelHi : new Color(0.090f, 0.094f, 0.070f));
            Fill(new Rect(r.x, r.y, 3f, r.height), hover ? Olive : OliveDim);
            Label(new Rect(r.x + 14, r.y + 13, 24, 22), (index + 1).ToString(), new GUIStyle(_hint) { fontSize = 14 });
            var st = new GUIStyle(_choiceBig);
            st.normal.textColor = hover ? new Color(0.95f, 0.95f, 0.90f) : Bone;
            Label(new Rect(r.x + 44, r.y + 10, r.width - 64, r.height - 14), choice.text, st);
            if (interactive && hover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Select(index);
                _overChoice = true;
                Event.current.Use();
            }
        }

        void DrawSidebar()
        {
            float x = 844f, y = 96f, w = 380f;   // 右边界 1224，两侧留白
            float right = x + w - 32f;
            Fill(new Rect(x, y, w, H - y - 40f), Panel);
            Fill(new Rect(x, y, 1f, H - y - 40f), OliveDim);
            Fill(new Rect(x + w - 1f, y, 1f, H - y - 40f), OliveDim);

            float cy = y + 16f;
            Label(new Rect(x + 28, cy, w - 40, 20), "小队状态  ·  9 人", _section);
            cy += 26f;

            Label(new Rect(x + 28, cy, 140, 20), "暴露度", _sideDim);
            var exSt = new GUIStyle(_side) { alignment = TextAnchor.MiddleRight };
            exSt.normal.textColor = _state.Exposure >= 60 ? Danger : Bone;
            Label(new Rect(right - 170, cy, 170, 20), _state.Exposure + "  " + _state.ExposureLabel(), exSt);
            cy += 24f;
            Fill(new Rect(x + 28, cy, w - 60, 6), new Color(0.14f, 0.145f, 0.11f));
            Fill(new Rect(x + 28, cy, (w - 60) * Mathf.Clamp01(_state.Exposure / 100f), 6), _state.Exposure >= 60 ? Danger : Olive);
            cy += 24f;

            Label(new Rect(x + 28, cy, 160, 20), "情报完整度", _sideDim);
            Label(new Rect(right - 110, cy, 110, 20), _state.Intel + " / 6", new GUIStyle(_side) { alignment = TextAnchor.MiddleRight });
            cy += 24f;

            Label(new Rect(x + 28, cy, 160, 20), "队伍信任", _sideDim);
            Label(new Rect(right - 110, cy, 110, 20), (_state.Trust >= 0 ? "+" : "") + _state.Trust, new GUIStyle(_side) { alignment = TextAnchor.MiddleRight });
            cy += 32f;
            Fill(new Rect(x + 28, cy, w - 60, 1), OliveDim);
            cy += 14f;

            for (int i = 0; i < GameState.MemberIds.Length; i++)
            {
                string id = GameState.MemberIds[i];
                string status = _state.MemberStatus(id);
                var nameSt = new GUIStyle(_side);
                nameSt.normal.textColor = (id == "vasili") ? Olive : Bone;
                Label(new Rect(x + 28, cy, 150, 20), GameState.MemberNames[i] + (id == "vasili" ? " (你)" : ""), nameSt);
                var stSt = new GUIStyle(_side) { alignment = TextAnchor.MiddleRight };
                stSt.normal.textColor = StatusColor(status);
                Label(new Rect(right - 150, cy, 150, 20), status, stSt);
                cy += 25f;
            }

            if (_toasts.Count > 0)
            {
                float ty = H - 130f;
                for (int i = 0; i < _toasts.Count; i++)
                {
                    var st = new GUIStyle(_hint);
                    st.normal.textColor = Olive;
                    Label(new Rect(x + 28, ty + i * 20f, w - 40, 20), "+ " + _toasts[i], st);
                }
            }

            if (!string.IsNullOrEmpty(_transient))
                Label(new Rect(x + 28, H - 150f, w - 40, 20), _transient, _hint);
        }

        static Color StatusColor(string status)
        {
            if (status.Contains("伤")) return Danger;
            if (status.Contains("焦虑") || status.Contains("紧张") || status.Contains("专注")) return new Color(0.75f, 0.62f, 0.30f);
            if (status.Contains("失联") || status.Contains("离队") || status.Contains("沉默")) return new Color(0.55f, 0.55f, 0.50f);
            if (status.Contains("开火") || status.Contains("驾驶") || status.Contains("救治")) return Olive;
            return Dim;
        }

        // ---------- 存档档位 ----------
        void DrawSlots()
        {
            bool saving = _slotMode == SlotMode.Save;
            Label(new Rect(80, 56, W - 160, 30), saving ? "保 存 进 度" : "读 取 存 档", _section);
            Label(new Rect(80, 84, W - 160, 60), saving ? "选择档位" : "选择要读取的存档",
                new GUIStyle(_title) { fontSize = 46, alignment = TextAnchor.UpperLeft });
            Label(new Rect(80, 150, W - 160, 24),
                saving ? "当前进度会写入你选择的档位；自动存档（继续游戏用）不受影响。"
                       : "读取会回到存档当时的那个决定，之后的进度作废。", _hint);

            for (int i = 0; i < 3; i++)
            {
                var r = new Rect(80, 192 + i * 118, W - 160, 104);
                var d = i < _slotMeta.Count ? _slotMeta[i] : null;
                Fill(r, Panel);
                Fill(new Rect(r.x, r.y, 3f, r.height), d != null ? Olive : OliveDim);
                Label(new Rect(r.x + 28, r.y + 16, 140, 24), SlotNames[i], _speaker);
                if (d == null)
                {
                    Label(new Rect(r.x + 180, r.y + 16, 520, 24), "（空档位）", _hint);
                    Label(new Rect(r.x + 180, r.y + 50, 520, 22), saving ? "可以保存到此档" : "没有可读取的存档", _hint);
                }
                else
                {
                    Label(new Rect(r.x + 180, r.y + 14, 560, 26), d.chapterTitle + "　·　" + d.savedAt, _side);
                    Label(new Rect(r.x + 180, r.y + 50, 560, 22),
                        "暴露 " + d.exposure + "　时间 " + d.minutes + " 分　情报 " + d.intel + "/6　信任 " + d.trust, _hint);
                }

                var br = new Rect(r.x + r.width - 208, r.y + 28, 180, 46);
                if (saving)
                {
                    if (Button(br, "保 存 到 此")) { SaveTo(SlotPath(i), false); RefreshSlotMeta(); }
                }
                else if (d != null)
                {
                    if (Button(br, "读 取")) { if (LoadFrom(SlotPath(i))) return; }
                }
                else
                {
                    Fill(br, new Color(0.07f, 0.075f, 0.055f));
                    Label(br, "空", Centered(_hint));
                }
            }

            if (Button(new Rect(W * 0.5f - 160f, H - 92, 320, 48), "返 回")) _phase = _slotReturn;
            Label(new Rect(0, H - 36, W, 22), "ESC 返回", Centered(_hint));
        }

        // ---------- 暂停菜单 ----------
        void DrawPause()
        {
            Fill(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, 0.72f));
            var box = new Rect(W * 0.5f - 200f, 96f, 400f, 528f);
            Fill(box, Panel);
            Fill(new Rect(box.x, box.y, 2f, box.height), Olive);
            Label(new Rect(box.x, box.y + 24, box.width, 40), "暂 停", Centered(new GUIStyle(_title) { fontSize = 40 }));

            float bx = box.x + 40f, by = box.y + 108f, bw = box.width - 80f;
            if (Button(new Rect(bx, by, bw, 44), "继 续")) _phase = Phase.Story;
            if (Button(new Rect(bx, by + 54, bw, 44), "保 存 到 档 位")) OpenSlots(SlotMode.Save, Phase.Pause);
            if (Button(new Rect(bx, by + 108, bw, 44), "读 取 存 档")) OpenSlots(SlotMode.Load, Phase.Pause);
            if (Button(new Rect(bx, by + 162, bw, 44), "设 置")) { _settingsReturn = Phase.Pause; _phase = Phase.Settings; }
            if (Button(new Rect(bx, by + 216, bw, 44), "返 回 主 界 面")) { _hasSave = HasAnySave(); _phase = Phase.Title; }
            if (Button(new Rect(bx, by + 270, bw, 44), "退 出 游 戏")) Application.Quit();
            if (!string.IsNullOrEmpty(_transient)) Label(new Rect(bx, by + 326, bw, 24), _transient, Centered(_hint));
            Label(new Rect(bx, by + 352, bw, 24), "ESC 返回游戏", Centered(_hint));
        }

        // ---------- 复盘 ----------
        void DrawDebrief()
        {
            Label(new Rect(80, 60, W - 160, 30), "复 盘", _section);
            Label(new Rect(80, 86, W - 160, 64), "任务结束", new GUIStyle(_title) { fontSize = 50, alignment = TextAnchor.UpperLeft });

            float y = 180f;
            y = Line(80, y, "时间　" + _state.ClockText() + "　（消耗 " + _state.Minutes + " 分钟）");
            y = Line(80, y, "暴露　" + _state.Exposure + " / 100　（" + _state.ExposureLabel() + "）");
            y = Line(80, y, "情报　" + _state.Intel + " / 6　　信任　" + (_state.Trust >= 0 ? "+" : "") + _state.Trust);

            if (_state.GetFlag("beacon_down")) y = Line(80, y, "目标　信标已失效——你把“不该存在的眼睛”按了下去。");
            else if (_state.GetFlag("beacon_located")) y = Line(80, y, "目标　信标已定位，但没能拆掉它。它会继续说话。");

            if (_state.GetFlag("sokol_rescued")) y = Line(80, y, "友军　«Сокол» 的两名幸存者被你带了回来。");
            else if (_state.GetFlag("sokol_found")) y = Line(80, y, "友军　你找到了 «Сокол» 的痕迹，但没有带走他们。");
            if (_state.GetFlag("barasuk_saved")) y = Line(80, y, "友军　«Барсук» 的幸存者也被你带了出来。");
            else if (_state.GetFlag("barasuk_partial")) y = Line(80, y, "友军　你只带走了沃罗宁，其他人自己撤。");
            if (_state.GetFlag("kept_recording") || _state.GetFlag("shared_recording"))
                y = Line(80, y, "证据　你手里有一段不该存在的命令录音。");
            else if (_state.GetFlag("deleted_recording"))
                y = Line(80, y, "证据　录音被删了。你选择忘记。");

            Fill(new Rect(80, y + 8, W - 160, 1), OliveDim);
            y += 30f;
            y = Line(80, y, Verdict(), _verdict);

            int care = 0, aggr = 0, deceive = 0, delay = 0;
            foreach (var n in _state.Notes)
            {
                if (n == "谨慎") care++; else if (n == "激进") aggr++;
                else if (n == "欺骗") deceive++; else if (n == "拖延") delay++;
            }
            y = Line(80, y + 10, "你的决策倾向：谨慎 " + care + "　激进 " + aggr + "　欺骗 " + deceive + "　拖延 " + delay, _bodySmall);

            if (Button(new Rect(80, H - 120, 260, 46), "返 回 主 界 面")) { _hasSave = HasAnySave(); _phase = Phase.Title; }
            if (Button(new Rect(360, H - 120, 220, 46), "再 玩 一 次")) BeginNewGame();
        }

        float Line(float x, float y, string text) { return Line(x, y, text, _body); }

        float Line(float x, float y, string text, GUIStyle style)
        {
            Label(new Rect(x, y, W - 200, 30), text, style);
            return y + 34f;
        }

        string Verdict()
        {
            int score = 0;
            if (_state.GetFlag("beacon_down")) score += 3;
            else if (_state.GetFlag("beacon_located")) score += 1;
            if (_state.GetFlag("sokol_rescued")) score += 3;
            else if (_state.GetFlag("sokol_found")) score += 1;
            if (_state.Exposure < 25) score += 2; else if (_state.Exposure > 65) score -= 2;
            score += Mathf.Clamp(_state.Trust, -2, 2);
            if (score >= 8) return "评价　А —— 你像一只猞猁：看见了，记住了，走了。";
            if (score >= 5) return "评价　Б —— 任务完成，代价可控。你能接受这个结果。";
            if (score >= 2) return "评价　В —— 你们回来了，但林子记住了你们。";
            return "评价　Г —— 这一夜，你做了不该做的决定。";
        }

        // =====================================================================
        // 无头自检
        // =====================================================================
        void AutoPlay()
        {
            _state.Reset();
            for (int c = 0; c < ChapterFiles.Length; c++)
            {
                var chapter = LoadChapter(c);
                if (chapter == null) { Debug.LogError("[ILVES-AUTOTEST] FAIL: 无法加载 " + ChapterFiles[c]); Application.Quit(1); return; }
                var engine = new StoryEngine(chapter);
                int steps = 0;
                while (engine.Current != null && !engine.Current.end && steps < 500)
                {
                    var ch = engine.VisibleChoices(_state);
                    if (ch.Count > 0) engine.Choose(ch[0], _state); else engine.GoTo(engine.Current.next);
                    steps++;
                }
                Debug.Log("[ILVES-AUTOTEST] chapter=" + chapter.chapterId + " steps=" + steps + " exposure=" + _state.Exposure + " time=" + _state.Minutes + " flags=" + _state.Flags.Count);
                if (steps >= 500) { Debug.LogError("[ILVES-AUTOTEST] FAIL: 死循环"); Application.Quit(1); return; }
            }
            Debug.Log("[ILVES-AUTOTEST] PASS");
            Application.Quit(0);
        }

        /// <summary>无头自检：写盘 → 清空内存 → 读盘，验证真实文件 IO。</summary>
        void SaveTest()
        {
            _resIndex = 0; _fullscreen = false;
            JumpTo("n09", 1, Phase.Story);
            _state.AddExposure(9); _state.SetFlag("savetest", true);
            SaveTo(_autoPath, true);
            int exp = _state.Exposure;
            _state.Reset();
            bool ok = LoadFrom(_autoPath);
            bool pass = ok && _state.Exposure == exp && _state.GetFlag("savetest") &&
                        _engine != null && _engine.Current != null && _engine.Current.id == "n09" && _chapterIndex == 1;
            Debug.Log("[ILVES-SAVETEST] " + (pass ? "PASS" : "FAIL") + " exposure=" + _state.Exposure + " node=" + (_engine.Current != null ? _engine.Current.id : "null") + " chapter=" + _chapterIndex);
            Application.Quit(pass ? 0 : 1);
        }

        void FastForwardToDebrief()
        {
            BeginNewGame();
            int guard = 0;
            while (_phase != Phase.Debrief && guard++ < 2000)
            {
                if (_phase == Phase.Briefing) { StartChapter(); continue; }
                if (_phase != Phase.Story) break;
                if (!_textDone) { _reveal = _fullText.Length; _textDone = true; continue; }
                if (_choices != null && _choices.Count > 0) { Select(0); continue; }
                Advance();
            }
            _phase = Phase.Debrief;
        }

        // =====================================================================
        // 绘制原语
        // =====================================================================
        static GUIStyle Centered(GUIStyle s) { var c = new GUIStyle(s) { alignment = TextAnchor.MiddleCenter }; return c; }
        static GUIStyle Righted(GUIStyle s) { var c = new GUIStyle(s) { alignment = TextAnchor.MiddleRight }; return c; }

        bool Button(Rect r, string text, bool active = false)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            Fill(r, active ? new Color(0.18f, 0.19f, 0.13f) : (hover ? PanelHi : new Color(0.090f, 0.094f, 0.070f)));
            Fill(new Rect(r.x, r.y, 2f, r.height), (hover || active) ? Olive : OliveDim);
            var st = new GUIStyle(_button);
            st.normal.textColor = (hover || active) ? new Color(0.95f, 0.95f, 0.90f) : Bone;
            Label(r, text, st);
            if (hover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Event.current.Use();
                return true;
            }
            return false;
        }

        static void Label(Rect r, string text, GUIStyle style)
        {
            if (!string.IsNullOrEmpty(text)) GUI.Label(r, text, style);
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
