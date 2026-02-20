using System.Threading.Tasks;
using BaselineMode.WPF.Core.Models.Shared;

namespace BaselineMode.WPF.Core.Interfaces.Shared
{
    public interface IDialogService
    {
        Task<string[]> OpenFilesAsync(string title, bool multiSelect = false, string filter = "All Files|*.*");
        Task<string?> OpenFileAsync(string title, string filter = "All Files|*.*");
        Task<string?> SaveFileAsync(string title, string? defaultName = null, string filter = "All Files|*.*");
        Task<string?> OpenFolderAsync(string title);
        Task<MsgBoxResult> ShowMessageAsync(string message, string title, MsgBoxButton button, MsgBoxImage image);
    }
}
