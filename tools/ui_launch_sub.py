"""验证主页开始游戏按钮两行内容：第二行显示版本号+加载器小字。"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(__file__))

from ui_walk import Walker, find_all  # noqa: E402
from ui_loader_multi import click_named_any  # noqa: E402


def main():
    exe = sys.argv[1] if len(sys.argv) > 1 else os.path.abspath(
        os.path.join(os.path.dirname(__file__), "..", "EukaryLauncher.App", "bin",
                     "Debug", "net8.0-windows", "win-x64", "EukaryLauncher.exe"))
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "UiShots")

    w = Walker(exe, out)
    w.start()
    try:
        w.dismiss_error_dialog()
        time.sleep(1.5)
        w.shot("97-home-launch-sub.png", wait=1)

        print("[1] 找开始游戏按钮内文本")
        texts = [(t.Name or "", t.BoundingRectangle) for t in find_all(w.win, "Text")]
        main_line = [x for x in texts if x[0] == "开始游戏"]
        assert main_line, "主页找不到「开始游戏」文本"
        r0 = main_line[0][1]
        print("  主行：", r0)

        print("[2] 检查第二行小字（同一按钮内、主行下方）")
        sub = [(n, r) for n, r in texts
               if r.top > r0.top and abs((r.left + r.width() // 2) - (r0.left + r0.width() // 2)) < 60
               and r.top - r0.bottom < 30 and r.width() > 0]
        assert sub, "开始按钮下方没有找到副标题文本"
        print("  副标题：", sub[0])
        print("\n✓ 通过：开始游戏按钮为两行内容")
    finally:
        w.stop()


if __name__ == "__main__":
    main()
