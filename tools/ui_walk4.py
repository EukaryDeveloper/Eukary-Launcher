"""第四轮走查：Toast 修复验证 + 模组真实入队 + 队列进度条。"""
import os
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
        print("[1] 下载 → 模组（自动搜索）→ Sodium 详情")
        walker.click_named("下载", "Text")
        time.sleep(2)
        walker.click_named("模组", "ListItem")
        time.sleep(8)
        edit = walker.find("", "Edit")
        edit.GetValuePattern().SetValue("sodium")
        walker.click_named("搜索", "Button")
        walker.dismiss_error_dialog()
        time.sleep(7)
        walker.click_named("Sodium", "Text")
        walker.shot("50-detail.png", wait=6)

        print("[2] 点 1.16.3 组文件行 → 应弹『尚未安装 Minecraft 1.16.3』Toast")
        import uiautomation as auto
        row = walker.find("Sodium 0.2.0", "Text")
        rect = row.BoundingRectangle
        auto.Click(rect.left + rect.width() // 2, rect.top + rect.height() // 2)
        walker.shot("51-toast-warning.png", wait=0.8)
        walker.shot("52-toast-warning2.png", wait=1.0)

        print("[3] 版本筛选选 1.20.4 → 点文件行 → 应弹『开始安装』并入队")
        import uiautomation as auto
        chip = walker.win.TextControl(SubName="1.20.4", searchDepth=0xFFFFFFFF)
        if not chip.Exists(maxSearchSeconds=8):
            raise RuntimeError("找不到 1.20.4 版本 chip")
        rect = chip.BoundingRectangle
        auto.Click(rect.left + rect.width() // 2, rect.top + rect.height() // 2)
        time.sleep(2.5)
        walker.shot("53-detail-1.20.4.png", wait=0.5)

        # 找 1.20.4 组下第一个可见的 Sodium 文件行（排除页面大标题）
        candidates = []

        def walk(ctrl, depth=0):
            if depth > 30:
                return
            try:
                for child in ctrl.GetChildren():
                    try:
                        name = child.Name or ""
                        if child.ControlTypeName == "TextControl" and name.startswith("Sodium ") and name != "Sodium":
                            r = child.BoundingRectangle
                            if r.width() > 100 and not child.IsOffscreen and r.top > 300:
                                candidates.append((name, r))
                    except Exception:
                        pass
                    walk(child, depth + 1)
            except Exception:
                pass

        walk(walker.win)
        for name, r in candidates[:6]:
            print("   候选行：", name, r)
        if not candidates:
            raise RuntimeError("没有可见的 Sodium 文件行")
        name, r = candidates[0]
        auto.Click(r.left + r.width() // 2, r.top + r.height() // 2)
        walker.shot("54-toast-start.png", wait=0.8)

        print("[4] 切到下载队列 → 看进度条")
        walker.click_named("下载队列", "ListItem")
        walker.shot("55-queue-1.png", wait=1.5)
        walker.shot("56-queue-2.png", wait=2)
        walker.shot("57-queue-3.png", wait=3)
        walker.dismiss_error_dialog()
        time.sleep(10)
        walker.shot("58-queue-done.png", wait=0.5)
        walker.dismiss_error_dialog()
    finally:
        walker.stop()


if __name__ == "__main__":
    main()
