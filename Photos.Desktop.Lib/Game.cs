using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Photos.Core;
using Photos.Lib;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Keys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;

namespace Photos.Desktop
{
    public class GameSetup
    {
        public Game Game { get; init; }
        public PhotoProvider PhotoProvider { get; init; }
        public WindowAdapter Window {  get; init; }
        public bool TransparentBackground;

    }

    public class Game : GameWindow
    {
        GRContext skiaCtx;
        SlideshowController _slideshows;
        MonitorLayout _monitors;
        bool _monitorsAutoStart = true;
        bool _layoutWasRunning;
        SKSurface skSurface;

        PhotoProvider photoProvider;

        WindowAdapter _window;

        libMouseEvent downEvent;
        SKPoint lastPoint;
        private bool IsViewValid;
        private readonly Task<PhotoProvider> providerPromise;
        private readonly Action<GameSetup> gameSetup;

        public Game(bool useLatinWindowTitle, Task<PhotoProvider> provider, Action<GameSetup> gameSetup) : base(GameWindowSettings.Default, new NativeWindowSettings()
        {
            WindowState = WindowState.Maximized,
            WindowBorder = WindowBorder.Hidden,
            Title = useLatinWindowTitle ? Core.Utils.LatinAppName : Core.Utils.AppName,
            ClientSize = new Vector2i(1152, 720),
            Icon = GetIcon(),
            Profile = ContextProfile.Core,
            APIVersion = new Version(3, 3),
            Flags = ContextFlags.ForwardCompatible,
            StencilBits = 8,
        })
        {
            UpdateFrequency = 60;

            skiaCtx = GRContext.CreateGl();
            if (skiaCtx == null)
                throw new Exception("SKIA GL error");

            KeyDown += Game_KeyUp;
            MouseWheel += Game_MouseWheel;
            MouseDown += Game_MouseDown;
            MouseUp += Game_MouseUp;
            MouseMove += Game_MouseMove;

            Closing += Game_Closing;
            providerPromise = provider;
            this.gameSetup = gameSetup;
            ResizeSkia(Size);
        }

        static OpenTK.Windowing.Common.Input.WindowIcon GetIcon()
        {
            var icons = GetIconStreams();
            var images = new OpenTK.Windowing.Common.Input.Image[icons.Length];
            for (int i = 0; i < icons.Length; i++)
            {
                var size = icons[i].size;
                using var bitmap = SKBitmap.Decode(icons[i].buf, new SKImageInfo() { Width = size, Height = size, ColorType = SKColorType.Rgba8888, AlphaType = SKAlphaType.Unpremul });
                images[i] = new OpenTK.Windowing.Common.Input.Image(size, size, bitmap.GetPixelSpan().ToArray());
            }

            return new OpenTK.Windowing.Common.Input.WindowIcon(images);
        }

        private static (byte[] buf, int size)[] GetIconStreams()
        {
            var asm = typeof(Game).Assembly;
            return asm.GetManifestResourceNames().Select(x => Regex.Match(x, @"^.*image_(\d+)\.png$")).Where(x => x.Success)
                .Select(x => {
                    using var stream = asm.GetManifestResourceStream(x.Value);
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    var buf = ms.ToArray();
                    return (buf, int.Parse(x.Groups[1].Value));
                }).ToArray();
        }

