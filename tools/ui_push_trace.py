"""排障：带 EKL_PUSH_TRACE=1 启动，跑几次页面切换，量化转场期间的 UI 线程卡顿。

应用侧的探针在后台每 4ms 往 UI 线程塞一个空委托并量往返耗时，一次推动结束时
把统计写进 %TEMP%\\ekl-push-trace.txt：

    stall n=80 max=18.7ms@95ms p95=1.8ms over16=1

- max 后面的 @xxms 是卡顿发生在推动开始后多少毫秒（@0 附近 = 起手顿，@中段 = 中间页首帧）
- over16 = 超过一帧预算（16.7ms）的采样数

用法：python tools/ui_push_trace.py [exe 路径]
"""
import os
import sys
import time
import tempfile

sys.path.insert(0, os.path.dirname(__file__))
os.environ["EKL_PUSH_TRACE"] = "1"

from ui_walk import Walker, find_all  # noqa: E402
import uiautomation as auto  # noqa: E402

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "UiShots")
TRACE = os.path.join(tempfile.gettempdir(), "ekl-push-trace.txt")

# 跨页最远的几条路线，卡顿最容易暴露
ROUTES = [
    ("关于", "主页"),   # 跨 4 页向上
    ("下载", "关于"),   # 跨 3 页向下
    ("主页", "下载"),   # 相邻一页
]


def click_nav(w, title):
    for it in find_all(w.win, "ListItem"):
        if f"Title = {title}" in (it.Name or ""):
            r = it.BoundingRectangle
            auto.Click(r.left + r.width() // 2, r.top + r.height() // 2)
            return True
    return False


def main():
    exe = sys.argv[1] if len(sys.argv) > 1 else os.path.abspath(
        os.path.join(os.path.dirname(__file__), "..", "EukaryLauncher.App", "bin",
                     "Debug", "net8.0-windows", "win-x64", "EukaryLauncher.exe"))

    if os.path.exists(TRACE):
        os.remove(TRACE)

    w = Walker(exe, OUT)
    w.start()
    time.sleep(1.0)

    for dst, src in ROUTES:
        if not click_nav(w, src):
            print("找不到侧栏项", src)
            continue
        time.sleep(1.4)
        if not click_nav(w, dst):
            print("找不到侧栏项", dst)
            continue
        time.sleep(1.6)

    w.stop()

    if not os.path.exists(TRACE):
        print("没有 trace 文件")
        return
    with open(TRACE, "r", encoding="utf-8", errors="replace") as f:
        for line in f:
            if "stall" in line:
                print(line.rstrip())


if __name__ == "__main__":
    main()
