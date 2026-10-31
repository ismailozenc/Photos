using Photos.Lib;
using SkiaSharp;
using System;
using System.Linq;

namespace Photos.Core
{
    /// <summary>
    /// Side-by-side comparison that feeds the ranking. The photo being placed is shown on the
    /// left and the ranked photo it is compared with on the right. Choose the left one with
    /// 1 or Left arrow, the right one with 2 or Right arrow. Esc returns to the viewer.
    /// </summary>
    public class CompareView : FreePanel
    {
        const int DecodeSideLength = 1400;

        readonly PhotoProvider photoProvider;
        readonly Random _rnd = new();
        readonly SKFont _font = new SKFont(SKTypeface.Default, 20);
        readonly SKPaint _textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        readonly SKPaint _backgroundPaint = new SKPaint { Color = SKColors.Black };

        RankingState _state = new RankingState();
        ulong _a;
        ulong _b;
        bool _hasPair;
        SKImage _imageA;
        SKImage _imageB;
        string _status = "";

        public event Action OnBackPressed;

        public CompareView(PhotoProvider photoProvider)
        {
            this.photoProvider = photoProvider;
            KeyUpHandler = KeyUp;
            RenderFrameHandler = RenderFrame;
            Enabled.Const = false;
        }

        /// <summary>Loads the ranking, places any new photos, and shows the first pair.</summary>
        public void Open()
        {
            _state = RankingStore.Load(photoProvider.DB);
            RankingEngine.Sync(_state, LiveSignatures(), _rnd);
            RankingStore.Save(photoProvider.DB, _state);
            LoadPair();
            Invalidate();
        }

        public void Close()
        {
            DisposeImages();
            OnBackPressed?.Invoke();
        }

        ulong[] LiveSignatures() => photoProvider.DB.ListFiles()
            .Where(f => f.IsIndexed())
            .Select(f => f.Sig)
            .ToArray();

        void KeyUp(libKeyMessage e)
        {
            if (e.Key == libKeys.Left || e.Key == libKeys.D1)
                Choose(0);
            else if (e.Key == libKeys.Right || e.Key == libKeys.D2)
                Choose(1);
            else if (e.Key == libKeys.Escape)
                Close();
            else
                return;

            e.Consume();
        }

        void Choose(int side)
        {
            if (!_hasPair)
                return;

            ulong winner = side == 0 ? _a : _b;
            var live = LiveSignatures();
            RankingEngine.ApplyResult(_state, winner, live, _rnd);
            RankingStore.Save(photoProvider.DB, _state);

            var progress = RankingEngine.GetProgress(_state, live);
            _status = progress.Complete
                ? $"All {progress.Total} photos ranked"
                : $"{progress.Ranked} of {progress.Total} ranked · about {progress.ComparisonsLeft} comparisons left";

            LoadPair();
            Invalidate();
        }

        void LoadPair()
        {
            DisposeImages();
            _hasPair = RankingEngine.TryCurrentPair(_state, out _a, out _b, out _);
            if (!_hasPair)
                return;

            _imageA = DecodeSignature(_a);
            _imageB = DecodeSignature(_b);
        }

        SKImage DecodeSignature(ulong sig)
        {
            var file = photoProvider.DB.TryGetFileBySignature(sig);
            if (file == null)
                return null;

            using var data = photoProvider.Indexer.GetFullImage(file);
            if (data == null)
                return null;

            ImageLoader.DecodeAndResizeImage(data, ImageLoader.ScaleToMinSide(DecodeSideLength, DecodeSideLength));
            return data.Bitmap == null ? null : SKImage.FromBitmap(data.Bitmap);
        }

        void RenderFrame(SKCanvas ctx)
        {
            var area = OuterRect;
            ctx.DrawRect(area, _backgroundPaint);

            float gap = 6;
            float half = (area.Width - gap) / 2;
            var leftArea = SKRect.Create(area.Left, area.Top, half, area.Height);
            var rightArea = SKRect.Create(area.Left + half + gap, area.Top, half, area.Height);

            if (_hasPair)
            {
                DrawFit(ctx, _imageA, leftArea);
                DrawFit(ctx, _imageB, rightArea);
                ctx.DrawText("1 / ← placed photo ranks higher", leftArea.Left + 12, leftArea.Bottom - 12, SKTextAlign.Left, _font, _textPaint);
                ctx.DrawText("2 / → placed photo ranks lower", rightArea.Left + 12, rightArea.Bottom - 12, SKTextAlign.Left, _font, _textPaint);
            }
            else
            {
                ctx.DrawText(
                    string.IsNullOrEmpty(_status) ? "Need at least two photos to compare" : _status,
                    area.MidX, area.MidY, SKTextAlign.Center, _font, _textPaint);
            }

            if (_hasPair && !string.IsNullOrEmpty(_status))
                ctx.DrawText(_status, area.Left + 12, area.Top + 28, SKTextAlign.Left, _font, _textPaint);
        }

        static void DrawFit(SKCanvas ctx, SKImage image, SKRect area)
        {
            if (image == null)
                return;

            float scale = Math.Min(area.Width / image.Width, area.Height / image.Height);
            float w = image.Width * scale;
            float h = image.Height * scale;
            var dest = SKRect.Create(area.Left + (area.Width - w) / 2, area.Top + (area.Height - h) / 2, w, h);
            ctx.DrawImage(image, dest);
        }

        void DisposeImages()
        {
            _imageA?.Dispose();
            _imageB?.Dispose();
            _imageA = null;
            _imageB = null;
        }
    }
}
