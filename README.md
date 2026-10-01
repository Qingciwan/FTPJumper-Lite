# FtpJumper Lite 课堂资料助手（liteEdition）

**lite 版**：把每个科目的 FTP 资料**直接交给 Windows 文件资源管理器打开**（写死 explorer.exe，不走系统 ftp 关联），
不下载、不缓存、不做增量比对。免安装单 exe，运行环境 Windows 11（24H2 验证目标）+ .NET Framework 4.8（系统自带）。

> 工作方式：只拼一个 `ftp://` 网址交给文件资源管理器（**秒开、零磁盘占用**，不怕一体机的冰点还原）；
> 代价是**不产生本地副本**——需要离线使用时，请先把资料下载到本地。

---

## 1. 教师使用说明

1. 双击 `FtpJumperLite.exe` 启动，出现科目按钮列表（单列列表，含高分屏适配）。
2. 单击某个科目 → 程序立刻拼出该科目的 `ftp://` 网址并交给**文件资源管理器**打开，
   直接浏览远端目录，不产生任何本地副本。
3. 修改配置：点底部「打开配置目录」→ 用记事本编辑 `subjects.json` → 保存 → 回程序点「刷新配置」。

状态栏会显示本次打开的脱敏网址（密码显示为 `***`）。

### subjects.json 说明

```jsonc
{
  "version": 2,                                   // 勿改
  "subjects": [
    {
      "id": "yuwen-2026",       // 唯一标识（字母/数字/-/_）
      "name": "语文",           // 界面显示的科目名
      "host": "ftp.example.com",// 主机或 IP，不要带 ftp://
      "port": 21,               // FTP 端口；21 时网址里会省略
      "user": "yuwen",          // 该科目的用户名
      "password": "123456",     // 该科目的密码（明文）
      "remotePath": "/pub/yuwen", // 远端目录；留空/省略/空串 = 站点根目录 /；非空必须以 / 开头且不含 ..
      "credentialMode": "inline", // inline=账号密码写进网址（默认）；prompt=网址不带凭据，由资源管理器询问
      "lastSyncAt": ""          // 最近打开时间（程序自动写回，可留空）
    }
  ]
}
```

要点：

- 添加科目 = 整段复制一个 `{...}` 并修改；删除科目 = 删掉对应条目；界面**不提供**增删改入口。
- `remotePath`：**可以留空**（写成 `""`、只写空格、或整段省略该字段），三种都等价于 **FTP 站点根目录 `/`**，
  网址就是 `ftp://账号:密码@主机[:端口]/`；若服务器把账号限制在家目录，看到的即为该账号的家目录。
  非空时必须是绝对路径（以 `/` 开头，如 `/pub/语文`），且不允许 `..`——写成 `pub/yuwen` 会在「刷新配置」时被拒。
- **旧的 `subjects.json` 可以直接沿用**：`localDir / passive / encoding / timeoutSec / syncMode` 这些历史字段
  不再使用（保留不报错，程序写回「最近打开时间」时会被自然去掉）。
- `credentialMode`：
  - `inline`（默认）：网址形如 `ftp://user:password@host/path/`，**一次点击直达**；
    代价是密码会明文出现在资源管理器地址栏，并可能进入地址栏历史。
  - `prompt`：网址形如 `ftp://host/path/`，不携带任何凭据，由资源管理器弹窗询问账号密码；更安全但每次要多输一次。
- 数据位置：默认放在**应用文件夹**下（`logs` 为运行日志）；应用文件夹不可写时自动回退 `%LOCALAPPDATA%\FtpJumperLite\data`。

---

## 2. 打开行为与已知边界（重要）

**程序不做任何 FTP 协议实现**，只是拼网址并**写死用文件资源管理器打开**：

1. `explorer.exe "ftp://…"` —— 把网址作为命令行参数交给资源管理器，
   由资源管理器**自己的 FTP 命名空间**（Microsoft FTP Folder，`{63da6ec0-2e98-11cf-8d82-444553540000}`）接管，**绕开系统 ftp 协议关联**；
2. 万一 explorer.exe 拉不起来，才退回 `ShellExecute(网址)`；
3. 两者都失败时，弹窗给出可手动粘贴的**脱敏**网址（明文需要自己看配置）。

