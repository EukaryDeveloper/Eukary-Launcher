"""启动器 UI 冒烟测试：启动 exe → 等窗口出现 → 用 ctypes 抓窗口矩形 → Pillow 截图。

用法（需先 pip install pillow）：
    python tools/ui_smoke.py

可选参数：
    python tools/ui_smoke.py <exe 路径> <输出 png 路径> <等待秒数>
"""
import ctypes
import os
import subprocess
import sys
import time

try:
    from PIL import ImageGrab
except ImportError:
    print("需要 Pillow：pip install pillow")
    sys.exit(2)

user32 = ctypes.windll.user32


class RECT(ctypes.Structure):
    _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long),
                ("right", ctypes.c_long), ("bottom", ctypes.c_long)]


def main():
    default_exe = os.path.join(os.path.dirname(__file__), "..", "EukaryLauncher.App",
                               "bin", "Debug", "net8.0-windows", "win-x64", "EukaryLauncher.exe")
    exe = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else default_exe)
    out_path = os.path.abspath(sys.argv[2] if len(sys.argv) > 2
                               else os.path.join(os.path.dirname(__file__), "..", "ui-smoke.png"))
    wait = float(sys.argv[3]) if len(sys.argv) > 3 else 12.0

    if not os.path.exists(exe):
        print(f"找不到程序：{exe}\n请先 dotnet build EukaryLauncher.App")
        return 1

    proc = subprocess.Popen([exe])
    time.sleep(wait)

    # 按 PID 找主窗口
    found = 0

    def cb(h, _):
        nonlocal found
        pid = ctypes.c_ulong()
        user32.GetWindowThreadProcessId(h, ctypes.byref(pid))
        if pid.value == proc.pid and user32.IsWindowVisible(h):
            found = h
        return True

    WNDENUMPROC = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)
    user32.EnumWindows(WNDENUMPROC(cb), 0)

    if not found:
        print("FAIL: 没找到主窗口")
        proc.kill()
        return 1

    title = ctypes.create_unicode_buffer(256)
    user32.GetWindowTextW(found, title, 256)

    user32.ShowWindow(found, 9)          # SW_RESTORE：启动时窗口可能是最小化态
    time.sleep(0.8)
    user32.SetForegroundWindow(found)
    time.sleep(1.2)

    rect = RECT()
    user32.GetWindowRect(found, ctypes.byref(rect))

    img = ImageGrab.grab(bbox=(rect.left, rect.top, rect.right, rect.bottom))
    out = out_path
    img.save(out)

    print(f"窗口标题: {title.value}")
    print(f"窗口矩形: {rect.left},{rect.top} → {rect.right},{rect.bottom}")
    print(f"截图已保存: {out}")

    proc.kill()
    return 0


if __name__ == "__main__":
    sys.exit(main())
