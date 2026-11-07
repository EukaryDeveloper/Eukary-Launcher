"""验证主页面滚轮行为：内部列表照常滚，竖直轨道不被滚走。

UIA 里能拿到的可滚容器（本机实测）：
  * 轨道本身：一个 Pane，矩形≈整个页面视口，ViewSize≈20%（5 页），当前页 = vp/20
  * 页面内部：ScrollViewer → Pane；ListBox → List，ViewSize 都比轨道大
判定不看截图（本机 GDI 抓帧不可靠），只比 ScrollPattern 的 VerticalScrollPercent。
"""
import os
import sys
import time

sys.path.insert(0, os.path.join(os.getcwd(), "tools"))

import uiautomation as auto
from ui_walk import Walker, find_all

EXE = os.path.join(os.getcwd(), "EukaryLauncher.App", "bin", "Debug",
                   "net8.0-windows", "win-x64", "EukaryLauncher.exe")

PAGES = ["下载", "日志", "设置", "关于"]


def click_nav(w, title):
    for it in find_all(w.win, "ListItem"):
        if f"Title = {title}" in (it.Name or ""):
            r = it.BoundingRectangle
            auto.Click(r.left + r.width() // 2, r.top + r.height() // 2)
            return True
    return False


def containers(w):
    """所有可纵向滚的容器：[(kind, (l,t,r,b), percent, viewsize)]"""
    out = []
    for kind in ("Pane", "List"):
        for c in find_all(w.win, kind):
            r = c.BoundingRectangle
            if r.width() <= 0 or r.height() <= 0:
                continue
            try:
                sp = c.GetScrollPattern()
            except Exception:
                continue
            if sp and sp.VerticallyScrollable:
                out.append((kind, (r.left, r.top, r.right, r.bottom),
                            sp.VerticalScrollPercent, sp.VerticalViewSize))
    return out


def rail_index(win, boxes):
    """轨道 = 页面视口矩形 + ViewSize 最小（5 页 → 约 20%）。"""
    page_top = win.top + 39
    cands = [i for i, (_, rect, _, vsz) in enumerate(boxes)
             if abs(rect[1] - page_top) <= 6 and abs(rect[3] - win.bottom) <= 6]
    if not cands:
        return None
    return min(cands, key=lambda i: boxes[i][3])


def run(w):
    ok = True
    win = w.win.BoundingRectangle
    cx = win.left + 182 + (win.width() - 182) // 2
    cy = win.top + 40 + (win.height() - 40) // 2

    for page in PAGES:
        click_nav(w, page)
        time.sleep(1.1)

        boxes = containers(w)
        rail = rail_index(win, boxes)
        # 只认「完全落在轨道视口内」的容器：离屏的下几页容器顶部会露出 1px，不能算
        inner = []
        if rail is not None:
            rt, rb = boxes[rail][1][1], boxes[rail][1][3]
            inner = [b for i, b in enumerate(boxes)
                     if i != rail and b[1][1] >= rt - 4 and b[1][3] <= rb + 4]
        if not inner:
            print(f"[{page}] 页面内容不足一屏（无可滚列表），跳过")
            continue

        rail_before = boxes[rail][2] if rail is not None else None

        auto.MoveTo(cx, cy)
        time.sleep(0.3)
        for _ in range(3):
            auto.WheelDown()
        time.sleep(0.9)

        after_boxes = containers(w)
        rail_after = next((b[2] for b in after_boxes
                           if rail is not None and b[1] == boxes[rail][1]
                           and abs(b[3] - boxes[rail][3]) < 0.5), None)

        moved = 0
        for kind, rect, pct, vsz in inner:
            now = next((b[2] for b in after_boxes
                        if b[1] == rect and abs(b[3] - vsz) < 0.5), None)
            if now is not None and now > pct + 0.5:
                moved += 1

        rail_ok = rail_before == rail_after
        print(f"[{page}] 内部容器 {len(inner)} 个，滚动生效 {moved} 个 "
              f"| 轨道 {rail_before}% -> {rail_after}% {'✓ 未动' if rail_ok else '✗ 被滚走'}")
        if not moved:
            print(f"  ✗ {page}: 滚轮没带动内部列表")
            ok = False
        if not rail_ok:
            ok = False
    return ok


if __name__ == "__main__":
    w = Walker(EXE, os.path.join(os.getcwd(), "tools", "UiShots"))
    w.start()
    try:
        w.dismiss_error_dialog()
        time.sleep(1.8)
        ok = run(w)
        print("结果:", "✓ 通过" if ok else "✗ 失败")
    finally:
        w.stop()
