"""验证模组详情页版本筛选条：单行横向排列 + 横向滚动。"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(__file__))

from ui_walk import Walker, find_all  # noqa: E402
from ui_loader_multi import click_named_any  # noqa: E402
import uiautomation as auto  # noqa: E402


def main():
    exe = sys.argv[1] if len(sys.argv) > 1 else os.path.abspath(
        os.path.join(os.path.dirname(__file__), "..", "EukaryLauncher.App", "bin",
                     "Debug", "net8.0-windows", "win-x64", "EukaryLauncher.exe"))
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "UiShots")

    w = Walker(exe, out)
    w.start()
    try:
        w.dismiss_error_dialog()
        print("[1] 下载 → 模组")
        click_named_any(w.win, "下载")
        time.sleep(2)
        click_named_any(w.win, "模组")
        time.sleep(8)
        w.dismiss_error_dialog()

        print("[2] 搜索 iris")
        for ed in find_all(w.win, "Edit"):
            ed.Click()
            time.sleep(0.3)
            ed.SendKeys("{Ctrl}a{Delete}iris")
            break
        time.sleep(0.5)
        click_named_any(w.win, "搜索")
        w.dismiss_error_dialog()
        time.sleep(7)

        print("[3] 打开 Iris Shaders 详情")
        click_named_any(w.win, "Iris Shaders")
        time.sleep(6)
        w.dismiss_error_dialog()
        w.shot("96-chip-bar-initial.png", wait=1)

        print("[4] 检查 chip 是否单行（限定在版本筛选条 ListBox 内）")
        tops = set()
        chip_list = None
        scroll_pct = None
        for lb in find_all(w.win, "List"):
            try:
                sp = lb.GetScrollPattern()
                h = sp.HorizontalScrollPercent
            except Exception:
                continue
            names = [(t.Name or "") for t in find_all(lb, "Text")]
            if any(n == "全部" for n in names):
                chip_list = lb
                scroll_pct = h
                break
        assert chip_list is not None, "找不到版本筛选条"
        version_chips = []
        for t in find_all(chip_list, "Text"):
            name = (t.Name or "").strip()
            if not name:
                continue
            try:
                r = t.BoundingRectangle
            except Exception:
                continue
            if r.height() == 0 or r.width() == 0:
                continue
            version_chips.append((name, r))
            tops.add(round(r.top))
        print("  可见 chip 数：", len(version_chips), " 不同 top 档数：", len(tops))
        assert len(tops) == 1, f"chip 出现在 {len(tops)} 个不同高度，未单行排列"

        print("[5] 实测横向滚动（点右缘被截断的 chip，选中项应滚入视野）")
        print("  初始横向位置：", scroll_pct, "%")
        box_r = chip_list.BoundingRectangle
        clipped = None
        for n, r in version_chips:
            # 右缘超出筛选条可视区 = 被截断的 chip
            if r.width() > 0 and r.right > box_r.right - 6 and r.left < box_r.right:
                clipped = (n, r)
        if clipped is None:
            print("  没有被截断的 chip（可能整个列表刚好放得下），跳过滚动实测")
            after = scroll_pct
        else:
            n, r = clipped
            print(f"  点击被截断的 chip：{n}")
            auto.Click(min(r.left + r.width() // 2, box_r.right - 4),
                       r.top + r.height() // 2)
            time.sleep(1.2)
            after = chip_list.GetScrollPattern().HorizontalScrollPercent
            print("  选中滚入后：", after, "%")
        assert clipped is None or after != scroll_pct, "横向滚动无效"
        w.shot("96-chip-bar-scrolled.png", wait=0.5)
        time.sleep(1)
        w.shot("96-chip-bar-scrolled.png", wait=1)
        print("\n✓ 通过：筛选条单行横向排列")
    finally:
        w.stop()


if __name__ == "__main__":
    main()
