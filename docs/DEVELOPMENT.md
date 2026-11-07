# 二次开发指南

这份文档面向想在本项目上继续做功能的人：怎么分层、改哪里、怎么验证、哪些坑已经踩过。
如果你只是想编译跑起来，看 [README「构建与运行」](../README.md) 就够；
想提 PR，先读 [CONTRIBUTING.md](../CONTRIBUTING.md)。

---

## 一、先明确边界

| 项目 | 职责 | 硬性约束 |
|---|---|---|
| `EukaryLauncher.Core` | 清单解析、下载、安装、参数拼装、进程启动、加载器元数据 | **零 UI 依赖**，不 `using System.Windows`；一切路径走 `GamePaths` |
| `EukaryLauncher.App` | WPF 界面、ViewModel、队列、设置持久化、Toast | 颜色/圆角/字号/间距只从 `Themes/Tokens.xaml` 取 |
| `EukaryLauncher.Smoke` | 纯逻辑断言 + 端到端下载/启动 | 纯逻辑部分必须秒级、不联网 |

判断代码该放哪：**能不能在没有窗口的机器上跑通？** 能，就属于 Core。
新增能力优先在 Core 里做成一个可测的服务，UI 层只负责喂参数和显示进度。

---

## 二、第一次跑起来

```bash
# 依赖 .NET 8 SDK（Windows 上构建，WPF 无法跨平台编译）
dotnet build EukaryLauncher.sln -c Debug
dotnet run --project EukaryLauncher.App

# 纯逻辑断言（秒级，改完就跑）
dotnet run --project EukaryLauncher.Smoke

# 端到端：真下载 + 真启动 1.20.4（需要网络，90 秒后自动结束进程）
dotnet run --project EukaryLauncher.Smoke -- --launch 1.20.4
```

运行期数据都在 `%LOCALAPPDATA%\EukaryLauncher\`：

```
settings.json    全局设置
logs\            日志（排障第一现场）
runtime\         自托管 JRE
.minecraft\      默认游戏目录（versions / libraries / assets / mods）
```

---

## 三、关键数据流

### 服务单例

没有引入 DI 容器，`AppServices` 就是极简服务定位器：

```csharp
AppServices.Settings    // settings.json 的读写与迁移
AppServices.Context     // GameContext：路径 / 镜像 / 安装状态 / HttpClient
AppServices.Queue       // DownloadManager：下载队列，最多 3 并发
AppServices.ModIndex    // mods-index.json：记录启动器装过哪些 mod
AppServices.Toasts      // Toast 服务
AppServices.Post(...)   // 把动作切回 UI 线程
```

需要弹窗、切页、收窗口这类"只有窗口才知道怎么做"的动作，用 `AppServices` 上的委托
（`ShowModalConfirm` / `OpenVersionSelect` / `NavigateToConsole` / `ApplyLauncherVisibility`），
由 `MainWindow` 在启动时赋值——ViewModel 里不要直接引用窗口类型。

### 装一个版本到进游戏

```
VanillaTabViewModel          「继续」
  → LoaderSelectViewModel    选加载器，点「开始下载」
  → AppServices.Queue.Enqueue(DownloadJobs.Vanilla / FabricLoader / Forge / NeoForge / OptiFine)
  → DownloadManager.PumpAsync 空槽就认领（同一 Post 里置 Running，防重复认领）
  → job.Runner(job, ct)       Job 内分步骤：AddStep / Report(步骤, 百分比, 文案, 状态)
  → GameContext.Installer    写版本 JSON、下库、下 assets、下主 jar
  → MarkIsolation            按当前隔离设置写 versions/<id>/eukary.json 旗标
  → ctx.RefreshInstalled()   刷新已安装列表，UI 上的「已装」标记随之更新
```

启动链路：

```
HomeViewModel.LaunchCommand
  → GameContext.LoadInstalledVersion(id)    读本地 JSON（含 inheritsFrom 合并）
  → JavaManager.EnsureJavaAsync             缺 JRE 就下 Adoptium
  → ArgumentBuilder.BuildJvmArgs / BuildGameArgs
  → GameLauncher.Start                      ProcessStartInfo.ArgumentList 启动
  → GameLauncher.WaitForMainWindowAsync     轮询到游戏窗口真的出现
  → AppServices.ApplyLauncherVisibility     按设置隐藏/最小化/关闭启动器
