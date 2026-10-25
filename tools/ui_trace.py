"""带绑定追踪的诊断：点文件行，抓 WPF 绑定错误到 trace.log。"""
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(__file__))

from ui_walk import Walker  # noqa: E402
import uiautomation as auto  # noqa: E402


def main():
    exe = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "EukaryLauncher.App", "bin",
                                       "Debug", "net8.0-windows", "win-x64", "EukaryLauncher.exe"))
    out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "UiShots")
    trace_path = os.path.join(out_dir, "trace.log")

    env = dict(os.environ)
    env["EKL_TRACE_BINDING"] = "1"

    log_file = open(trace_path, "w", encoding="utf-8", errors="replace")
    proc = subprocess.Popen([exe], env=env, stdout=log_file, stderr=subprocess.STDOUT)

    walker = Walker(exe, out_dir)
    walker.proc = proc  # stop() 时杀掉
    time.sleep(6)

    for attempt in range(6):
        try:
            walker.win = auto.WindowControl(Name="真核启动器", searchDepth=1)
            if walker.win.Exists(maxSearchSeconds=2):
                break
        except Exception:
            pass
        time.sleep(2)
    walker.win.SetActive()
    time.sleep(1)

    try:
        print("[1] 下载 → 模组 → 搜索 → 详情")
        walker.click_named("下载", "Text")
        time.sleep(2)
        walker.click_named("模组", "ListItem")
        time.sleep(8)
        edit = walker.find("", "Edit")
        edit.GetValuePattern().SetValue("sodium")
        walker.click_named("搜索", "Button")
        time.sleep(7)
        walker.click_named("Sodium", "Text")
        time.sleep(6)

        print("[2] 点击文件行")
        row = walker.find("Sodium 0.2.0", "Text")
        rect = row.BoundingRectangle
        auto.Click(rect.left + rect.width() // 2, rect.top + rect.height() // 2)
        time.sleep(2)
        print("[3] 完成，trace 写入", trace_path)
    finally:
        walker.stop()
        log_file.close()


if __name__ == "__main__":
    main()
