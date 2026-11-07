"""验证主页面切换的上下推动动画。

- 主页 → 下载：轨道上移（原页向上退、下载从下进）
- 设置 → 日志：轨道下移（反向）
- 日志 → 主页：跨两页，逐帧应能看到设置页掠过

侧栏 ListItem 的 Name 是 record 的 ToString，只能按 "Title = xxx" 匹配。
轨道上非当前页仍然参与布局（只是被视口裁掉），所以判断"当前页"必须用
文本矩形是否落在视口（PageHost）范围内，不能只看在不在树里、宽高是否为 0。
"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(__file__))

from ui_walk import Walker, find_all  # noqa: E402
import uiautomation as auto  # noqa: E402
from PIL import ImageGrab  # noqa: E402

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "UiShots")
FRAMES = os.path.join(OUT, "push-frames")

# 各页面独有的特征文本
PAGE_MARKS = {
    "Home": "开始游戏",
    "Download": "原版游戏",
    "Console": "日志输出",
    "Settings": "游戏文件",
    "About": "真核启动器 Eukary Launcher",
}

# 日志页标题就是「日志」，与设置页里「日志、Java...」撞词，单独处理：
# 判定 Console 要求「日志」文本几乎顶到视口顶端（标题位置）
CONSOLE_TITLE_TOP_RATIO = 0.25


def click_nav(w, title):
    for it in find_all(w.win, "ListItem"):
        if f"Title = {title}" in (it.Name or ""):
            r = it.BoundingRectangle
            auto.Click(r.left + r.width() // 2, r.top + r.height() // 2)
            return
    raise RuntimeError(f"找不到侧栏项：{title}")


def host_rect(w):
    r = w.win.BoundingRectangle
    # 标题栏 38 + 侧栏 180
    return (r.left + 182, r.top + 40, r.right - 2, r.bottom - 2)


def visible_texts(w):
    hl, ht, hr, hb = host_rect(w)
    out = {}
    for t in find_all(w.win, "Text"):
        name = (t.Name or "").strip()
        if not name:
            continue
        try:
            r = t.BoundingRectangle
        except Exception:
            continue
        cx, cy = (r.left + r.right) // 2, (r.top + r.bottom) // 2
        if hl < cx < hr and ht < cy < hb and r.width() > 0:
            out.setdefault(name, r.top)
    return out


def current_page(w):
    texts = visible_texts(w)
    hl, ht, hr, hb = host_rect(w)
    host_h = hb - ht
    hits = {p: texts[m] for p, m in PAGE_MARKS.items() if m in texts}
    # 日志页标题「日志」顶到视口上部才算 Console（设置页里也有「日志、Java...」）
    if "日志" in texts and texts["日志"] - ht < host_h * CONSOLE_TITLE_TOP_RATIO:
        hits["Console"] = texts["日志"]
    if not hits:
        return "?", texts
    # 同时出现多个标记时（页面边缘还在），取标记最靠上的那个
    return min(hits, key=hits.get), texts


def capture(w, tag, min_ms=750, max_frames=18):
    os.makedirs(FRAMES, exist_ok=True)
    l, t, r, b = host_rect(w)
    saved = []
    t0 = time.time()
    for i in range(max_frames):
        img = ImageGrab.grab(bbox=(l, t, r, b))
        ms = int((time.time() - t0) * 1000)
        path = os.path.join(FRAMES, f"{tag}-{i:02d}-{ms}ms.png")
        img.save(path)
        saved.append((ms, path))
        if ms > min_ms:
            break
    return saved


def main():
    exe = sys.argv[1] if len(sys.argv) > 1 else os.path.abspath(
        os.path.join(os.path.dirname(__file__), "..", "EukaryLauncher.App", "bin",
                     "Debug", "net8.0-windows", "win-x64", "EukaryLauncher.exe"))

    w = Walker(exe, OUT)
    w.start()
    try:
        w.dismiss_error_dialog()
        time.sleep(1)
        page, _ = current_page(w)
        print("初始页：", page)
        assert page == "Home", f"初始不是主页：{page}"
        w.shot("98-push-home.png", wait=0.3)

        print("[1] 主页 → 下载（向上推）")
        click_nav(w, "下载")
        frames = capture(w, "up")
        print("  抓帧：", [ms for ms, _ in frames])
        time.sleep(1.0)
        page, _ = current_page(w)
        print("  落定：", page)
        assert page == "Download", f"未切到下载页：{page}"

        print("[2] 下载 → 设置（向上推一页）")
        click_nav(w, "设置")
        frames = capture(w, "up2")
        print("  抓帧：", [ms for ms, _ in frames])
        time.sleep(1.0)
        page, _ = current_page(w)
        print("  落定：", page)
        assert page == "Settings", f"未切到设置页：{page}"

        print("[3] 设置 → 日志（向下推）")
        click_nav(w, "日志")
        frames = capture(w, "down")
        print("  抓帧：", [ms for ms, _ in frames])
        time.sleep(1.0)
        page, _ = current_page(w)
        print("  落定：", page)
        assert page == "Console", f"未切到日志页：{page}"

        print("[4] 日志 → 主页（跨两页，掠过设置页）")
        click_nav(w, "主页")
        frames = capture(w, "jump")
        print("  抓帧：", [ms for ms, _ in frames])
        time.sleep(1.0)
        page, _ = current_page(w)
        print("  落定：", page)
        assert page == "Home", f"未切回主页：{page}"

        print("\n✓ 切换全部到位，帧保存在", FRAMES)
    finally:
        w.stop()


if __name__ == "__main__":
    main()
