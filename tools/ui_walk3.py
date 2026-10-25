"""第三轮 UI 走查：模组文件行点击入队 + Toast + 队列进度条验证。"""
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(__file__))

from ui_walk import Walker  # noqa: E402


def list_buttons(walker, keyword=""):
    """递归遍历控件树，收集名字含 keyword 的按钮。"""
    found = []

    def walk(ctrl, depth=0):
        if depth > 30:
            return
        try:
            for child in ctrl.GetChildren():
                try:
                    if child.ControlTypeName == "ButtonControl":
                        name = child.Name or ""
                        if keyword in name:
                            found.append((name, child.BoundingRectangle))
                except Exception:
                    pass
                walk(child, depth + 1)
        except Exception:
            pass

    walk(walker.win)
    return found


def main():
    exe = sys.argv[1] if len(sys.argv) > 1 else os.path.abspath(
        os.path.join(os.path.dirname(__file__), "..", "EukaryLauncher.App", "bin",
                     "Debug", "net8.0-windows", "win-x64", "EukaryLauncher.exe"))
    out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "UiShots")

    walker = Walker(exe, out_dir)
    walker.start()

    try:
        print("[1] 下载 → 模组 Tab（这次应该自动搜索）")
        walker.click_named("下载", "Text")
        time.sleep(2)
        walker.click_named("模组", "ListItem")
        walker.shot("20-mods-auto-search.png", wait=9)

        print("[2] 搜索 sodium")
        edit = walker.find("", "Edit")
        edit.GetValuePattern().SetValue("sodium")
        walker.click_named("搜索", "Button")
        walker.dismiss_error_dialog()
        walker.shot("21-mods-results.png", wait=8)

        print("[3] 打开 Sodium 详情")
        walker.click_named("Sodium", "Text")
        walker.shot("22-mod-detail.png", wait=7)

        print("[4] 点击第一个文件行（Sodium 0.2.0）")
        import uiautomation as auto
        row = walker.find("Sodium 0.2.0", "Text")
        rect = row.BoundingRectangle
        print("   行位置：", rect)
        auto.Click(rect.left + rect.width() // 2, rect.top + rect.height() // 2)
        time.sleep(0.4)

        print("[5] 点击后立刻截图看 Toast")
        walker.shot("23-after-click-toast.png", wait=0.6)
        walker.shot("24-after-click-toast2.png", wait=1.2)
        walker.dismiss_error_dialog()

        print("[6] 切到下载队列看有没有任务")
        walker.click_named("下载队列", "ListItem")
        walker.shot("25-queue-after-mod-click.png", wait=2)
        walker.shot("26-queue-progress.png", wait=3)
        walker.shot("27-queue-final.png", wait=15)
        walker.dismiss_error_dialog()
    finally:
        walker.stop()


if __name__ == "__main__":
    main()
