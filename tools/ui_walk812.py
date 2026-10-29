"""模组页链路走查：抽屉出来的版本列表、安装按钮、Toast、返回后搜索保留。

输入框按屏幕位置排序（名称/作者一行，游戏版本一行），用真实按键输入。
"""
import os
import sys
import time

import uiautomation as auto

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from ui_walk import Walker, find_all  # noqa: E402

auto.uiautomation.SetGlobalSearchTimeout(3)

OUT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "tools", "UiShots"))
EXE = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "EukaryLauncher.App",
                                   "bin", "Debug", "net8.0-windows", "win-x64", "EukaryLauncher.exe"))


def edits_sorted(win):
    return sorted(find_all(win, "Edit"),
                  key=lambda e: (e.BoundingRectangle.top, e.BoundingRectangle.left))


def type_into(ctrl, text):
    ctrl.Click()
    time.sleep(0.3)
    auto.SendKeys("{CTRL}a")
    auto.SendKeys(text)
    time.sleep(0.4)


def buttons_named(win, name):
    return [b for b in find_all(win, "Button") if (b.Name or "").strip() == name]


def version_rows(win):
    """[(ListItem, [行内文本...])]，只取模组页的版本行。"""
    out = []
    for li in find_all(win, "ListItem"):
        if "Row" not in (li.Name or ""):
            continue
        texts = [t.Name for t in find_all(li, "Text")]
        if texts:
            out.append((li, texts))
    return out


def continue_in(row_li):
    for b in find_all(row_li, "Button"):
        if (b.Name or "").strip() == "继续":
            return b
    return None


def main():
    w = Walker(EXE, OUT)
    w.start()
    try:
        w.dismiss_error_dialog()

        print("[1] 进下载页 → 模组页")
        w.click_named("下载", "ListItem")
        time.sleep(1.5)
        w.click_named("模组", "ListItem")
        time.sleep(3)
        w.dismiss_error_dialog()

        es = edits_sorted(w.win)
        print(f"  输入框 {len(es)} 个，位置：{[(e.BoundingRectangle.top, e.BoundingRectangle.left) for e in es]}")

        print("[2] 名称搜 sodium")
        type_into(es[0], "sodium")
        time.sleep(3)
        w.dismiss_error_dialog()
        rows = version_rows(w.win)
        print("  版本行：")
        for _, texts in rows:
            print("   ", texts)

        print("[3] 收起已安装列表")
        w.click_named("已安装（", "Text")
        time.sleep(1)
        rows = version_rows(w.win)
        print(f"  收起后版本行 {len(rows)}：{[t for _, t in rows]}")

        target = "1.20.1"
        picked = None
        for li, texts in rows:
            if texts and texts[0] == target:
                picked = li
                break
        if picked is None and rows:
            picked, texts = rows[-1]
            target = texts[0]
        print(f"  选中版本：{target}")

        print("[4] 点这一行的继续 → 打开抽屉")
        btn = continue_in(picked)
        btn.Click()
        time.sleep(5)
        w.dismiss_error_dialog()
        w.shot("90-drawer.png", wait=1)
        print("  抽屉里「继续 →」数：", len(buttons_named(w.win, "继续 →")))

        print("[5] 抽屉里点安装")
        installs = buttons_named(w.win, "安装")
        print("  安装按钮：", len(installs))
        if installs:
            installs[0].Click()
            time.sleep(6)
            w.dismiss_error_dialog()
            w.shot("91-installed.png", wait=1)
            print("  打开文件夹：", len(buttons_named(w.win, "打开文件夹")))
            print("  安装还在吗：", len(buttons_named(w.win, "安装")))

        print("[6] 关抽屉，看搜索词是否还在")
        closes = buttons_named(w.win, "关闭")
        if closes:
            closes[0].Click()
            time.sleep(2)
        w.dismiss_error_dialog()
        w.click_named("已安装（", "Text")
        time.sleep(1)
        print(f"  版本行：{[t for _, t in version_rows(w.win)]}")
        w.shot("92-after-close.png", wait=1)

        print("[7] 按作者搜 jellysquid")
        es = edits_sorted(w.win)
        type_into(es[0], "")
        time.sleep(2.5)
        type_into(es[1], "jellysquid")
        time.sleep(3)
        w.dismiss_error_dialog()
        print(f"  版本行：{[t for _, t in version_rows(w.win)]}")
        w.shot("93-author.png", wait=1)
    finally:
        w.stop()


if __name__ == "__main__":
    main()
