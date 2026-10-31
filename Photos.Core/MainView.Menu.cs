using System;
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

        /// <summary>Right-click menu for a thumbnail in browse mode.</summary>
        void ShowItemMenu(int idx)
        {
            var file = photoProvider.GetFile(idx);
            if (file == null || !file.IsIndexed())
                return;

            mainMenu.Show(m =>
            {
                m.Title = "Image";
                m.Items.Add(new MenuItem() { Text = "Info", Submenu = x => _photoView.BuildImageInfo(x, file) });
                m.Items.Add(new MenuItem()
                {
                    Text = photoProvider.DB.GetAdditionalImageData(file).IsFavorite ? "Remove from favorites" : "Add to favorites",
                    OnClick = () => _photoView.ToggleFavorite(file),
                    ReturnLevel = Menu.CloseMenuLevel,
                });
                AddDeleteEntries(m, file);
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

        void RunFileAction(Func<string> action, string successText)
        {
            var error = action();
            MainToast.Show(error == null ? successText : $"Failed: {error}");
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
