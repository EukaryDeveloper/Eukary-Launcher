using System.Windows.Controls;
using System.Windows.Input;

namespace EukaryLauncher.App.Controls;

/// <summary>
/// 主页面用的竖直轨道容器：自身不响应滚轮，但完全不干扰内部页面的滚动。
///
/// 主页面五个页面排在一条竖直轨道上、由代码滚动到当前页。如果滚轮也能滚这条轨道，
/// 用户随手一滚就会把页面滚到半页位置，和侧栏选中项脱节，所以这里吃掉自己的滚轮。
///
/// 关键点：只在「冒泡阶段、且没人处理」时才轮到本类（OverrideMetadata 之外的常规路径），
/// 因此页面内部的 ListBox / ScrollViewer 会先一步处理滚轮并标记 Handled，
/// 根本走不到这里——滚动条、版本列表、日志输出都照常滚。
/// 之前的写法是在 PreviewMouseWheel（隧道，根→叶）里无条件 Handled = true，
/// 等于在页面内部控件之前就把事件掐掉，所有带滚动条的列表一起失灵。
/// </summary>
public class PageScrollViewer : ScrollViewer
{
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        // 故意不调用 base：base 会把自身滚动一屏，轨道就偏出当前页了。
        // 也不设置 e.Handled，让它继续冒泡，外层如需处理仍有机会。
    }
}
