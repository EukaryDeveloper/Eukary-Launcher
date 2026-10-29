"""启动器 UI 走查：真实点击各页面并截图，用于验证界面交互是否正常。

用法：
    python tools/ui_walk.py [exe 路径] [输出目录]

依赖：pip install pillow uiautomation
"""
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

import uiautomation as auto  # noqa: E402
from PIL import ImageGrab  # noqa: E402

auto.uiautomation.SetGlobalSearchTimeout(3)


class Walker:
    def __init__(self, exe: str, out_dir: str):
        self.exe = exe
        self.out_dir = out_dir
        os.makedirs(out_dir, exist_ok=True)
        self.proc: subprocess.Popen | None = None
        self.win = None

    def start(self):
        self.proc = subprocess.Popen([self.exe])
        time.sleep(6)

        for attempt in range(6):
            try:
                self.win = auto.WindowControl(Name="真核启动器", searchDepth=1)
                if self.win.Exists(maxSearchSeconds=2):
                    break
            except Exception:
                pass
            time.sleep(2)
        else:
            raise RuntimeError("窗口没有出现")

        self.win.SetActive()
        time.sleep(1)

    def dismiss_error_dialog(self):
        """异常兜底弹窗（真核启动器出错了）出现时点掉它，避免挡住后续操作。"""
        try:
            dlg = auto.WindowControl(Name="真核启动器出错了", searchDepth=1)
            if dlg.Exists(maxSearchSeconds=1):
                btn = dlg.ButtonControl(Name="确定")
                if btn.Exists(maxSearchSeconds=1):
                    btn.Click()
                time.sleep(0.6)
                return True
        except Exception:
            pass
        return False

    def find(self, name: str, kind: str = "Text", timeout: float = 12):
        end = time.time() + timeout
        while time.time() < end:
            try:
                if kind == "Text":
                    ctrl = self.win.TextControl(Name=name, searchDepth=0xFFFFFFFF)
                elif kind == "ListItem":
                    ctrl = self.win.ListItemControl(Name=name, searchDepth=0xFFFFFFFF)
                elif kind == "Button":
                    ctrl = self.win.ButtonControl(Name=name, searchDepth=0xFFFFFFFF)
                elif kind == "Edit":
                    ctrl = self.win.EditControl(Name=name, searchDepth=0xFFFFFFFF)
                else:
                    raise ValueError(kind)

                if ctrl.Exists(maxSearchSeconds=1):
                    return ctrl
            except Exception:
                pass
            time.sleep(0.4)
        raise RuntimeError(f"找不到控件：{kind} / {name}")

    @staticmethod
    def click(ctrl):
        # TextBlock 本身不可点，点它的宿主（ListBoxItem / Button）
        rect = ctrl.BoundingRectangle
        if rect.width() <= 0:
            ctrl = ctrl.GetParentControl()
        try:
            ctrl.Click()
        except Exception:
            ctrl.Click(x=10, y=10)
        time.sleep(0.9)

    def click_named(self, name, kind="Text"):
        ctrl = self.find(name, kind)
        self.click(ctrl)
        return ctrl

    def shot(self, name: str, wait: float = 0.8):
        time.sleep(wait)
        rect = self.win.BoundingRectangle
        box = (rect.left, rect.top, rect.right, rect.bottom)
        path = os.path.join(self.out_dir, name)
        ImageGrab.grab(bbox=box).save(path)
        print("  截图 →", path)
        return path

    def stop(self):
        if self.proc:
            try:
                self.proc.kill()
            except Exception:
                pass


CONTROL_TYPES = {
    "Edit": "EditControl", "Button": "ButtonControl", "Text": "TextControl",
    "ListItem": "ListItemControl", "CheckBox": "CheckBoxControl",
    "List": "ListControl", "Combo": "ComboBoxControl",
}


def find_all(root, kind):
    """这版的 uiautomation 没有 Control.FindAll，只能自己递归 UIA 树按类型过滤。

    注意：Collapsed 的元素也还在树里，判断可见性别只看在不在。
    """
    wanted = CONTROL_TYPES[kind]
    out = []

    def walk(ctrl, depth):
        if depth > 40:
            return
        try:
            children = ctrl.GetChildren()
        except Exception:
            return
        for child in children:
            try:
                if child.ControlTypeName == wanted:
                    out.append(child)
            except Exception:
                pass
            walk(child, depth + 1)

    walk(root, 0)
    return out


def find_all(win, kind):
    """这版的 uiautomation 没有 Control.FindAll，只能自己递归 UIA 树按类型名过滤。

    注意：Collapsed 的元素也仍然在树里，判断可见性不能只看在不在。
    """
    wanted = CONTROL_TYPES[kind]
    out = []

    def walk(ctrl, depth):
        if depth > 40:
            return
        try:
            children = ctrl.GetChildren()
        except Exception:
            return
        for child in children:
            try:
                if child.ControlTypeName == wanted:
                    out.append(child)
            except Exception:
                pass
            walk(child, depth + 1)

    walk(win, 0)
    return out


def main():
    exe = sys.argv[1] if len(sys.argv) > 1 else os.path.abspath(
        os.path.join(os.path.dirname(__file__), "..", "EukaryLauncher.App", "bin",
                     "Debug", "net8.0-windows", "win-x64", "EukaryLauncher.exe"))
    out_dir = sys.argv[2] if len(sys.argv) > 2 else os.path.abspath(
        os.path.join(os.path.dirname(__file__), "..", "tools", "UiShots"))

    walker = Walker(exe, out_dir)
    walker.start()

    try:
        walker.shot("01-home.png")

        print("[1] 打开下载页")
        walker.click_named("下载", "Text")
        walker.shot("02-download-vanilla.png", wait=3)

        print("[2] 切到模组 Tab")
        walker.click_named("模组", "ListItem")
        walker.shot("03-mods-empty.png", wait=2)

        print("[3] 搜索 sodium")
        try:
            edit = walker.find("", "Edit")
            edit.GetValuePattern().SetValue("sodium")
            time.sleep(0.5)
            walker.click_named("搜索", "Button")
            walker.dismiss_error_dialog()
            walker.shot("04-mods-results.png", wait=8)
        except Exception as exc:
            print("  搜索失败：", exc)

        print("[4] 打开模组版本页")
        try:
            walker.click_named("Sodium", "Text")
            walker.shot("05-mod-detail.png", wait=6)
            walker.click_named("← 返回", "Button")
            time.sleep(1)
        except Exception as exc:
            print("  打开详情失败：", exc)

        print("[5] 占位 Tab")
        walker.click_named("整合包", "ListItem")
        walker.shot("06-placeholder.png", wait=1.5)

        print("[6] 原版游戏 → 继续 → 加载器页")
        walker.click_named("原版游戏", "ListItem")
        walker.dismiss_error_dialog()
        time.sleep(1.5)
        walker.dismiss_error_dialog()
        walker.click_named("继续", "Button")
        walker.shot("07-loader-select.png", wait=6)

        print("[7] 开始下载（真实入队）")
        walker.click_named("开始下载", "Button")
        time.sleep(2)
        walker.click_named("下载队列", "ListItem")
        walker.shot("08-queue-running.png", wait=6)
        walker.shot("09-queue-later.png", wait=45)

    finally:
        walker.stop()


if __name__ == "__main__":
    main()
