using Photos.Lib;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Photos.Core
{
    public partial class MainView : FreePanel
    {
        PhotoView _photoView;
        CompareView _compareView;
        ThumbnailSizeSlider _sizeSlider;
        public readonly ThumbnailView ThumbnailView;
        Menu mainMenu;
        readonly Menu _itemMenu;
        SKRect _itemMenuRect;
        public readonly Toast MainToast;
        readonly MessageBox _mbox;
        public MessageBox MessageBox => _mbox;
        public Menu MainMenu => mainMenu;
        public event Action SlideshowRequested;
        /// <summary>Raised when the grid is shown again, for example after a photo is closed.</summary>
        public event Action BrowseShown;

        public readonly Val<float> MenuButtonSize = new(-1);
        public readonly Val<float> ButtonSize = new(-1);
        public readonly Val<float> TextLabelSize = new(-1);
        public static int ButtonPadding = 2;
        public float BigButtonSize => (ButtonSize * 3 + ButtonPadding) / 2;
        public Val<float> WorkStatusLabelHeight = new(-1);
        private readonly PhotoProvider photoProvider;
        public readonly StackPanel ButtonPanel;
        readonly StackPanel WindowControlPanel;

        public MainView(PhotoProvider photoProvider)
        {
            MenuButtonSize.Func = () => 38 * Window.ScaleFactor;
            ButtonSize.Func = () => 30 * Window.ScaleFactor;
            TextLabelSize.Func = () => 32 * Window.ScaleFactor;
            WorkStatusLabelHeight.Func = () => 28 * Window.ScaleFactor;


            OnDispose += () => { photoProvider.Dispose(); Utils.Trace("Dispose"); };

            Padding.Const = 0;


            _photoView = new(photoProvider)
            {
                Link = Attach(() => OuterRect),
                Padding = { Func = () => Padding },
                ButtonPanelButtonSize = { Func = () => BigButtonSize }
            };
            _photoView.OnBackPressed += SwitchView;

            _compareView = new(photoProvider)
            {
                Link = Attach(() => OuterRect),
            };
            _compareView.OnBackPressed += () => _compareView.Enabled.Const = false;
            _photoView.OnCompareRequested += () =>
            {
                _compareView.Open();
                _compareView.Enabled.Const = true;
            };
            _photoView.Enabled.Func = () => !ThumbnailView.Enabled && !_compareView.Enabled;

            ThumbnailView = new(photoProvider, ButtonSize)
            {
                Link = Attach(() => InnerRect),
                RightImageMargin = { Func = () => (BigButtonSize + ButtonPadding) },
                ScrollContainer = { Padding = { Func = () => ButtonPanel.Position.Bottom + ButtonPadding - ThumbnailView.Position.Top } }
            };

            photoProvider.RequestUILock = wait =>
            {
                var lockAquired = new TaskCompletionSource();

                Task.Factory.StartNew(() => Window.Run(() =>
                {
                    lockAquired.SetResult();
                    wait.Wait();
                }), TaskCreationOptions.LongRunning);

                return lockAquired.Task;
            };

            photoProvider.PhotoSelected += () => ThumbnailView.Enabled.Const = false;
            _photoView.MenuButton.AdjustRect(x => x.OffsetRect(y: (WindowControlPanel.Enabled ? WindowControlPanel.Position.Bottom + ButtonPadding : 0)));

            ButtonPanel = new StackPanel()
            {
                MeasureChild = x => BigButtonSize,
                ItemMargin = { Func = () => ButtonPadding },
                Link = Attach(() => SKRect.Create(InnerRect.Right - BigButtonSize, (WindowControlPanel.Enabled ? WindowControlPanel.Position.Bottom : InnerRect.Top) + ButtonPadding,
                    BigButtonSize, (BigButtonSize + ButtonPadding) * 5 - ButtonPadding)),
                Enabled = { Func = () => !photoProvider.IsInitRunning && ThumbnailView.Enabled },
            };

            new Button() // return to gallery
            {
                Link = ButtonPanel.Attach(),
                Icon = { Const = IconStore.ArrowLeft },
                Enabled = { Func = () => photoProvider.IsSingleFolderMode },
                OnClick = () => photoProvider.CloseSingleFolderMode(),
            };

            new Button() // slideshow on all screens, browse mode only
            {
                Link = ButtonPanel.Attach(),
                Icon = { Const = IconStore.Play },
                OnClick = RequestSlideshow,
                Enabled = { Func = () => !photoProvider.IsInitRunning && ThumbnailView.Enabled && photoProvider.DisplayedCount > 0 }
            };

            new Button() // menu
            {
                Link = ButtonPanel.Attach(),
                Icon = { Const = IconStore.List },
                OnClick = () => mainMenu.Show(MainMenuFunc),
            };

            new Button()
            {
                Link = ButtonPanel.Attach(),
                Icon = { Const = IconStore.Folder },
                OnClick = () => mainMenu.Show(m => photoProvider.FilterService.FilterMenu(m, photoProvider.FilterService.GetFilterGroup(FilterService.FolderFilter))),
                Enabled = { Func = () => photoProvider.FilterService.FiltersReady && !photoProvider.IsSingleFolderMode },
                IsHighlighted = { Func = () => photoProvider.FilterService.IsFilterGroupActive(FilterService.FolderFilter) }
            };

            new Button()
            {
                Link = ButtonPanel.Attach(),
                Icon = { Const = IconStore.Funnel },
                OnClick = () => ShowFilterMenu(photoProvider.FilterService.FilterGroups.Where(x => !x.HideInFilterList).ToArray()),
                Enabled = { Func = () => photoProvider.FilterService.FiltersReady },
                IsHighlighted = { Func = () => photoProvider.FilterService.GetActiveFilterTypes().Any(x => !x.HideInFilterList) }
            };

            new Button()
            {
                Link = ButtonPanel.Attach(),
                Icon = { Const = IconStore.Star },
                OnClick = () =>
                {
                    var fgr = photoProvider.FilterService.GetFilterGroup(FilterService.FavFilter);
                    photoProvider.FilterService.ToggleFilters(fgr, fgr.Filters.Values.Single());
                },
                Enabled = { Func = () => photoProvider.FilterService.FiltersReady },
                IsHighlighted = { Func = () => photoProvider.FilterService.IsFilterGroupActive(FilterService.FavFilter) }
            };

            new TextLabel()
            {
                Text = getPlaceholderText,
                Link = Attach(() => CenterRect(new SKSize(0, 40))),
                Padding = { Const = 3 },
                TextAlign = { Const = SKTextAlign.Center },
                Enabled = { Func = () => !_mbox.Enabled && !mainMenu.Enabled && getPlaceholderText() != null },
                DrawBorder = false,
            };

            string getPlaceholderText()
            {
                if (photoProvider.IsInitRunning)
                    return "... loading ...";
                else if (EmptyLibrary)
                    return "Click Menu -> 'Photo library settings' to add pictures";
                return null;
            }

            mainMenu = new()
            {
                Link = Attach(() => OuterRect),
                DialogRect = () => CenterRect(new SKSize((int)Math.Min(Size.Width, Size.Height * 1.1), Size.Height * 3 / 4)),
                ButtonSize = { Func = () => MenuButtonSize }
            };

            _itemMenu = new Menu()
            {
                Link = Attach(() => OuterRect),
                DialogRect = () => _itemMenuRect,
                ButtonSize = { Func = () => MenuButtonSize }
            };

            _photoView.MainMenu = mainMenu;
            ThumbnailView.ItemContextRequested += ShowItemMenu;
            ThumbnailView.Enabled.OnConstSet += (o, n) => { if (n) BrowseShown?.Invoke(); };

            _sizeSlider = new ThumbnailSizeSlider()
            {
                Link = Attach(() => InnerRect.Select(x => SKRect.Create(x.Right - 282, x.Bottom - 50, 272, 40))),
                Enabled = { Func = () => ThumbnailView.Enabled },
            };
            _sizeSlider.OnChange = step => photoProvider.DB.Settings = photoProvider.DB.Settings with { ThumbnailDrawSize = ThumbnailSizes.Sizes[step] };
            if (photoProvider.DB.Settings != null)
                _sizeSlider.Index = ThumbnailSizes.SlotFor(photoProvider.DB.Settings.ThumbnailDrawSize);
            photoProvider.DB.OnSettingsUpdate += (oldSettings, newSettings) => _sizeSlider.Index = ThumbnailSizes.SlotFor(newSettings.ThumbnailDrawSize);

            var statusToast = new Toast()
            {
                Text = () => photoProvider.Indexer.WorkStatus,
                Link = ThumbnailView.Attach(() => ThumbnailView.Position.Select(x => SKRect.Create(0, x.Height - WorkStatusLabelHeight * 1.3f, x.Width, WorkStatusLabelHeight))),
                Padding = { Const = 1 },
                TextAlign = { Const = SKTextAlign.Center },
            };
            statusToast.Enabled.Adjust(x => x && photoProvider.Indexer.WorkStatus != null);
            photoProvider.Indexer.OnIndexing += indexing => Window?.Run(() => statusToast.Show(indexing ? float.PositiveInfinity : 5f));

            MainToast = new Toast()
            {
                Link = Attach(() => ThumbnailView.Position.Select(x => SKRect.Create(0, 30, x.Width, WorkStatusLabelHeight * 1.2f))),
            };

            new TextLabel()
            {
                Text = () => Window.GetRenderStats(),
                Link = Attach(() => InnerRect.Select(x => SKRect.Create(0, x.Bottom - 35, x.Width - 10, 25))),
                Padding = { Const = 3 },
                TextAlign = { Const = SKTextAlign.Right },
                Enabled = { Func = () => photoProvider.DB.Settings.DevMode }
            };


            _mbox = new()
            {
                Link = Attach(() => OuterRect),
                ButtonSize = { Func = () => MenuButtonSize },
                TextSize = { Func = () => ButtonSize }
            };

            WindowControlPanel = new StackPanel()
            {
                MeasureChild = x => ButtonSize,
                ItemMargin = { Func = () => ButtonPadding },
                Link = Attach(() => InnerRect.OffsetEdge(bottom: ButtonSize -InnerRect.Height)), //SKRect.Create(0, ButtonPadding, Size.Width, ButtonSize)),
                Enabled = { Func = () => Window.IsBorderless() },
                IsHorizontal = true,
                IsReversed = { Const = true },
            };

            _photoView.StyleButton(new Button() // close
            {
                Link = WindowControlPanel.Attach(),
                Icon = { Const = IconStore.X },
                OnClick = () => Window.Close(),
                Padding = { Const = 1 },
            });

            _photoView.StyleButton(new Button() // set normal
            {
                Link = WindowControlPanel.Attach(),
                Icon = { Const = IconStore.MyArrowDownLeft },
                OnClick = () => Window.Minimize(false),
                Padding = { Const = 1 },
            });

            _photoView.StyleButton(new Button() // minimize
            {
                Link = WindowControlPanel.Attach(),
                Icon = { Const = IconStore.Dash },
                OnClick = () => Window.Minimize(true),
                Padding = { Const = 1 },
            });

            this.photoProvider = photoProvider;
            KeyUpHandler = KeyUp;
        }

        public override void Init()
        {
            photoProvider.Indexer.FirstIndexTask.ContinueWith(x => Window.Run(() => ShowWelcomeBox())); // wait for init to finish

            photoProvider.OnDisplayOrderUpdate += () => Window.Run(() =>
            {
                ThumbnailView.SelectedItems.Clear();
                Window.Invalidate();
            });

            if (photoProvider.IsSingleFolderMode)
                SwitchView(); // showselected pic

            Window.RenderFinished += x => x.PrintUtcMs("render", 50, true);

            if (Window.TotalMemoryHintKB == 0)
            {
                try
                {
                    var memFile = "/proc/meminfo";
                    if (File.Exists(memFile))
                    {
                        var match = Regex.Match(File.ReadAllLines(memFile).First(x => x.StartsWith("MemTotal:")), @"\d+");
                        if (match.Success)
                            Window.TotalMemoryHintKB = long.Parse(match.Value);
                    }
                }
                catch { }

                if (Window.TotalMemoryHintKB == 0)
                    Window.TotalMemoryHintKB = 2L << 20; // assume at least 2GB
            }

            RenderFrameHandler = ctx =>
            {
                if (!_limitSet)
                {
                    _limitSet = true;
                    Window.GR.SetResourceCacheLimit(Math.Clamp(Window.TotalMemoryHintKB * 1024 / 4, 1L << 29, 4L << 30)); // 1/4 if mem between 512mb and 4096mb
                }

                ctx.Clear(BackgroundColor());
            };
        }

        public Func<SKColor> BackgroundColor = () => SKColors.Black;

        bool _limitSet;

        bool EmptyLibrary => photoProvider.FolderLibraryMode && photoProvider.GetDirs().Count == 0 && photoProvider.TotalCount == 0;

        private void ShowWelcomeBox()
        {
            if (EmptyLibrary)
                _mbox.Show(new[] {
                    "Your photo library is empty",
                    //"Please add a folder with photos to the library"
                    "Would you like to add your user directory to the library?"
                    },
                new MessageBoxButton()
                {
                    Text = "Yes",
                    Action = () => photoProvider.AddDir(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
                },
                new MessageBoxButton() { Text = "Choose another folder", Action = () => mainMenu.Show(PhotoLibMenu) },
                new MessageBoxButton() { Text = "Cancel", Action = () => { } }
                );
        }

        void KeyUp(libKeyMessage e)
        {
            if (e.Key == libKeys.Escape)
            {
                e.Consume();
                GoBack();
            }
            else if (e.Key == libKeys.S && ThumbnailView.Enabled && !photoProvider.IsInitRunning && photoProvider.DisplayedCount > 0)
            {
                e.Consume();
                RequestSlideshow();
            }
        }

        private void GoBack()
        {
            //{ 
            //    // TODO: cancel?
            //}
            //else 
            if (ThumbnailView.Enabled)
                Window.Close();
            else if (_compareView.Enabled)
                _compareView.Close();
            else if (_photoView.Enabled)
                _photoView.GoBack();
        }

        void RequestSlideshow()
        {
            if (photoProvider.ShowDeleted)
                MainToast.Show("Deleted images are not shown in slideshows");
            else
                SlideshowRequested?.Invoke();
        }

        private void SwitchView()
        {
            ThumbnailView.Enabled.Const = !ThumbnailView.Enabled;
        }

    }
}
