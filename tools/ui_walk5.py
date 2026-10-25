"""第五轮走查：抓拍队列进度条填充状态（用稍大的 Iris）。"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(__file__))

from ui_walk import Walker  # noqa: E402
import uiautomation as auto  # noqa: E402


def main():
    exe = sys.argv[1] if len(sys.argv) > 1 else os.path.abspath(
        os.path.join(os.path.dirname(__file__), "..", "EukaryLauncher.App", "bin",
                     "Debug", "net8.0-windows", "win-x64", "EukaryLauncher.exe"))
    out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "UiShots")

    walker = Walker(exe, out_dir)
    walker.start()

    try:
        print("[1] 模组 → 搜 iris → 详情")
        walker.click_named("下载", "Text")
        time.sleep(2)
        walker.click_named("模组", "ListItem")
        time.sleep(8)
        edit = walker.find("", "Edit")
        edit.GetValuePattern().SetValue("iris")
        walker.click_named("搜索", "Button")
        walker.dismiss_error_dialog()
        time.sleep(7)
        walker.click_named("Iris Shaders", "Text")
        time.sleep(6)

        print("[2] 选 1.20.4 → 点文件行 → 立刻切队列连拍")
        chip = walker.win.TextControl(SubName="1.20.4", searchDepth=0xFFFFFFFF)
        if not chip.Exists(maxSearchSeconds=8):
            raise RuntimeError("找不到 1.20.4 chip")
        r = chip.BoundingRectangle
        auto.Click(r.left + r.width() // 2, r.top + r.height() // 2)
        time.sleep(2.5)

        candidates = []

        def walk(ctrl, depth=0):
            if depth > 30:
                return
            try:
                for child in ctrl.GetChildren():
                    try:
                        name = child.Name or ""
                        if child.ControlTypeName == "TextControl" and "Iris" in name and name != "Iris Shaders":
                            rect = child.BoundingRectangle
                            if rect.width() > 100 and not child.IsOffscreen and rect.top > 300:
                                candidates.append((name, rect))
                    except Exception:
                        pass
                    walk(child, depth + 1)
            except Exception:
                pass

        walk(walker.win)
        for name, rect in candidates[:5]:
            print("   候选：", name, rect)
        if not candidates:
            raise RuntimeError("没有可见的 Iris 文件行")

        name, rect = candidates[0]
        auto.Click(rect.left + rect.width() // 2, rect.top + rect.height() // 2)
        time.sleep(0.3)
        walker.click_named("下载队列", "ListItem")
        for i in range(8):
            walker.shot(f"60-progress-{i}.png", wait=0.35)
        walker.dismiss_error_dialog()
        time.sleep(12)
        walker.shot("68-progress-done.png", wait=0.5)
        walker.dismiss_error_dialog()
    finally:
        walker.stop()


if __name__ == "__main__":
    main()