> 为什么写死 explorer：ftp 是 URL 协议关联
> （`HKCU\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\ftp`），教室里常被第三方浏览器抢走。
> 本机实测该关联是 `Progid = 360ChromeURL`（`HKLM\SOFTWARE\Classes\ftp\shell\open\command` 指向 360 浏览器），
> 而 `explorer.exe "ftp://user:pass@host:port/path/"` 能稳定打开资源管理器并列出远端目录，
> 因此不再依赖系统关联，也就不必在每台一体机上改默认应用。

实测证据（本地测试 FTP 服务器 + 真实资源管理器）：
资源管理器新开窗口、地址栏显示主机名，窗口内正常列出远端文件与中文目录；
服务器端可见命令序列 `USER / PASS / OPTS / SYST / SITE / PWD / NOOP / CWD / TYPE / PASV / LIST`。

仍需知道的边界：

- **密码在网址里**（`credentialMode=inline`）：地址栏可见，且可能留在地址栏下拉历史里。
  不想暴露就改成 `prompt`。程序自身的日志与状态栏一律脱敏（`***`），绝不打印明文密码。
- **中文密码/用户名依赖系统的百分号解码**：非 ASCII 凭据会被编码成 `%E4%B8%AD` 这类形式，
  资源管理器能否正确还原取决于系统实现；若某科目始终登录失败，建议改用 `prompt` 模式手输。
- **网址里的中文路径与空格按原样保留**（不编码），这样在资源管理器里显示更自然；
  `% ? # : " < > \ | *` 等字符会被转义。
- **资源管理器会用 LIST**（而不是 MLSD），并且会额外发 `SITE`/`NOOP`/`CWD` 等命令；
  只支持 MLSD 的服务器可能不兼容。
- 本程序**不下载任何文件**：远端断网、或需要离线使用时，请先把资料下载到本地。

---

## 3. 界面与高分屏（2K@200% / 本机 175%）

### 尺寸与字号

- 界面是单列科目列表（默认正好 10 行），**默认窗口更高**：高度取满屏幕可用高度（留 40 逻辑像素），
  宽度按高度约 0.72 取，并夹在 430~760 逻辑像素之间。
- **科目名居中**：每行按钮 `TextAlign = MiddleCenter`，两行文本（科目名 + 主机·远端路径）水平且垂直居中。
- **所有尺寸常量都是“96 DPI 逻辑像素”**，使用前一律经 `Scale()` 按当前 DPI 换算。字号是“点”（随 DPI 放大），
  常量若不同步放大就会出现“字变大了、行高没变”，布局必错。
- **字号按真实测量选取**：从 17pt 起每 0.5pt 递减，直到两行加粗中文真正放得下；
  另留 8 逻辑像素安全余量（主题按钮的文字区在 `Padding` 之外还有内缩，卡边界会把第二行裁掉）。

### 三条结构性的“防重排”约束

`TableLayoutPanel` 在行高小于控件最小高度时会**自行重排**，把多余空间塞给最后一行——高 DPI 下的错位就是这么来的。
因此布局上有三条硬约束（都有回归测试守着）：

1. **行高永不小于内容所需**：内容下限 = 最大字号下限下测得的两行文本高 + 按钮内边距 + 安全余量 + 行间距，行高取 `max(计算值, 内容下限)`。
2. **末尾永远保留一个吸收行**：即使科目超过一屏（滚动）也保留，剩余像素只进吸收行，绝不拉伸某个科目行。
3. **列宽固定 100%**：`_root` 与科目表都显式加 `ColumnStyle(Percent, 100)`。
   不设列样式时列会按内容 AutoSize，被按钮文字撑得比窗口还宽 → 整行向右溢出、左右留白不对称（就是“不居中”）。
   同时每行文字按**像素宽度**截断（超出加省略号），保证文本既不换行也不超宽。

### 实测环境与结论（本机）

| 项 | 值 |
|---|---|
| 分辨率 / 缩放 | 1920×1080 / **175%**（`GetDpiForSystem()=168`） |
| 感知进程看到的桌面 | 1920×1080，可用区 1920×1010 |
| 非感知进程看到的桌面 | 1097×617（被虚拟化，**测不到真实问题**） |
| 程序内实测 | `scale=1.75`、窗口 823×940、科目表 763×718 |

