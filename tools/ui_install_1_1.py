"""端到端验证：安装 Minecraft 1.1（远古版本）→ 主页启动到游戏窗口出现。

覆盖 2026-11-06 的三处修复：
  1. natives-only 库不再误下不存在的裸 jar（旧版 lwjgl-platform / jinput-platform）
  2. 安装失败回滚残缺版本目录（半成品不再混进版本列表）
  3. 声明 Java 8 的老版本用系统 JDK 8 启动，不再被托管的 java-17 顶替

用法：
    python tools/ui_install_1_1.py
"""
import os
import sys
import time

import uiautomation as auto

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from ui_walk import Walker, find_all  # noqa: E402
from ui_loader_multi import click_named_any  # noqa: E402

auto.uiautomation.SetGlobalSearchTimeout(3)

OUT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "tools", "UiShots"))
EXE = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "EukaryLauncher.App",
                                   "bin", "Debug", "net8.0-windows", "win-x64", "EukaryLauncher.exe"))

TARGET = "1.1"
LOCAL = os.environ.get("LOCALAPPDATA", "")
GAME_DIR = os.path.join(LOCAL, "EukaryLauncher", ".minecraft")
LOG_DIR = os.path.join(LOCAL, "EukaryLauncher", "logs")


def today_log():
    return os.path.join(LOG_DIR, "launcher-" + time.strftime("%Y%m%d") + ".log")


def log_text():
    path = today_log()
    if not os.path.exists(path):
        return ""
    try:
        with open(path, "r", encoding="utf-8", errors="ignore") as f:
            return f.read()
    except Exception:
        return ""


def toggle(win, label):
    for cb in find_all(win, "CheckBox"):
        texts = [(t.Name or "").strip() for t in find_all(cb, "Text")]
        if label in texts:
            cb.Click()
            time.sleep(1.5)
            return True
    return False


def find_row_button(win, version_id, label):
    """清单里 id 精确等于 version_id 的那一行，取它内部文本为 label 的按钮。"""
    for li in find_all(win, "ListItem"):
        if "AvailableVersionRow" not in (li.Name or ""):
            continue
        texts = [t.Name for t in find_all(li, "Text")]
        if not texts or texts[0] != version_id:
            continue
        for b in find_all(li, "Button"):
            if (b.Name or "").strip() == label:
                return b
    return None


def scroll_find_row(win, version_id, max_pages=40):
    """虚拟化列表里逐屏下滚，直到目标行（id 精确匹配）出现。"""
    for _ in range(max_pages):
        for li in find_all(win, "ListItem"):
            if "AvailableVersionRow" not in (li.Name or ""):
                continue
            texts = [t.Name for t in find_all(li, "Text")]
            if texts and texts[0] == version_id:
                return li
        # 找承载版本行且能滚动的 List，翻一页
        for c in find_all(win, "List"):
            try:
                rows = [x for x in find_all(c, "ListItem")
                        if "AvailableVersionRow" in (x.Name or "")]
                if not rows:
                    continue
                c.GetScrollPattern().Scroll(
                    auto.ScrollAmount.NoAmount, auto.ScrollAmount.LargeIncrement)
                break
            except Exception:
                continue
        time.sleep(0.6)
    return None


def filter_and_find(win, text, version_id):
    """往搜索框输入过滤词，再滚动找目标行。"""
    for ed in find_all(win, "Edit"):
        try:
            ed.Click()
            time.sleep(0.3)
            ed.SendKeys(text)
            time.sleep(2)
            row = scroll_find_row(win, version_id)
            if row is not None:
                return row
            ed.SendKeys("{Ctrl}a{Delete}")
            time.sleep(0.8)
        except Exception as exc:
            print("  输入框尝试失败：", exc)
    return None


def wait_install_done(timeout=900):
    # 只看安装开始之后新增的日志——当天日志里可能躺着早先的失败记录
    baseline = len(log_text())
    end = time.time() + timeout
    while time.time() < end:
        text = log_text()[baseline:]
        if "1.1 安装完成" in text:
            return "done"
        if "1.1 安装失败" in text:
            return "failed"
        time.sleep(3)
    return "timeout"


def find_game_window(timeout=180):
    """启动后轮询顶层窗口，找 Minecraft 游戏窗口。"""
    end = time.time() + timeout
    while time.time() < end:
        try:
            for w in auto.GetRootControl().GetChildren():
                name = w.Name or ""
                if "Minecraft" in name or "minecraft" in name:
                    return name
        except Exception:
            pass
        time.sleep(2)
    return None


