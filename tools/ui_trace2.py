"""精确诊断：找屏幕上真正可见的文件行，分别用 坐标点击 / InvokePattern 触发。"""
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(__file__))

from ui_walk import Walker  # noqa: E402
import uiautomation as auto  # noqa: E402


def all_texts(walker, name):
    """收集所有指定名字的 TextControl 及其可见性。"""
    out = []

    def walk(ctrl, depth=0):
        if depth > 30:
            return
        try:
            for child in ctrl.GetChildren():
                try:
                    if child.ControlTypeName == "TextControl" and child.Name == name:
                        out.append(child)
                except Exception:
                    pass
                walk(child, depth + 1)
        except Exception:
            pass

    walk(walker.win)
    return out


def visible_texts(walker, name):
    result = []
    for t in all_texts(walker, name):
        try:
            rect = t.BoundingRectangle
            off = t.IsOffscreen
        except Exception:
            continue
        if rect and rect.width() > 0 and not off:
            result.append((t, rect))
    return result


def find_button_ancestor(ctrl, max_depth=8):
    cur = ctrl
    for _ in range(max_depth):
        cur = cur.GetParentControl()
        if cur is None:
            return None
        if cur.ControlTypeName == "ButtonControl":
            return cur
    return None


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
    walker.proc = proc
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
        print("[1] 进入 Sodium 详情页")
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

        print("[2] 枚举所有可见的 Sodium 0.2.0 文本")
        vis = visible_texts(walker, "Sodium 0.2.0")
        for t, rect in vis:
            print("   可见行：", rect)
        if not vis:
            raise RuntimeError("没有可见的 Sodium 0.2.0")

        text, rect = vis[0]
        cx, cy = rect.left + rect.width() // 2, rect.top + rect.height() // 2

        print("[3] 坐标点击 →", (cx, cy))
        auto.Click(cx, cy)

        print("[3b] 点击后 0.3s/1s/2s 用 UIA 找 Toast 文本")
        for delay in (0.3, 1.0, 2.0):
            time.sleep(delay)
            try:
                t = walker.win.TextControl(SubName="尚未安装", searchDepth=0xFFFFFFFF)
                found = t.Exists(maxSearchSeconds=0.5)
                print(f"   +{delay}s 找到 Toast 文本：{found}",
                      (t.Name, t.BoundingRectangle) if found else "")
            except Exception as exc:
                print(f"   +{delay}s 查找异常：", exc)

        walker.shot("30-click-coord.png", wait=0.5)

        print("[4] 找按钮祖先 + InvokePattern")
        btn = find_button_ancestor(text)
        if btn is None:
            print("   没找到按钮祖先！")
        else:
            print("   按钮 rect：", btn.BoundingRectangle)
            try:
                inv = btn.GetPattern(auto.InvokePattern)
                inv.Invoke()
                print("   Invoke() 已调用")
            except Exception as exc:
                print("   InvokePattern 失败：", exc)
        time.sleep(1.5)
        walker.shot("31-click-invoke.png", wait=0.5)
        walker.dismiss_error_dialog()
        print("[5] 完成")
    finally:
        walker.stop()
        log_file.close()


if __name__ == "__main__":
    main()
