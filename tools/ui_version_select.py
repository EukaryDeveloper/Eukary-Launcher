"""版本选择页端到端验证：开页动画、文件夹设置、版本切换、导入或导出 toast、返回。"""
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
        print("[1] 主页点版本选择 → 覆盖层打开")
        click_named_any(w.win, "版本选择")
        time.sleep(1)  # 等动画完成
        texts = [t.Name for t in find_all(w.win, "Text")]
        for need in ("版本选择", "文件夹列表", "导入或导出", "文件夹设置",
                     "文件夹名称", "文件夹路径", "版本列表"):
            assert any(need in (t or "") for t in texts), f"缺少：{need}"
        print("  版本选择页元素齐全")
        # 默认文件夹不应有"移除文件夹"栏目（Collapsed 元素仍在 UIA 树，要看可见矩形）
        def visible_texts():
            out = []
            for t in find_all(w.win, "Text"):
                try:
                    if t.IsOffscreen:
                        continue
                    r = t.BoundingRectangle
                    if r.width() <= 0 or r.height() <= 0:
                        continue
                except Exception:
                    continue
                out.append(t.Name or "")
            return out

        texts = visible_texts()
        assert not any(t == "移除文件夹" for t in texts), "默认文件夹不应显示移除栏目"
        print("  默认文件夹正确隐藏移除栏目")
        # 版本列表行数
        cards = [t.Name for t in find_all(w.win, "Text") if t.Name == "切换"]
        print("  版本行数（切换按钮数）：", len(cards))
        assert len(cards) >= 1, "版本列表为空"
        w.shot("103-vs-open.png", wait=0.5)

        print("[2] 改文件夹名 → 应用 → toast")
        for ed in find_all(w.win, "Edit"):
            r = ed.BoundingRectangle
            print("  edit:", (ed.Name or "")[:20], r)
        # 第一个可编辑 Edit 是文件夹名称输入框
        def is_readonly(e):
            try:
                return e.GetValuePattern().IsReadOnly
            except Exception:
                return False

        edits = [e for e in find_all(w.win, "Edit") if not is_readonly(e)]
        assert edits, "找不到可编辑的名称输入框"
        edits[0].Click(); time.sleep(0.3)
        edits[0].SendKeys("{Ctrl}a{Delete}我的游戏")
        time.sleep(0.5)
        click_named_any(w.win, "应用")
        time.sleep(1)
        texts = [t.Name for t in find_all(w.win, "Text")]
        assert "我的游戏" in texts, "侧边栏/标题没有更新为新名称"
        assert any("已应用文件夹名称" in (t or "") for t in texts), "没有成功 toast"
        print("  改名生效 + toast 出现")

        print("[3] 导入或导出 → toast 正在开发")
        click_named_any(w.win, "导入或导出配置")
        time.sleep(0.8)
        texts = [t.Name for t in find_all(w.win, "Text")]
        assert any("正在开发" in (t or "") for t in texts), "没有开发中 toast"
        print("  toast 正常")

        print("[4] 切换版本（选第一个非当前行）")
        # 记录当前版本行（带"当前"标记的行）
        before = _home_version(w)
        click_named_any(w.win, "切换")
        time.sleep(1.2)  # 动画
        texts = [t.Name for t in find_all(w.win, "Text")]
        assert any("启动版本已切换" in (t or "") for t in texts), "没有切换成功 toast"
        after = _home_version(w)
        print(f"  启动版本 {before} → {after}")
        assert after != before or before is None, "启动版本没有变化"
        # 覆盖层应已关闭（回到主页）
        time.sleep(1.5)
        assert not any(t == "文件夹列表" for t in visible_texts()), "覆盖层没有关闭"
        print("  已按原路线返回主页")

        print("[5] 再开页 → 返回按钮回主页")
        click_named_any(w.win, "版本选择")
        time.sleep(1)
        # 返回按钮内容是 Segoe MDL2 图标（Name 为图标字符），按子文本定位
        back_btn = None
        for b in find_all(w.win, "Button"):
            if any((t.Name or "") == "\uE72B" for t in find_all(b, "Text")):
                back_btn = b
                break
        assert back_btn is not None, "找不到返回按钮"
        back_btn.GetInvokePattern().Invoke()
        time.sleep(1.5)
        assert not any(t == "文件夹列表" for t in visible_texts()), "返回按钮没关闭覆盖层"
        print("✓ 全部通过")
        w.shot("103-final-home.png", wait=0.5)
    finally:
        w.stop()


def _home_version(w):
    """主页上开始游戏旁的可启动状态没有版本显示，改从设置文件读。"""
    import json
    p = os.path.join(os.environ["LOCALAPPDATA"], "EukaryLauncher", "settings.json")
    try:
        return json.load(open(p, encoding="utf-8")).get("LastVersionId")
    except Exception:
        return None


if __name__ == "__main__":
    main()
