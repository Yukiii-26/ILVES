# ILVES · 猞猁小队

> 一款以**文字与决策**为主的 2D 战术指挥游戏。
> 你是一支九人侦察小队的队长——你不扣扳机，你下命令。

**看，记住，然后离开。**

![标题画面](docs/screens/01-title.png)

---

## 现在可以玩（v0.2 demo）

| 项 | 说明 |
|---|---|
| 运行 | `Builds/Windows/Ilves.exe`（Windows x64） |
| 内容 | **序章 + 第一章 + 第二章**，首次约 45–60 分钟 |
| 存档 | 手动存档（暂停菜单）＋ 每章开始自动存档 |
| 设置 | 1280×720 / 1920×1080 / 2560×1440，窗口 / 全屏 |

**操作**

| 键 | 作用 |
|---|---|
| 任意键 / 点击 | 开始 |
| 1–4 | 选择选项 |
| 空格 / 回车 | 继续 / 开始任务 |
| **Backspace** 或界面「← 退回」 | **退回一步（最多两步）** |
| **Esc** | 暂停菜单（继续 / 保存 / 设置 / 返回主界面 / 退出） |
| F11 | 全屏切换 |

![正文与小队状态](docs/screens/02-story.png)

![任务简报](docs/screens/06-briefing.png)

![决策](docs/screens/03-choice.png)

![暂停菜单](docs/screens/07-pause.png)

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
| 平台 | Windows x64（开发期 Mono） |
| 架构 | `Ilves.Core` 纯 C# 逻辑（无 UnityEngine 依赖）+ `Ilves.UI` 表现层 |
| 内容 | `Assets/StreamingAssets/content/*.json`（章节 / 节点 / 选项 / 效果） |
| 存档 | `%USERPROFILE%\AppData\LocalLow\Understory\Ilves\save01.json` |
| 校验 | `Ilves.EditorTools.ContentCheck`：跳转、可达性、全流程、存档往返、简报检查 |

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
  Scripts/UI/          DemoBootstrap（IMGUI 原型界面：菜单 / 简报 / 正文 / 暂停 / 复盘）
  Editor/              DemoBuild（建场景 + 打包）/ ContentCheck（内容与存档校验）
  StreamingAssets/
    content/           prologue.json / chapter01.json / chapter02.json
  Scenes/Main.unity
docs/
  游戏大纲.md          全篇 6 章结构与剧情大纲
  游戏构想.md          玩法与形态（唯一事实源）
  设定集/              世界观、九人档案、队徽与视觉规范
  工程初始化记录.md
  screens/             demo 截图
```

## 构建

```powershell
# 内容校验（无头）
Unity.exe -batchmode -projectPath . -executeMethod Ilves.EditorTools.ContentCheck.Run -logFile Logs/check.log

# 打包 Windows demo
Unity.exe -batchmode -projectPath . -executeMethod Ilves.EditorTools.DemoBuild.BuildDemo -logFile Logs/build.log
```

## 版权

代码、设定、队徽与美术素材版权归作者所有，未经许可不得复制或商用。