        protected override void OnLoad()
        {
            base.OnLoad();

            DesktopImageLoader.Register();
            WinFormsDialogs.Register();
            Core.Utils.Log($"Displays detected: {string.Join(", ", ScreenDetection.GetDisplayBounds())}");
            photoProvider = providerPromise.Result;
            _slideshows = new SlideshowController(photoProvider, () => Context.MakeCurrent());
            _monitors = new MonitorLayout(photoProvider, () => Context.MakeCurrent());
            _slideshows.Finished += () =>
            {
                if (_layoutWasRunning)
                {
                    _layoutWasRunning = false;
                    _monitors.Start();
                }
            };
            photoProvider.PhotoSelected += () => _monitors.HidePreviewForPhoto();
            if (_monitors.Roles.Main is System.Drawing.Rectangle mainBounds)
                MoveToDisplay(mainBounds);

            bool transparentBackground = false;
            if (OperatingSystem.IsWindows())
                transparentBackground = WinUtils.PrepareWindow(this);

            _window = new WindowAdapter()
            {
                //ClientSize = () => ClientSize.ToSKSize(),
                Close = () => Close(),
                Minimize = x => WindowState = x ? WindowState.Minimized : WindowState.Normal,
                Invalidate = () => IsViewValid = false,
                SetClipboard = x => ClipboardString = x,
                GetClipboard = () => ClipboardString,
                OpenUrl = url =>
                {
                    if (url.StartsWith("geo:"))
                        url = "https://maps.google.com/?q=" + url[4..];
                    
                    
                    if (!url.StartsWith("https://") && !url.StartsWith("file://"))
                        throw new NotImplementedException();

                    try
                    {
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    }
                    catch (Exception e) { return e.Message; }
                    return null;
                },
                MousePosition = () => MousePosition.ToSK(),
                IsCtrlDown = () => IsKeyDown(Keys.LeftControl) || IsKeyDown(Keys.RightControl),
                IsBorderless = () => WindowBorder == WindowBorder.Hidden,
                GR = skiaCtx,
                MaxRenderTargetSize = Math.Min(GL.GetInteger(GetPName.MaxTextureSize), GL.GetInteger(GetPName.MaxRenderbufferSize)),
                ScaleFactor = _lastScaleFactor,
                ManualClickDetection = true
            };

            if (OperatingSystem.IsWindows())
                _window.TotalMemoryHintKB = RegistryUtils.GetTotalMemoryKB();

            if (gameSetup != null)
            {
                var setup = new GameSetup()
                {
                    PhotoProvider = photoProvider,
                    Window = _window,
                    Game = this,
                    TransparentBackground = transparentBackground
                };

                gameSetup(setup);
                transparentBackground = setup.TransparentBackground;
            }

            var mainView = new MainView(photoProvider);
            mainView.SlideshowRequested += StartSlideshowFromBrowse;
            mainView.BrowseShown += () => _monitors.ShowPreviewIfHidden();

            if (OperatingSystem.IsWindows())
            {
                mainView.OnBuildPlatformSpecifiMenu += (kind, menu) =>
                {
                    if (kind == MenuSection.Settings)
                    {
                        menu.Items.Add(new()
                        {
                            Text = "Register file associations",
                            ReturnLevel = Menu.CloseMenuLevel,
                            OnClick = () =>
                            {
                                if (OperatingSystem.IsWindows())
                                {
                                    var res = RegistryUtils.Register(true);
                                    mainView.MessageBox.Show(new string[] { res == null ? "Done!" : $"Error: {res}" },
                                        new MessageBoxButton() { Text = "OK", Action = () => { } });
                                }
                            }
                        });

                        if (transparentBackground)
                            menu.Items.Add(new()
                            {
                                Text = $"Window background: {(photoProvider.DB.Settings.IsOpaqueBackground ? "opaque" : "transparent")}",
                                OnClick = () =>
                                {
                                    photoProvider.DB.Settings.IsOpaqueBackground = !photoProvider.DB.Settings.IsOpaqueBackground;
                                    photoProvider.DB.SaveSettings();
                                }
                            });
                    }
                };
            }

            if (transparentBackground)
                mainView.BackgroundColor = () => photoProvider.DB.Settings.IsOpaqueBackground ? SKColors.Black : SKColors.Black.WithAlpha(0xD0);

            mainView.OnBuildPlatformSpecifiMenu += (kind, menu) =>
            {
                if (kind == MenuSection.Main)
                    menu.Items.Add(new()
                    {
                        Text = _monitors.IsRunning ? "Stop monitor layout" : "Monitor layout (folders and preview)",
                        ReturnLevel = Menu.CloseMenuLevel,
                        OnClick = () =>
                        {
                            if (_monitors.IsRunning)
                                _monitors.Stop();
                            else
                                _monitors.Start();
                        }
                    });
            };

            _window.Initialize(mainView);
        }


        private void Game_MouseMove(MouseMoveEventArgs obj)
        {
            var isDown = IsMouseButtonDown(MouseButton.Left);
            var curPoint = new SKPoint(obj.X, obj.Y);
            var e = new libMouseEvent(isDown ? downEvent : null)
            {
                InitialPoint = isDown ? downEvent.InitialPoint : lastPoint,
                Kind = isDown ? libMouseEventKind.DragScroll : libMouseEventKind.FreeMove,
                CurrentPoint = curPoint,
                Offset = lastPoint - curPoint
            };

            _window.RouteMouseEvent(e);
            //moveAccepted |= !e.Rejected;
            lastPoint = curPoint;
        }

        private void Game_MouseDown(MouseButtonEventArgs obj)
        {
            lastPoint = MousePosition.ToSK();
            downEvent = new libMouseEvent()
            {
                InitialPoint = lastPoint,
                Kind = libMouseEventKind.Down,
                CurrentPoint = lastPoint,
            };


            _window.RouteMouseEvent(downEvent);
        }

