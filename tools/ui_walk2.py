"""第二轮 UI 走查：Fabric 真实入队 + 模组版本页复核。"""
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(__file__))

from ui_walk import Walker  # noqa: E402


def main():
    exe = sys.argv[1] if len(sys.argv) > 1 else os.path.abspath(
        os.path.join(os.path.dirname(__file__), "..", "EukaryLauncher.App", "bin",
                     "Debug", "net8.0-windows", "win-x64", "EukaryLauncher.exe"))
    out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "UiShots")

    walker = Walker(exe, out_dir)
    walker.start()

    try:
        print("[1] 下载 → 原版游戏 → 搜 1.20.4 → 继续")
        walker.click_named("下载", "Text")
        time.sleep(2)
        edit = walker.find("", "Edit")
        edit.GetValuePattern().SetValue("1.20.4")
        time.sleep(1)
        walker.click_named("继续", "Button")
        walker.shot("10-loader-1.20.4.png", wait=5)

        print("[2] 选中 Fabric")
        walker.click_named("Fabric", "Text")
        walker.shot("11-loader-fabric-selected.png", wait=1)

        print("[3] 开始下载 → 看队列")
        walker.click_named("开始下载", "Button")
        time.sleep(2)
        walker.click_named("下载队列", "ListItem")
        walker.shot("12-queue-fabric.png", wait=3)
        walker.shot("13-queue-fabric-later.png", wait=40)

        print("[4] 模组版本页复核")
        walker.click_named("模组", "ListItem")
        time.sleep(1.5)
        edit = walker.find("", "Edit")
        edit.GetValuePattern().SetValue("sodium")
        walker.click_named("搜索", "Button")
        time.sleep(7)
        walker.click_named("Sodium", "Text")
        walker.shot("14-mod-detail-fixed.png", wait=6)
    finally:
        walker.stop()


if __name__ == "__main__":
    main()
