using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Photos.Core;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Drawing;

namespace Photos.Desktop
{
    /// <summary>
    /// Folder list on the portrait display. It lists every library folder, including subfolders,
    /// with its photo count. Clicking a folder, or pressing Enter on it, toggles the same folder
    /// filter the main window uses. Up and Down move the cursor, and the mouse wheel scrolls.
    /// </summary>
    internal sealed class FolderWindow : SkiaWindow
    {
        const float HeaderHeight = 48;
        const float RowHeight = 34;
        const float Padding = 14;
        const float IndentStep = 16;
        const int RefreshMs = 250;

        readonly PhotoProvider _provider;
        readonly Stopwatch _refreshTimer = Stopwatch.StartNew();
        FileFilterGroup _group;
        List<FileFilter> _rows = new List<FileFilter>();
        int _cursor;
        float _scroll;

        public FolderWindow(PhotoProvider provider, Rectangle bounds)
            : base(bounds.X, bounds.Y, bounds.Width, bounds.Height, Core.Utils.LatinAppName)
        {
            _provider = provider;
            KeyDown += HandleKeyDown;
            MouseDown += HandleMouseDown;
            MouseWheel += HandleMouseWheel;
        }

        protected override void Advance()
        {
            if (_rows.Count > 0 && _refreshTimer.ElapsedMilliseconds < RefreshMs)
                return;

            _refreshTimer.Restart();
            _group = _provider.FilterService.FilterGroups.FirstOrDefault(g => g.GroupId == FilterService.FolderFilter);
            _rows = _group?.Filters.Values.OrderBy(f => f.PreText).ThenBy(f => f.Text).ToList() ?? new List<FileFilter>();
            _cursor = Math.Clamp(_cursor, 0, Math.Max(0, _rows.Count - 1));
            _scroll = Math.Clamp(_scroll, 0, MaxScroll(ClientSize.Y));
        }

        protected override void DrawFrame(SKCanvas canvas, int width, int height)
        {
            canvas.Clear(SKColors.Black);

            using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            using var textPaint = new SKPaint { Color = SKColors.White.WithAlpha(220), IsAntialias = true };
            using var activePaint = new SKPaint { Color = SKColors.LightGreen, IsAntialias = true };
            using var countPaint = new SKPaint { Color = SKColors.Gray, IsAntialias = true };
            using var selectionPaint = new SKPaint { Color = SKColor.Parse("#33FFFFFF") };
            using var font = new SKFont(SKTypeface.Default, 18);
            using var titleFont = new SKFont(SKTypeface.Default, 20);

            canvas.DrawText("Folders", Padding, 32, titleFont, titlePaint);

            if (_rows.Count == 0)
            {
                canvas.DrawText("No folders yet. Add a folder to the library.", Padding, HeaderHeight + 24, font, countPaint);
                return;
            }

            canvas.Save();
            canvas.ClipRect(SKRect.Create(0, HeaderHeight, width, height - HeaderHeight));

            for (int i = 0; i < _rows.Count; i++)
            {
                float top = HeaderHeight + i * RowHeight - _scroll;
                if (top + RowHeight < HeaderHeight || top > height)
                    continue;

                var row = _rows[i];
                if (i == _cursor)
                    canvas.DrawRect(SKRect.Create(0, top, width, RowHeight), selectionPaint);

                string rel = row.Text.Trim().Trim('\\', '/');
                int depth = rel.Count(c => c == '\\' || c == '/');
                string name = rel.Length == 0 ? "<root>" : rel.Split('\\', '/').Last();

                bool active = _group != null && _provider.FilterService.IsFilterActive(_group, row);
                float baseline = top + RowHeight * 0.7f;
                canvas.DrawText(name, Padding + depth * IndentStep, baseline, font, active ? activePaint : textPaint);
                canvas.DrawText(row.Count.ToString(), width - Padding, baseline, SKTextAlign.Right, font, countPaint);
            }

            canvas.Restore();
        }

        float MaxScroll(int clientHeight) => Math.Max(0, _rows.Count * RowHeight - (clientHeight - HeaderHeight));

        void EnsureCursorVisible()
        {
            float top = _cursor * RowHeight;
            float viewHeight = ClientSize.Y - HeaderHeight;
            if (top < _scroll)
                _scroll = top;
            else if (top + RowHeight > _scroll + viewHeight)
                _scroll = top + RowHeight - viewHeight;
            _scroll = Math.Clamp(_scroll, 0, MaxScroll(ClientSize.Y));
        }

        void Toggle(int index)
        {
            if (_group == null || index < 0 || index >= _rows.Count)
                return;
            _provider.FilterService.ToggleFilters(_group, _rows[index]);
        }

        void HandleKeyDown(KeyboardKeyEventArgs e)
        {
            switch (e.Key)
            {
                case Keys.Escape:
                    Close();
                    break;
                case Keys.Up:
                    _cursor = Math.Max(0, _cursor - 1);
                    EnsureCursorVisible();
                    break;
                case Keys.Down:
                    _cursor = Math.Min(Math.Max(0, _rows.Count - 1), _cursor + 1);
                    EnsureCursorVisible();
                    break;
                case Keys.Enter:
                case Keys.KeyPadEnter:
                case Keys.Space:
                    Toggle(_cursor);
                    break;
            }
        }

        void HandleMouseDown(MouseButtonEventArgs e)
        {
            if (e.Button != MouseButton.Left)
                return;

            float y = MousePosition.Y;
            if (y < HeaderHeight)
                return;

            int index = (int)((y - HeaderHeight + _scroll) / RowHeight);
            if (index < 0 || index >= _rows.Count)
                return;

            _cursor = index;
            Toggle(index);
        }

        void HandleMouseWheel(MouseWheelEventArgs e)
        {
            _scroll = Math.Clamp(_scroll - e.OffsetY * RowHeight, 0, MaxScroll(ClientSize.Y));
        }
    }
}
