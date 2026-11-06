"""启动侧验证：主页选 1.1 → 开始游戏 → 校验用 Java 8 启动且游戏窗口出现。

前置：1.1 已通过 ui_install_1_1.py 装好。
用法：
    python tools/ui_launch_1_1.py
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

LOCAL = os.environ.get("LOCALAPPDATA", "")
LOG_PATH = os.path.join(LOCAL, "EukaryLauncher", "logs",
                        "launcher-" + time.strftime("%Y%m%d") + ".log")


def log_text():
    try:
        with open(LOG_PATH, "r", encoding="utf-8", errors="ignore") as f:
            return f.read()
    except Exception:
        return ""


def combo_boxes(win):
    out = []
    for c in find_all(win, "Combo"):
        out.append(c)
    return out


def find_game_window(timeout=240):
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
        print("[1] 回主页")
        click_named_any(w.win, "主页")
        time.sleep(2)
        w.dismiss_error_dialog()

        print("[2] 打开版本下拉，选 1.1")
        combos = combo_boxes(w.win)
        print(f"  找到 {len(combos)} 个下拉框")
        assert combos, "主页没有下拉框"
        combo = combos[0]
        combo.Click()
        time.sleep(0.8)
        hit = None
        for li in find_all(w.win, "ListItem"):
            if (li.Name or "").strip() == "1.1":
                hit = li
                break
        assert hit is not None, "下拉里没有 1.1（安装后列表应包含它）"
        hit.Click()
        time.sleep(1)

        print("[3] 点开始游戏")
        btn = None
        for b in find_all(w.win, "Button"):
            if (b.Name or "").strip() == "开始游戏":
                btn = b
                break
        assert btn is not None, "找不到开始游戏按钮"
        # baseline 必须在点击前取——启动日志行可能点完立刻就写盘
        baseline = len(log_text())
        btn.GetInvokePattern().Invoke()

        print("[4] 等启动日志（校验 Java 版本）…")
        launch_line = ""
        end = time.time() + 120
        while time.time() < end:
            text = log_text()[baseline:]
            idx = text.find("启动 1.1：")
            if idx >= 0:
                launch_line = text[idx:idx + 400].splitlines()[0]
                break
            time.sleep(2)
        print("  启动命令行（截断）：", launch_line[:200])
        assert launch_line, "没等到「启动 1.1：」日志"
        assert "java-17" not in launch_line, "老版本仍然在用 java-17！"
        low = launch_line.lower()
        assert "jdk8" in low or "jdk-8" in low, f"没有用上 Java 8：{launch_line[:150]}"

        print("[5] 等游戏窗口出现…")
        game_win = find_game_window()
        print("  游戏窗口：", game_win)
        assert game_win, "游戏窗口没有出现"

        print("[6] 再等 25 秒确认游戏真的活着（老版本的 NPE 崩溃发生在开屏前）…")
        time.sleep(25)
        baseline = len(log_text())
        text_now = log_text()[baseline:]
        if "NullPointerException" in text_now:
            print("  ✗ 日志里出现 NPE：")
            for line in text_now.splitlines():
                if "NullPointer" in line or "at abn" in line:
                    print("   -", line)
            raise AssertionError("游戏启动后崩了（NPE）")
        opts = os.path.join(LOCAL, "EukaryLauncher", ".minecraft", "options.txt")
        with open(opts, "r", encoding="utf-8", errors="ignore") as f:
            for line in f:
                if line.startswith("lang:"):
                    print("  options.txt lang =", line.strip(), "（应为 zh_CN 正确大小写）")
        w.shot("95-1-1-game-window.png", wait=1)

        print("\n✓ 通过：1.1 用 Java 8 启动，游戏窗口已出现且存活")
    finally:
        if game_win:
            os.system('taskkill /FI "IMAGENAME eq java.exe" /F >nul 2>&1')
            time.sleep(1)
        w.stop()


if __name__ == "__main__":
    main()
