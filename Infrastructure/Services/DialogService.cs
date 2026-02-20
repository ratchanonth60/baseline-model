using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BaselineMode.WPF.Core.Interfaces.Shared;
using BaselineMode.WPF.Core.Models.Shared;

namespace BaselineMode.WPF.Infrastructure.Services
{
    public class DialogService : IDialogService
    {
        private Window? GetMainWindow()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow;
            }
            return null;
        }

        private IStorageProvider? GetStorageProvider()
        {
            var window = GetMainWindow();
            return window?.StorageProvider; // TopLevel? Window inherits TopLevel.
        }

        public async Task<string[]> OpenFilesAsync(string title, bool multiSelect = false, string filter = "All Files|*.*")
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var provider = GetStorageProvider();
                if (provider == null) return Array.Empty<string>();

                var options = new FilePickerOpenOptions
                {
                    Title = title,
                    AllowMultiple = multiSelect,
                    FileTypeFilter = ParseFilter(filter)
                };

                var files = await provider.OpenFilePickerAsync(options);
                return files.Select(f => f.Path.LocalPath).ToArray();
            });
        }

        public async Task<string?> OpenFileAsync(string title, string filter = "All Files|*.*")
        {
            var files = await OpenFilesAsync(title, false, filter);
            return files.FirstOrDefault();
        }

        public async Task<string?> SaveFileAsync(string title, string? defaultName = null, string filter = "All Files|*.*")
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
           {
               var provider = GetStorageProvider();
               if (provider == null) return null;

               var options = new FilePickerSaveOptions
               {
                   Title = title,
                   SuggestedFileName = defaultName,
                   FileTypeChoices = ParseFilter(filter)
               };

               var file = await provider.SaveFilePickerAsync(options);
               return file?.Path.LocalPath;
           });
        }

        public async Task<string?> OpenFolderAsync(string title)
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var provider = GetStorageProvider();
                if (provider == null) return null;

                var options = new FolderPickerOpenOptions
                {
                    Title = title,
                    AllowMultiple = false
                };

                var folders = await provider.OpenFolderPickerAsync(options);
                var first = folders.FirstOrDefault();
                return first?.Path.LocalPath;
            });
        }

        public async Task<MsgBoxResult> ShowMessageAsync(string message, string title, MsgBoxButton button, MsgBoxImage image)
        {
            // Delegate to existing MessageBoxService logic (which handles ownership)
            // Or reimplement here. MessageBoxService is static helper.
            return await MessageBoxService.ShowAsync(message, title, button, image);
        }

        private List<FilePickerFileType> ParseFilter(string filter)
        {
            var result = new List<FilePickerFileType>();
            if (string.IsNullOrEmpty(filter)) return result;

            // Format: "Text Files (*.txt)|*.txt|All Files (*.*)|*.*" (WPF style)
            // We need to adapt manually or use simple splitting.
            // Avalonia expects FilePickerFileType objects.

            // Simple parser: Split by '|'. Pairs of Name|Pattern.
            var parts = filter.Split('|');
            for (int i = 0; i < parts.Length; i += 2)
            {
                if (i + 1 >= parts.Length) break;
                var name = parts[i];
                var pattern = parts[i + 1]; // e.g. "*.txt;*.csv" or "*.*"

                var extensions = pattern.Split(';')
                    .Select(p => p.Trim().TrimStart('*', '.'))
                    .Where(e => !string.IsNullOrEmpty(e))
                    .ToList();

                if (extensions.Count > 0)
                {
                    result.Add(new FilePickerFileType(name) { Patterns = extensions });
                }
                else
                {
                    result.Add(new FilePickerFileType(name) { Patterns = new[] { "*" } });
                }
            }
            return result;
        }
    }
}
