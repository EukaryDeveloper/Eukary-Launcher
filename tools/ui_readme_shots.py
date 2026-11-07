"""抓 README 用截图：主页 + 下载页。

用法：python tools/ui_readme_shots.py
输出：docs/screenshot-main.png、docs/screenshot-downloads.png
"""
import os
import shutil
import sys
import time

sys.path.insert(0, os.path.dirname(__file__))

from ui_walk import Walker, find_all  # noqa: E402
import uiautomation as auto  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
EXE = os.path.join(ROOT, "EukaryLauncher.App", "bin", "Debug", "net8.0-windows",
                   "win-x64", "EukaryLauncher.exe")
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "UiShots", "readme")


def click_nav(w, title):
    for it in find_all(w.win, "ListItem"):
        if f"Title = {title}" in (it.Name or ""):
            r = it.BoundingRectangle
            auto.Click(r.left + r.width() // 2, r.top + r.height() // 2)
            return True
    return False


def main():
    exe = sys.argv[1] if len(sys.argv) > 1 else EXE
    os.makedirs(OUT, exist_ok=True)
    w = Walker(exe, OUT)
    w.start()
    try:
        time.sleep(1)
        w.shot("main.png", wait=0)
        shutil.copyfile(os.path.join(OUT, "main.png"),
                        os.path.join(ROOT, "docs", "screenshot-main.png"))
        print("已更新 docs/screenshot-main.png")

        if click_nav(w, "下载"):
            time.sleep(1.8)
            w.shot("downloads.png", wait=0)
            shutil.copyfile(os.path.join(OUT, "downloads.png"),
                            os.path.join(ROOT, "docs", "screenshot-downloads.png"))
            print("已更新 docs/screenshot-downloads.png")
        else:
            print("FAIL: 没找到侧栏「下载」")
    finally:
        if w.proc:
            w.proc.kill()


if __name__ == "__main__":
    main()
