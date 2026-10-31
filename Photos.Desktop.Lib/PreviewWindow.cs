using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Photos.Core;
using Photos.Core.StoredTypes;
using SkiaSharp;
using System;
using System.Drawing;
using System.Linq;

namespace Photos.Desktop
{
    /// <summary>
    /// Fullscreen preview on the landscape display. It shows the photo the main window has open,
    /// so choosing a photo in the library shows it here. Left and Right move through the library.
    /// </summary>
    internal sealed class PreviewWindow : SkiaWindow
    {
        readonly PhotoProvider _provider;
        FileRecord _loadedFile;
        SKImage _image;
        string _caption = "";

        public PreviewWindow(PhotoProvider provider, Rectangle bounds)
            : base(bounds.X, bounds.Y, bounds.Width, bounds.Height, Core.Utils.LatinAppName)
        {
            _provider = provider;
            KeyDown += HandleKeyDown;
        }

        protected override void Advance()
        {
            int count = _provider.DisplayedCount;
            if (count == 0)
                return;

            var file = _provider.GetFile(Math.Clamp(_provider.Idx, 0, count - 1));
            if (ReferenceEquals(file, _loadedFile))
                return;

            _loadedFile = file;
            LoadPhoto(file);
        }

        void LoadPhoto(FileRecord file)
        {
            _image?.Dispose();
            _image = null;
            if (file == null)
                return;

            int position = Math.Clamp(_provider.Idx, 0, _provider.DisplayedCount - 1) + 1;
            _caption = $"{position} / {_provider.DisplayedCount}   {file.Src.FirstOrDefault().FN}";

            using var data = _provider.Indexer.GetFullImage(file);
            if (data == null)
                return;

            int side = Math.Min(ClientSize.X, ClientSize.Y);
            ImageLoader.DecodeAndResizeImage(data, ImageLoader.ScaleToMinSide(side, side));
            _image = data.Bitmap == null ? null : SKImage.FromBitmap(data.Bitmap);
        }

        protected override void DrawFrame(SKCanvas canvas, int width, int height)
        {
            canvas.Clear(SKColors.Black);

            if (_image != null)
            {
                float scale = Math.Min((float)width / _image.Width, (float)height / _image.Height);
                float dw = _image.Width * scale;
                float dh = _image.Height * scale;
                canvas.DrawImage(_image, SKRect.Create((width - dw) / 2, (height - dh) / 2, dw, dh));
            }

            if (_caption.Length > 0)
            {
                using var paint = new SKPaint { Color = SKColors.White.WithAlpha(190), IsAntialias = true };
                using var font = new SKFont(SKTypeface.Default, 18);
                canvas.DrawText(_caption, 16, height - 16, font, paint);
            }
        }

        void HandleKeyDown(KeyboardKeyEventArgs e)
        {
            switch (e.Key)
            {
                case Keys.Escape:
                    Close();
                    break;
                case Keys.Right:
                    _provider.Idx += 1;
                    break;
                case Keys.Left:
                    _provider.Idx -= 1;
                    break;
            }
        }

        protected override void ReleaseResources()
        {
            _image?.Dispose();
            _image = null;
        }
    }
}
