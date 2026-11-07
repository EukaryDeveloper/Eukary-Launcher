using CommunityToolkit.Mvvm.ComponentModel;
using EukaryLauncher.Core.Services;

namespace EukaryLauncher.App.Services;

/// <summary>版本选择页「文件夹列表」的一个条目（默认文件夹 + 额外文件夹）。</summary>
public sealed partial class GameFolderItem : ObservableObject
{
    public GameFolderItem(string id, string path, bool isDefault, string displayName)
    {
        _id = id;
        _path = path;
        _isDefault = isDefault;
        _displayName = displayName;
    }

    [ObservableProperty]
    private string _id = "";

    [ObservableProperty]
    private string _path = "";

    [ObservableProperty]
    private bool _isDefault;

    /// <summary>个性化显示名称，仅存在于启动器设置里，不是磁盘上的文件夹名。</summary>
    [ObservableProperty]
    private string _displayName = "";
}

/// <summary>
/// 游戏文件夹清单：默认文件夹恒在（路径来自设置的游戏目录），
/// 额外文件夹从设置持久化。改名只改显示名称，不动磁盘。
/// </summary>
public static class GameFolderStore
{
    public static List<GameFolderItem> GetFolders(SettingsService settings)
    {
        var list = new List<GameFolderItem>
        {
            new("default", settings.Current.GameDirectory, true, settings.Current.DefaultFolderName),
        };
        foreach (var f in settings.Current.ExtraFolders)
        {
            list.Add(new GameFolderItem(f.Id, f.Path, false,
                string.IsNullOrWhiteSpace(f.DisplayName) ? Path.GetFileName(f.Path) : f.DisplayName));
        }
        return list;
    }

    public static void Rename(SettingsService settings, GameFolderItem folder, string displayName)
    {
        if (folder.IsDefault)
            settings.Current.DefaultFolderName = displayName;
        else
        {
            var entry = settings.Current.ExtraFolders.FirstOrDefault(f => f.Id == folder.Id);
            if (entry is null) return;
            entry.DisplayName = displayName;
        }
        settings.Save();
        folder.DisplayName = displayName;
    }

    /// <summary>从列表移除一个额外文件夹（不动磁盘）。默认文件夹不可移除。</summary>
    public static bool Remove(SettingsService settings, string folderId)
    {
        var removed = settings.Current.ExtraFolders.RemoveAll(f => f.Id == folderId) > 0;
        if (removed) settings.Save();
        return removed;
    }

    /// <summary>找到额外文件夹的磁盘路径（默认文件夹返回 null）。</summary>
    public static string? ExtraFolderPath(SettingsService settings, string folderId) =>
        settings.Current.ExtraFolders.FirstOrDefault(f => f.Id == folderId)?.Path;
}
