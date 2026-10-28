"""模组页链路走查：搜索 → 版本抽屉 → 安装 → Toast → 返回后搜索保留。

用真实按键输入（SetWindowText 不会触发 WPF 绑定，必须用 SendKeys）。
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


def type_into(ctrl, text):
    ctrl.Click()
    time.sleep(0.3)
    auto.SendKeys("{CTRL}a")
    auto.SendKeys(text)
    time.sleep(0.3)


def buttons_named(win, name):
    return [b for b in find_all(win, "Button") if (b.Name or "").strip() == name]


def available_rows(win):
    rows = []
    for li in find_all(win, "ListItem"):
        if "ModVersionRow" not in (li.Name or ""):
            continue
        texts = [t.Name for t in find_all(li, "Text")]
        if texts:
            rows.append(texts)
    return rows


def main():
    w = Walker(EXE, OUT)
    w.start()
    try:
        w.dismiss_error_dialog()
        w.click_named("下载", "ListItem")
        time.sleep(1.5)
        w.click_named("模组", "ListItem")
        time.sleep(3)
        w.dismiss_error_dialog()

        print("[1] 名称搜 sodium（真实按键）")
        type_into(find_all(w.win, "Edit")[0], "sodium")
        time.sleep(3)
        w.dismiss_error_dialog()
        print("  可用版本行：", available_rows(w.win))
        print("  继续按钮数：", len(buttons_named(w.win, "继续")))

        print("[2] 收起已安装列表")
        w.click_named("已安装（", "Text")
        time.sleep(1)
        print("  继续按钮数：", len(buttons_named(w.win, "继续")))

        print("[3] 版本筛选 1.20.1，把候选收敛到一个")
        type_into(find_all(w.win, "Edit")[1], "1.20.1{ENTER}")
        time.sleep(3)
        w.dismiss_error_dialog()
        rows = available_rows(w.win)
        print("  可用版本行：", rows)
        conts = buttons_named(w.win, "继续")
        print("  继续按钮数：", len(conts))
        w.shot("90-mods-sodium-1201.png", wait=1)

        if not conts:
            print("  没有候选，跳过")
            return

        print("[4] 点继续 → 抽屉")
        conts[0].Click()
        time.sleep(5)
        w.dismiss_error_dialog()
        w.shot("91-drawer.png", wait=1)

        print("[5] 抽屉里点安装")
        installs = buttons_named(w.win, "安装")
        print("  安装按钮：", len(installs))
        if installs:
            installs[0].Click()
            time.sleep(6)
            w.dismiss_error_dialog()
            w.shot("92-installed.png", wait=1)
            print("  打开文件夹：", len(buttons_named(w.win, "打开文件夹")))
            print("  安装还在吗：", len(buttons_named(w.win, "安装")))

        print("[6] 关抽屉，看搜索词还在不在")
        closes = buttons_named(w.win, "关闭")
        if closes:
            closes[0].Click()
            time.sleep(2)
        w.dismiss_error_dialog()
        w.click_named("已安装（", "Text")
        time.sleep(1)
        print("  可用版本行：", available_rows(w.win))
        w.shot("93-after-close.png", wait=1)

        print("[7] 清掉名称，按作者搜")
        type_into(find_all(w.win, "Edit")[0], "")
        time.sleep(3)
        type_into(find_all(w.win, "Edit")[1], "jellysquid")
        time.sleep(3)
        w.dismiss_error_dialog()
        print("  作者搜结果：", available_rows(w.win))
        w.shot("94-author.png", wait=1)
    finally:
        w.stop()


if __name__ == "__main__":
    main()
