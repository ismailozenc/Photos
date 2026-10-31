using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Photos.Lib
{
    public class Dialog : FreePanel
    {
        public readonly Val<float> ButtonSize = new(-1);

        protected FreePanel DialogContainer;
        public SKPaint OverlayPaint;
        public Func<SKRect> DialogRect;
        public bool Modal = true;
        public bool Dim = true;
        /// <summary>When set, a click outside the dialog runs OnOutsideClick and is not passed on.</summary>
        public bool CloseOnOutsideClick;
        public Action OnOutsideClick;
        bool _outsideDown;

        public Dialog()
        {
            Enabled.Const = false;
            DialogContainer = new FreePanel() { Link = Attach(() => DialogRect()), Padding = { Func = () => Padding } };

            MouseHandler = e =>
            {
                if (CloseOnOutsideClick)
                {
                    bool inside = DialogContainer.Position.Contains(e.CurrentPoint);
                    if (e.Kind == libMouseEventKind.Down && !inside)
                    {
                        _outsideDown = true;
                        OnOutsideClick?.Invoke();
                    }
                    if (_outsideDown)
                    {
                        // swallow the rest of this click so the view underneath does not react to it
                        if (e.Kind == libMouseEventKind.Up)
                            _outsideDown = false;
                        e.Consume(null);
                        return;
                    }
                }

                if (Modal)
                    e.Consume(null);
            };

            KeyUpHandler = e =>
            {
                if (Modal)
                    e.Consume();
            };

            RenderFrameHandler = RenderFrame;

            Background.Color = SKColors.Black.WithAlpha(0xF0);
            Background.Style = SKPaintStyle.Fill;
            OverlayPaint = new SKPaint() { Color = SKColors.Black.WithAlpha(0x50), Style = SKPaintStyle.Fill, IsAntialias = true };
            OnDispose += () => OverlayPaint.Dispose();
        }

        void RenderFrame(SKCanvas ctx)
        {
            if (Dim)
                ctx.DrawRect(OuterRect, OverlayPaint);

            var rect = DialogContainer.Position;
            var roundRect = new SKRoundRect(rect, 3);
            ctx.DrawRoundRect(roundRect, Background);
        }
    }
}