两个踩过的坑，改代码前务必知道：

- **不要在 `App.config` 里加 `DpiAwareness=PerMonitorV2`**：本程序的感知由 `app.manifest` 负责，
  两者同时存在时实测进程会退化成“非感知”（窗口被虚拟化成 96 DPI 尺寸、布局全错）。详见 `App.config` 里的注释。
- **WinForms 4.8 会把 `Control.DeviceDpi` 固定报成 96**（未启用其内置 PerMonitorV2 模式时），
  所以 `MainForm.CurrentDpiScale()` 会再兜一层 `GetDpiForWindow` / `Graphics.DpiX` 拿真实 DPI；
  程序启动时会往日志写一行 `显示环境：… 系统DPI=…` 和 `DPI：scale=…`，现场排障先看这两行。
- 挂 DPI 校正的入口必须在**构造函数跑完之后**（`OnLoad` 且带 `_layoutReady` 守卫）：
  窗体句柄可能在构造期间就被创建，那时字段还是 null，直接缩放会 `NullReferenceException`。

最小窗口 470×520（逻辑）：再窄底部三个按钮就会被裁到窗口外。

---

## 4. 目录结构

```
（仓库根目录 = 本项目根）
├─ FtpJumperLite.sln              # 解决方案（可整体拷贝本文件夹后独立编译）
├─ Directory.Build.props          # 编译约定（TreatWarningsAsErrors、nullable 等）
├─ subjects.json                  # 配置示例（随 exe 同目录，明文）
├─ LICENSE.txt                    # 许可协议（CC BY-NC-SA 4.0，含署名栏）
├─ THIRD-PARTY-NOTICES.txt        # 第三方组件声明（Newtonsoft.Json 等）
├─ build.ps1                      # 一键 restore + build + test + selfcheck
├─ publish.ps1                    # 打包到 release\ （教室一体机拷贝用）
├─ Report1.ico                    # 图标（32bpp，16/24/32/48/64/72/96/128/256；仓库自带，保证独立可编译）
├─ src/FtpJumperLite/
│   ├─ Program.cs                 # 入口：界面 / --selfcheck / --url；启动时把显示环境写入日志
│   ├─ App.config                 # 只放 supportedRuntime（**切勿**加 DpiAwareness，见第 3 节）
│   ├─ app.manifest               # DPI 感知（PerMonitorV2）+ Win10/11 兼容
│   ├─ Models/Subject.cs, AppConfig.cs
│   ├─ Services/
│   │   ├─ FtpUrlBuilder.cs       # ★ lite 核心：把科目配置拼成 ftp:// 网址
│   │   ├─ FtpUrlLauncher.cs      # ★ lite 核心：写死用 explorer.exe 打开，失败才退回系统处理器
│   │   ├─ ShellLauncher.cs       # 用指定程序打开网址（不经过 URL 协议关联）
│   │   ├─ FtpUrlDiagnostics.cs   # --url 诊断（默认只打印脱敏网址）
│   │   ├─ ConfigStore.cs         # subjects.json 读写/校验
│   │   ├─ AppPaths.cs, AppLogger.cs, SelfCheck.cs
│   └─ Ui/MainForm.cs, AppIcon.cs # 界面：单列 10 行、底部三按钮、高分屏字号自适应 + 防重排约束
├─ tests/FtpJumperLite.Tests/             # xUnit：网址拼装 / 配置 / 窗体布局 / 端到端
├─ tests/FtpJumperLite.TestFtpServer/     # 类库：MiniFtpServer（真实 TCP 的极简 FTP 服务端）
├─ tests/DpiProbe/                        # 可运行：DpiProbe.exe [aware]（报告真实分辨率/缩放/DPI 视角）
└─ tools/
    ├─ FtpLiteTestServer/                 # 可运行：FtpLiteTestServer.exe
    └─ UiShot/                            # 可运行：按尺寸渲染主窗体截图（自带 PerMonitorV2 清单）
```

### 布局相关工具