        private void Game_MouseUp(MouseButtonEventArgs obj)
        {
            if (obj.Button != MouseButton.Left && obj.Button != MouseButton.Right)
                return;

            _window.RouteMouseEvent(new libMouseEvent(downEvent)
            {
                Kind = obj.Button == MouseButton.Right ? libMouseEventKind.ContextUp : libMouseEventKind.Up,
                CurrentPoint = MousePosition.ToSK(),
            });

        }
        private void Game_Closing(System.ComponentModel.CancelEventArgs obj)
        {
            _window.Dispose();
        }

        private void Game_MouseWheel(MouseWheelEventArgs obj) => _window.RouteMouseEvent(new libMouseEvent()
        {
            InitialPoint = MousePosition.ToSK(),
            Kind = libMouseEventKind.Wheel,
            CurrentPoint = MousePosition.ToSK(),
            Offset = new SKPoint(obj.OffsetX, obj.OffsetY)
        });

        private void Game_KeyUp(KeyboardKeyEventArgs e)
        {
            _window.RouteKeyEvent(new libKeyMessage() { Key = (libKeys)e.Key });
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            ResizeSkia(e.Size);
            base.OnResize(e);
        }

        Vector2i _lastSize;
        float _lastScaleFactor = 1;
        void ResizeSkia(Vector2i size)
        {
            if (skiaCtx == null || _lastSize == size)
                return;
            _lastSize = size;
            var monInfo = Monitors.GetMonitorFromWindow(this);
            _lastScaleFactor = Math.Min(monInfo.HorizontalScale, monInfo.VerticalScale);
            if (_window != null && !_window.FixedScaleFactor)
                _window.ScaleFactor = _lastScaleFactor;

            if (OperatingSystem.IsWindows())
            {
                if (WindowState == WindowState.Maximized && (Location != monInfo.WorkArea.Min || WindowBorder != WindowBorder.Hidden))
                {
                    WindowBorder = WindowBorder.Hidden;
                    Location = monInfo.WorkArea.Min;
                    Size = monInfo.WorkArea.Size;
                    return;
                }
            }
            else
            {
                if (WindowState == WindowState.Maximized && WindowBorder != WindowBorder.Hidden)
                {
                    WindowBorder = WindowBorder.Hidden;
                    return;
                }
            }

            if (WindowState != WindowState.Maximized && WindowBorder == WindowBorder.Hidden)
            {
                WindowBorder = WindowBorder.Resizable;
                CenterWindow(size);
            }

            GL.Viewport(0, 0, size.X, size.Y);

            GRGlFramebufferInfo fbi = new GRGlFramebufferInfo(0, (uint)InternalFormat.Rgba8);
            var ctype = SKColorType.Rgba8888;
            var beTarget = new GRBackendRenderTarget(size.X, size.Y, 0, 0, fbi);

            // Dispose Previous Surface
            skSurface?.Dispose();
            skSurface = SKSurface.Create(skiaCtx, beTarget, GRSurfaceOrigin.BottomLeft, ctype, null, null);
            if (skSurface == null)
                throw new Exception("SKIA surface error");


            IsViewValid = false;
        }
        /// <summary>Starts the all-screens slideshow. The monitor layout closes for the slideshow and comes back after it.</summary>
        void StartSlideshowFromBrowse()
        {
            if (_slideshows.IsRunning)
                return;

            _layoutWasRunning = _monitors.IsRunning;
            _monitors.Stop();
            _slideshows.Start();
            if (!_slideshows.IsRunning && _layoutWasRunning)
            {
                _layoutWasRunning = false;
                _monitors.Start();
            }
        }

        void MoveToDisplay(System.Drawing.Rectangle bounds)
        {
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            Location = new Vector2i(bounds.X + 100, bounds.Y + 100);
            WindowState = WindowState.Maximized;
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            if (_slideshows != null && _slideshows.IsRunning)
                _slideshows.Tick();

            if (_monitorsAutoStart && _monitors != null)
            {
                _monitorsAutoStart = false;
                if (_monitors.Roles.HasSecondaryRoles)
                    _monitors.Start();
            }
            _monitors?.Tick();

            if (ClientSize == default || IsViewValid || skSurface == null)
                return;

            IsViewValid = true;
            skSurface.Canvas.Clear();
            //((MainView)_window.RootView).Padding.Const = 400;
            _window.RenderFrame(skSurface);
            skiaCtx.Flush();
            SwapBuffers();
        }

        protected override void OnUnload()
        {
            _monitors?.Stop();
            _slideshows?.Stop();
            skiaCtx?.Dispose();
            skSurface?.Dispose();
            base.OnUnload();
        }

        internal void OpenFile(string commFile)
        {
            while (photoProvider == null)
            {
                providerPromise.Wait();
                Thread.Sleep(300);
            }

            photoProvider.OpenFile(commFile);
            Focus();
        }
    }

   
}