```

---

## 四、常见扩展点

### 4.1 给下载页加一个标签

1. 建 `XxxTabViewModel : ViewModelBase`（需要加载数据就重写 `OnNavigatedAsync`）和对应 View；
2. `Views/DownloadsView.xaml` 资源区加 `DataTemplate DataType="{x:Type vm:XxxTabViewModel}"`；
3. `DownloadsViewModel` 里三处同步改：
   - `Tabs` 数组插标签名
   - `CurrentContent` 的 `switch (SelectedTabIndex)` 加分支
   - `OnSelectedTabIndexChanged` 里按需触发 `OnNavigatedAsync`
4. 需要二级页面（像加载器选择、模组详情）就照 `LoaderSelect` 的写法：一个可空子视图字段，
   `CurrentContent` 优先返回它，返回时置 null。

> `Tabs` 的下标和 `CurrentContent` 的分支是强耦合的，插在中间要把后面所有 case 一起改。

### 4.2 支持一种新的模组加载器

1. `LoaderIds` 加常量与 `DisplayName`；
2. `LoaderService.GetKnownLoaders` 加进去，`LoaderSelectViewModel` 的互斥矩阵说明它和谁能共存；
3. `LoaderMetaService` 提供版本列表与安装器地址（能拿到官方安装器 CLI 就复用 Forge 那套写法）；
4. `DownloadJobs` 加一个工厂方法，骨架照 `FabricLoader`：`AddStep` → `Report` → `Runner`，
   结尾一定要 `ctx.RefreshInstalled()` + `MarkIsolation(...)`；
5. `FolderVersionScanner` 的加载器识别要能把这个版本认出来，否则主页只显示游戏版本号；
6. `GameContext.IsLoaderInstalled / InstalledLoaders` 补判定，模组安装前的校验依赖它；
7. `EukaryLauncher.Smoke` 里加断言，至少覆盖"版本 id 拼对"和"识别得出加载器"。

### 4.3 加一个设置项

1. `LauncherSettings` 加属性并给默认值（旧 `settings.json` 缺字段时自然取默认，不需要写迁移，
   除非语义变了——参考 `KeepLauncherOpen → LauncherVisibility` 的迁移）；
2. `SettingsViewModel` 加 `[ObservableProperty]`，在保存路径里落盘；需要二次确认就调
   `AppServices.ShowModalConfirm`（`HideCancel: true` 可做纯提示弹窗）；
3. `SettingsView.xaml` 用现成的信息流行：组内首行 `SettingsRowFirst`、中间 `SettingsRow`、
   末行 `SettingsRowLast`、单行组 `SettingsRowSingle`；
4. **一定要接到启动/安装链路上**（`LaunchOptions` / `ArgumentBuilder` / `GamePaths` / `Installer`）。
   只存不用的设置项是 bug，不是功能。

### 4.4 加一个主页面

1. `NavPage` 枚举加值（顺序 = 侧栏顺序 = 轨道顺序）；
2. `MainViewModel.NavItems` 加条目，`OnCurrentPageChanged` 的 switch 加分支；
3. `MainWindow.xaml` 的 `PageRail` 里加一个 `ContentControl`，
   `Height="{Binding ActualHeight, ElementName=PageHost}"`，并在资源区加 `DataTemplate`；
4. `MainWindow.xaml.cs` 里有一份页面顺序数组，要和枚举顺序保持一致，切页动画靠它算偏移；
5. 页面里读目录、重建列表、探测 Java 这类重活写进 `OnNavigatedAsync`，
   它会被延后到转场动画落定后才执行——别挪到构造函数里，那样会卡第一帧。

### 4.5 加一个下载源

1. 实现 `IMirrorService`（清单 / 版本 JSON / 主 jar / 库 / 资产索引 / 资产对象 六个 URL 变换）；
2. `MirrorFactory.All` 里注册，设置页下拉框自动出现；
3. 不要在 UI 层或 Job 里手拼镜像地址——`DownloadService` 已经支持每任务 `FallbackUrls`，
   主源 403/404 会自动切备源。

### 4.6 改视觉

- 颜色、圆角、字号、间距、动画时长**只改 `Themes/Tokens.xaml`**；
- 新样式组追加到 `Themes/Controls.xaml` **末尾**——`BasedOn="{StaticResource X}"` 前向引用
  会让整个资源字典加载失败（启动即崩，异常名 `StaticResourceHolder`）；
- 要做颜色动画，就动模板里实例化的 `<SolidColorBrush>`；直接动 Tokens 里的共享画刷会把
  所有用到它的控件一起染色；
- 圆角容器里想"贴边变背景"要自己算内层圆角（外层半径 − 边框宽度），
  WPF 的 `ClipToBounds` 不会按 `CornerRadius` 裁剪子元素。

---

## 五、必须遵守的约定

1. **后台线程不许碰 UI 集合**。`ObservableCollection` 只能在 UI 线程改，否则 ItemsControl
   计数错乱，之后任何刷新都会抛「与其项源不一致」，还可能把全局异常处理器二次引爆导致进程直接消失。
   用 `AppServices.Post(() => ...)`。
2. **`new Progress<T>()` 在后台线程创建会同步回调**（没有 SynchronizationContext），
   等于还是跨线程。一律用 `DownloadManager.UiProgress<T>()` 或 `RouteToSingleStep(job, index)`。
3. **全局异常处理器里每一步都要自己 try/catch**。在 `DispatcherUnhandledException` 里再抛异常
   没人接得住，CLR 直接终止进程，表现就是"软件莫名自动关闭"。
4. **启动参数走 `ProcessStartInfo.ArgumentList`**，不拼字符串——路径带空格会炸。
5. **提交信息用一句中文直述，不加 `feat:` / `fix:` 前缀**（见 CONTRIBUTING）。
6. **不主动推送远端**。`installer/`、`dist/`、`backups/` 都不入库。

---

## 六、踩过的坑（改之前先看）

| 现象 | 原因 | 正确做法 |
|---|---|---|
| 页面动画"憋一下再猛冲" | `EasingFunctionBase` 默认 `EasingMode=EaseOut` 会把自定义贝塞尔按 `1-t` 反演 | 自定义缓动显式 `EasingMode=EaseIn` |
| 大块元素平移后整块白屏（UIA 矩形却正常） | 本机渲染线程对 `TranslateTransform` 平移的大元素不重绘 | 长距离滚动走 `ScrollViewer.ScrollToVerticalOffset` 逐帧推进 |
| 切页总有一帧顿 | ① 新页 `OnNavigatedAsync` 同步段抢 UI 线程 ② 首帧要排版+光栅 | 加载延后到动画落定 + 开滚前"露 2px peek"等 20ms |
| 动画帧间隔不稳、某帧位移翻倍 | `DispatcherTimer` 是 Background 优先级；`Rendering` 偶尔同帧触发两次 | 用 `CompositionTarget.Rendering` 驱动，<8ms 的推进跳过 |
| 进度条永远是空条 | 自定义模板缺 `PART_Indicator` | `PART_Track` + `PART_Indicator` 两个都要有 |
| 可编辑 ComboBox 无法输入 | 模板缺 `PART_EditableTextBox` | 输入区与下拉箭头拆成两个 `ToggleButton` |
| `ItemsSource` 绑了服务对象，控件空白且无绑定错误 | 绑的是对象不是集合，隐式转换器吐出 null | `ItemsSource="{Binding Toasts, Source={StaticResource ...}}"` 分开写 |
| Toast 切页后消失（树里还在） | 卡片带 `DropShadowEffect`，重绘时该区域被跳过 | 别给经常增删的浮层加位图效果 |
| 圆角分组首末行悬停背景直角溢出 | WPF 不按圆角裁剪子元素 | 给行按位置分配内层圆角（附加属性 `SettingsRow.Corner`） |
| 分隔线两端顺着圆角翘起来 | 线画在 Border 的底边框上 | 用独立 1px `Rectangle` 画直线 |
| 列表滚轮滚不动 | `PreviewMouseWheel` 是隧道事件，在 ScrollViewer 层 `Handled=true` 掐死了 | 重写 `OnMouseWheel` 不调 base、也不设 Handled（见 `PageScrollViewer`） |
| 最大化后窗口盖住任务栏 | `WindowStyle=None` 时系统给的是整块屏幕 | 接管 `WM_GETMINMAXINFO`，按显示器 `rcWork` 设 `ptMaxSize/ptMaxPosition` |
| 老版本（≤1.12）装了启动不了 | ① Java 17 顶替 Java 8 ② `options.txt` 里 `lang:zh_cn` 大小写不匹配 → NPE | `JavaManager` 只允许向上取 ≥17 的版本；`LegacyCompat.NormalizeLangCase` |
| 老版本安装 404 | natives-only 库只有 classifier 没有裸 jar | 跳过这类库的裸 jar 下载 |
| 安装失败留下"幽灵版本" | json+jar 先落盘，列表按两者存在判定已装 | 失败时回滚残缺版本目录 |
| 进程无声消失、日志没堆栈 | 异常发生在全局异常处理器内部 | 查 Windows 事件日志 Application 下 `.NET Runtime` 条目，有 CLR 完整堆栈 |

---

## 七、怎么验证

**逻辑改动**：`dotnet run --project EukaryLauncher.Smoke`，31 条断言必须全绿。
涉及下载/安装/启动的改动，再跑一次 `--launch <version>`；动过 natives / 库下载逻辑，
**必须手动验一次老版本**（1.7.10 / 1.12.2 / 1.1 这类），新旧机制完全不同。

**UI 改动**：`tools/` 下的 uiautomation 脚本是走查工具，`ui_walk.py` 里有可复用的
`find_all`（这版 uiautomation 没有 `Control.FindAll`，只能递归 `GetChildren` 按类型过滤）。

```bash
python tools/ui_walk.py            # 通用控件树走查
python tools/ui_page_push.py       # 四向切页落位
python tools/ui_tab_check.py       # 下载页标签切换
python tools/ui_wheel_check.py     # 内层列表滚轮
python tools/ui_version_select.py  # 版本选择页全流程
```

自动化验证有四个固定陷阱：

1. **UIA 树里 `Collapsed` 元素仍然存在**，`IsOffscreen` 也不反映滚动裁剪——
   判断可见性只能看 `BoundingRectangle` 是否落在窗口内；
2. **UIA 修改 WPF `TextBox` 不走绑定**（`SetWindowText` / `ValuePattern` 都不触发 `TextChanged`），
   必须 `Click()` + `SendKeys()`；
3. **GDI 抓屏在本机不可靠**（会返回缓存帧），要看动画中间帧，用应用内
   `RenderTargetBitmap` 抓帧（注意它是惰性的，必须同步 `Render` 后再 `Encode`），
   或者直接读控件的 `BoundingRectangle` 坐标变化；
4. **模态列表靠近窗口底部时会向上弹出**，别按"在下方"去匹配坐标，
   用 `ExpandCollapsePattern` 展开最稳。

**切页卡顿**：`EKL_PUSH_TRACE=1` 启动后跑 `tools/ui_push_trace.py`，
后台每 4ms 往 UI 线程塞一个空委托，往返耗时就是真实卡顿值。

---

## 八、发一个版本（维护者）

1. 版本号要一起改这几处：
   - `EukaryLauncher.App/EukaryLauncher.App.csproj` 的 `<Version>`
   - `EukaryLauncher.Core/EukaryLauncher.Core.csproj` 的 `<Version>`
   - `MainWindow.xaml` 侧栏底部 `EKL vX.Y.Z · M?`
   - `AboutViewModel.Version`
   - `ArgumentBuilder.LauncherVersion`、`DownloadService.UserAgent`
2. `dotnet publish EukaryLauncher.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`
3. 安装包用 NSIS 脚本在本机打（`installer/` 不入库）：
   `powershell -ExecutionPolicy Bypass -File installer\build_installer.ps1`
4. 推 `main` → 推 tag `vX.Y.Z` → CI 自动构建并建 Release（上传 zip 产物）；
   `Setup.exe` 需要手动补传到该 Release。
