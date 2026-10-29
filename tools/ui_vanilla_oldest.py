"""验证：同时勾选「显示快照」+「显示远古版本」后，版本清单能一直翻到最早的 rd-xxx。"""
import os
import sys
import time

import uiautomation as auto

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from ui_walk import Walker, find_all  # noqa: E402
from ui_loader_multi import click_named_any  # noqa: E402

auto.uiautomation.SetGlobalSearchTimeout(3)

OUT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "tools", "UiShots"))
EXE = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "EukaryLauncher.App",
                                   "bin", "Debug", "net8.0-windows", "win-x64", "EukaryLauncher.exe"))


def row_texts(win):
    out = []
    for li in find_all(win, "ListItem"):
        if "AvailableVersionRow" not in (li.Name or ""):
            continue
        texts = [t.Name for t in find_all(li, "Text")]
        if texts:
            out.append(texts)
    return out


def status_of(win):
    for t in find_all(win, "Text"):
        n = (t.Name or "").strip()
        if n.startswith("共 ") or n.startswith("显示 "):
            return n
    return ""


def toggle(win, label):
    for cb in find_all(win, "CheckBox"):
        texts = [(t.Name or "").strip() for t in find_all(cb, "Text")]
        if label in texts:
            cb.Click()
            time.sleep(1.5)
            return True
    return False


def scroll_list_bottom(win):
    for c in find_all(win, "List"):
        try:
            c.GetScrollPattern().SetScrollPercent(-1, 100)
            return True
        except Exception:
            continue
    return False


def main():
    w = Walker(EXE, OUT)
    w.start()
    try:
        w.dismiss_error_dialog()
        w.click_named("下载", "Text")
        time.sleep(1.5)
        click_named_any(w.win, "原版游戏")
        time.sleep(5)
        w.dismiss_error_dialog()

        print("初始：", status_of(w.win), "可见行", len(row_texts(w.win)))

        print("\n[1] 勾上「显示快照」")
        print("  找到开关：", toggle(w.win, "显示快照"))
        print("  状态：", status_of(w.win))

        print("\n[2] 勾上「显示远古版本」")
        print("  找到开关：", toggle(w.win, "显示远古版本"))
        print("  状态：", status_of(w.win))

        print("\n[3] 滚到清单底部")
        if scroll_list_bottom(w.win):
            time.sleep(2)
        else:
            print("  没拿到滚动条，改用按键")
        rows = row_texts(w.win)
        print("  底部可见行：")
        for r in rows[-6:]:
            print("   ", r)
        w.shot("87-vanilla-oldest.png", wait=1)

        ids = [r[0] for r in rows]
        print("\n最早可见版本：", ids[-1] if ids else "（空）")
        if any(i.startswith("rd-") for i in ids):
            print("  ✓ 已经能翻到 rd- 开头的远古版本")
        else:
            print("  ✗ 还是没翻到最早版本")
    finally:
        w.stop()


if __name__ == "__main__":
    main()
