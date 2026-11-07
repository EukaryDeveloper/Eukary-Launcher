"""验证下载页顶部 Tab 的切换过渡：下划线滑条、内容交叉过渡、字色渐变。

流程：进下载页 → 依次点 模组 / 整合包 / 下载队列 / 原版游戏，
每个 tab 停留后截图；中间抓一帧转场中的画面；最后验证加载器选择页还能正常进出。
"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(__file__))

from ui_walk import Walker, find_all  # noqa: E402
import uiautomation as auto  # noqa: E402

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "UiShots", "tabs")

TAB_NAMES = ["原版游戏", "模组", "整合包", "数据包", "资源包", "光影包", "下载队列"]


def click_tab(w, title):
    """点顶部 Tab（ListItem 的 Name 就是 tab 文本本身）。"""
    for it in find_all(w.win, "ListItem"):
        if (it.Name or "").strip() == title:
            r = it.BoundingRectangle
            auto.Click(r.left + r.width() // 2, r.top + r.height() // 2)
            return True
    return False


def click_nav(w, title):
    for it in find_all(w.win, "ListItem"):
        if f"Title = {title}" in (it.Name or ""):
            r = it.BoundingRectangle
            auto.Click(r.left + r.width() // 2, r.top + r.height() // 2)
            return True
    return False


def shot(w, name, wait=0.0):
    if wait:
        time.sleep(wait)
    w.shot(os.path.join(OUT, name), wait=0)
    print("截图", name)


def main():
    exe = sys.argv[1] if len(sys.argv) > 1 else os.path.abspath(
        os.path.join(os.path.dirname(__file__), "..", "EukaryLauncher.App", "bin",
                     "Debug", "net8.0-windows", "win-x64", "EukaryLauncher.exe"))
    os.makedirs(OUT, exist_ok=True)

    w = Walker(exe, OUT)
    w.start()

    if not click_nav(w, "下载"):
        raise RuntimeError("进不了下载页")
    time.sleep(0.8)

    # 初始下划线应停在 原版游戏 下面
    shot(w, "0-原版.png", wait=0.4)

    click_tab(w, "模组")
    shot(w, "1-转场中-模组.png", wait=0.12)   # 转场中间帧
    shot(w, "2-模组落定.png", wait=0.6)

    click_tab(w, "整合包")
    shot(w, "3-整合包落定.png", wait=0.7)

    click_tab(w, "下载队列")
    shot(w, "4-队列落定.png", wait=0.7)

    # 跨多个 tab 回到最左边，滑条行程最长
    click_tab(w, "原版游戏")
    shot(w, "5-回原版落定.png", wait=0.8)

    # 加载器选择页进出（同一 tab 内的内容切换）
    rows = [t for t in find_all(w.win, "Button") if (t.Name or "") == "继续"]
    if rows:
        rows[0].Click()
        shot(w, "6-加载器选择.png", wait=0.8)
        backs = [t for t in find_all(w.win, "Button") if (t.Name or "") == "返回"]
        if backs:
            backs[0].Click()
            shot(w, "7-返回原版.png", wait=0.8)
    else:
        print("没找到「继续」按钮，跳过加载器选择验证")

    w.stop()
    print("完成，截图在", OUT)


if __name__ == "__main__":
    main()
