using System;
using SkiaSharp;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Photos.Core.StoredTypes;
using Photos.Lib;

namespace Photos.Core
{
    public enum MenuSection { Main, PhotoLibrary, Settings }
    public partial class MainView
    {

        public event Action<MenuSection, MenuDef> OnBuildPlatformSpecifiMenu;

        private void MainMenuFunc(MenuDef menu)
        {
            menu.Title = "Main menu";

            if (photoProvider.IsSingleFolderMode)
                menu.Items.Add(new() { Text = "Return to gallery", ReturnLevel = menu.Parent, OnClick = photoProvider.CloseSingleFolderMode });
            else if (photoProvider.FolderLibraryMode)
                menu.Items.Add(new() { Text = "Open folder", Submenu = Utils.CreateFileMenu(photoProvider.OpenFile, returnLevel: menu.Parent) });

            if (photoProvider.DispalyGroups.Count > 0)
            {
                menu.Items.Add(new() { Text = "Display options" });
                menu.Items.Add(new()
                {
                    Text = $"Sort photos by: {photoProvider.FilterService.CurrentSortMode switch { SortModeEnum.Date => "date", SortModeEnum.Filename => "file name", _ => "???" }}",
                    OnClick = photoProvider.FilterService.ToggleSortOrder
                });
                menu.Items.Add(new() { Text = "Thumbnail display size", Submenu = ShowThumbnailDisplaySizeMenu });
            }

            if (photoProvider.HasDeletedImages || photoProvider.ShowDeleted)
                menu.Items.Add(new()
                {
                    Text = photoProvider.ShowDeleted ? "Back to library" : "Deleted images",
                    OnClick = () => photoProvider.SetShowDeleted(!photoProvider.ShowDeleted),
                });

            if (photoProvider.HasPrivateFolders)
                menu.Items.Add(new()
                {
                    Text = photoProvider.PrivateUnlocked ? "Lock private folders" : "Unlock private folders",
                    OnClick = ToggleLockPrivate,
                });

            menu.Items.Add(new() { Text = "Settings" });
            menu.Items.Add(new() { Text = "Photo library", Submenu = PhotoLibMenu });
            OnBuildPlatformSpecifiMenu?.Invoke(MenuSection.Main, menu);
            menu.Items.Add(new() { Text = "Settings", Submenu = SettingsMenu });

             if (photoProvider.DB.Settings.DevMode)
                menu.Items.Add(new()
                {
                    Text = $"Debug",
                    Submenu = ShowDebugMenu
                });
        }

        public void SettingsMenu(MenuDef menu)
        {

            if (!photoProvider.IsIndexEmpty)
                menu.Items.Add(new()
                {
                    Text = "Clear thumbnail index",
                    Submenu = m => Menu.Confirm(m, "Delete all thumbnails!", () => photoProvider.ClearIndex(),
                    new[] { "Use this option to refresh existing thumbnails", "All photos will be reindexed", "It can take some time!" })
                });

            OnBuildPlatformSpecifiMenu?.Invoke(MenuSection.Settings, menu);

            menu.Items.Add(new()
            {
                Text = $"Debug mode: {(photoProvider.DB.Settings.DevMode ? "ON" : "OFF")}",
                OnClick = () => photoProvider.DB.Settings = photoProvider.DB.Settings with { DevMode = !photoProvider.DB.Settings.DevMode }
            });
        }

        void ToggleLockPrivate()
        {
            if (photoProvider.PrivateUnlocked)
            {
                photoProvider.LockPrivate();
                return;
            }
            var password = NativeDialogs.PasswordPrompt?.Invoke("Enter password for private folders");
            if (password == null)
                return; // cancelled, or no password prompt on this platform
            if (!photoProvider.TryUnlockPrivate(password))
                MainToast.Show("Wrong password");
        }

        /// <summary>Right-click menu for thumbnails in browse mode. It works on the selection when the photo is selected.</summary>
        void ShowItemMenu(int idx)
        {
            var clicked = photoProvider.GetFile(idx);
            if (clicked == null || !clicked.IsIndexed())
                return;

            var indexes = ThumbnailView.SelectedItems.Contains(idx) ? ThumbnailView.SelectedItems.OrderBy(x => x).ToList() : new List<int> { idx };
            var files = indexes.Select(i => photoProvider.GetFile(i)).Where(f => f != null && f.IsIndexed()).ToList();
            var items = new List<MenuItem>();

            if (files.Count == 1)
            {
                var file = files[0];
                items.Add(new MenuItem() { Text = "Info", Submenu = x => _photoView.BuildImageInfo(x, file) });


                if (OperatingSystem.IsWindows())
                    foreach (var src in file.Src)
                    {
                        var dir = photoProvider.DB.Directories.GetValue(src.D);
                        var path = src.GetFullPath(photoProvider);
                        if (!dir.IsLocal() || !File.Exists(path.osPath))
                            continue;
                        var osPath = path.osPath;
                        items.Add(new MenuItem()
                        {
                            Text = $"Show in Explorer: {path.display}",
                            OnClick = () => Process.Start("explorer.exe", "/select, \"" + osPath + "\""),
                            ReturnLevel = Menu.CloseMenuLevel,
                        });
                    }
            }

            bool allFavorite = files.All(f => photoProvider.DB.GetAdditionalImageData(f).IsFavorite);
            string favoriteText = files.Count == 1
                ? (allFavorite ? "Remove from favorites" : "Add to favorites")
                : (allFavorite ? $"Remove {files.Count} from favorites" : $"Add {files.Count} to favorites");
            items.Add(new MenuItem()
            {
                Text = favoriteText,
                OnClick = () => { foreach (var f in files) SetFavorite(f, !allFavorite); },
                ReturnLevel = Menu.CloseMenuLevel,
            });

            AddDeleteItems(items, files);
            ShowItemMenuAt(items, files.Count);
        }

