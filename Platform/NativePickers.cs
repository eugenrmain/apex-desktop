using Microsoft.UI.Xaml;
using Microsoft.Windows.Storage.Pickers;
using System.Runtime.InteropServices;
namespace Apex.Platform;
public sealed class NativePickers(Window window)
{
    public static string DownloadsPath()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        Marshal.ThrowExceptionForHR(SHGetKnownFolderPath(ref id, 0, 0, out var path));
        try
        {
            return Marshal.PtrToStringUni(path)!;
        }
        finally { Marshal.FreeCoTaskMem(path); }
    }
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, nint token, out nint path);
    public async Task<string?> FolderAsync()
    {
        var picker = new FolderPicker(window.AppWindow.Id);
        return (await picker.PickSingleFolderAsync())?.Path;
    }
    public async Task<IReadOnlyList<string>> FilesAsync()
    {
        var picker = new FileOpenPicker(window.AppWindow.Id);
        picker.FileTypeFilter.Add("*");
        var selected = await picker.PickMultipleFilesAsync();
        return selected?.Select(f => f.Path).ToArray() ?? [];
    }
    public async Task<string?> FileAsync(string extension = "*")
    {
        var picker = new FileOpenPicker(window.AppWindow.Id);
        picker.FileTypeFilter.Add(extension);
        return (await picker.PickSingleFileAsync())?.Path;
    }
}
