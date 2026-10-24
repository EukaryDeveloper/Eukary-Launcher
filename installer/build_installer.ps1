# =============================================================================
#  Eukary Launcher 安装包构建脚本
#  一步到位：Release 单文件发布 -> 编译 NSIS 安装包 -> 输出 dist\*-Setup.exe
#
#  用法：
#    powershell -ExecutionPolicy Bypass -File installer\build_installer.ps1
#    powershell -ExecutionPolicy Bypass -File installer\build_installer.ps1 -SkipPublish
#
#  NSIS 缺失时会自动下载便携版到 %LOCALAPPDATA%\Tools\NSIS
# =============================================================================

param(
    [switch]$SkipPublish,
    [string]$Configuration = "Release",
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8   ; # 让中文在控制台正常显示

$RepoRoot   = Split-Path -Parent $PSScriptRoot
$Installer  = $PSScriptRoot
$DistDir    = Join-Path $RepoRoot "dist"
$PublishDir = Join-Path $RepoRoot "EukaryLauncher.App\bin\$Configuration\net8.0-windows\win-x64\publish"
$AppProject = Join-Path $RepoRoot "EukaryLauncher.App\EukaryLauncher.App.csproj"

# dotnet：优先使用本会话已知的 SDK 路径
$dotnet = "dotnet"
$sdkDotnet = Join-Path $env:USERPROFILE ".workbuddy\binaries\dotnet\dotnet.exe"
if (Test-Path $sdkDotnet) { $dotnet = $sdkDotnet }

# -----------------------------------------------------------------------------
function Write-Step($msg) {
    Write-Host ""
    Write-Host "==> $msg" -ForegroundColor Cyan
}

# -----------------------------------------------------------------------------
# 1. 发布
# -----------------------------------------------------------------------------
if (-not $SkipPublish) {
    Write-Step "发布 $Configuration 单文件自包含程序"
    & $dotnet publish $AppProject -c $Configuration -v quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败，退出码 $LASTEXITCODE" }
}
else {
    Write-Step "跳过发布（使用现有产物）"
}

if (-not (Test-Path (Join-Path $PublishDir "EukaryLauncher.exe"))) {
    throw "未找到发布产物：$PublishDir\EukaryLauncher.exe"
}

$exe = Get-Item (Join-Path $PublishDir "EukaryLauncher.exe")
$dlls = Get-ChildItem $PublishDir -Filter "*.dll"
Write-Host ("    主程序 {0} ({1:N1} MB)，附加组件 {2} 个" -f `
    $exe.Name, ($exe.Length / 1MB), $dlls.Count) -ForegroundColor DarkGray

# 读取 csproj 里的版本号，保证和程序一致
$csproj = Get-Content (Join-Path $RepoRoot "EukaryLauncher.App\EukaryLauncher.App.csproj") -Raw
$m = [regex]::Match($csproj, '<Version>([^<]+)</Version>')
if ($m.Success) { $Version = $m.Groups[1].Value.Trim() }
Write-Host "    版本号 $Version" -ForegroundColor DarkGray

# -----------------------------------------------------------------------------
# 2. 准备 NSIS
# -----------------------------------------------------------------------------
Write-Step "准备 NSIS 编译器"
$nsisRoot  = Join-Path $env:LOCALAPPDATA "Tools\NSIS"
$makensis  = Join-Path $nsisRoot "makensis.exe"

if (-not (Test-Path $makensis)) {
    Write-Host "    未找到 NSIS，开始下载便携版…" -ForegroundColor Yellow
    $tmp = Join-Path $env:TEMP "nsis-dl.zip"
    New-Item -ItemType Directory -Force -Path (Split-Path $nsisRoot) | Out-Null
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri "https://prdownloads.sourceforge.net/nsis/nsis-3.10.zip?download" `
        -OutFile $tmp -UseBasicParsing -TimeoutSec 300
    $extractRoot = Join-Path $env:TEMP "nsis-extract"
    Remove-Item -Recurse -Force $extractRoot -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
    Expand-Archive -Path $tmp -DestinationPath $extractRoot -Force
    Move-Item -Path (Join-Path $extractRoot "nsis-3.10") -Destination $nsisRoot -Force
    Remove-Item -Recurse -Force $extractRoot, $tmp -ErrorAction SilentlyContinue
    Write-Host "    NSIS 已就位：$nsisRoot" -ForegroundColor Green
}

$nsisProductVer = (& $makensis /VERSION) -join ''
Write-Host "    $nsisProductVer" -ForegroundColor DarkGray

# -----------------------------------------------------------------------------
# 3. NSIS 源码必须是 UTF-8 BOM，否则中文会乱码
# -----------------------------------------------------------------------------
Write-Step "准备 NSIS 脚本（按 UTF-8 读入，带 BOM 写出）"
$srcNsi = Join-Path $Installer "installer.nsi"
$tmpNsi = Join-Path $env:TEMP "ekl-installer.bom.nsi"
# 必须显式指定 UTF8 读取：PowerShell 5.1 对无 BOM 的文件会按系统 GBK 解码，会把中文搞坏
$utf8     = New-Object System.Text.UTF8Encoding($false)
$utf8Bom  = New-Object System.Text.UTF8Encoding($true)
$nsiText  = [System.IO.File]::ReadAllText($srcNsi, $utf8)
[System.IO.File]::WriteAllText($tmpNsi, $nsiText, $utf8Bom)

# -----------------------------------------------------------------------------
# 4. 编译安装包
# -----------------------------------------------------------------------------
Write-Step "编译安装包"
New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

$mkArgs = @(
    "/INPUTCHARSET", "UTF8",
    "/OUTPUTCHARSET", "UTF8SIG",
    "/V2",
    "/DVERSION=$Version",
    "/DPUBLISH_DIR=$PublishDir",
    "/DREPO_ROOT=$RepoRoot",
    "/DDIST_DIR=$DistDir",
    $tmpNsi
)
& $makensis $mkArgs
if ($LASTEXITCODE -ne 0) { throw "NSIS 编译失败，退出码 $LASTEXITCODE" }

Remove-Item $tmpNsi -Force -ErrorAction SilentlyContinue

# -----------------------------------------------------------------------------
# 5. 输出
# -----------------------------------------------------------------------------
$out = Join-Path $DistDir "EukaryLauncher-$Version-Setup.exe"
if (-not (Test-Path $out)) { throw "未生成安装包：$out" }
$size = (Get-Item $out).Length
Write-Host ""
Write-Host ("安装包已生成：{0}" -f $out) -ForegroundColor Green
Write-Host ("大小：{0:N1} MB（发布的原始文件合计 {1:N1} MB，压缩率 {2:P0}）" -f `
    ($size / 1MB), (($exe.Length + ($dlls | Measure-Object Length -Sum).Sum) / 1MB), `
    ($size / ($exe.Length + ($dlls | Measure-Object Length -Sum).Sum))) -ForegroundColor Green
Write-Host ""
Write-Host "安装说明：" -ForegroundColor DarkGray
Write-Host "  · 双机运行即可安装，按用户目录安装，不需要管理员权限" -ForegroundColor DarkGray
Write-Host "  · 默认安装到 %LOCALAPPDATA%\Programs\Eukary Launcher" -ForegroundColor DarkGray
Write-Host "  · 卸载：开始菜单或「应用和功能」，不要把 <DIR> 的文件手动删掉再卸载" -ForegroundColor DarkGray
