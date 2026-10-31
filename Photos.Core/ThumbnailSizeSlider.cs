using Photos.Lib;
using SkiaSharp;
using System;

namespace Photos.Core
{
    /// <summary>
    /// Four-step slider for the grid thumbnail size (Mini, Small, Medium, Large).
    /// Click or drag to choose a step; <see cref="OnChange"/> reports the new step.
    /// </summary>
    public class ThumbnailSizeSlider : BaseView
    {
        const float TrackPadding = 14;

        readonly SKPaint _background = new SKPaint { Color = SKColors.Black.WithAlpha(0xB0) };
        readonly SKPaint _track = new SKPaint { Color = SKColors.White.WithAlpha(0x70), StrokeWidth = 2, IsAntialias = true };
        readonly SKPaint _dot = new SKPaint { Color = SKColors.White.WithAlpha(0xC0), IsAntialias = true };
        readonly SKPaint _knob = new SKPaint { Color = SKColors.DodgerBlue, IsAntialias = true };
        readonly SKPaint _text = new SKPaint { Color = SKColors.White, IsAntialias = true };
        readonly SKFont _font = new SKFont(SKTypeface.Default, 12);

        int _index;

        /// <summary>Called with the new step after the user picks one.</summary>
        public Action<int> OnChange;

        public ThumbnailSizeSlider()
        {
            RenderFrameHandler = Render;
            MouseHandler = OnMouse;
        }

        public int Index
        {
            get => _index;
            set
            {
                int clamped = Math.Clamp(value, 0, ThumbnailSizes.Sizes.Length - 1);
                if (clamped == _index)
                    return;
                _index = clamped;
                Invalidate();
            }
        }

        float PositionOf(int step)
        {
            var rect = OuterRect;
            float span = rect.Width - 2 * TrackPadding;
            return TrackPadding + span * step / (ThumbnailSizes.Sizes.Length - 1);
        }

        void Render(SKCanvas ctx)
        {
            var rect = OuterRect;
            ctx.DrawRoundRect(rect, 6, 6, _background);

            float y = rect.Bottom - 10;
            ctx.DrawLine(PositionOf(0), y, PositionOf(ThumbnailSizes.Sizes.Length - 1), y, _track);
            for (int i = 0; i < ThumbnailSizes.Sizes.Length; i++)
            {
                ctx.DrawCircle(PositionOf(i), y, 3, _dot);
                ctx.DrawText(ThumbnailSizes.Names[i], PositionOf(i), rect.Top + 14, SKTextAlign.Center, _font, _text);
            }
            ctx.DrawCircle(PositionOf(_index), y, 7, _knob);
        }

        void OnMouse(libMouseEvent e)
        {
            if (e.Kind != libMouseEventKind.Down && e.Kind != libMouseEventKind.DragScroll)
                return;

            // CurrentPoint is relative to this slider.
            int nearest = 0;
            float nearestDistance = float.MaxValue;
            for (int i = 0; i < ThumbnailSizes.Sizes.Length; i++)
            {
                float distance = Math.Abs(PositionOf(i) - e.CurrentPoint.X);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = i;
                }
            }

            e.Consume(this);
            if (nearest != _index)
            {
                Index = nearest;
                OnChange?.Invoke(nearest);
            }
        }
    }
}
