"""验证「加载器选择页」的多选：Forge + OptiFine 能同时选中并一起入队。

用法：
    python tools/ui_loader_multi.py
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

TARGET = "1.20.4"


def click_named_any(win, name, kinds=("ListItem", "Text", "Button")):
    """导航栏/标签栏的项可能是 ListItem，也可能是 Text，逐个试。"""
    for kind in kinds:
        try:
            ctrl = getattr(win, kind + "Control")(Name=name, searchDepth=0xFFFFFFFF)
            if ctrl.Exists(maxSearchSeconds=2):
                rect = ctrl.BoundingRectangle
                if rect.width() <= 0:
                    ctrl = ctrl.GetParentControl()
                ctrl.Click()
                time.sleep(1.2)
                print(f"  点击 {kind} / {name} ✓")
                return True
        except Exception:
            pass
    raise RuntimeError(f"找不到控件：{name}（试过 {kinds}）")


def type_search(win, text):
    """原版页里有多个 EditControl（模组页的搜索框也在树里），逐个试到清单被过滤为止。"""
    edits = find_all(win, "Edit")
    print(f"  找到 {len(edits)} 个输入框")
    for i, ed in enumerate(edits):
        try:
            ed.Click()
            time.sleep(0.2)
            ed.SendKeys(text)
            time.sleep(1.5)
            items = [c for c in find_all(win, "ListItem") if TARGET in "".join(t.Name or "" for t in find_all(c, "Text"))]
            if items:
                print(f"  输入框 #{i} 生效，命中 {TARGET} 的行：{[t.Name for t in find_all(items[0], 'Text')]}")
                return True
            print(f"  输入框 #{i} 没过滤出结果")
        except Exception as exc:
            print(f"  输入框 #{i} 失败：{exc}")
    raise RuntimeError("没有输入框能把清单过滤到目标版本")


def version_row(win):
    for c in find_all(win, "ListItem"):
        if "AvailableVersionRow" not in (c.Name or ""):
            continue
        texts = [t.Name for t in find_all(c, "Text")]
        if texts and texts[0] == TARGET:
            return c
    raise RuntimeError(f"清单里没有 {TARGET} 这一行")


def continue_button(win):
    for b in find_all(version_row(win), "Button"):
        if (b.Name or "").strip() == "继续":
            return b
    raise RuntimeError("这一行里没有「继续」按钮")


def loader_rows(win):
    """加载器页每行是一个 Button（内容是 Grid，按钮本身的 Name 为空），按行内文本匹配。"""
    out = {}
    for b in find_all(win, "Button"):
        texts = [(t.Name or "").strip() for t in find_all(b, "Text")]
        if not texts:
            continue
        for key in ("Fabric", "Forge", "NeoForge", "OptiFine"):
            if key in texts:
                out.setdefault(key, b)
    return out


def row_state(btn):
    """返回 (可点, 复选框状态列表, 右列文本)"""
    enabled = btn.IsEnabled
    checks = []
    for cb in ([btn] if btn.ControlTypeName == "CheckBoxControl" else find_all(btn, "CheckBox")):
        try:
            checks.append(cb.GetTogglePattern().ToggleState)
        except Exception:
            pass
    texts = [t.Name for t in find_all(btn, "Text") if (t.Name or "").strip()]
    return enabled, checks, texts


def summary_text(win):
    for t in find_all(win, "Text"):
        name = (t.Name or "").strip()
        if "一起安装" in name or "不会安装" in name or "只装原版" in name or "将安装" in name:
            return name
    return ""


def main():
    walker = Walker(EXE, OUT)
    walker.start()
    try:
        print("[1] 进下载页 → 原版游戏")
        walker.dismiss_error_dialog()
        click_named_any(walker.win, "下载")
        walker.dismiss_error_dialog()
        click_named_any(walker.win, "原版游戏")
        walker.dismiss_error_dialog()

        print(f"[2] 搜索 {TARGET}")
        type_search(walker.win, TARGET)
        walker.shot("80-vanilla-filtered.png", wait=1)

        print("[3] 点 1.20.4 的「继续」进入加载器选择页")
        continue_button(walker.win).GetInvokePattern().Invoke()
        time.sleep(12)  # 三个加载器都要去网上拉版本列表
        walker.dismiss_error_dialog()
        walker.shot("81-loader-select.png", wait=1)

        rows = loader_rows(walker.win)
        print("  发现加载器行：", sorted(rows))
        assert {"Forge", "OptiFine", "Fabric"}.issubset(rows), "加载器行不全"

        print("\n[4] 初始状态")
        for key in ("Fabric", "Forge", "NeoForge", "OptiFine"):
            if key in rows:
                en, chk, texts = row_state(rows[key])
                print(f"  {key:9} 可点={en} 勾选={chk} :: {texts}")

        print("\n[5] 关键一步：先点 Forge")
        rows["Forge"].GetInvokePattern().Invoke()
        time.sleep(1)
        en_f, _, _ = row_state(rows["Forge"])
        print("  Forge 勾选状态：", row_state(rows["Forge"])[1])
        print("  Fabric 可点（Forge 已选，应 False）：", row_state(rows["Fabric"])[0])
        print("  NeoForge 可点（应 False）：", row_state(rows["NeoForge"])[0])
        print("  OptiFine 可点（应 True，可共存）：", row_state(rows["OptiFine"])[0])
        print("  摘要：", summary_text(walker.win))
        walker.shot("82-forge-selected.png", wait=0.6)

        print("\n[6] 再点 OptiFine（用户要求的 Forge + OptiFine 同时安装）")
        print("  OptiFine 按钮 IsEnabled：", rows["OptiFine"].IsEnabled)
        rows["OptiFine"].GetInvokePattern().Invoke()
        time.sleep(1)
        print("  Forge 勾选：", row_state(rows["Forge"])[1])
        print("  OptiFine 勾选：", row_state(rows["OptiFine"])[1])
        print("  摘要：", summary_text(walker.win))
        walker.shot("83-forge-optifine.png", wait=0.6)

        print("\n[7] 断言：Fabric / NeoForge 被置灰")
        assert not row_state(rows["Fabric"])[0], "Fabric 应该被置灰"
        assert not row_state(rows["NeoForge"])[0], "NeoForge 应该被置灰"
        print("  ✓ Fabric / NeoForge 已置灰")

        print("\n[8] 取消 OptiFine，改成 Forge 单选，看 NeoForge 是否仍置灰")
        rows["OptiFine"].GetInvokePattern().Invoke()
        time.sleep(1)
        print("  Forge 勾选：", row_state(rows["Forge"])[1])
        print("  OptiFine 勾选：", row_state(rows["OptiFine"])[1])
        print("  NeoForge 可点（应 False）：", row_state(rows["NeoForge"])[0])
        print("  Fabric 可点（应 False）：", row_state(rows["Fabric"])[0])
        print("  摘要：", summary_text(walker.win))

        print("\n[9] 全取消，看摘要是否变成只装原版")
        rows["Forge"].GetInvokePattern().Invoke()
        time.sleep(1)
        print("  摘要：", summary_text(walker.win))
        print("  Fabric 可点（应 True）：", row_state(rows["Fabric"])[0])
        print("  NeoForge 可点（应 True）：", row_state(rows["NeoForge"])[0])
        print("  OptiFine 可点（应 True）：", row_state(rows["OptiFine"])[0])
        walker.shot("84-none-selected.png", wait=0.6)

        print("\n[10] 重新选 Forge + OptiFine，点开始下载，看队列")
        rows["Forge"].GetInvokePattern().Invoke()
        time.sleep(0.8)
        rows["OptiFine"].GetInvokePattern().Invoke()
        time.sleep(0.8)
        print("  摘要：", summary_text(walker.win))
        click_named_any(walker.win, "开始下载", kinds=("Button",))
        time.sleep(3)
        walker.dismiss_error_dialog()
        walker.shot("85-queued.png", wait=1)

        print("\n[11] 切到下载队列页看任务")
        click_named_any(walker.win, "下载队列")
        time.sleep(2)
        walker.dismiss_error_dialog()
        walker.shot("86-queue-tab.png", wait=1)
        texts = [t.Name for t in find_all(walker.win, "Text")
                 if (t.Name or "").strip() and ("Forge" in t.Name or "OptiFine" in t.Name or "原版" in t.Name)]
        print("  队列里含关键字的文本：")
        for t in texts[:25]:
            print("   -", t)
    finally:
        walker.stop()


if __name__ == "__main__":
    main()