        void SetFavorite(FileRecord file, bool isFavorite)
        {
            var dat = photoProvider.DB.GetAdditionalImageData(file);
            dat.IsFavorite = isFavorite;
            photoProvider.DB.SetAdditionalImageData(file, dat);
        }

        /// <summary>Delete, or restore in the deleted view. Every copy is included, and no confirmation is asked.</summary>
        void AddDeleteItems(List<MenuItem> items, List<FileRecord> files)
        {
            var restoring = photoProvider.ShowDeleted;
            int copies = files.Sum(f => photoProvider.ActionableSources(f).Count());
            if (copies == 0)
                return;

            string verb = restoring ? "Restore" : "Delete";
            string text = files.Count == 1
                ? (copies > 1 ? $"{verb} all copies" : verb)
                : $"{verb} {files.Count} images";
            items.Add(new MenuItem()
            {
                Text = text,
                OnClick = () => RunOnCopies(files, restoring),
                ReturnLevel = Menu.CloseMenuLevel,
            });

            if (files.Count == 1 && copies > 1)
            {
                var single = files[0];
                var copyEntries = photoProvider.ActionableSources(single).Select(i => new MenuItem()
                {
                    Text = $"{verb} {single.Src[i].GetFullPath(photoProvider).display}",
                    OnClick = () => RunFileAction(
                        () => restoring ? photoProvider.RestoreFromDeleted(single, i) : photoProvider.MoveToDeleted(single, i),
                        restoring ? "Restored" : "Moved to deleted images"),
                    ReturnLevel = Menu.CloseMenuLevel,
                }).ToList();
                items.Add(new MenuItem() { Text = $"{verb} a copy", Submenu = s => s.Items.AddRange(copyEntries) });
            }
        }

        void RunOnCopies(List<FileRecord> files, bool restoring)
        {
            int done = 0, failed = 0;
            string lastError = null;
            foreach (var file in files)
                foreach (var index in photoProvider.ActionableSources(file).ToList())
                {
                    var error = restoring
                        ? photoProvider.RestoreFromDeleted(file, index, refresh: false)
                        : photoProvider.MoveToDeleted(file, index, refresh: false);
                    if (error == null)
                        done++;
                    else
                    {
                        failed++;
                        lastError = error;
                    }
                }

            photoProvider.EnqueueRefreshDisplayOrder();
            string success = restoring ? "Restored" : "Moved to deleted images";
            MainToast.Show(failed == 0 ? $"{success}: {done}" : $"{done} done, {failed} failed: {lastError}");
        }

        void RunFileAction(Func<string> action, string successText)
        {
            var error = action();
            MainToast.Show(error == null ? successText : $"Failed: {error}");
        }

        /// <summary>The browse menu's rectangle: next to the pointer, sized to the items of the level being shown.</summary>
        SKRect ItemMenuRect()
        {
            const float width = 300, padding = 10;
            float row = _itemMenu.ButtonSize.Value + 6; // one row, with the spacing the menu uses
            int rows = (_itemMenu.ActiveMenu?.Items?.Count ?? 1) + 1; // plus the title row
            float height = Math.Min(rows * row + padding * 2, Size.Height - 8);
            float x = Math.Clamp(_itemMenuAnchor.X + 8, 4, Size.Width - width - 4);
            float y = Math.Clamp(_itemMenuAnchor.Y + 8, 4, Size.Height - height - 4);
            return SKRect.Create(x, y, width, height);
        }

        /// <summary>Opens the browse menu next to the pointer.</summary>
        void ShowItemMenuAt(List<MenuItem> items, int count)
        {
            _itemMenuAnchor = Window.MousePosition() ?? new SKPoint(Size.Width / 2, Size.Height / 2);
            _itemMenu.Show(m =>
            {
                m.Title = count == 1 ? "Image" : $"{count} images selected";
                m.Items.AddRange(items);
            });
        }

