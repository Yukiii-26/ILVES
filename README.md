# ILVES · 猞猁小队

> 一款以**文字与决策**为主的 2D 战术指挥游戏。
> 你是一支九人侦察小队的队长——你不扣扳机，你下命令。

**看，记住，然后离开。**

![标题画面](docs/screens/01-title.png)

---

## 现在可以玩（v0.3.0）

| 项 | 说明 |
|---|---|
| 下载 | [Releases &#8594; v0.3.0](https://github.com/Yukiii-26/ILVES/releases/tag/v0.3.0)（Windows x64 zip，约 130 MB） |
| 美术 | 主界面用的是正式 key art：泰加林黄昏的九人与载具（背景）＋ 橄榄绿标题艺术字 ＋ 独占的 ILVES 应用图标。正文/简报/复盘界面**仍是单色 IMGUI**，照片只做「进门那一张脸」 |
| 运行 | 解压后 `Ilves.exe`；或本地构建 `Builds/Windows/Ilves.exe` |
| 内容 | **序章 + 第一章 + 第二章**，首次约 45–60 分钟 |
| 音乐 | **3 首**（主界面 1 + 游戏内 2，游戏内按章节奇偶轮换），循环播放；音量四档（关 / 低 / 中 / 高，默认「中」） |
| 存档 | **6 个手动档位**（保存/读取都要选档，**可删除**）＋ 每章开始自动存档（供「继续游戏」） |
| 设置 | **两栏**（左栏目 / 右内容）：画面 = 1280×720 / 1920×1080 / 2560×1440、窗口 / 全屏、界面字号（小 / 标准 / 大，默认标准）；声音 = 音乐音量四档与当前曲目；其它 = 版本号、平台、存档路径、「声明」入口、项目主页 |

**操作**

| 键 | 作用 |
|---|---|
| 启动 | 先弹「基本提示」；点「我已知悉」或按 空格 / 回车 进入主界面 |
| 任意键 / 点击 | 开始 |
| 1–4 | 选择选项 |
| 空格 / 回车 | 继续 / 开始任务 |
| **Backspace** 或界面「← 退回」 | **退回一步（最多两步）** |
| **Esc** | 暂停菜单（继续 / 保存到档位 / 读取存档 / 设置 / 返回主界面 / 退出） |
| F11 | 全屏切换 |

启动先弹「基本提示」（四条 + 项目主页按钮）；主界面「声明」入口是它的可滚动详细版。

![基本提示](docs/screens/12-notice.png)

![声明](docs/screens/13-declaration.png)

![正文与小队状态](docs/screens/02-story.png)

![任务简报](docs/screens/06-briefing.png)

![决策](docs/screens/03-choice.png)

![暂停菜单](docs/screens/07-pause.png)

![存档档位](docs/screens/08-slots.png)

![设置](docs/screens/05-settings.png)

![复盘](docs/screens/04-debrief.png)

---

## 游戏是什么

- **全篇 6 章 + 序章**，每章 15–30 分钟，**强连续**：伤员、暴露、信任与关键选择跨章延续，故事线不重启。
- **指挥而非作战**：任务以潜伏、侦察、与友军队伍配合为主；自家小队不以交火为主，**交火是失败后的补救**（「猞猁准则」）。
- **核心是博弈**：与一个同样在推断你的对手指挥官博弈、与环境（冷、湿、虫、距离）博弈、与自己九个有裂缝的人博弈。
- **文字优先**：先用简洁文字让玩家认识九个人，再在相处中逐渐了解他们。
- **保留俄语术语**（呼号、军语、番号），首次出现时在括号里解释。

> 全篇章节规划与剧情大纲：[docs/游戏大纲.md](docs/游戏大纲.md)

---

## 技术

| 项 | 值 |
|---|---|
| 引擎 | Unity 6.3 LTS（`6000.3.27f1`） |
| 平台 | Windows x64（已发布）。Android 构建流程 2026-10-26 已打通（arm64-v8a / IL2CPP），但 **2026-10-30 起负责人要求暂停 APK 更新**，暂不再出包 |
| 架构 | `Ilves.Core` 纯 C# 逻辑（无 UnityEngine 依赖）+ `Ilves.UI` 表现层 |
| 内容 | `Assets/Resources/content/*.json`（章节 / 节点 / 选项 / 效果；Resources 以便 Android 读取） |
| 存档 | `%USERPROFILE%\AppData\LocalLow\Understory\Ilves\save_slot1..6.json`（手动）＋ `save_auto.json`（自动） |
| 校验 | `Ilves.EditorTools.ContentCheck`：跳转、可达性、全流程、存档往返、简报检查、音乐资源与导入参数、合规文案 |

### 效果 DSL（写在 JSON 的 `effects` 里）

```
exposure+5    暴露度（0–100）      time+15   消耗分钟
intel+1       情报完整度（0–6）    trust-1   队伍信任（-5–5）
flag.beacon_down=true             置标志（用于分支 / 复盘 / 跨章延续）
member.igor=受伤                   队员状态（侧栏显示）
```

---

## 目录

```
Assets/
  Scripts/Core/        StoryModel / StoryEngine / GameState / EffectParser / SaveCodec（纯逻辑）
  Scripts/UI/          DemoBootstrap（IMGUI 原型界面：主菜单 / 基本提示 / 声明 / 设置 / 简报 / 正文 / 暂停 / 复盘）
  Editor/              DemoBuild（建场景 + 打包 + 图标）/ ContentCheck（内容与存档校验）
                       ArtImportSettings（主界面三张图的导入规格）
                       AudioImportSettings（音乐导入规格：Streaming / Vorbis q0.7 / PreserveSampleRate）
  Resources/
    content/           prologue.json / chapter01.json / chapter02.json
    fonts/             Noto Sans CJK SC（内置中文字体，OFL）
    music/             title.flac / story-1.flac / story-2.flac（三首第三方音乐，Streaming + Vorbis）
    emblem.png         队徽（线稿生成透明通道版）
    art/title-logo.png 标题艺术字（黑底键成透明的橄榄绿 ILVES 字标）
    art/title-bg.jpg   主界面背景（泰加林黄昏的九人与载具）
  Art/icon.png         应用图标母版（1024×1024，构建期生成全部平台槽位）
  Scenes/Main.unity
docs/
  游戏大纲.md          全篇 6 章结构与剧情大纲
  游戏构想.md          玩法与形态（唯一事实源）
  设定集/              世界观、九人档案、队徽与视觉规范（§3.5 = 标题字标 / 背景 / 图标；§3.6 = 音乐）
     assets/           三版队徽 + 标题艺术字 + 主界面背景 + 应用图标的原始素材
  初始化记录.md
  交接文档.md          给下一个会话的交接（先读这份）
  screens/             demo 截图
```

## 构建

```powershell
# 内容校验（无头）
Unity.exe -batchmode -projectPath . -executeMethod Ilves.EditorTools.ContentCheck.Run -logFile Logs/check.log

# 打包 Windows demo
Unity.exe -batchmode -projectPath . -executeMethod Ilves.EditorTools.DemoBuild.BuildDemo -logFile Logs/build.log
```

## 音乐与版权

| 曲目 | 作者 | 用途 | 时长 |
|---|---|---|---|
| Ripples | Sean Oban | 主界面（含设置 / 存档 / 声明等所有非游玩界面） | 2:12 |
| Onthou | Ever So Blue | 游戏内音乐一（序章、第二章…偶数章） | 2:08 |
| Veloma | Fabrizio Paterlini | 游戏内音乐二（第一章…奇数章） | 2:48 |

工程内是三首 FLAC（`Assets/Resources/music/`）：Unity 6 直接导入，构建时重新编码为 Vorbis，播放走 Streaming。

音乐版权**归原作者所有**，本作仅作**非商业使用**；若权利人认为使用不当，可通过 GitHub 联系移除。游戏内主界面 &#8594;「声明」页是正式声明位置，含完整曲目表与上述说明。

## 版权

队徽、标题艺术字、主界面背景、应用图标，以及代码与文字的版权归作者所有，未经许可不得复制或商用（音乐版权归原作者，见上一节）。

---

## Android（已打通，但暂停更新）

构建入口：`Ilves.EditorTools.DemoBuild.BuildAndroidDemo`（ARM64 / IL2CPP / 横屏 / `com.understory.ilves`，产物 `Builds/Android/Ilves.apk`）。

2026-10-26 曾把 `Failed to update Android SDK` 的问题彻底修好（根因是 SDK 根目录遗留的旧 `source.properties` 与重复解压目录、外加 NDK 版本不对），当时成功出过 APK（约 30 MB / arm64-v8a / versionCode 3）。修法记在 `docs/交接文档.md` §7。

**2026-10-30 起：负责人要求暂停所有 Android APK 更新，不再构建、不再装包。** Windows 是唯一发布平台；iOS/Android 何时恢复由负责人决定。