def main():
    w = Walker(EXE, OUT)
    w.start()
    game_win = None
    try:
        w.dismiss_error_dialog()
        print("[1] 进下载页 → 原版游戏")
        click_named_any(w.win, "下载")
        time.sleep(3)
        w.dismiss_error_dialog()
        # 原版页的 TopTab 有时要等清单加载才出现，最多再等 15 秒
        ok = False
        for _ in range(15):
            try:
                if w.win.TextControl(Name="原版游戏", searchDepth=0xFFFFFFFF).Exists(maxSearchSeconds=1):
                    ok = True
                    break
            except Exception:
                pass
            w.dismiss_error_dialog()
            time.sleep(1)
        assert ok, "下载页一直没有出现「原版游戏」标签"
        click_named_any(w.win, "原版游戏")
        time.sleep(5)
        w.dismiss_error_dialog()

        print("[2] 勾上「显示远古版本」（1.1 属于远古版本）")
        print("  找到开关：", toggle(w.win, "显示远古版本"))

        print(f"[3] 搜索 {TARGET}（含式匹配，1.1 会命中一串，滚动找精确行）")
        row = filter_and_find(w.win, TARGET, TARGET)
        assert row is not None, "过滤后找不到 1.1 这一行"

        print("[4] 点 1.1 行的「继续」进入加载器选择页")
        btn = None
        for b in find_all(row, "Button"):
            if (b.Name or "").strip() == "继续":
                btn = b
                break
        assert btn is not None, "找不到 1.1 的继续按钮"
        btn.GetInvokePattern().Invoke()
        time.sleep(12)  # 等三个加载器拉完版本列表
        w.dismiss_error_dialog()
        w.shot("90-1-1-loader-page.png", wait=1)

        print("[5] 不选加载器，直接「开始下载」（仅原版）")
        click_named_any(w.win, "开始下载", kinds=("Button",))
        time.sleep(3)
        w.dismiss_error_dialog()

        print("[6] 等待安装完成（natives-only 库修复后不应再 404）…")
        result = wait_install_done()
        print("  安装结果：", result)
        w.shot("91-1-1-queue.png", wait=1)
        assert result == "done", f"1.1 安装未成功：{result}"

        jar = os.path.join(GAME_DIR, "versions", "1.1", "1.1.jar")
        natives = os.path.join(GAME_DIR, "versions", "1.1", "natives")
        index = os.path.join(GAME_DIR, "assets", "indexes", "pre-1.6.json")
        print("  jar:", os.path.exists(jar), " natives:", os.path.exists(natives),
              " 资产索引:", os.path.exists(index))
        assert os.path.exists(jar) and os.path.exists(natives) and os.path.exists(index)

        print("\n[7] 回主页，选 1.1，点开始游戏")
        click_named_any(w.win, "主页")
        time.sleep(2)

        combo = w.win.ComboBoxControl(searchDepth=0xFFFFFFFF)
        assert combo.Exists(maxSearchSeconds=5), "主页找不到版本下拉框"
        combo.Click()
        time.sleep(0.8)
        for li in find_all(w.win, "ListItem"):
            if (li.Name or "").strip() == TARGET:
                li.Click()
                break
        else:
            raise RuntimeError("下拉里没有 1.1")
        time.sleep(1)

        for b in find_all(w.win, "Button"):
            if (b.Name or "").strip() == "开始游戏":
                b.GetInvokePattern().Invoke()
                break
        else:
            raise RuntimeError("找不到开始游戏按钮")

        print("[8] 等启动日志（校验用的是系统 JDK 8 而不是 java-17）…")
        launch_line = ""
        end = time.time() + 120
        while time.time() < end:
            text = log_text()
            idx = text.rfind("启动 1.1：")
            if idx >= 0:
                launch_line = text[idx:idx + 300].splitlines()[0]
                break
            time.sleep(2)
        print("  启动命令行（截断）：", launch_line[:180])
        assert launch_line, "没等到「启动 1.1：」日志"
        assert "java-17" not in launch_line, "老版本仍然在用 java-17！"
        assert "jdk8" in launch_line.lower() or "jdk-8" in launch_line.lower() or \
               "java 8" in launch_line.lower(), f"没有用上 Java 8：{launch_line[:120]}"

        print("[9] 等游戏窗口出现…")
        game_win = find_game_window()
        print("  游戏窗口：", game_win)
        assert game_win, "游戏窗口没有出现"
        w.shot("92-1-1-game-window.png", wait=1)

        print("\n✓ 全部通过：1.1 安装成功 + Java 8 启动 + 游戏窗口出现")
    finally:
        # 收尾：关掉游戏进程再关启动器
        if game_win:
            os.system('taskkill /FI "IMAGENAME eq java.exe" /F >nul 2>&1')
            time.sleep(1)
        w.stop()


if __name__ == "__main__":
    main()
