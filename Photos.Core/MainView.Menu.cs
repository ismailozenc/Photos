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

            menu.Items.Add(new() { Text = "Settings" });
            menu.Items.Add(new() { Text = "Photo library", Submenu = PhotoLibMenu });
            OnBuildPlatformSpecifiMenu?.Invoke(MenuSection.Main, menu);
            menu.Items.Add(new() { Text = "Settings", Submenu = SettingsMenu });

            menu.Items.Add(new() { Text = "About" });
            menu.Items.Add(new()
            {
                Text = $"About {Utils.AppName}",
                Submenu = ShowAbout
            });

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
                Text = $"Automated error reports: {(photoProvider.DB.Settings.ErrorOptOut ? "disabled" : "enabled")}",
                OnClick = () =>
                {
                    photoProvider.DB.Settings = photoProvider.DB.Settings with { ErrorOptOut = !photoProvider.DB.Settings.ErrorOptOut };
                }
            });

            menu.Items.Add(new()
            {
                Text = $"Update notifications: {(photoProvider.DB.Settings.UpdatesNotificationOptOut ? "disabled" : "enabled")}",
                OnClick = () =>
                {
                    photoProvider.DB.Settings = photoProvider.DB.Settings with
                        { UpdatesNotificationOptOut = !photoProvider.DB.Settings.UpdatesNotificationOptOut };
                }
            });

        }

        int _devMenuCntr;
        MenuItem _devMenuItem;
        private void MainMenu_SectionClick(MenuItem obj)
        {
            if (obj == _devMenuItem)
            {
                _devMenuCntr++;
                if (_devMenuCntr > 6)
                {
                    photoProvider.DB.Settings = photoProvider.DB.Settings with { DevMode = !photoProvider.DB.Settings.DevMode };
                    mainMenu.Close();
                }
            }
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
                new()
                {
                    Text = $"Debug mode: {(photoProvider.DB.Settings.DevMode ? "ON" : "OFF")}",
                    OnClick = () => photoProvider.DB.Settings = photoProvider.DB.Settings with { DevMode = !photoProvider.DB.Settings.DevMode },
                    ReturnLevel = menu.Parent
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

        public string LicenseUrl = "https://www.bytificial.com/uphoto_license_mit";

        void ShowAbout(MenuDef menu)
        {
            var about = new List<MenuItem>
                {
                    new MenuItem() {Text = $"{Utils.AppName} v{Version}" },
                    new MenuItem() {Text = Copyright, TextScale = 0.6f },
                    new MenuItem() {Text = "License", OnClick = () => Window.OpenUrl(LicenseUrl) },
                    new MenuItem() {Text = "Privacy policy", OnClick = () => Window.OpenUrl("https://www.bytificial.com/privacy") },
                    new MenuItem() {Text = "Website", OnClick = () => Window.OpenUrl("https://www.bytificial.com/uPhotos") },
                };

            _devMenuItem = about[0];
            _devMenuCntr = 0;
            menu.Items.AddRange(about);
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
