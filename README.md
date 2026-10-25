# ILVES · 猞猁小队

> 一款以**文字与决策**为主的 2D 战术指挥游戏。
> 你是一支九人侦察小队的队长——你不扣扳机，你下命令。

**看，记住，然后离开。**

![标题画面](docs/screens/01-title.png)

---

## 现在可以玩的东西（v0.1 demo）

| 项 | 说明 |
|---|---|
| 运行 | `Builds/Windows/Ilves.exe`（Windows x64） |
| 内容 | **序章「入队」+ 第一章「边境线上的眼睛」**，约 15–20 分钟 |
| 形态 | 文本 + 决策 + 数值；无战斗操作、无美术资源（原型阶段） |
| 操作 | 任意键开始 · **1–4 数字键**选选项 · 空格/点击继续 · F11 全屏 · Esc 退出 |

![正文与小队状态](docs/screens/02-story.png)

![决策](docs/screens/03-choice.png)

![复盘](docs/screens/04-debrief.png)

> 这一版是**文本原型**：用 IMGUI 直接绘制界面，内容全部来自 `Assets/StreamingAssets/content/*.json`。
> 目的不是好看，是先把"指挥—博弈—复盘"的手感做出来给人试。

---

## 游戏是什么

- **章节制**：全篇计划 **5–7 章**，每章 15–30 分钟，**强连续**——上一章的伤员、暴露、信任与选择会带进下一章。
- **指挥而非作战**：任务以潜伏、侦察、与友军队伍配合为主；自家小队不以交火为主，**交火是失败后的补救**（"猞猁准则"）。
- **核心是博弈**：与一个同样在推断你的对手指挥官博弈、与环境（冷、湿、虫、距离）博弈、与自己九个有裂缝的人博弈。
- **文字优先**：先用简洁文字让玩家认识九个人，再在相处中逐渐了解他们。
- **保留俄语术语**（呼号、军语、番号），但**首次出现时全部在括号里解释**。

设定里的一句话是设计底线：**在这片林子里，最先要打败的从来不是人，是冷、是湿、是虫、是距离。**

---

## 技术

| 项 | 值 |
|---|---|
| 引擎 | Unity 6.3 LTS（`6000.3.27f1`） |
| 平台 | Windows x64（开发期 Mono） |
| 架构 | `Ilves.Core` 纯 C# 逻辑（无 UnityEngine 依赖）+ `Ilves.UI` 表现层 |
| 内容 | `Assets/StreamingAssets/content/*.json`（章节 / 节点 / 选项 / 效果），改文本不用改代码 |
| 界面 | 当前为 IMGUI 原型；正式版会换成 uGUI + TextMeshPro（中文字库） |
| 校验 | `Ilves.EditorTools.ContentCheck`：无头检查跳转、可达性、跑通全流程 |

### 效果 DSL（写在 JSON 的 `effects` 里）

```
exposure+5    暴露度（0–100）      time+15   消耗分钟
intel+1       情报完整度（0–6）    trust-1   队伍信任（-5–5）
flag.beacon_down=true             置标志（用于分支/复盘）
member.igor=受伤                   队员状态（侧栏显示）
```

---

## 目录

```
Assets/
  Scripts/Core/        StoryModel / StoryEngine / GameState / EffectParser（纯逻辑）
  Scripts/UI/          DemoBootstrap（IMGUI 原型界面）
  Editor/              DemoBuild（建场景+打包）/ ContentCheck（内容校验）
  StreamingAssets/
    content/           prologue.json / chapter01.json（文本内容）
  Scenes/Main.unity
docs/
  游戏构想.md          玩法与形态（唯一事实源）
  设定集/              世界观、九人档案、队徽与视觉规范
  命名提案.md          为什么叫 ILVES
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

私有 → 现已公开，但**代码、设定、队徽与美术素材版权归作者所有**，未经许可不得复制或商用。
