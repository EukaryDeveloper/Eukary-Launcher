"""截图：主页的「版本设置」按钮 + 设置页改名后的「全局设置」分组。"""
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from ui_walk import Walker  # noqa: E402
from ui_loader_multi import click_named_any  # noqa: E402

EXE = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "EukaryLauncher.App",
                                   "bin", "Debug", "net8.0-windows", "win-x64", "EukaryLauncher.exe"))
OUT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "tools", "UiShots"))


def main():
    w = Walker(EXE, OUT)
    w.start()
    try:
        w.dismiss_error_dialog()
        print("[1] 主页截图（看开始游戏下方的版本设置）")
        w.shot("95-home-version-settings.png", wait=1.5)

        print("[2] 点版本设置，看 Toast")
        try:
            click_named_any(w.win, "版本设置", kinds=("Button",))
            w.shot("96-version-settings-toast.png", wait=1)
        except Exception as exc:
            print("  点击失败：", exc)

        print("[3] 设置页看「全局设置」分组")
        click_named_any(w.win, "设置")
        time.sleep(2)
        w.dismiss_error_dialog()
        click_named_any(w.win, "全局设置", kinds=("Text",))
        w.shot("97-settings-global.png", wait=1)
    finally:
        w.stop()


if __name__ == "__main__":
    main()