```powershell
# 报告本机显示环境：非感知视角 vs PerMonitorV2 感知视角（分辨率、可用区、系统 DPI、物理分辨率）
tests\DpiProbe\bin\Release\net48\DpiProbe.exe
tests\DpiProbe\bin\Release\net48\DpiProbe.exe aware

# 按指定尺寸渲染主窗体（宽高填 0 = 用默认尺寸，等价于“刚启动”）；--diag 打印每行几何；--print 用 PrintWindow 抓真实窗口
tools\UiShot\bin\Release\net48\UiShot.exe subjects.json 0 0 shot.png --diag
```

> 注意：`UiShot` 必须在**自带 PerMonitorV2 清单**的进程里跑才能看到真实高 DPI 布局（它已带 `app.manifest`）；
> 用 DPI 非感知的进程（如普通 PowerShell）渲染只会得到被虚拟化的 96 DPI 结果。

---

## 5. 开发与验证

```powershell
# 一键：还原 + 编译 + 测试 + 自检
powershell -ExecutionPolicy Bypass -File .\build.ps1

# 打包到干净目录（教室一体机拷贝用）
powershell -ExecutionPolicy Bypass -File .\publish.ps1
```

- 本机无 .NET SDK 时，先从 https://aka.ms/dotnet/download 安装 .NET 8 SDK（x64）。
- `build.ps1` 用 `-p:UseSharedCompilation=false`，避免 csc 编译器服务器在受限环境里被取消（MSB5021）。
- 若 `.packages\` 存在，脚本会把 NuGet 包目录指向它，构建可离线/自包含（该目录不入库）。
- 图标：`Report1.ico`（32bpp，16/24/32/48/64/72/96/128/256 九个尺寸）经 `<ApplicationIcon>` 嵌入 exe，
  并由 `Ui/AppIcon.cs` 赋给各窗体，保证资源管理器、标题栏、任务栏与 Alt+Tab 显示一致
  （主程序 `FtpJumperLite.exe` 与本地测试服务器 `FtpLiteTestServer.exe` 都已应用）。
  - `AppIcon` 用 Win32 `PrivateExtractIcons` 取**最大的一帧（256）**再交给系统缩放：
    `Icon.ExtractAssociatedIcon` 只能拿到 32×32，在 2K@200% 下标题栏/任务栏会发虚；
    而 `new Icon(exe路径, 宽, 高)` 对 exe **一律抛异常**（只对 .ico 有效），
    且 .NET 的 `Icon` 读不了 Vista 之后 PNG 压缩的 256×256 帧（`PrivateExtractIcons` 可以）。
- 源码文件（`.csproj` / `.props`）一律保存为**带 BOM 的 UTF-8**，否则 MSBuild 会按系统代码页读取，
  导致 `AssemblyTitle` 里的中文变成乱码。

### 命令行

```powershell
FtpJumperLite.exe --selfcheck [--cfg 路径]                  # 只读配置、不联网、不打印明文密码
FtpJumperLite.exe --url [科目id或名称] [--show-password] [--open] [--cfg 路径]
```

`--url` 打印该科目将打开的完整字段（主机/端口/用户名/远端路径/凭据方式）与网址：
默认脱敏（`ftp://user:***@host/path/`），加 `--show-password` 才打印明文，加 `--open` 直接用文件资源管理器打开。
退出码：`0` 通过；`2` 打开/网址问题；`3` 配置或参数问题。

### 本地测试环境（没有真实 FTP 服务时）

```powershell
# 默认端口 2121、账号 class / 123456，根目录为 exe 同级 root\（首次启动自动生成中文示例资料）
.\tools\FtpLiteTestServer\bin\Release\net48\FtpLiteTestServer.exe
# 常用参数
.\FtpLiteTestServer.exe --port 21219 --root D:\ftp-test --user demo --password 123456
.\FtpLiteTestServer.exe --no-mlsd --list-mode dos   # 只提供 LIST / DOS 列表
.\FtpLiteTestServer.exe --gbk                       # 模拟不认 OPTS UTF8 的老服务器
```

启动后按屏幕提示把示例科目粘进 `subjects.json`（`host` 填 `127.0.0.1`，端口与之一致），
回程序点「刷新配置」，再单击该科目即可验证「拼网址 → 交给系统 → 浏览远端」。

