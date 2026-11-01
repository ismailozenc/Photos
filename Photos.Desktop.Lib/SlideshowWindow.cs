using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Photos.Core;
using SkiaSharp;
using System;
using System.Diagnostics;

namespace Photos.Desktop
{
    /// <summary>
    /// A borderless window covering one display, showing the photos in display order.
    /// It is created, drawn and disposed on the main thread, and shares the photo provider
    /// with the library window, so nothing is accessed from two threads.
    /// </summary>
    internal sealed class SlideshowWindow : GameWindow
    {
        const int IntervalMs = 5000;

        readonly PhotoProvider _provider;
        readonly Stopwatch _timer = new Stopwatch();
        GRContext _skiaCtx;
        SKSurface _surface;
        Vector2i _surfaceSize;
        SKImage _image;
        int _index;
        bool _paused;
        readonly AsyncImage _loader = new AsyncImage();
        /// <summary>Set by the controller: Escape on any screen stops every screen.</summary>
        public Action OnEscape;

        public SlideshowWindow(PhotoProvider provider, int x, int y, int width, int height, int startIndex)
            : base(GameWindowSettings.Default, new NativeWindowSettings
            {
                Title = Core.Utils.LatinAppName,
                ClientSize = new Vector2i(width, height),
                Location = new Vector2i(x, y),
                WindowBorder = WindowBorder.Hidden,
                StartVisible = true,
                Profile = ContextProfile.Core,
                APIVersion = new Version(3, 3),
                Flags = ContextFlags.ForwardCompatible,
                StencilBits = 8,
            })
        {
            _provider = provider;
            _index = startIndex;
            KeyDown += HandleKeyDown;
        }

        /// <summary>Creates the Skia context and loads the first photo. Call once, after construction.</summary>
        public void Initialize()
        {
            Context.MakeCurrent();
            _skiaCtx = GRContext.CreateGl();
            if (_skiaCtx == null)
                throw new InvalidOperationException("Skia GL context could not be created");

            EnsureSurface();
            LoadCurrent();
            _timer.Restart();
        }

        /// <summary>Advances when due and redraws when something changed.</summary>
        public void DrawAndPresent()
        {
            if (_skiaCtx == null || IsExiting)
                return;

            Context.MakeCurrent();
            EnsureSurface();

            if (!_paused && _timer.ElapsedMilliseconds >= IntervalMs)
                Step(1);

            _loader.Poll();
            _image = _loader.Current;

            // Present every tick: the OS can discard a window's contents, which would leave it
            // blank until the next photo change.
            Draw();
            _skiaCtx.Flush();
            SwapBuffers();
        }

        void Step(int direction)
        {
            int count = _provider.DisplayedCount;
            if (count == 0)
                return;

            _index = ((_index + direction) % count + count) % count;
            _timer.Restart();
            LoadCurrent();
        }

        void LoadCurrent()
        {
            int count = _provider.DisplayedCount;
            var file = count == 0 ? null : _provider.GetFile(_index % count);
            var data = file == null ? null : _provider.Indexer.GetFullImage(file); // the file is read here; decoding happens off this thread
            int side = Math.Min(ClientSize.X, ClientSize.Y);
            _loader.Request(() =>
            {
                if (data == null)
                    return null;
                using (data)
                {
                    ImageLoader.DecodeAndResizeImage(data, ImageLoader.ScaleToMinSide(side, side));
                    return data.Bitmap == null ? null : SKImage.FromBitmap(data.Bitmap);
                }
            });
        }

        void Draw()
        {
            var canvas = _surface.Canvas;
            canvas.Clear(SKColors.Black);
            if (_image == null)
                return;

            float w = ClientSize.X;
            float h = ClientSize.Y;
            float scale = Math.Min(w / _image.Width, h / _image.Height);
            float dw = _image.Width * scale;
            float dh = _image.Height * scale;
            canvas.DrawImage(_image, SKRect.Create((w - dw) / 2, (h - dh) / 2, dw, dh));
        }

        void EnsureSurface()
        {
            var size = ClientSize;
            if (_surface != null && size == _surfaceSize)
                return;

            _surface?.Dispose();
            GL.Viewport(0, 0, size.X, size.Y);
            var framebuffer = new GRGlFramebufferInfo(0, (uint)InternalFormat.Rgba8);
            var target = new GRBackendRenderTarget(size.X, size.Y, 0, 0, framebuffer);
            _surface = SKSurface.Create(_skiaCtx, target, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888, null, null);
            if (_surface == null)
                throw new InvalidOperationException("Skia surface could not be created");

            _surfaceSize = size;
        }

        void HandleKeyDown(KeyboardKeyEventArgs e)
        {
            switch (e.Key)
            {
                case Keys.Escape:
                    if (OnEscape != null)
                        OnEscape();
                    else
                        Close();
                    break;
                case Keys.Space:
                    _paused = !_paused;
                    _timer.Restart();
                    break;
                case Keys.Right:
                    Step(1);
                    break;
                case Keys.Left:
                    Step(-1);
                    break;
            }
        }

        public override void Dispose()
        {
            if (_skiaCtx != null)
            {
                Context.MakeCurrent();
                _loader.Dispose();
                _surface?.Dispose();
                _skiaCtx.Dispose();
            }

            _image = null;
            _surface = null;
            _skiaCtx = null;
            base.Dispose();
        }
    }
}
