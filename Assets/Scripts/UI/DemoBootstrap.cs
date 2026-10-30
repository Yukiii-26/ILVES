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

        enum Phase { Notice, Declaration, Title, Settings, Briefing, Story, Pause, Debrief, Slots }
        enum SlotMode { Save, Load }
        enum SettingsTab { Display, Audio, About }

        // 存档档位数量：界面按 2 列 × 3 行排。加档位只改这个常量 + SlotNames 即可。
        const int SlotCount = 6;
        static readonly string[] SlotNames = { "存档 01", "存档 02", "存档 03", "存档 04", "存档 05", "存档 06" };

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

        // 章节基准钟点（剧情里的「03:10 / 15:20」由 clockStart 提供，分钟数仍存在 GameState）
        bool _hasChapterClock;
        int _clockBaseMin;
        int _minuteBase;

        // 正文分页（PaginateText + MeasureText）/ 队员档案弹窗（滚轮滚动，按真实行距对齐整行）
        readonly List<string> _pages = new List<string>();
        int _page;
        bool _pagesDirty;
        bool _profileOpen;
        string _profileId;
        float _profileScroll;
        readonly List<MemberData> _members = new List<MemberData>();
        bool _membersLoaded;
        string _membersError;

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
        int _slotDeleteArmed = -1;   // 删除需要点两次确认
        bool _noSavePrefs;
        bool _freezeTyping;   // QA：冻结打字机，便于检查提示行等静态文字
        int _fontLevel = 1;                 // 0=小 1=标准 2=大
        static readonly int[] SideSizes = { 16, 18, 21 };
        static readonly int[] RowHeights = { 24, 27, 32 };
        static readonly int[] SectionSizes = { 13, 14, 16 };
        static readonly int[] HintSizes = { 14, 16, 18 };
        static readonly int[] SpeakerSizes = { 17, 18, 20 };
        int _sideFontSize = 18;
        int _sideRowH = 27;
        Phase _slotReturn = Phase.Title;
        readonly List<SaveData> _slotMeta = new List<SaveData>();

        // 音乐：主界面一首，游戏内两首按章节轮流；档位见 MusicLevels
        AudioSource _music;
        AudioClip _musicTitle, _musicStory1, _musicStory2;
        string _musicKey;        // 当前在放的曲目 key（"title" / "story-1" / "story-2"）
        int _musicLevel = 2;     // 0 关 / 1 低 / 2 中 / 3 高
        static readonly float[] MusicLevels = { 0f, 0.28f, 0.55f, 0.85f };
        static readonly string[] MusicLevelLabels = { "关", "低", "中", "高" };

        SettingsTab _settingsTab = SettingsTab.Display;
        Phase _declReturn = Phase.Title;   // 「声明」是从主界面还是设置里打开的
        float _declScroll;

        Texture2D _emblem;
        Texture2D _titleBg;      // 主界面背景图（负责人提供的关键美术，铺满后压暗）
        Texture2D _titleLogo;    // 标题艺术字（ILVES 字标，透明底）
        Texture2D _titleScrim;   // 运行时生成的左侧横向渐变，保证左栏文字在任何背景上都可读

        // 样式
        Font _font;
        GUIStyle _body, _bodySmall, _scene, _speaker, _title, _subtitle, _section, _side, _sideDim, _profileBody, _profileMeta, _choiceBig, _verdict, _button, _hint;
        bool _stylesReady;

        // =====================================================================
        void Awake()
        {
            Application.targetFrameRate = 60;
            LoadFont();
            var sprite = Resources.Load<Sprite>("emblem");
            _emblem = sprite != null ? sprite.texture : Resources.Load<Texture2D>("emblem");
            _titleBg = Resources.Load<Texture2D>("art/title-bg");
            _titleLogo = Resources.Load<Texture2D>("art/title-logo");
            _titleScrim = MakeLeftScrim();
            if (_titleLogo == null) Debug.LogWarning("[ILVES] 缺少 art/title-logo，主界面退回纯文字标题");
            if (_titleBg == null) Debug.LogWarning("[ILVES] 缺少 art/title-bg，主界面退回纯色底");
            SetupMusic();
            LoadSettings();
            EnsureMembers();
            _autoPath = Path.Combine(Application.persistentDataPath, "save_auto.json");
        }

        void Start()
        {
            if (HasArg("-typing")) { _freezeTyping = true; _fontLevel = 2; }
            if (HasArg("-bigfont")) _fontLevel = 2;
            string fontArg = ArgValue("-font");
            if (!string.IsNullOrEmpty(fontArg)) { int fv; if (int.TryParse(fontArg, out fv)) _fontLevel = Mathf.Clamp(fv, 0, 2); }

            string resArg = ArgValue("-res");
            if (!string.IsNullOrEmpty(resArg))
            {
                int rv;
                if (int.TryParse(resArg, out rv)) { _resIndex = Mathf.Clamp(rv, 0, 2); _fullscreen = false; _noSavePrefs = true; }
            }

            // QA：-member=<id> 直接进第一章正文并打开该队员档案（用于三档字号的截图验收）
            string memberArg = ArgValue("-member");
            if (!string.IsNullOrEmpty(memberArg) && FindMember(memberArg) != null)
            {
                ApplyDisplay();
                JumpTo("n01", 1, Phase.Story);
                _textDone = true;
                _reveal = _fullText != null ? _fullText.Length : 0f;
                OpenProfile(memberArg);
                return;
            }

            if (HasArg("-autoplay-demo")) { AutoPlay(); return; }
            if (HasArg("-savetest")) { SaveTest(); return; }
            if (HasArg("-audiocheck")) { ApplyDisplay(); _phase = Phase.Title; _audioCheck = true; return; }
            if (HasArg("-debrief")) { ApplyDisplay(); FastForwardToDebrief(); return; }

            ApplyDisplay();
            _hasSave = HasAnySave();

            // 正常开局先出「基本提示」，点过才进主界面；
            // 任何 QA 参数都可以配 -skipnotice 跳过它（截图/无头跑流程时用），-typing 默认跳过。
            _phase = Phase.Notice;
            if (HasArg("-skipnotice") || HasArg("-typing")) _phase = Phase.Title;

            string ph = ArgValue("-phase");
            if (ph == "notice") { _phase = Phase.Notice; return; }
            if (ph == "declaration") { _phase = Phase.Declaration; return; }
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
            ApplyChapterClock(_chapter);
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
            _fontLevel = Mathf.Clamp(PlayerPrefs.GetInt("ilves.fontlevel", 1), 0, 2);
            _musicLevel = Mathf.Clamp(PlayerPrefs.GetInt("ilves.musiclevel", 2), 0, MusicLevels.Length - 1);
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

        void ApplyFontSetting(int level)
        {
            _fontLevel = Mathf.Clamp(level, 0, 2);
            PlayerPrefs.SetInt("ilves.fontlevel", _fontLevel);
            PlayerPrefs.Save();
            _stylesReady = false;
        }

        void LoadFont()
        {
            var bundled = Resources.Load<Font>("fonts/NotoSansCJKsc-Regular");
            if (bundled != null) { _font = bundled; Debug.Log("[ILVES] 使用内置字体 Noto Sans CJK SC"); return; }

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
            _speaker = new GUIStyle(_body) { fontSize = SpeakerSizes[_fontLevel] };
            _speaker.normal.textColor = Olive;
            _title = new GUIStyle(_body) { fontSize = 96, wordWrap = false, alignment = TextAnchor.MiddleCenter };
            _title.normal.textColor = Olive;
            _subtitle = new GUIStyle(_body) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            _subtitle.normal.textColor = Dim;
            _section = new GUIStyle(_body) { fontSize = SectionSizes[_fontLevel] };
            _section.normal.textColor = OliveDim;
            _sideFontSize = SideSizes[_fontLevel];
            _sideRowH = RowHeights[_fontLevel];
            _side = new GUIStyle(_body) { fontSize = _sideFontSize, wordWrap = false };
            _side.normal.textColor = Bone;
            _sideDim = new GUIStyle(_side);
            _sideDim.normal.textColor = new Color(0.64f, 0.65f, 0.55f);   // 次级文字提高对比度，小字号下不发虚
            _profileBody = new GUIStyle(_body) { fontSize = 17 + _fontLevel * 2 };          // 17 / 19 / 21
            _profileMeta = new GUIStyle(_bodySmall) { fontSize = Mathf.Max(13, _sideFontSize - 2) };
            _choiceBig = new GUIStyle(_body) { fontSize = 20 };
            _verdict = new GUIStyle(_body) { fontSize = 22 };
            _verdict.normal.textColor = Olive;
            _button = new GUIStyle(_body) { fontSize = 19, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _button.normal.textColor = Bone;
            _hint = new GUIStyle(_bodySmall) { fontSize = HintSizes[_fontLevel] };
            _hint.normal.textColor = new Color(0.62f, 0.63f, 0.52f);
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
                // 优先从 Resources 读取：Android 上 StreamingAssets 在 APK 内，File IO 不可用
                var asset = Resources.Load<TextAsset>("content/" + Path.GetFileNameWithoutExtension(file));
                if (asset != null) return JsonUtility.FromJson<StoryChapter>(asset.text);

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

        /// <summary>进入章节时记下基准：剧情钟点 clockStart + 当时的 GameState.Minutes。</summary>
        void ApplyChapterClock(StoryChapter ch)
        {
            _minuteBase = _state.Minutes;
            _hasChapterClock = false;
            _clockBaseMin = 0;
            if (ch == null) return;
            int h, m;
            if (!TryParseClock(ch.clockStart, out h, out m)) return;   // 空 / 格式错 → 回退旧逻辑
            _clockBaseMin = h * 60 + m;
            _hasChapterClock = true;
        }

        /// <summary>解析 "HH:MM"；长度不对、没冒号、非数字、越界都返回 false（不抛异常）。</summary>
        static bool TryParseClock(string s, out int h, out int m)
        {
            h = 0; m = 0;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            int colon = s.IndexOf(':');
            if (colon <= 0 || colon >= s.Length - 1) return false;
            string hh = s.Substring(0, colon).Trim();
            string mm = s.Substring(colon + 1).Trim();
            if (hh.Length == 0 || hh.Length > 2 || mm.Length == 0 || mm.Length > 2) return false;
            if (!int.TryParse(hh, out h) || !int.TryParse(mm, out m)) return false;
            if (h < 0 || h > 23 || m < 0 || m > 59) return false;
            return true;
        }

        /// <summary>侧栏时间：章节基准 + 本章已消耗分钟（跨日取模）；没有 clockStart 就用旧的累加钟。</summary>
        string DisplayClockText()
        {
            if (!_hasChapterClock) return _state.ClockText();
            int total = _clockBaseMin + (_state.Minutes - _minuteBase);
            int h = ((total / 60) % 24 + 24) % 24;
            int m = ((total % 60) + 60) % 60;
            return string.Format("{0:00}:{1:00}", h, m);
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
            ApplyChapterClock(_chapter);
            _engine = new StoryEngine(_chapter);
            _phase = Phase.Briefing;
            AutoSave();
        }

        void StartChapter()
        {
            if (_chapter == null) return;
            ApplyChapterClock(_chapter);
            _engine = new StoryEngine(_chapter);
            EnterNode();
            _phase = Phase.Story;
        }

        void EnterNode()
        {
            if (_engine == null || _engine.Current == null) { _phase = Phase.Debrief; return; }
            _fullText = _engine.Current.text ?? "";
            _choices = _engine.VisibleChoices(_state);
            _overChoice = false;
            // 先占位，真正的分页在 DrawStory（OnGUI 里）做：真实行距要用 GUIStyle.GetCursorPixelPosition，
            // 它必须在 GUI 上下文里调用（Update 里按空格推进节点时也能正确分页）。
            _pages.Clear();
            _pages.Add(_fullText);
            _page = 0;
            _reveal = 0f;
            _textDone = string.IsNullOrEmpty(_fullText);
            _pagesDirty = true;
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
            bool chapterChanged = snap.chapter != _chapterIndex;
            if (chapterChanged)
            {
                _chapterIndex = snap.chapter;
                _chapter = LoadChapter(_chapterIndex);
                _engine = new StoryEngine(_chapter);
            }
            _state = snap.state.Clone();
            if (chapterChanged) ApplyChapterClock(_chapter);   // 跨章退回后钟点按该章起始重算
            _engine.GoTo(snap.node);
            EnterNode();
            _phase = Phase.Story;
            _transient = "已退回一步";
        }

        void Advance()
        {
            if (!_textDone) { FinishPage(); return; }              // 打字中：先打完本页
            if (_page < _pages.Count - 1) { NextPage(); return; }  // 本页打完且还有下一页：翻页
            if (_choices != null && _choices.Count > 0) return;    // 只有最后一页能选
            if (_engine == null || _engine.Current == null) return;
            if (_engine.Current.end || string.IsNullOrEmpty(_engine.Current.next)) { EndChapter(); return; }
            PushHistory();
            _engine.GoTo(_engine.Current.next);
            EnterNode();
        }

        // ---------- 正文分页：一页一屏 ----------
        /// <summary>正文视口：从文字起点到选项区上沿（选项起点 H-236，再留 10px）。</summary>
        Rect BodyViewport(StoryNode node)
        {
            float bx = 56f, by = 96f, bw = 744f;
            float ty = by + (node == null || string.IsNullOrEmpty(node.speaker) ? 26f : 58f);
            float bottom = H - 236f - 10f;
            return new Rect(bx, ty, bw, Mathf.Max(60f, bottom - ty));
        }

        string PageText()
        {
            if (_pages.Count == 0) return "";
            return _pages[Mathf.Clamp(_page, 0, _pages.Count - 1)];
        }

        bool IsLastPage { get { return _pages.Count <= 1 || _page >= _pages.Count - 1; } }

        void FinishPage()
        {
            _reveal = PageText().Length;
            _textDone = true;
        }

        void NextPage()
        {
            if (_page >= _pages.Count - 1) return;
            _page++;
            _reveal = 0f;
            _textDone = string.IsNullOrEmpty(PageText());
            _overChoice = false;
        }

        /// <summary>翻页（不推进节点，不处理选项）：打字中先打完本页，否则下一页。</summary>
        void PageForward()
        {
            if (!_textDone) { FinishPage(); return; }
            if (_page < _pages.Count - 1) NextPage();
        }

        /// <summary>滚轮向上回看上一页（只在多页且不是第一页时有效）。</summary>
        void PageBackward()
        {
            if (_page <= 0) return;
            _page--;
            _reveal = PageText().Length;
            _textDone = true;
        }

        /// <summary>
        /// 统一文字真实高度：行数按 CalcHeight 的行数比例（准），行距用实测真实值（30.4082 @ 22px）。
        /// CalcHeight 对中文每行会高估约 6px（实测 CalcHeight("国")=36.41 vs 真实行距 30.4082），
        /// 直接用它会少塞约 1.5 行、裁剪时还会切半行；所有分页 / 裁剪判断都改用 MeasureText。
        /// </summary>
        static float MeasureText(GUIStyle style, string text, float w)
        {
            if (style == null || string.IsNullOrEmpty(text)) return 0f;
            w = Mathf.Max(8f, w);
            float one = style.CalcHeight(new GUIContent("国"), w);
            if (one <= 0f) return 0f;
            float lines = style.CalcHeight(new GUIContent(text), w) / one;
            return lines * LineHeight(style, w);
        }

        /// <summary>
        /// 通用分页：按空行分段 → 贪心塞段 → 单段超页时按字符二分（优先切在句末标点后）。
        /// 正文与队员档案弹窗共用；maxH 已是「视口高度 - 余量」。
        /// </summary>
        static List<string> PaginateText(string text, GUIStyle style, float w, float maxH)
        {
            var pages = new List<string>();
            if (string.IsNullOrEmpty(text)) { pages.Add(""); return pages; }
            w = Mathf.Max(20f, w);
            maxH = Mathf.Max(20f, maxH);

            // 把文字拆成「句子级原子块」：段 → 行 → 句。
            // 这样页尾只要还能塞进一个完整句子就会塞（不会留大半页空白），
            // 而且永远不会把一句话劈成两页（阅读节奏）。只有单句本身超过一页时才按字符二分。
            var blocks = new List<string>();
            var joins = new List<string>();   // 该块接在上一块后面时用的分隔符
            string[] paras = text.Split(new[] { "\n\n" }, StringSplitOptions.None);
            for (int p = 0; p < paras.Length; p++)
            {
                string para = paras[p];
                if (para.Length == 0) continue;
                bool firstLineOfPara = true;

                string[] lines = para.Split('\n');
                for (int li = 0; li < lines.Length; li++)
                {
                    string line = lines[li];
                    if (line.Length == 0) continue;
                    bool firstAtomOfLine = true;

                    var sentences = SplitSentences(line);
                    for (int si = 0; si < sentences.Count; si++)
                    {
                        string sent = sentences[si];
                        if (sent.Length == 0) continue;

                        string join = firstAtomOfLine
                            ? (firstLineOfPara ? "\n\n" : "\n")
                            : "";

                        if (MeasureText(style, sent, w) <= maxH)
                        {
                            blocks.Add(sent);
                            joins.Add(join);
                        }
                        else
                        {
                            // 极端情况：单句也超一页 → 字符二分（正文没有富文本标签，切字符安全）
                            int start = 0;
                            while (start < sent.Length)
                            {
                                int take = LongestFittingPrefix(style, sent, start, w, maxH);
                                if (take <= 0) take = 1;
                                if (start + take < sent.Length) take = PreferPunctuationBreak(sent, start, take);
                                if (take <= 0) take = 1;
                                blocks.Add(sent.Substring(start, take));
                                joins.Add(start == 0 ? join : "");
                                start += take;
                            }
                        }
                        firstAtomOfLine = false;
                    }
                    firstLineOfPara = false;
                }
            }

            if (blocks.Count == 0) { pages.Add(""); return pages; }

            var page = new StringBuilder();
            for (int i = 0; i < blocks.Count; i++)
            {
                string candidate = page.Length == 0 ? blocks[i] : page.ToString() + joins[i] + blocks[i];
                if (page.Length == 0 || TrueTextHeight(style, candidate, w) <= maxH)
                {
                    page.Length = 0;
                    page.Append(candidate);
                }
                else
                {
                    pages.Add(page.ToString());
                    page.Length = 0;
                    page.Append(blocks[i]);
                }
            }
            if (page.Length > 0) pages.Add(page.ToString());
            if (pages.Count == 0) pages.Add("");
            return pages;
        }

        /// <summary>
        /// 把一行文字切成句子（句末标点后断，右引号/右括号跟随本句）。
        /// 用于分页装页：宁可少装半行，也不把一句话劈到两页去。
        /// </summary>
        static List<string> SplitSentences(string line)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(line)) return list;
            var sb = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                sb.Append(line[i]);
                if (!IsPageBreakPunct(line[i])) continue;
                // 把紧跟的右引号 / 右括号 / 右书名号一起收进本句
                while (i + 1 < line.Length)
                {
                    char n = line[i + 1];
                    if (n != '\u300D' && n != '\u300F' && n != '\uFF09' && n != '\u201D' && n != '\u300B') break;
                    i++;
                    sb.Append(line[i]);
                }
                list.Add(sb.ToString());
                sb.Length = 0;
            }
            if (sb.Length > 0) list.Add(sb.ToString());
            return list;
        }

        /// <summary>二分找「刚好放得下的最长前缀」长度（从 start 起，用真实高度判断）。</summary>
        static int LongestFittingPrefix(GUIStyle style, string s, int start, float w, float maxH)
        {
            int lo = 1, hi = s.Length - start, best = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (TrueTextHeight(style, s.Substring(start, mid), w) <= maxH)
                { best = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return best;
        }

        static bool IsPageBreakPunct(char c)
        {
            return c == '。' || c == '！' || c == '？' || c == '；' || c == '」' || c == '…';
        }

        /// <summary>二分截断点不一定是句尾：在尽量靠后的句末标点后断开（至少保留半页）。</summary>
        static int PreferPunctuationBreak(string s, int start, int take)
        {
            if (take <= 2) return take;
            int min = Mathf.Max(1, take / 2);
            for (int i = start + take - 1; i >= start + min; i--)
            {
                if (IsPageBreakPunct(s[i])) return i - start + 1;
            }
            return take;
        }

        /// <summary>正文分页：用 MeasureText 的真实高度切页（一屏比 CalcHeight 多塞约 2 行）。</summary>
        void BuildPages(StoryNode node)
        {
            _pagesDirty = false;
            _pages.Clear();
            _page = 0;
            _reveal = 0f;
            _textDone = false;
            string text = _fullText ?? "";

            if (_body == null)
            {
                // 样式还没建（Start 里的初始跳转）：先占位，第一次 OnGUI 再真正分页
                _pagesDirty = true;
                _pages.Add(text);
                return;
            }

            Rect viewport = BodyViewport(node);
            float w = Mathf.Max(20f, viewport.width);
            float maxH = Mathf.Max(20f, viewport.height - 5f);   // 4–6px 余量

            _pages.AddRange(PaginateText(text, _body, w, maxH));
            _page = 0;
            _reveal = 0f;
            _textDone = string.IsNullOrEmpty(_pages[0]);

            // 分页自检：用真实渲染高度复核，任何一页高于视口才报警（正常永远不应触发）
            for (int i = 0; i < _pages.Count; i++)
            {
                float ph = TrueTextHeight(_body, _pages[i], w);
                if (ph > viewport.height + 1f)
                    Debug.LogWarning("[ILVES-PAGES] 页高超视口 node=" + (node != null ? node.id : "?")
                                     + " page=" + (i + 1) + " h=" + Mathf.RoundToInt(ph)
                                     + " viewport=" + Mathf.RoundToInt(viewport.height));
            }

            Debug.Log("[ILVES-PAGES] node=" + (node != null ? node.id : "?") + " pages=" + _pages.Count
                      + " viewport=" + Mathf.RoundToInt(viewport.height) + "px lineH=" + LineHeight(_body, w).ToString("F1"));
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
            for (int i = 0; i < SlotCount; i++) _slotMeta.Add(ReadSlot(SlotPath(i)));
        }

        bool HasAnySave()
        {
            if (File.Exists(_autoPath)) return true;
            for (int i = 0; i < SlotCount; i++) if (File.Exists(SlotPath(i))) return true;
            return false;
        }

        string LatestSavePath()
        {
            string best = null, bestTime = null;
            var paths = new List<string> { _autoPath };
            for (int i = 0; i < SlotCount; i++) paths.Add(SlotPath(i));
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
                ApplyChapterClock(_chapter);
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
            _slotDeleteArmed = -1;
            _transient = null;
            RefreshSlotMeta();
            _phase = Phase.Slots;
        }

        void DeleteSlot(int index)
        {
            try
            {
                if (File.Exists(SlotPath(index))) File.Delete(SlotPath(index));
                _slotDeleteArmed = -1;
                RefreshSlotMeta();
                _hasSave = HasAnySave();
                _transient = "已删除 " + SlotNames[index];
            }
            catch (Exception e)
            {
                Debug.LogError("[ILVES] 删除存档失败: " + e.Message);
                _transient = "删除失败：" + e.Message;
            }
        }

        // =====================================================================
        // Update
        // =====================================================================
        // QA：-audiocheck 起来后等 2.5 秒，把「音乐到底有没有真的在解码播放」打进 Player.log 再退出
        bool _audioCheck;
        float _audioCheckT;
        float _audioCheckNext;

        void Update()
        {
            UpdateMusic();

            if (_audioCheck)
            {
                _audioCheckT += Time.unscaledDeltaTime;
                if (_audioCheckT > _audioCheckNext)
                {
                    bool hasListener = UnityEngine.Object.FindFirstObjectByType<AudioListener>() != null;
                    Debug.Log("[ILVES-AUDIOCHECK] t=" + _audioCheckT.ToString("F2")
                              + " key=" + _musicKey
                              + " clip=" + (_music != null && _music.clip != null ? _music.clip.name : "null")
                              + " isPlaying=" + (_music != null && _music.isPlaying)
                              + " audioTime=" + (_music != null ? _music.time.ToString("F3") : "-")
                              + " dsp=" + AudioSettings.dspTime.ToString("F2")
                              + " sampleRate=" + AudioSettings.outputSampleRate
                              + " listener=" + hasListener);
                    _audioCheckNext += 0.8f;
                }
                if (_audioCheckT > 2.6f)
                {
                    bool ok = _music != null && _music.clip != null && _music.isPlaying
                              && _music.time > 0.05f && _music.volume > 0.01f;
                    Debug.Log("[ILVES-AUDIOCHECK] " + (ok ? "PASS" : "FAIL"));
                    Application.Quit(ok ? 0 : 1);
                }
            }

            if (_toastTimer > 0f)
            {
                _toastTimer -= Time.unscaledDeltaTime;
                if (_toastTimer <= 0f) _toasts.Clear();
            }

            if (_phase == Phase.Story)
            {
                if (!_textDone && !_freezeTyping)
                {
                    string pageNow = PageText();
                    _reveal += Time.unscaledDeltaTime * 42f;
                    if (_reveal >= pageNow.Length) { _reveal = pageNow.Length; _textDone = true; }
                }
                // 队员档案弹窗打开时：推进 / 选项 / 暂停全部让路，ESC 只负责关弹窗；
                // 弹窗内滚动交给 DrawScrolledText（滚轮 / PageUp / PageDown / ↑↓）
                if (_profileOpen)
                {
                    if (Input.GetKeyDown(KeyCode.Escape)) CloseProfile();
                    return;
                }
                if (Input.GetKeyDown(KeyCode.Escape)) { _phase = Phase.Pause; return; }
                if (Input.GetKeyDown(KeyCode.Backspace)) Undo();
                if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)) Advance();
                if (Input.GetKeyDown(KeyCode.PageDown) || Input.GetKeyDown(KeyCode.DownArrow)) PageForward();
                for (int i = 0; i < 9; i++)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                    {
                        // 数字键只在最后一页生效，避免跳过没读的文字
                        if (_textDone && IsLastPage && _choices != null && i < _choices.Count) Select(i);
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
            else if (_phase == Phase.Notice)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
                    _phase = Phase.Title;
            }
            else if (_phase == Phase.Declaration)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) _phase = _declReturn;
            }
        }

        // =====================================================================
        // 音乐
        //
        // 主界面一首、游戏内两首，按章节序号轮流（序章/第二章 → story-1，第一章 → story-2），
        // 这样连读两章不会一直是同一段旋律。全部从 Resources 走，缺失只是静音，不报错。
        // =====================================================================
        void SetupMusic()
        {
            _musicTitle = Resources.Load<AudioClip>("music/title");
            _musicStory1 = Resources.Load<AudioClip>("music/story-1");
            _musicStory2 = Resources.Load<AudioClip>("music/story-2");
            if (_musicTitle == null || _musicStory1 == null || _musicStory2 == null)
                Debug.LogWarning("[ILVES] 音乐资源缺失，将在静音下运行（检查 Resources/music/）");

            // 场景是构建时新建的，里面只有相机和本组件——没有 AudioListener 就是全程静音，且不报错。
            if (UnityEngine.Object.FindFirstObjectByType<AudioListener>() == null)
            {
                gameObject.AddComponent<AudioListener>();
                Debug.Log("[ILVES] 场景里没有 AudioListener，已在 DemoBootstrap 上补一个");
            }

            _music = gameObject.GetComponent<AudioSource>();
            if (_music == null) _music = gameObject.AddComponent<AudioSource>();
            _music.playOnAwake = false;
            _music.loop = true;
            _music.spatialBlend = 0f;
            _music.volume = MusicLevels[Mathf.Clamp(_musicLevel, 0, MusicLevels.Length - 1)];
        }

        /// <summary>当前界面该放哪首；只在换界面时真正切歌，不会每帧重启音频。</summary>
        void UpdateMusic()
        {
            if (_music == null) return;
            string want;
            switch (_phase)
            {
                case Phase.Briefing:
                case Phase.Story:
                case Phase.Pause:
                case Phase.Debrief:
                    want = (_chapterIndex % 2 == 0) ? "story-1" : "story-2";
                    break;
                default:
                    want = "title";
                    break;
            }
            if (want == _musicKey) return;

            AudioClip clip = want == "title" ? _musicTitle : (want == "story-1" ? _musicStory1 : _musicStory2);
            _musicKey = want;
            if (clip == null) { _music.Stop(); return; }
            _music.clip = clip;
            _music.volume = MusicLevels[Mathf.Clamp(_musicLevel, 0, MusicLevels.Length - 1)];
            if (_music.volume <= 0.001f) { _music.Stop(); return; }
            _music.Play();
        }

        void ApplyMusicLevel(int level)
        {
            _musicLevel = Mathf.Clamp(level, 0, MusicLevels.Length - 1);
            if (_music != null) _music.volume = MusicLevels[_musicLevel];
            if (_music != null && MusicLevels[_musicLevel] > 0.001f && !_music.isPlaying && _music.clip != null) _music.Play();
            if (_music != null && MusicLevels[_musicLevel] <= 0.001f) _music.Stop();
            PlayerPrefs.SetInt("ilves.musiclevel", _musicLevel);
            PlayerPrefs.Save();
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
                case Phase.Notice: DrawNotice(); break;
                case Phase.Declaration: DrawDeclaration(); break;
                case Phase.Title: DrawTitle(); break;
                case Phase.Settings: DrawSettings(); break;
                case Phase.Briefing: DrawBriefing(); break;
                case Phase.Story: DrawStory(true); break;
                case Phase.Pause: DrawStory(false); DrawPause(); break;
                case Phase.Debrief: DrawDebrief(); break;
                case Phase.Slots: DrawSlots(); break;
            }

            if (_profileOpen) DrawMemberProfile();

            GUI.matrix = old;
        }

        // ---------- 主界面 ----------
        // 视觉：负责人提供的主界面背景图（九人与载具）+ 标题艺术字（ILVES 字标）。
        // 版式：内容整列靠左——背景图左侧本来就是压暗的留白区，右侧是主体人像，
        //       按钮压到人像上会脏；左栏再叠一道黑色横向渐变，任何背景亮度下文字都可读。
        // 两张图任一缺失都会自动退回旧版（纯文字标题 / 纯色底），不会白屏。
        const float TitleColX = 92f;      // 左栏左边距
        const float TitleLogoW = 520f;    // 字标宽度（字标原生约 4:1 宽横幅）
        const float TitleBtnW = 320f;
        const float TitleBtnH = 40f;
        const float TitleBtnGap = 44f;
        const string ProjectUrl = "https://github.com/Yukiii-26/ILVES";

        void DrawTitle()
        {
            DrawTitleBackdrop();

            float y = 88f;

            // 标题艺术字：宽高比按贴图实测比例算，换图不用改代码
            if (_titleLogo != null)
            {
                float lh = TitleLogoW * _titleLogo.height / Mathf.Max(1f, _titleLogo.width);
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.97f);
                GUI.DrawTexture(new Rect(TitleColX, y, TitleLogoW, lh), _titleLogo, ScaleMode.ScaleToFit, true);
                GUI.color = old;
                y += lh + 16f;
            }
            else
            {
                Label(new Rect(TitleColX - 8f, y - 14f, TitleLogoW + 40f, 120f), "ILVES",
                      new GUIStyle(_title) { alignment = TextAnchor.UpperLeft });
                y += 108f;
            }

            Label(new Rect(TitleColX + 6f, y, TitleLogoW, 36f), "猞 猁 小 队",
                  new GUIStyle(_subtitle) { alignment = TextAnchor.UpperLeft });
            y += 44f;

            Fill(new Rect(TitleColX, y, 300f, 1f), OliveDim);
            y += 14f;
            Label(new Rect(TitleColX + 6f, y, 480f, 26f), "卡累利阿 · 一支九人侦察小队的故事", _bodySmall);
            y += 34f;

            // 六项：新游戏 / 继续 / 读档 / 声明 / 设置 / 退出
            if (Button(new Rect(TitleColX, y, TitleBtnW, TitleBtnH), "新 游 戏")) BeginNewGame();
            GUI.enabled = _hasSave;
            if (Button(new Rect(TitleColX, y + TitleBtnGap, TitleBtnW, TitleBtnH), "继 续 游 戏")) ContinueGame();
            if (Button(new Rect(TitleColX, y + TitleBtnGap * 2f, TitleBtnW, TitleBtnH), "读 取 存 档")) OpenSlots(SlotMode.Load, Phase.Title);
            GUI.enabled = true;
            if (Button(new Rect(TitleColX, y + TitleBtnGap * 3f, TitleBtnW, TitleBtnH), "声 明")) { _declReturn = Phase.Title; _declScroll = 0f; _phase = Phase.Declaration; }
            if (Button(new Rect(TitleColX, y + TitleBtnGap * 4f, TitleBtnW, TitleBtnH), "设 置")) { _settingsReturn = Phase.Title; _phase = Phase.Settings; }
            if (Button(new Rect(TitleColX, y + TitleBtnGap * 5f, TitleBtnW, TitleBtnH), "退 出")) { Application.Quit(); }

            // 左下角只写版本号（负责人 2026-10-30 要求）；临时提示单独一行放在按钮下面
            if (!string.IsNullOrEmpty(_transient))
                Label(new Rect(TitleColX + 2f, y + TitleBtnGap * 6f + 2f, 620f, 24f), _transient, _hint);
            Label(new Rect(TitleColX + 2f, 674f, 320f, 24f), "v" + Application.version, _hint);
        }

        // ---------- 基本提示（进游戏先弹这一面） ----------
        /// <summary>自适应高度的文字段：用 MeasureText 量真实高度再画，返回下一段的 y（不会压字、不会被切）。</summary>
        float Block(float x, float y, float w, string text, GUIStyle style, float gap)
        {
            float h = Mathf.Max(24f, MeasureText(style, text, w));
            Label(new Rect(x, y, w, h + 3f), text, style);
            return y + h + gap;
        }

        /// <summary>提示/声明通用的暗底卡片：返回卡片矩形。</summary>
        Rect NoticeCard(float y, float h)
        {
            DrawTitleBackdrop();
            Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, 0.42f));
            var box = new Rect(W * 0.5f - 440f, y, 880f, h);
            Fill(box, new Color(0.055f, 0.060f, 0.042f, 0.97f));
            Fill(new Rect(box.x, box.y, 2f, box.height), Olive);
            return box;
        }

        void DrawNotice()
        {
            var box = NoticeCard(150f, 420f);
            GUIStyle item = new GUIStyle(_body) { fontSize = 18 };
            GUIStyle head = new GUIStyle(_body) { fontSize = 30, alignment = TextAnchor.UpperLeft };

            float x = box.x + 44f, w = box.width - 88f;
            Label(new Rect(x, box.y + 26f, w, 44f), "基 本 提 示", head);
            Fill(new Rect(x, box.y + 78f, w, 1f), OliveDim);

            float y = box.y + 94f;
            y = Block(x, y, w, "一、本游戏的故事、人物、部队番号与地名均为虚构，与任何真实的人、组织或事件无关。", item, 10f);
            y = Block(x, y, w, "二、本游戏完全免费，不含内购、广告或任何形式的盈利行为。项目主页：", item, 4f);
            y = Block(x, y + 2f, w, ProjectUrl, new GUIStyle(item) { fontSize = 17 }, 10f);
            y = Block(x, y, w, "三、本游戏不含任何不良导向内容。", item, 10f);
            y = Block(x, y, w, "四、游戏内音乐版权归原作者所有，仅在本作中非商业性使用。", item, 10f);

            Label(new Rect(x, y + 2f, w, 24f), "更详细的说明见主界面「声明」。", _hint);

            if (Button(new Rect(box.x + box.width * 0.5f - 300f, box.y + box.height - 78f, 280f, 46f),
                       "我 已 知 悉")) _phase = Phase.Title;
            if (Button(new Rect(box.x + box.width * 0.5f + 20f, box.y + box.height - 78f, 280f, 46f),
                       "打开项目主页")) Application.OpenURL(ProjectUrl);
            Label(new Rect(box.x, box.y + box.height + 6f, box.width, 22f), "空格 / 回车 也可以继续", Centered(_hint));
        }

        // ---------- 声明（主界面入口，内容比提示更详细） ----------
        const string DeclarationText =
            "一、虚构声明\n" +
            "本游戏的故事情节、人物姓名与呼号、部队番号、地名、任务与事件均为创作虚构，与任何真实的个人、组织、国家、部队或历史事件无关。游戏中出现的俄语术语只用于氛围塑造，不代表任何现实立场。\n\n" +
            "二、免费与来源\n" +
            "本游戏完全免费，不含内购、广告、付费下载或任何形式的盈利行为。项目源代码、版本发布与问题反馈都在 GitHub：" + ProjectUrl + "\n\n" +
            "三、内容导向\n" +
            "本游戏不含色情、赌博、政治宣传或其它不良导向内容。它是一部以指挥、判断与克制为主题的文字游戏；游戏中的战斗描写意在表现代价，而非渲染暴力。\n\n" +
            "四、音乐与音频版权\n" +
            "游戏内音乐版权归原作者所有，本作仅在非商业场景下使用，不作任何商用：\n" +
            "· Sean Oban — Ripples（主界面）\n" +
            "· Ever So Blue — Onthou（游戏内音乐一）\n" +
            "· Fabrizio Paterlini — Veloma（游戏内音乐二）\n" +
            "若权利人认为使用不当，可通过项目 GitHub 页面联系，我们会在第一时间移除。\n\n" +
            "五、其它\n" +
            "美术与文字均由本项目作者创作或持有授权，未经许可不得复制或商用。\n";

        void DrawDeclaration()
        {
            var box = NoticeCard(24f, 672f);
            GUIStyle head = new GUIStyle(_body) { fontSize = 30, alignment = TextAnchor.UpperLeft };
            float x = box.x + 44f, w = box.width - 88f;

            Label(new Rect(x, box.y + 22f, w, 44f), "声 明", head);
            Fill(new Rect(x, box.y + 74f, w, 1f), OliveDim);

            var view = new Rect(x, box.y + 88f, w, box.height - 88f - 68f);
            DrawScrolledText(view, DeclarationText, DeclarationText, new GUIStyle(_body) { fontSize = 16 }, ref _declScroll);

            if (Button(new Rect(box.x + box.width * 0.5f - 300f, box.y + box.height - 60f, 280f, 44f), "返 回")) _phase = _declReturn;
            if (Button(new Rect(box.x + box.width * 0.5f + 20f, box.y + box.height - 60f, 280f, 44f), "打开项目主页")) Application.OpenURL(ProjectUrl);
        }

        /// <summary>
        /// 主界面背景：背景图等比裁切铺满（ScaleAndCrop＝cover），
        /// 上面压一层整体暗化 + 一道左侧横向渐变；右半边的关键美术基本保持原样。
        /// </summary>
        void DrawTitleBackdrop()
        {
            if (_titleBg == null)
            {
                Fill(new Rect(0f, 0f, W, H), Bg);
                return;
            }
            var old = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0f, 0f, W, H), _titleBg, ScaleMode.ScaleAndCrop, true);
            GUI.color = old;

            // 整体暗化：这张是带天光的照片，不压下去橄榄绿文字就压不住
            Fill(new Rect(0f, 0f, W, H), new Color(0.024f, 0.031f, 0.016f, 0.34f));

            // 左侧渐变（覆盖左侧 74% 宽）：背景图左缘本身是暗的，叠上去刚好接住
            if (_titleScrim != null)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(0f, 0f, W * 0.74f, H), _titleScrim, ScaleMode.StretchToFill, true);
                GUI.color = old;
            }
        }

        /// <summary>
        /// 生成 256×1 的黑色横向渐变贴图（左实右透），用作主界面左栏遮罩。
        /// 不用外部资源：透明通道就是 alpha，任何分辨率下都不会有渐变条带。
        /// </summary>
        static Texture2D MakeLeftScrim()
        {
            const int n = 256;
            var tex = new Texture2D(n, 1, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.name = "title-left-scrim";
            var px = new Color32[n];
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)(n - 1);
                float t = Mathf.Clamp01(1f - u / 0.80f);           // u>=0.80 完全透明
                float a = 0.94f * Mathf.Pow(t, 1.35f);
                px[i] = new Color32(3, 4, 2, (byte)Mathf.RoundToInt(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        // ---------- 设置 ----------
        void DrawSettings()
        {
            const float NavX = 80f, NavY = 140f, NavW = 420f, NavH = 64f, NavGap = 12f;
            const float PaneX = 560f, PaneW = 640f, PaneY = 140f;

            Label(new Rect(80, 54, 400, 60), "设 置", new GUIStyle(_title) { fontSize = 46, alignment = TextAnchor.UpperLeft });
            Fill(new Rect(80, 122, W - 160, 1), OliveDim);

            // ---- 左半部分：栏目 ----
            string[] tabs = { "画 面", "声 音", "其 它" };
            for (int i = 0; i < tabs.Length; i++)
            {
                var r = new Rect(NavX, NavY + i * (NavH + NavGap), NavW, NavH);
                bool active = (int)_settingsTab == i;
                if (Button(r, tabs[i], active)) { _settingsTab = (SettingsTab)i; _transient = null; }
            }

            // 右栏提示条（当前栏目说明）
            Fill(new Rect(PaneX - 40f, NavY, 1f, H - NavY - 150f), new Color(0.14f, 0.145f, 0.11f));

            float y = PaneY;
            switch (_settingsTab)
            {
                case SettingsTab.Display: y = SettingsDisplay(PaneX, PaneW, y); break;
                case SettingsTab.Audio: y = SettingsAudio(PaneX, PaneW, y); break;
                default: y = SettingsAbout(PaneX, PaneW, y); break;
            }

            if (!string.IsNullOrEmpty(_transient))
                Label(new Rect(PaneX, H - 150f, PaneW, 24f), _transient, _hint);

            if (Button(new Rect(W * 0.5f - 160f, H - 92f, 320f, 46f), "返 回")) _phase = _settingsReturn;
            Label(new Rect(0, H - 34f, W, 22f), "ESC 返回　·　设置即时生效", Centered(_hint));
        }

        /// <summary>右栏的三档/四档按钮排一行；返回选中项下标（未选中返回 -1）。</summary>
        int ChoiceRow(float x, float y, float totalW, string[] labels, int current)
        {
            int picked = -1;
            int n = labels.Length;
            float gap = 12f;
            float bw = (totalW - gap * (n - 1)) / n;
            for (int i = 0; i < n; i++)
            {
                if (Button(new Rect(x + i * (bw + gap), y, bw, 52f), labels[i], current == i)) picked = i;
            }
            return picked;
        }

        float SettingsDisplay(float x, float w, float y)
        {
            Label(new Rect(x, y, w, 28f), "分 辨 率", _section);
            int ri = ChoiceRow(x, y + 36f, w, ResLabels, _resIndex);
            if (ri >= 0) { _resIndex = ri; ApplyDisplay(); }
            y += 108f;

            Label(new Rect(x, y, w, 28f), "显 示 模 式", _section);
            int fi = ChoiceRow(x, y + 36f, w, new[] { "窗 口", "全 屏" }, _fullscreen ? 1 : 0);
            if (fi >= 0) { _fullscreen = fi == 1; ApplyDisplay(); }
            y += 108f;

            Label(new Rect(x, y, w, 28f), "界 面 字 号（状态栏 / 菜单小字）", _section);
            int si = ChoiceRow(x, y + 36f, w, new[] { "小", "标 准", "大" }, _fontLevel);
            if (si >= 0) ApplyFontSetting(si);
            y += 108f;

            y = Block(x, y, w, "提示：界面按 16:9 等比缩放；窗口模式会自动限制在屏幕的 90% 以内，避免窗口超出屏幕。", _hint, 6f);
            return y;
        }

        float SettingsAudio(float x, float w, float y)
        {
            Label(new Rect(x, y, w, 28f), "音 乐 音 量", _section);
            int mi = ChoiceRow(x, y + 36f, w, MusicLevelLabels, _musicLevel);
            if (mi >= 0) ApplyMusicLevel(mi);
            y += 108f;

            string now = _musicLevel <= 0 ? "当前：静音"
                       : (_musicKey == null ? "当前：" + MusicLevelLabels[_musicLevel]
                                            : "当前：" + MusicLevelLabels[_musicLevel] + "　正在播放 " + MusicTrackName(_musicKey));
            y = Block(x, y, w, now, _bodySmall, 8f);
            y = Block(x, y, w, "主界面 1 首、游戏内 2 首（按章节轮流）。音乐版权归原作者所有，仅非商业使用；曲目与作者见主界面「声明」。", _hint, 8f);
            y = Block(x, y, w, "音量档位即时生效，写入本地设置，下次启动保持。", _hint, 8f);
            return y;
        }

        static string MusicTrackName(string key)
        {
            if (key == "title") return "Ripples";
            if (key == "story-1") return "Onthou";
            if (key == "story-2") return "Veloma";
            return "—";
        }

        float SettingsAbout(float x, float w, float y)
        {
            y = Block(x, y, w, "版本　v" + Application.version, _body, 10f);
            y = Block(x, y, w, "平台　Windows x64（Unity 6.3 LTS）", _bodySmall, 8f);
            y = Block(x, y, w, "存档　" + Application.persistentDataPath + "（存档 01–" + SlotCount.ToString("00") + " ＋ 自动存档）", _hint, 8f);
            y = Block(x, y, w, "项目主页　" + ProjectUrl, _hint, 12f);

            if (Button(new Rect(x, y, 300f, 46f), "查 看 声 明")) { _declReturn = Phase.Settings; _declScroll = 0f; _phase = Phase.Declaration; }
            if (Button(new Rect(x + 316f, y, 300f, 46f), "打开项目主页")) Application.OpenURL(ProjectUrl);
            y += 62f;
            y = Block(x, y, w, "本游戏完全免费，无内购与广告；不含任何不良导向内容。详见「声明」。", _hint, 6f);
            return y;
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
                Label(new Rect(900, 228, 320, 30), "任 务 目 标", _section);
                float oy = 258f;
                for (int i = 0; i < _chapter.objectives.Length; i++)
                {
                    Label(new Rect(900, oy, 340, 32), "▸ " + _chapter.objectives[i], _side);
                    oy += 36f;
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
            if (_emblem != null) GUI.DrawTexture(new Rect(40, 8, 38, 38), _emblem, ScaleMode.ScaleToFit);
            Label(new Rect(86, 14, 200, 28), "ILVES", _speaker);
            string mid = (_chapter != null ? _chapter.title + " · " + _chapter.subtitle : "");
            Label(new Rect(240, 16, 760, 28), mid, Centered(_bodySmall));
            Label(new Rect(1000, 16, 240, 28), "时间 " + DisplayClockText(), Righted(_bodySmall));
            Fill(new Rect(0, 54, W, 1), OliveDim);

            // 档案弹窗打开时，顶栏的「退回 / 菜单」也要让路（否则会穿透到正文层）
            bool hudInteractive = interactive && !_profileOpen;
            if (hudInteractive && _history.Count > 0)
            {
                if (Button(new Rect(40, 66, 120, 28), "← 退回 (" + _history.Count + ")", false)) Undo();
            }
            if (hudInteractive)
            {
                if (Button(new Rect(1090, 66, 132, 30), "菜单 (ESC)", false)) _phase = Phase.Pause;
            }
            else if (!interactive)
            {
                Label(new Rect(1000, 70, 240, 24), "ESC 菜单", Righted(_hint));
            }

            var node = _engine != null ? _engine.Current : null;
            if (node == null) return;

            // Start() 里的初始跳转发生在样式建立之前，这里补一次真正的分页
            if (_pagesDirty) BuildPages(node);

            float bx = 56f, by = 96f, bw = 744f;
            if (!string.IsNullOrEmpty(node.scene)) Label(new Rect(bx, by, bw, 22), node.scene, _scene);
            if (!string.IsNullOrEmpty(node.speaker)) Label(new Rect(bx, by + 26, bw, 34), node.speaker, _speaker);

            // 正文分页：只画当前页；矩形本身就是裁切边界，分页保证页高不溢出
            string pageText = PageText();
            string visible = _textDone
                ? pageText
                : pageText.Substring(0, Mathf.Clamp(Mathf.FloorToInt(_reveal), 0, pageText.Length));
            var viewport = BodyViewport(node);
            // 正文**不能**用 Label()：那个原语会按 CalcHeight（对中文每行高估约 6px）自动加高矩形，
            // 一页 10 行就会被撑高 48px，正文下沿直接压到选项上（用户报的"文本多时被选项挡住"就是这个）。
            // 分页已经用真实行距算过页高，这里按真实渲染高度画、并以视口高度兜底裁切。
            LabelClipV(viewport, visible, _body);

            float cy = H - 236f;   // 选项区上移 14px，给正文多一行空间
            bool canClick = interactive && !_profileOpen;
            // 提示行固定放左下角：放在选项区上方会被选项/正文压住（用户 2026-10-30 反馈）
            var hintRect = new Rect(bx, H - 26f, bw, 22f);
            if (!IsLastPage)
            {
                Label(hintRect, "▾  继续（" + (_page + 1) + " / " + _pages.Count + "）　滚轮 / 空格 / 点击", _hint);
            }
            else if (!_textDone)
            {
                Label(hintRect, "点击 / 空格 跳过", _hint);
            }
            else if (_choices != null && _choices.Count > 0)
            {
                for (int i = 0; i < _choices.Count && i < 4; i++)
                    DrawChoice(new Rect(bx, cy + i * 52f, bw, 46f), i, _choices[i], canClick);
            }
            else
            {
                Label(hintRect, "▾  空格 / 点击 继续", _hint);
            }

            // 滚轮翻页（指针在正文区内）：实测 Event.delta.y 在滚轮**向上**时为负，
            // 所以 >0 = 向下 = 往后翻，<0 = 向上 = 回看上一页（方向 2026-10-30 校准过）
            if (canClick && Event.current.type == EventType.ScrollWheel && viewport.Contains(Event.current.mousePosition))
            {
                if (Event.current.delta.y > 0f) PageForward(); else PageBackward();
                Event.current.Use();
            }

            DrawSidebar(canClick);

            if (canClick && !_overChoice && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Advance();
                Event.current.Use();
            }
            // 只在"这一帧"里压住点击推进。之前这个标记只在 EnterNode 重置，
            // 而 DrawChoice 又是在 EnterNode 之后才把它置 true —— 于是点过一次选项后，
            // 整个节点期间鼠标点击推进都失效（表现就是用户说的"有时候点击跳过不管用"）。
            _overChoice = false;
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

        void DrawSidebar(bool interactive)
        {
            float x = 820f, y = 96f, w = 404f;   // 右边界 1224，两侧留白
            float right = x + w - 36f;
            float bottom = H - 40f;
            Fill(new Rect(x, y, w, bottom - y), Panel);
            Fill(new Rect(x, y, 1f, bottom - y), OliveDim);
            Fill(new Rect(x + w - 1f, y, 1f, bottom - y), OliveDim);

            int rh = _sideRowH;
            float cy = y + 18f;
            Label(new Rect(x + 32, cy, w - 64, rh), "小队状态  ·  9 人", _section);
            cy += rh + 6f;

            Label(new Rect(x + 32, cy, 170, rh), "暴露度", _sideDim);
            var exSt = new GUIStyle(_side) { alignment = TextAnchor.MiddleRight };
            exSt.normal.textColor = _state.Exposure >= 60 ? Danger : Bone;
            Label(new Rect(right - 190, cy, 190, rh), _state.Exposure + "  " + _state.ExposureLabel(), exSt);
            cy += rh;
            Fill(new Rect(x + 32, cy + 2, w - 72, 6), new Color(0.14f, 0.145f, 0.11f));
            Fill(new Rect(x + 32, cy + 2, (w - 72) * Mathf.Clamp01(_state.Exposure / 100f), 6), _state.Exposure >= 60 ? Danger : Olive);
            cy += rh + 8f;

            Label(new Rect(x + 32, cy, 190, rh), "情报完整度", _sideDim);
            Label(new Rect(right - 120, cy, 120, rh), _state.Intel + " / 6", new GUIStyle(_side) { alignment = TextAnchor.MiddleRight });
            cy += rh;

            Label(new Rect(x + 32, cy, 190, rh), "队伍信任", _sideDim);
            Label(new Rect(right - 120, cy, 120, rh), (_state.Trust >= 0 ? "+" : "") + _state.Trust, new GUIStyle(_side) { alignment = TextAnchor.MiddleRight });
            cy += rh + 10f;
            Fill(new Rect(x + 32, cy, w - 72, 1), OliveDim);
            cy += 12f;

            var mouse = Event.current.mousePosition;
            for (int i = 0; i < GameState.MemberIds.Length; i++)
            {
                string id = GameState.MemberIds[i];
                string status = _state.MemberStatus(id);
                var md = FindMember(id);

                var row = new Rect(x + 16f, cy, w - 32f, rh);
                bool rowHover = interactive && row.Contains(mouse);
                if (rowHover) Fill(row, PanelHi);   // 整行 hover 高亮，末尾 i 按钮只是视觉提示，整行可点

                // 姓名
                string name = GameState.MemberNames[i];
                var nameSt = new GUIStyle(_side);
                nameSt.normal.textColor = (id == "vasili") ? Olive : Bone;
                float nameW = _side.CalcSize(new GUIContent(name)).x;
                float nameX = row.x + 16f;
                Label(new Rect(nameX, cy, nameW + 4f, rh), name, nameSt);

                // 职位：短称紧跟姓名之后，用 CalcSize 量出的真实姓名宽度定位（不写死偏移）
                string role = md != null ? md.roleShort : "";
                if (id == "vasili") role = string.IsNullOrEmpty(role) ? "你" : role + " · 你";
                string roleText = string.IsNullOrEmpty(role) ? "" : "（" + role + "）";

                // 状态区（右对齐）与按钮区从右往左排，姓名+职位区不许越过状态区
                var stSt = Righted(_side);
                stSt.normal.textColor = StatusColor(status);
                float statusW = _side.CalcSize(new GUIContent(status)).x + 2f;
                var infoR = new Rect(row.xMax - 26f, cy + 2f, 26f, rh - 4f);
                float statusX = infoR.x - 10f - statusW;

                float roleX = nameX + nameW;
                float roleLimit = statusX - 8f;
                if (!string.IsNullOrEmpty(roleText) && roleLimit > roleX)
                {
                    // 只在横向裁（不能压到状态列），纵向按 CalcHeight 自动加高——三档字号都不许切字
                    LabelClipH(new Rect(roleX, cy, roleLimit - roleX, rh), roleText, _sideDim);
                }

                Label(new Rect(statusX, cy, statusW, rh), status, stSt);

                // 行末小按钮：方框 + 小字 i，hover 橄榄绿高亮
                bool infoHover = interactive && infoR.Contains(mouse);
                Fill(infoR, (infoHover || rowHover) ? new Color(0.18f, 0.19f, 0.13f) : new Color(0.090f, 0.094f, 0.070f));
                Outline(infoR, (infoHover || rowHover) ? Olive : OliveDim);
                var infoSt = new GUIStyle(_hint) { fontSize = Mathf.Max(11, _sideFontSize - 5), alignment = TextAnchor.MiddleCenter, wordWrap = false };
                infoSt.normal.textColor = (infoHover || rowHover) ? new Color(0.95f, 0.95f, 0.90f) : OliveDim;
                Label(infoR, "i", infoSt);

                if (interactive && rowHover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    OpenProfile(id);
                    Event.current.Use();
                }
                cy += rh;
            }

            if (_toasts.Count > 0)
            {
                float ty = bottom - 58f;
                for (int i = 0; i < _toasts.Count && i < 3; i++)
                {
                    var st = new GUIStyle(_hint);
                    st.normal.textColor = Olive;
                    Label(new Rect(x + 32, ty + i * 20f, w - 40, 20), "+ " + _toasts[i], st);
                }
            }
            else if (!string.IsNullOrEmpty(_transient))
            {
                Label(new Rect(x + 32, bottom - 34f, w - 40, 22), _transient, _hint);
            }
        }

        static Color StatusColor(string status)
        {
            if (status.Contains("伤")) return Danger;
            if (status.Contains("焦虑") || status.Contains("紧张") || status.Contains("专注")) return new Color(0.75f, 0.62f, 0.30f);
            if (status.Contains("失联") || status.Contains("离队") || status.Contains("沉默")) return new Color(0.55f, 0.55f, 0.50f);
            if (status.Contains("开火") || status.Contains("驾驶") || status.Contains("救治") ||
                status.Contains("射击") || status.Contains("警戒") || status.Contains("观察") ||
                status.Contains("布雷") || status.Contains("探路") || status.Contains("引导")) return Olive;
            return Dim;
        }

        // =====================================================================
        // 队员档案（members.json）与弹窗
        // =====================================================================
        void EnsureMembers()
        {
            if (_membersLoaded) return;
            _membersLoaded = true;
            try
            {
                string json = null;
                var asset = Resources.Load<TextAsset>("content/members");
                if (asset != null) json = asset.text;
                if (string.IsNullOrEmpty(json))
                {
                    string path = Path.Combine(Application.streamingAssetsPath, "content", "members.json");
                    if (File.Exists(path)) json = File.ReadAllText(path, Encoding.UTF8);
                }
                if (string.IsNullOrEmpty(json))
                {
                    _membersError = "members.json 缺失";
                    Debug.LogWarning("[ILVES] " + _membersError);
                    return;
                }
                var roster = JsonUtility.FromJson<MemberRoster>(json);
                if (roster == null || roster.members == null || roster.members.Length == 0)
                {
                    _membersError = "members.json 为空";
                    Debug.LogWarning("[ILVES] " + _membersError);
                    return;
                }
                _members.AddRange(roster.members);
            }
            catch (Exception e)
            {
                _membersError = e.Message;
                Debug.LogError("[ILVES] 读取队员档案失败: " + e.Message);
            }
        }

        MemberData FindMember(string id)
        {
            EnsureMembers();
            for (int i = 0; i < _members.Count; i++)
                if (_members[i] != null && _members[i].id == id) return _members[i];
            return null;
        }

        void OpenProfile(string id)
        {
            _profileId = id;
            _profileScroll = 0f;   // 打开时从顶部开始
            _profileOpen = true;
        }

        void CloseProfile()
        {
            _profileOpen = false;
            _profileId = null;
            _profileScroll = 0f;
        }

        string MemberTitle(MemberData m)
        {
            if (m == null)
            {
                int idx = Array.IndexOf(GameState.MemberIds, _profileId);
                if (idx >= 0 && idx < GameState.MemberNames.Length) return GameState.MemberNames[idx] + "（档案缺失）";
                return "队员档案（缺失）";
            }
            string t = m.name;
            if (!string.IsNullOrEmpty(m.callsign)) t += " " + m.callsign;
            if (!string.IsNullOrEmpty(m.callsignCn)) t += "（" + m.callsignCn + "）";
            return t;
        }

        static string WeaponLine(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            int i = raw.IndexOf('\u3000');   // 全角空格：拆出「主武器／副武器」标签
            if (i < 0) return "<color=#D8D9CC>" + raw + "</color>";
            return "<color=#4E5037>" + raw.Substring(0, i) + "</color>\u3000<color=#D8D9CC>" + raw.Substring(i + 1) + "</color>";
        }

        string BuildProfileBody(MemberData m)
        {
            var sb = new StringBuilder();
            if (m.weapons != null && m.weapons.Length > 0)
            {
                sb.Append("<color=#4E5037>武　器</color>\n");
                for (int i = 0; i < m.weapons.Length; i++) sb.Append(WeaponLine(m.weapons[i])).Append('\n');
                sb.Append('\n');
            }
            if (m.gear != null && m.gear.Length > 0)
            {
                sb.Append("<color=#4E5037>随 身 装 备</color>\n");
                for (int i = 0; i < m.gear.Length; i++) sb.Append("<color=#8A8C7A>· ").Append(m.gear[i]).Append("</color>\n");
                sb.Append('\n');
            }
            sb.Append("<color=#4E5037>档　案</color>\n");
            sb.Append("<color=#D8D9CC>").Append(m.bio ?? "").Append("</color>\n\n");
            sb.Append("<color=#8A8C7A>").Append(m.traits ?? "").Append("</color>\n");
            return sb.ToString();
        }

        void DrawMemberProfile()
        {
            Fill(new Rect(0f, 0f, W, H), new Color(0f, 0f, 0f, 0.72f));
            // 800x600 → 860x620：更高更宽，一屏多显示 2–3 行；y 在顶栏（54px）下方，不压住标题栏
            var box = new Rect(W * 0.5f - 430f, 58f, 860f, 620f);
            Fill(box, Panel);
            Fill(new Rect(box.x, box.y, 2f, box.height), Olive);

            float pad = 32f;
            float cx = box.x + pad;
            float cw = box.width - pad * 2f;
            var m = FindMember(_profileId);

            // 标题：瓦西里 «Хозяин»（当家的）
            int titleSize = 26 + _fontLevel * 2;   // 26 / 28 / 30，随字号档放大
            var titleSt = new GUIStyle(_body) { fontSize = titleSize, wordWrap = false };
            titleSt.normal.textColor = Olive;
            float y = box.y + 20f;
            Label(new Rect(cx, y, cw - 30f, titleSize + 14f), MemberTitle(m), titleSt);

            // 三行：岗位 / 军衔 / 年龄·服役
            y += titleSize + 22f;
            var metaLabelSt = new GUIStyle(_profileMeta);
            metaLabelSt.normal.textColor = OliveDim;
            var metaValueSt = new GUIStyle(_profileMeta);
            metaValueSt.normal.textColor = Bone;
            float metaRowH = Mathf.Max(22f, _sideRowH - 2f);
            string[] metaLabels = { "岗位", "军衔", "年龄·服役" };
            string[] metaValues =
            {
                m != null ? m.role : "—",
                m != null ? m.rank : "—",
                m != null ? m.age : "—"
            };
            for (int i = 0; i < metaLabels.Length; i++)
            {
                Label(new Rect(cx, y, 104f, metaRowH), metaLabels[i], metaLabelSt);
                Label(new Rect(cx + 110f, y, cw - 110f, metaRowH), metaValues[i], metaValueSt);
                y += metaRowH;
            }

            float bodyTop = y + 8f;
            Fill(new Rect(cx, bodyTop, cw, 1f), OliveDim);
            bodyTop += 14f;
            float footerTop = box.yMax - 62f;
            var vp = new Rect(cx, bodyTop, cw, Mathf.Max(80f, footerTop - bodyTop - 8f));

            string body;
            if (m != null) body = BuildProfileBody(m);
            else
            {
                body = "<color=#D8D9CC>档案缺失</color>\n\n<color=#8A8C7A>"
                       + (string.IsNullOrEmpty(_membersError) ? "members.json 里没有这名队员的记录。" : _membersError)
                       + "</color>";
            }
            // 弹窗正文恢复滚轮滚动：用真实行距（LineHeight）对齐整行，滚到任何位置都不会切半行
            DrawScrolledText(vp, body, body, _profileBody, ref _profileScroll);

            // 右下关闭；左侧保留 ESC / 点面板外提示，关闭按钮左边提示滚动方式（不显示页码）
            var closeR = new Rect(box.xMax - pad - 124f, box.yMax - 52f, 124f, 38f);
            Label(new Rect(cx, box.yMax - 44f, 258f, 22f), "ESC 关闭  ·  点击面板外关闭", _hint);
            Label(new Rect(cx + 264f, box.yMax - 44f, closeR.x - 12f - (cx + 264f), 22f),
                  "滚轮 / PageUp / PageDown 滚动", Righted(_hint));
            if (Button(closeR, "关 闭"))
            {
                CloseProfile();
                return;
            }

            // 点面板外关闭；面板内的点击一律吞掉，避免穿透到正文（滚轮/按键由 DrawScrolledText 处理）
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                if (!box.Contains(Event.current.mousePosition)) CloseProfile();
                Event.current.Use();
            }
        }

        // ---------- 存档档位 ----------
        void DrawSlots()
        {
            bool saving = _slotMode == SlotMode.Save;
            Label(new Rect(80, 38, W - 160, 26), saving ? "保 存 进 度" : "读 取 存 档", _section);
            Label(new Rect(80, 62, W - 160, 54), saving ? "选择档位" : "选择要读取的存档",
                new GUIStyle(_title) { fontSize = 40, alignment = TextAnchor.UpperLeft });
            Label(new Rect(80, 116, W - 160, 24),
                saving ? "当前进度会写入你选择的档位；自动存档（继续游戏用）不受影响。"
                       : "读取会回到存档当时的那个决定，之后的进度作废。", _hint);

            // 单列 6 行（每行 68px）：一屏放得下，不用滚动；行窄反而给信息留出整行宽度
            const float RowH = 68f, RowGap = 8f;
            for (int i = 0; i < SlotCount; i++)
            {
                var r = new Rect(80, 152 + i * (RowH + RowGap), W - 160, RowH);
                var d = i < _slotMeta.Count ? _slotMeta[i] : null;
                Fill(r, Panel);
                Fill(new Rect(r.x, r.y, 3f, r.height), d != null ? Olive : OliveDim);

                Label(new Rect(r.x + 24, r.y + 17, 122, 32), SlotNames[i], _speaker);
                if (d == null)
                {
                    LabelClipH(new Rect(r.x + 152, r.y + 10, 640, 26), "（空档位）", _side);
                    LabelClipH(new Rect(r.x + 152, r.y + 38, 640, 22), saving ? "可以保存到此档" : "没有可读取的存档", _hint);
                }
                else
                {
                    // 秒不显示：存档时间精确到分钟就够，多出来的 :xx 只会把行挤断
                    string when = d.savedAt != null && d.savedAt.Length > 16 ? d.savedAt.Substring(0, 16) : d.savedAt;
                    LabelClipH(new Rect(r.x + 152, r.y + 8, 640, 28), d.chapterTitle + "　·　" + when, _side);
                    LabelClipH(new Rect(r.x + 152, r.y + 36, 640, 22),
                        "暴露 " + d.exposure + "　时间 " + d.minutes + " 分　情报 " + d.intel + "/6　信任 " + d.trust, _hint);
                }

                var br = new Rect(r.x + r.width - 180, r.y + 12, 170, 44);
                if (saving)
                {
                    if (Button(br, "保 存")) { SaveTo(SlotPath(i), false); RefreshSlotMeta(); }
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

                if (d != null)
                {
                    var dr = new Rect(r.x + r.width - 300, r.y + 12, 110, 44);
                    bool armed = _slotDeleteArmed == i;
                    if (Button(dr, armed ? "确认" : "删 除", armed))
                    {
                        if (armed) DeleteSlot(i);
                        else { _slotDeleteArmed = i; _transient = "再点一次「确认」删除 " + SlotNames[i]; }
                        return;
                    }
                }
                else if (_slotDeleteArmed == i)
                {
                    _slotDeleteArmed = -1;
                }
            }

            if (!string.IsNullOrEmpty(_transient))
                Label(new Rect(80, 608, W - 160, 24), _transient, _hint);

            if (Button(new Rect(W * 0.5f - 160f, 636, 320, 46), "返 回")) { _slotDeleteArmed = -1; _phase = _slotReturn; }
            Label(new Rect(0, 690, W, 22), "ESC 返回", Centered(_hint));
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

            float y = 160f;   // 第二章旗标行变多，整体上移并收一点行距，避免压到下方按钮
            y = Line(80, y, "时间　" + DisplayClockText() + "　（全战役消耗 " + _state.Minutes + " 分钟）");
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

            // 第二章的旗标（交接文档 §7 遗留）
            if (_state.GetFlag("deceived")) y = Line(80, y, "欺骗　你喂过他们一个假呼号。");
            if (_state.GetFlag("trapped")) y = Line(80, y, "阻滞　你在撤离路上留了雷。");
            if (_state.GetFlag("warned")) y = Line(80, y, "情报　你提醒过沃罗宁：他的电台被渗透了。");
            else if (_state.GetFlag("watched")) y = Line(80, y, "情报　你看出他的电台有问题，但你什么也没说。");
            if (_state.GetFlag("secret_kept")) y = Line(80, y, "秘密　旧编号的事，你选择了闭嘴。");
            else if (_state.GetFlag("reported")) y = Line(80, y, "秘密　旧编号的事，你写进了报告。");

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
            return y + 30f;
        }

        string Verdict()
        {
            int score = 0;
            if (_state.GetFlag("beacon_down")) score += 3;
            else if (_state.GetFlag("beacon_located")) score += 1;
            if (_state.GetFlag("sokol_rescued")) score += 3;
            else if (_state.GetFlag("sokol_found")) score += 1;
            if (_state.GetFlag("barasuk_saved")) score += 2;
            else if (_state.GetFlag("barasuk_partial")) score += 1;
            if (_state.GetFlag("warned")) score += 1;
            if (_state.Exposure < 25) score += 2; else if (_state.Exposure > 65) score -= 2;
            score += Mathf.Clamp(_state.Trust, -2, 2);

            // 曝光闸门：猞猁准则「潜伏优先」——"看得太清楚"时分数再高也不能给 А
            if (_state.Exposure >= 70) return "评价　Г —— 你被看得太清楚了。这一夜，林子记住了你们。";
            if (score >= 8 && _state.Exposure < 55) return "评价　А —— 你像一只猞猁：看见了，记住了，走了。";
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
                if (!_textDone) { FinishPage(); continue; }
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

        // =====================================================================
        // 文字绘制唯一入口（项目铁律）
        //
        // 所有文字都必须走 Label() / LabelClipH()：先用 GUIStyle.CalcHeight() 量出这段文字
        // 实际需要的高度，矩形不够就自动加高（居中样式上下对称加高），再画。
        // 这样无论分辨率、字号档怎么变，文字都不会被矩形下沿切掉（历史 bug 代号"遁地"）。
        //
        // 唯一允许直接 GUI.Label 的地方是本文件里带 [text-primitive] 标记的三个原语
        // （Label / LabelClipH / DrawScrolledText 的滚动裁剪层），
        // ContentCheck 会用源码 lint 卡住裸 GUI.Label —— 新增界面时不要绕开。
        // 分页高度判断不用 CalcHeight，一律走 MeasureText()（行数比例 × 实测真实行距）。
        // =====================================================================
        /// <summary>绘制文本前按实际行高扩展矩形，避免字号变大时文字底部被裁（"遁地"）。</summary>
        static void Label(Rect r, string text, GUIStyle style)
        {
            if (string.IsNullOrEmpty(text)) return;
            float need = style.CalcHeight(new GUIContent(text), Mathf.Max(1f, r.width)) + 4f;
            if (need > r.height)
            {
                bool centered = style.alignment == TextAnchor.MiddleCenter ||
                                style.alignment == TextAnchor.MiddleLeft ||
                                style.alignment == TextAnchor.MiddleRight;
                r = centered
                    ? new Rect(r.x, r.y - (need - r.height) * 0.5f, r.width, need)
                    : new Rect(r.x, r.y, r.width, need);
            }
            GUI.Label(r, text, style);   // [text-primitive]
        }

        /// <summary>
        /// 需要「只在横向裁切」的文字（例如人物栏的职位，不许压到右边的状态列）：
        /// 宽度按给定值裁，高度一律按 CalcHeight 自动加高，所以纵向永远不会切字。
        /// </summary>
        static void LabelClipH(Rect r, string text, GUIStyle style)
        {
            if (string.IsNullOrEmpty(text) || r.width <= 1f) return;
            float need = style.CalcHeight(new GUIContent(text), Mathf.Max(1f, r.width)) + 4f;
            float h = Mathf.Max(r.height, need);
            GUI.BeginGroup(new Rect(r.x, r.y, r.width, h));   // 只裁横向
            GUI.Label(new Rect(0f, 0f, style.CalcSize(new GUIContent(text)).x + 8f, h), text, style);   // [text-primitive]
            GUI.EndGroup();
        }

        /// <summary>
        /// 真实渲染高度：用 GetCursorPixelPosition 量最后一行首字的 y，再加一行行距。
        /// 这是渲染器自己的排版结果，比分页常用的 MeasureText（CalcHeight 行数比例 × 实测行距）更准；
        /// 正文分页与正文绘制都用它，避免"页高估小了 → 最后一行被选项挡住或裁掉"。
        /// </summary>
        static float TrueTextHeight(GUIStyle style, string text, float w)
        {
            if (style == null || string.IsNullOrEmpty(text)) return 0f;
            w = Mathf.Max(8f, w);
            float lh = LineHeight(style, w);
            if (lh < 1f) return MeasureText(style, text, w);
            float yLast = style.GetCursorPixelPosition(new Rect(0f, 0f, w, 100000f), new GUIContent(text), text.Length - 1).y;
            if (yLast < 0f || yLast > 90000f) return MeasureText(style, text, w);
            return yLast + lh;
        }

        /// <summary>
        /// 纵向也按视口裁切的文字（正文分页专用）：
        /// 页高已按真实高度算好，正常情况一行都不会被切；这个裁切只是兜底——
        /// 万一某页真的比视口高，也只会裁在视口下沿，**绝不压到下面的选项区**。
        /// </summary>
        static void LabelClipV(Rect r, string text, GUIStyle style)
        {
            if (string.IsNullOrEmpty(text) || r.width < 4f) return;
            float need = TrueTextHeight(style, text, r.width);
            float h = Mathf.Min(r.height, need + 2f);
            if (h < 8f) h = 8f;
            GUI.BeginGroup(new Rect(r.x, r.y, r.width, h));
            GUI.Label(new Rect(0f, 0f, r.width, need + 2f), text, style);   // [text-primitive]
            GUI.EndGroup();
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// <summary>1px 方框描边（人物栏末尾 i 按钮的框）。</summary>
        static void Outline(Rect r, Color c)
        {
            if (r.width < 2f || r.height < 2f) return;
            Fill(new Rect(r.x, r.y, r.width, 1f), c);
            Fill(new Rect(r.x, r.yMax - 1f, r.width, 1f), c);
            Fill(new Rect(r.x, r.y, 1f, r.height), c);
            Fill(new Rect(r.xMax - 1f, r.y, 1f, r.height), c);
        }

        // =====================================================================
        // 真实行距（MeasureText 用）
        // =====================================================================
        static GUIStyle _lineHeightStyle;
        static float _lineHeightCache;

        /// <summary>
        /// 从**真实排版**里量行距（不用 CalcHeight 猜）：喂一段三行文本，取第 1 行与第 3 行
        /// 光标顶点的 y 差再除以 2。CalcHeight 对中文每行会高估约 6px（实测 22px 字号 36.41 vs 30.4082），
        /// 分页/裁剪按它算就会每页少塞 1.5 行、甚至切半行。结果按 style 实例缓存，样式重建（换字号档）时重测。
        /// </summary>
        static float LineHeight(GUIStyle style, float w)
        {
            if (style == null) return 0f;
            if (ReferenceEquals(style, _lineHeightStyle) && _lineHeightCache > 0f) return _lineHeightCache;

            var probe = new GUIContent("国\n国\n国");
            var big = new Rect(0f, 0f, Mathf.Max(8f, w), 10000f);
            float y0 = style.GetCursorPixelPosition(big, probe, 0).y;
            float y2 = style.GetCursorPixelPosition(big, probe, 4).y;   // 第三行首字
            float lh = (y2 - y0) * 0.5f;
            float calc = style.CalcHeight(new GUIContent("国"), Mathf.Max(8f, w));
            // 每个样式实例只量一次（缓存未命中时打印，便于核对真实行距）
            Debug.Log("[ILVES-LINE] lineH=" + lh + " calcHeight=" + calc
                      + " ratio=" + (calc > 0f ? lh / calc : 0f) + " fontSize=" + style.fontSize);
            if (lh < 8f || lh > 400f) lh = Mathf.Max(8f, calc);
            _lineHeightStyle = style;
            _lineHeightCache = lh;
            return lh;
        }

        // =====================================================================
        // 滚轮滚动（只给队员档案弹窗用）：按真实行距对齐整行，绝不切半行
        // =====================================================================
        const float ScrollBarW = 8f;

        /// <summary>返回真实行距，同时给出首行相对文本框顶部的偏移（拿不到偏移就按 0）。</summary>
        static float LineGrid(GUIStyle style, float w, out float topOffset)
        {
            var probe = new GUIContent("国\n国\n国");
            var big = new Rect(0f, 0f, Mathf.Max(8f, w), 10000f);
            topOffset = style.GetCursorPixelPosition(big, probe, 0).y;
            if (topOffset < 0f || topOffset > 200f) topOffset = 0f;
            return LineHeight(style, w);
        }

        /// <summary>
        /// 在 viewport 里绘制可滚动的长文，返回最大滚动量。
        /// 两条硬约束，保证滚到任何位置都不切半行：
        ///   1) 可视高度 innerH = 首行偏移 + floor((视口高 - 首行偏移) / 真实行距) × 真实行距；
        ///   2) 滚动量吸附到真实行距的整数倍。
        /// 行距来自 LineHeight()（GetCursorPixelPosition 实测），不再用 CalcHeight 猜。
        /// </summary>
        static float DrawScrolledText(Rect viewport, string full, string visible, GUIStyle style, ref float scroll, bool handleInput = true)
        {
            if (style == null || viewport.width < 8f || viewport.height < 8f) { scroll = 0f; return 0f; }
            if (string.IsNullOrEmpty(full) && string.IsNullOrEmpty(visible)) { scroll = 0f; return 0f; }

            float w = Mathf.Max(8f, viewport.width - ScrollBarW);
            float topOffset;
            float lineH = LineGrid(style, w, out topOffset);
            if (lineH < 1f) { scroll = 0f; return 0f; }

            float usable = Mathf.Max(lineH, viewport.height - topOffset);
            float innerH = topOffset + Mathf.Max(lineH, Mathf.Floor(usable / lineH) * lineH);
            float lineArea = Mathf.Max(0f, innerH - topOffset);                        // 能显示的整行总高
            string text = full ?? "";
            // 真实渲染高度＝最后一行首字的 y + 一行高（GetCursorPixelPosition 是渲染器自己的排版，
            // 不受 CalcHeight 对中文行距高估/低估的影响）。取三种口径的最大值，宁可多留空白也不切字。
            float trueH = 0f;
            if (text.Length > 0)
            {
                var content = new GUIContent(text);
                var bigRect = new Rect(0f, 0f, w, 100000f);
                float yLast = style.GetCursorPixelPosition(bigRect, content, text.Length - 1).y;
                if (yLast >= 0f && yLast < 100000f) trueH = yLast + lineH;
            }
            float contentH = Mathf.Max(lineH, Mathf.Max(trueH, Mathf.Round(MeasureText(style, text, w) / lineH) * lineH));
            contentH = Mathf.Round(contentH / lineH) * lineH;
            float maxScroll = Mathf.Max(0f, contentH - lineArea);

            if (handleInput)
            {
                var e = Event.current;
                if (e.type == EventType.ScrollWheel && viewport.Contains(e.mousePosition))
                {
                    // 方向按 Windows 实测校准（2026-10-30）：滚轮**向上**时 Event.delta.y 为**负**。
                    // 向上滚应该回看前面的内容 → scroll 减小，所以这里是 +=（之前写反过，用户报过"方向是反的"）。
                    if (Mathf.Abs(e.delta.y) > 0.001f) { scroll += Mathf.Sign(e.delta.y) * lineH * 1.5f; e.Use(); }
                }
                else if (e.type == EventType.KeyDown)
                {
                    if (e.keyCode == KeyCode.PageDown) { scroll += lineArea; e.Use(); }
                    else if (e.keyCode == KeyCode.PageUp) { scroll -= lineArea; e.Use(); }
                    else if (e.keyCode == KeyCode.DownArrow) { scroll += lineH; e.Use(); }
                    else if (e.keyCode == KeyCode.UpArrow) { scroll -= lineH; e.Use(); }
                }
            }

            scroll = Mathf.Clamp(scroll, 0f, maxScroll);
            // 吸附到真实行网格：只要 scroll 是 lineH 的整数倍、innerH 也落在网格上，
            // 视口底部就永远是一条完整的行边界，最后一行不会被切半截
            scroll = Mathf.Clamp(Mathf.Round(scroll / lineH) * lineH, 0f, maxScroll);

            GUI.BeginGroup(new Rect(viewport.x, viewport.y, viewport.width - ScrollBarW, innerH));   // 裁剪层：只露整行
            GUI.Label(new Rect(0f, -scroll, w, contentH + topOffset + lineH), visible ?? "", style);   // [text-primitive]
            GUI.EndGroup();

            if (maxScroll > 0.5f) DrawScrollBar(viewport, scroll, maxScroll, innerH);
            return maxScroll;
        }

        /// <summary>自绘细滚动条（橄榄绿滑块），不用 GUI.skin 默认的浅灰滚动条。</summary>
        static void DrawScrollBar(Rect viewport, float scroll, float maxScroll, float innerH)
        {
            float sx = viewport.xMax - 2f;
            Fill(new Rect(sx, viewport.y, 2f, innerH), new Color(0.14f, 0.145f, 0.11f));
            float content = innerH + maxScroll;
            float thumbH = Mathf.Min(innerH, Mathf.Max(26f, innerH * (innerH / content)));
            float t = maxScroll > 0f ? Mathf.Clamp01(scroll / maxScroll) : 0f;
            float thumbY = viewport.y + (innerH - thumbH) * t;
            Fill(new Rect(sx, thumbY, 2f, thumbH), Olive);
        }
    }
}