### 测试覆盖（53 项全绿）

- 布局（**在真实 DPI 下运行**：测试线程显式声明 PerMonitorV2，本机即 1.75×）：
  - 5 种科目数（2/9/10/11/12）× 6 种窗口尺寸（含“不设尺寸 = 启动默认”）下：
    所有科目行等高、左右留白对称、行宽+留白=表格客户区宽、**每行都装得下自己的两行文本**。
  - 行高与 DPI **等比**（`ComputeRowHeightPx` 在 1.0/1.5/2.0 下的比例关系），内容下限优先于夹紧值，
    11 个科目时行高与 10 个科目一致（不会因为多一行而变矮）。
  - 尺寸扫描：9 种宽度 × 10 种高度 × {2, 11} 科目，全面扫“行高不一致 / 留白不对称”。
- 图标：从构建产物 `FtpJumperLite.exe` 中能取到 ≥48px 的大尺寸帧（高分屏用），非法路径返回 null。

- 网址拼装：默认端口省略、非默认端口、中文/空格路径、`..`/冗余斜杠归一化、特殊字符与中文密码转义、
  IPv6 方括号（含已带括号/括号不匹配）、`prompt` 模式不带凭据、匿名账号、脱敏只隐藏密码、
  非法主机报错、生成结果能被 `System.Uri` 解析回来、空网址被启动器拒绝、
  **`remotePath` 留空/纯空格/省略/`/` 都等价于站点根目录**。
- 配置：缺失文件自动建示例、非法 JSON/缺字段/重复 id/非法 host/非法 credentialMode/非法 remotePath 的中文报错、
  **含历史字段（localDir/passive/encoding/timeoutSec/syncMode）的旧配置可直接加载**、写回保留明文密码并丢弃无用字段。
- 端到端：起真实 TCP 的 MiniFtpServer，用 `System.Uri` 解析程序拼出的网址，拿其中的主机/端口/凭据/路径
  完成 `USER/PASS/PASV/MLSD`，并断言列出的中文文件名正确。
- 窗体布局回归（在 740×1000 / 697×680 / 660×620 / 470×520 四种尺寸下逐项校验）：
  底部三个按钮完整落在客户区内且不重叠；单列 10 行要么精确填满、要么按 64px 下限滚动；
  科目名块水平居中、两行文本不被裁切；字号表随行高单调递增且任何行高下都放得下。
- 布局核对工具：`tools\UiShot\UiShot.exe <配置> <宽> <高> <输出.png>` 可按任意尺寸渲染主窗体截图
  （本机 DPI 改不了时，用 2K@200% 的逻辑尺寸 697×680 渲染即可核对）。

> 已实测确认：`explorer.exe "ftp://class:123456@127.0.0.1:2121/"` 打开资源管理器并列出远端中文目录，
> 服务器端可见完整命令序列（详见第 2 节）。
> 仍建议在真机上确认：中文密码的 `%XX` 解码、以及远端服务器对资源管理器并发连接的容忍度。

---

## 6. 许可与合规

- 本作品采用 **CC BY-NC-SA 4.0**（署名—非商业性使用—相同方式共享）许可，详见 `LICENSE.txt`。
- 入库前请确认：仓库中不得包含真实 FTP 凭据（`release\`、`_private\`、`bin\`、`obj\`、`.packages\` 已在 `.gitignore` 中排除）。
  **发布前请先在 `LICENSE.txt` 里补全署名与来源地址**（CC BY 要求署名 + 许可链接 + 是否修改）。
- 随程序分发的第三方组件与许可：见 `THIRD-PARTY-NOTICES.txt`（当前只有 `Newtonsoft.Json`，MIT）。
- 程序**不分发** .NET Framework 运行时；要求系统已启用 .NET Framework 4.8。
- 凭据：所有账号密码以**明文**存放在 `subjects.json`，这是需求本身的要求。发布/外发前请把 `subjects.json`
  换成占位模板（`publish.ps1` 默认就是这么做的：它会把目标目录里的真实配置移到 `_private\` 再放模板，两者都不入库）。
- 日志：程序只记录非敏感信息（主机、脱敏后的网址），**不写密码**；日志位于数据目录的 `logs\`，不随发布外发。
