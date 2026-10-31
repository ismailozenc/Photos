using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using SkiaSharp;
using System;

namespace Photos.Desktop
{
    /// <summary>
    /// A borderless window with its own Skia context, drawn by the main window's render loop.
    /// Every tick presents a frame, so the OS can never leave the window blank until the next change.
    /// </summary>
    internal abstract class SkiaWindow : GameWindow
    {
        GRContext _skiaCtx;
        SKSurface _surface;
        Vector2i _surfaceSize;

        protected SkiaWindow(int x, int y, int width, int height, string title)
            : base(GameWindowSettings.Default, new NativeWindowSettings
            {
                Title = title,
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
        }

        /// <summary>Creates the Skia context. Call once, after construction.</summary>
        public void Initialize()
        {
            Context.MakeCurrent();
            _skiaCtx = GRContext.CreateGl();
            if (_skiaCtx == null)
                throw new InvalidOperationException("Skia GL context could not be created");
        }

        /// <summary>Updates state before a frame is drawn. Runs every tick.</summary>
        protected virtual void Advance() { }

        /// <summary>Draws one complete frame. Runs every tick.</summary>
        protected abstract void DrawFrame(SKCanvas canvas, int width, int height);

        /// <summary>Frees photos and other resources. The Skia context is still alive when this runs.</summary>
        protected virtual void ReleaseResources() { }

        public void RenderFrame()
        {
            if (_skiaCtx == null || IsExiting)
                return;

            Context.MakeCurrent();
            Advance();
            EnsureSurface();

            DrawFrame(_surface.Canvas, ClientSize.X, ClientSize.Y);
            _surface.Canvas.Flush();
            _skiaCtx.Flush();
            SwapBuffers();
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

        public override void Dispose()
        {
            if (_skiaCtx != null)
            {
                Context.MakeCurrent();
                ReleaseResources();
                _surface?.Dispose();
                _skiaCtx.Dispose();
            }

            _surface = null;
            _skiaCtx = null;
            base.Dispose();
        }
    }
}