        /// <summary>Delete, or restore in the deleted view. No confirmation is asked here.</summary>
        void AddDeleteEntries(MenuDef m, FileRecord file)
        {
            var restoring = photoProvider.ShowDeleted;
            var entries = new List<MenuItem>();
            for (int i = 0; i < file.Src.Length; i++)
            {
                var index = i;
                var dir = photoProvider.DB.Directories.GetValue(file.Src[i].D);
                var fullPath = file.Src[i].GetFullPath(photoProvider);
                if (!dir.IsLocal() || !File.Exists(fullPath.osPath))
                    continue;

                entries.Add(new MenuItem()
                {
                    Text = $"{(restoring ? "Restore" : "Delete")} {fullPath.display}",
                    OnClick = () => RunFileAction(
                        () => restoring ? photoProvider.RestoreFromDeleted(file, index) : photoProvider.MoveToDeleted(file, index),
                        restoring ? "Restored" : "Moved to deleted images"),
                    ReturnLevel = Menu.CloseMenuLevel,
                });
            }

            if (entries.Count == 1)
                m.Items.Add(entries[0]);
            else if (entries.Count > 1)
                m.Items.Add(new MenuItem()
                {
                    Text = restoring ? "Restore a copy" : "Delete a copy",
                    Submenu = s => s.Items.AddRange(entries),
                });
        }

        void ShowDebugMenu(MenuDef menu)
        {
            var debug = new List<MenuItem>
            {
                new() { Text = $"renderSize {Window.MaxRenderTargetSize} px" },
                new() { Text = $"cacheLimit {Window.GR?.GetResourceCacheLimit() >> 20} mb" },
                new()
                {
                    Text = "Open log",
                    OnClick = () =>
                    {
                        if (File.Exists(Utils.LogFile))
                        {
                            var err = Window.OpenUrl("file://" + Utils.LogFile);
                            if (err != null)
                                MainToast.Show(err, 10);
                        }
                    }
                },
                new()
                {
                    Text = "Clear log",
                    OnClick = () =>
                    {
                        if (File.Exists(Utils.LogFile))
                            File.Delete(Utils.LogFile);
                    }
                },
                new () { Text = "Throw", OnClick = () => throw new Exception("test") },
                new () { Text = "mbox", OnClick = () => _mbox.Show(new [] { "test", "test test asdfa asdf asdf test asdfa asdf asdf test asdfa asdf asdf test asdfa", "test asdfa asdf asdf" }, new MessageBoxButton() { Text= "close" }, new MessageBoxButton() { Text= "close" } ) },
                new () { Text = "Toast", OnClick = () => MainToast.Show("test test test!!!") },
                new () { Text = "Grid", OnClick = () => Window.ShowGrid = !Window.ShowGrid },
            };

            foreach (var item in debug)
                item.TextAlign = SkiaSharp.SKTextAlign.Left;

            menu.Items.AddRange(debug);
        }

        private void ShowThumbnailDisplaySizeMenu(MenuDef menu)
        {
            menu.Items.AddRange(Enumerable.Range(0, ThumbnailSizes.Sizes.Length).Select(i => new MenuItem()
            {
                Text = $"{ThumbnailSizes.Names[i]} ({ThumbnailSizes.Sizes[i]}px)",
                OnClick = () => photoProvider.DB.Settings = photoProvider.DB.Settings with { ThumbnailDrawSize = ThumbnailSizes.Sizes[i] },
                Selected = () => ThumbnailSizes.SlotFor(photoProvider.DB.Settings.ThumbnailDrawSize) == i,
            }));
        }

        void ShowFilterMenu(FileFilterGroup[] filterGroups)
        {
            mainMenu.Show(menu =>
            {
                menu.Title = "Filter";

                photoProvider.FilterService.ActiveFiltersMenu(menu);

                menu.Items.AddRange(filterGroups.Select(x => photoProvider.FilterService.ToMenuItem(x)));
            });
        }

        void PhotoLibMenu(MenuDef menu)
        {
            if (photoProvider.FolderLibraryMode)
            {
                menu.Items.Add(new() { Text = "Add a new folder", Submenu = Utils.CreateFileMenu(photoProvider.AddDir) });
                menu.Items.AddRange(photoProvider.GetDirs().Select(x => new MenuItem()
                {
                    Text = $"Remove folder: {x}",
                    Submenu = m => Menu.Confirm(m, $"Remove {x}", () => photoProvider.RemoveDir(x))
                }));
            }

            OnBuildPlatformSpecifiMenu?.Invoke(MenuSection.PhotoLibrary, menu);

            menu.Items.Add(new());

            menu.Items.Add(new()
            {
                Text = "Check for new photos",
                ReturnLevel = Menu.CloseMenuLevel,
                OnClick = () => { photoProvider.UpdateLibraryMaybe(new IndexTaskConfig() { DoneMessage = "library refresh completed" }); }
            });

            menu.Items.Add(new()
            {
                Text = "Rescan EXIF data",
                Submenu = m => Menu.Confirm(m, $"Start scan", () => { photoProvider.UpdateLibraryMaybe(new() { ForceMetaRefresh = true, DoneMessage = "scan completed" }); },
                    new[] { "Refresh metadata for all photos?", "It could take some time" }, Menu.CloseMenuLevel)
            });
        }

    }
}
