# 贡献指南

感谢你愿意为真核启动器贡献代码。第一次参与请先读这篇，能省掉大量来回沟通。

---

## 一、开工前必读

### 1. 本项目是 GPL-3.0

你提交的代码**默认会以 GPL-3.0 授权发布**。如果不同意，请不要提交 Pull Request。
PR 合并即视为你同意将自己的贡献以 GPL-3.0 授权给本项目。

### 2. 合规红线（触碰一律拒收）

这是 Minecraft 第三方启动器的生存底线，不是形式主义：

| 禁止 | 说明 |
|---|---|
| ❌ 打包 / 分发 Minecraft 游戏本体 | 本仓库永远不得出现 `.minecraft` 里的 jar、assets、libraries |
| ❌ 使用 Minecraft 官方素材 | 包括 Logo、Steve / Alex、游戏内纹理。品牌标识只能用自绘 Logo |
| ❌ 破解登录 / 伪造会话 | 不得绕过正版验证去登正版服务器 |
| ❌ 硬编码别人的 Client ID | 微软登录的 Azure 应用必须自己申请，**严禁抄 PCL2 / HMCL 的** |
| ❌ 付费墙 / 订阅 / 强制打赏 | GPL-3.0 允许收费分发源码，但不得限制用户的基本使用 |

**绝对不要提交任何密钥**：Azure Client Secret、API Token、本机用户名绝对路径。

### 3. 别把 .NET 单文件路径外泄 `.gitignore` 未覆盖的产物

`publish/`、`dist/`、`runtime/`、`.minecraft/` 都不能进仓库。`.gitignore` 已配好，提交前请确认：

```bash
git status          # 仔细看一眼有没有多余文件
git diff --check    # 检查空白错误
```

---

## 二、开发环境

| 组件 | 版本 |
|---|---|
| Windows | 10 1607+（WPF 项目只能在 Windows 上构建） |
| .NET SDK | **8.0**（LTS） |
| IDE | Visual Studio 2022 或 Rider，VS Code 亦可（无 XAML 设计器） |

```bash
git clone https://github.com/EukaryDeveloper/Eukary-Launcher.git
cd Eukary-Launcher
dotnet restore
dotnet build -c Release
```

---

## 三、分支与提交规范

### 分支

- `main` —— 唯一主干，**永远保持可构建**。不直接往 main 推，一律走 PR。
- 功能分支：`feat/<m里程碑>-<简述>`，例如 `feat/m2-msa-login`
- 修复分支：`fix/<简述>`，例如 `fix/authcodecheck-403-fallback`
- 文档 / 杂项：`docs/`、`chore/`

### 提交信息

**直接用一句中文说清做了什么，不加任何前缀**（如 `chore:` / `feat:`）——
仓库首页的文件列表会显示每个文件最后一条提交信息，干净的可读性比形式化的类型标记更重要。

正文可补充背景与原因：

```
给 DownloadService 加官方源兜底

BMCLAPI 对 oshi-core 返回 403，导致部分库下载失败。
现在每个 DownloadTask 都能配 FallbackUrls，主源失败自动切备源。

Closes #12
```

**一行 TODO 就走的 commit 不要超过一个 PR 的三分之一**，粒度太碎会让 review 无法进行。

---

## 四、代码风格约定

这些是本项目已经定下来的规矩，请保持一致：

- **Core 零 UI 依赖**。`EukaryLauncher.Core` 不知道 WPF 存在，路径规则集中在 `GamePaths`。
- **颜色 / 圆角 / 字号 / 间距 / 时长一律从 `Themes/Tokens.xaml` 取**，控件里禁止硬编码。
- **动效统一 `cubic-bezier(0.2,0.8,0.2,1)`**，用自写的 `CubicBezierEase`，不要自己造 `KeySpline`。
- **启动参数必须走 `ProcessStartInfo.ArgumentList`**，不要拼字符串（路径带空格会炸）。
- 公共 API 写 XML doc 注释；私有实现随缘。
- 新能力优先在 Core 里做，再接 UI——UI 层应尽量薄。

---

## 五、改了代码要自测

```bash
# 必跑：纯逻辑断言，不需要网络，秒级完成
dotnet run --project EukaryLauncher.Smoke

# 改了下载 / 安装逻辑，跑真下载
dotnet run --project EukaryLauncher.Smoke -- --install 1.20.4

# 改了启动参数 / natives 处理，跑端到端真启动
dotnet run --project EukaryLauncher.Smoke -- --launch 1.20.4
```

CI 会自动跑上面第一条。改动涉及下载器时，请**至少手动验证一次老 MC 版本**（如 1.7.10 / 1.12.2）——
现代版本（1.19+）与原 natives classifier 机制的老版本行为完全不同，最容易漏。

---

## 六、Pull Request 流程

1. Fork 本仓库，建分支
2. 改代码 + 自测通过
3. 提交时按上面的规范写 commit message
4. 发起 PR，目标分支 `main`，**填完整 PR 模板**（特别是「你怎么测的」那一栏）
5. CI 全绿后等待 review

### Review 关注点

我们会重点看：是否触碰合规红线、是否破坏 Core 的零 UI 依赖、是否绕过 Tokens、有没有引入不必要的第三方依赖。

**不要在一次 PR 里同时做「重构 + 加功能」**，请拆开——重构类的 PR 需要更仔细的 diff 审查。

---

## 七、Issue 怎么提

- 提 Bug 请带上：启动器版本、Windows 版本、**游戏版本**、Java 版本、复现步骤、日志片段（`%LOCALAPPDATA%\EukaryLauncher\logs\`）
- 提新功能请先说明**你要解决什么问题**，再描述方案。直接甩方案我们很难判断优先级
- 提之前先搜一下现有 Issue，重复的直接 +1 就行

---

## 八、里程碑

当前进度见 [README](README.md)。M2（微软账号登录）正在推进，
想接手某个里程碑请先在 Issue 里认领，避免和别人撞车。

---

有问题欢迎在 [Discussions](https://github.com/EukaryDeveloper/Eukary-Launcher/discussions) 里聊，不用拘束。
