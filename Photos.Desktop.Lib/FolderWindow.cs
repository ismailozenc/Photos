using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Photos.Core;
using Photos.Lib;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Photos.Desktop
{
    /// <summary>
    /// Folder list on the portrait display. Each row shows the folder's shown count and its total of
    /// known images on disk. Images that are not shown (not displayable, or private while locked) are
    /// listed in colour. Clicking a folder, or pressing Enter, toggles the main window's folder filter.
    /// Right-click marks a folder private (recursively), unlocks private folders, or unmarks them.
    /// </summary>
    internal sealed class FolderWindow : SkiaWindow
    {
        const float HeaderHeight = 48;
        const float RowHeight = 34;
        const float Padding = 14;
        const float IndentStep = 16;
        const float MenuWidth = 280;
        const int RefreshMs = 250;

        readonly PhotoProvider _provider;
        readonly Stopwatch _refreshTimer = Stopwatch.StartNew();
        FileFilterGroup _group;
        List<FileFilter> _rows = new List<FileFilter>();
        int _cursor;
        float _scroll;
        readonly Dictionary<string, int> _totals = new Dictionary<string, int>(); // -1 = unknown
        Task<Dictionary<string, int>> _countTask;

        bool _menuOpen;
        float _menuX, _menuY;
        List<(string Text, Action Run)> _menuItems = new List<(string Text, Action Run)>();

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

            if (_countTask != null && _countTask.IsCompleted)
            {
                foreach (var pair in _countTask.Result)
                    _totals[pair.Key] = pair.Value;
                _countTask = null;
            }

            UpdateRootTotals();

            if (_countTask == null)
            {
                var pending = new List<(string Id, string Dir)>();
                foreach (var row in _rows)
                {
                    if (_totals.ContainsKey(row.Id))
                        continue;
                    var dir = LocalPath(row.Id);
                    if (dir == null)
                        _totals[row.Id] = -1;
                    else
                        pending.Add((row.Id, dir));
                }
                if (pending.Count > 0)
                    _countTask = Task.Run(() => CountImageFiles(pending));
            }
        }

        protected override void DrawFrame(SKCanvas canvas, int width, int height)
        {
            canvas.Clear(SKColors.Black);

            using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            using var textPaint = new SKPaint { Color = SKColors.White.WithAlpha(220), IsAntialias = true };
            using var activePaint = new SKPaint { Color = SKColors.LightGreen, IsAntialias = true };
            using var privatePaint = new SKPaint { Color = SKColors.Orange, IsAntialias = true };
            using var countPaint = new SKPaint { Color = SKColors.Gray, IsAntialias = true };
            using var hiddenPaint = new SKPaint { Color = SKColors.Orange, IsAntialias = true };
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

                bool isPrivate = IsPrivateRow(row.Id);
                bool active = _group != null && _provider.FilterService.IsFilterActive(_group, row);
                float baseline = top + RowHeight * 0.7f;
                var namePaint = isPrivate ? privatePaint : active ? activePaint : textPaint;
                canvas.DrawText(name, Padding + depth * IndentStep, baseline, font, namePaint);

                // shown / total known images; the part that is not shown is drawn in colour
                int shown = row.Count;
                bool hasTotal = _totals.TryGetValue(row.Id, out int total) && total >= shown;
                string shownText = hasTotal ? $"{shown}/{total}" : shown.ToString();
                float right = width - Padding;
                canvas.DrawText(shownText, right, baseline, SKTextAlign.Right, font, countPaint);
                if (hasTotal && total > shown)
                {
                    float shownWidth = font.MeasureText(shownText);
                    canvas.DrawText($"{total - shown} not shown   ", right - shownWidth, baseline, SKTextAlign.Right, font, hiddenPaint);
                }
            }

            canvas.Restore();

            if (_menuOpen)
                DrawMenu(canvas, font);
        }

        void DrawMenu(SKCanvas canvas, SKFont font)
        {
            float height = _menuItems.Count * RowHeight + 8;
            var rect = SKRect.Create(_menuX, _menuY, MenuWidth, height);

            using var back = new SKPaint { Color = SKColor.Parse("#F0202020") };
            using var border = new SKPaint { Color = SKColors.Gray, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            using var text = new SKPaint { Color = SKColors.White, IsAntialias = true };
            using var disabled = new SKPaint { Color = SKColors.Gray, IsAntialias = true };

            canvas.DrawRect(rect, back);
            canvas.DrawRect(rect, border);
            for (int i = 0; i < _menuItems.Count; i++)
            {
                float baseline = _menuY + 4 + i * RowHeight + RowHeight * 0.7f;
                canvas.DrawText(_menuItems[i].Text, _menuX + 12, baseline, font, _menuItems[i].Run == null ? disabled : text);
            }
        }

        /// <summary>Folder path for a local folder row, or null for other sources and the source root.</summary>
        static string LocalPath(string id)
        {
            int slash = id.IndexOf('/');
            if (slash <= 0 || !int.TryParse(id.AsSpan(0, slash), out int sourceId) || sourceId != 0)
                return null;
            var path = id[(slash + 1)..];
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }

        static bool IsRootRow(string id) => id.EndsWith('/');

        static int SourceOf(string id)
        {
            int slash = id.IndexOf('/');
            return slash > 0 && int.TryParse(id.AsSpan(0, slash), out int source) ? source : -1;
        }

        static bool IsTopLevel(string text)
        {
            var rel = text.Trim().Trim('\\', '/');
            return rel.Length > 0 && rel.IndexOfAny(new[] { '\\', '/' }) < 0;
        }

        /// <summary>A local source's root row covers every folder under it, so its total is the sum of its top-level folders.</summary>
        void UpdateRootTotals()
        {
            foreach (var root in _rows.Where(r => IsRootRow(r.Id) && SourceOf(r.Id) == 0))
            {
                var tops = _rows.Where(r => !IsRootRow(r.Id) && SourceOf(r.Id) == 0 && IsTopLevel(r.Text))
                    .Select(r => _totals.TryGetValue(r.Id, out int t) ? t : -1).ToList();
                _totals[root.Id] = tops.Count > 0 && tops.All(t => t >= 0) ? tops.Sum() : -1;
            }
        }

        /// <summary>Counts every file with a known image extension in each folder, including subfolders.</summary>
        static Dictionary<string, int> CountImageFiles(List<(string Id, string Dir)> dirs)
        {
            var result = new Dictionary<string, int>();
            foreach (var item in dirs)
            {
                int count = -1; // stays -1 if the folder cannot be read
                try
                {
                    count = 0;
                    if (Directory.Exists(item.Dir))
                        foreach (var file in Directory.EnumerateFiles(item.Dir, "*", SearchOption.AllDirectories))
                            if (ImageLoader.KnownEndings.Contains(Path.GetExtension(file).ToLowerInvariant()))
                                count++;
                }
                catch (Exception) { }
                result[item.Id] = count;
            }
            return result;
        }

        bool IsPrivateRow(string id) => PrivateFolderRules.IsPrivate(_provider.PrivateFolderIds, id);

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

        int RowAt(float y)
        {
            if (y < HeaderHeight)
                return -1;
            int index = (int)((y - HeaderHeight + _scroll) / RowHeight);
            return index >= 0 && index < _rows.Count ? index : -1;
        }

        int MenuItemAt(float x, float y)
        {
            float height = _menuItems.Count * RowHeight + 8;
            if (x < _menuX || x > _menuX + MenuWidth || y < _menuY + 4 || y > _menuY + height - 4)
                return -1;
            int index = (int)((y - _menuY - 4) / RowHeight);
            return index >= 0 && index < _menuItems.Count ? index : -1;
        }

        void OpenMenu(float x, float y)
        {
            int index = RowAt(y);
            if (index < 0)
                return;

            _cursor = index;
            _menuItems = BuildMenu(_rows[index]);
            float height = _menuItems.Count * RowHeight + 8;
            _menuX = Math.Max(0, Math.Min(x, ClientSize.X - MenuWidth));
            _menuY = Math.Max(0, Math.Min(y, ClientSize.Y - height));
            _menuOpen = true;
        }

        List<(string Text, Action Run)> BuildMenu(FileFilter row)
        {
            var items = new List<(string Text, Action Run)>();
            string id = row.Id;
            bool isPrivate = IsPrivateRow(id);
            bool isMarked = _provider.PrivateFolderIds.Contains(id, StringComparer.OrdinalIgnoreCase);

            void Add(string text, Action run) => items.Add((text, run));

            if (isPrivate && !_provider.PrivateUnlocked)
                Add("Unlock private folders...", UnlockFromWindow);
            else if (!isPrivate)
                Add("Mark as private (recursive)", () => _provider.SetFolderPrivate(id, true));
            else if (isMarked)
                Add("Unmark private (recursive)", () => _provider.SetFolderPrivate(id, false));
            else
                Add("Private through a parent folder", null);

            return items;
        }

        void UnlockFromWindow()
        {
            var password = NativeDialogs.PasswordPrompt?.Invoke("Enter password for private folders");
            if (password == null)
                return; // cancelled, or no password prompt on this platform
            if (!_provider.TryUnlockPrivate(password))
                NativeDialogs.Show?.Invoke(new[] { "Wrong password" }, new[] { new MessageBoxButton() { Text = "OK", Action = () => { } } });
        }

        void HandleKeyDown(KeyboardKeyEventArgs e)
        {
            switch (e.Key)
            {
                case Keys.Escape:
                    if (_menuOpen)
                        _menuOpen = false;
                    else
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
            float x = MousePosition.X;
            float y = MousePosition.Y;

            if (_menuOpen)
            {
                _menuOpen = false;
                int item = MenuItemAt(x, y);
                if (item >= 0 && _menuItems[item].Run != null)
                    _menuItems[item].Run();
                return;
            }

            if (e.Button == MouseButton.Right)
            {
                OpenMenu(x, y);
                return;
            }
            if (e.Button != MouseButton.Left)
                return;

            int index = RowAt(y);
            if (index < 0)
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
