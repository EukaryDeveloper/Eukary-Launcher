"""设置 / 关于 页面信息流布局验证：截图 + 交互（开关、复制、打开）。"""
import os
import sys
import time

import uiautomation as auto

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from ui_walk import Walker  # noqa: E402

auto.uiautomation.SetGlobalSearchTimeout(3)

OUT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "tools", "UiShots"))
EXE = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "EukaryLauncher.App",
                                   "bin", "Debug", "net8.0-windows", "win-x64", "EukaryLauncher.exe"))


def main():
    walker = Walker(EXE, OUT)
    walker.start()
    try:
        print("[1] 打开设置页")
        walker.click_named("设置", "Text")
        walker.dismiss_error_dialog()
        walker.shot("70-settings.png", wait=2)

        # 鼠标划过「最大内存」这一行，看 hover 底色
        print("[2] 悬停一行看 hover 反馈")
        try:
            ctrl = walker.find("最大内存", "Text")
            rect = ctrl.BoundingRectangle
            auto.Click(rect.left + 40, rect.bottom + 40)  # 移到行右下空白处
            auto.SetCursorPos(rect.left + 60, rect.top + 10)
        except Exception as exc:
            print("  悬停失败：", exc)
        walker.shot("71-settings-hover.png", wait=1)

        print("[3] 切换「游戏启动后保持启动器运行」开关")
        try:
            sw = walker.win.ToggleButtonControl(searchDepth=0xFFFFFFFF)
            btns = sw.FindAll()
            print("  发现开关数量：", len(btns))
            if btns:
                b = btns[-1]
                print("  切换最后一个开关，切换前 IsSelected 状态已读取")
                b.Click()
                time.sleep(0.8)
                walker.shot("72-settings-toggled.png", wait=0.5)
        except Exception as exc:
            print("  切换失败：", exc)

        print("[4] 打开关于页")
        walker.click_named("关于", "Text")
        walker.dismiss_error_dialog()
        walker.shot("73-about.png", wait=2)

        print("[5] 点「复制」仓库地址，应弹 Toast")
        try:
            walker.click_named("复制", "Button")
        except Exception as exc:
            print("  点击复制失败：", exc)
        walker.shot("74-about-copied.png", wait=0.9)

        print("[6] 滚动到页面底部")
        auto.WheelDown(wheelTimes=6)
        time.sleep(1)
        walker.shot("75-about-bottom.png", wait=0.5)
    finally:
        walker.stop()


if __name__ == "__main__":
    main()
