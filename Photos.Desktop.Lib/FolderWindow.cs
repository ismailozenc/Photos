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

        readonly PhotoProvider _provider;
        int _rowsVersion = -1;
        FileFilterGroup _group;
        List<FileFilter> _rows = new List<FileFilter>();
        int _cursor;
        float _scroll;
        readonly Dictionary<string, int> _totals = new Dictionary<string, int>(); // -1 = unknown
        Task<Dictionary<string, int>> _countTask;
        readonly HashSet<string> _collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<FileFilter> _visible = new List<FileFilter>();
        HashSet<string> _parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int _deletedCount;

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
            if (_rowsVersion != _provider.DisplayVersion)
            {
                _rowsVersion = _provider.DisplayVersion;
                _group = _provider.FilterService.FilterGroups.FirstOrDefault(g => g.GroupId == FilterService.FolderFilter);
                _rows = _group?.Filters.Values.OrderBy(f => f.PreText).ThenBy(f => f.Text).ToList() ?? new List<FileFilter>();
                _deletedCount = _provider.DeletedCopyCount;
                RebuildVisible();
                _cursor = Math.Clamp(_cursor, 0, Math.Max(0, _visible.Count - 1));
                _scroll = Math.Clamp(_scroll, 0, MaxScroll(ClientSize.Y));
            }

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
            using var folderPaint = new SKPaint { Color = SKColor.Parse("#4D4CAF50") };
            using var excludedPaint = new SKPaint { Color = SKColors.Gray, IsAntialias = true };
            using var font = new SKFont(SKTypeface.Default, 18);
            using var titleFont = new SKFont(SKTypeface.Default, 20);
            using var focusPaint = new SKPaint { Color = SKColors.LightGreen, Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };

            canvas.DrawText("Folders", Padding, 32, SKTextAlign.Left, titleFont, titlePaint);
            if (IsFocused)
                canvas.DrawRect(SKRect.Create(1, 1, width - 2, height - 2), focusPaint);
            else
                canvas.DrawText("click to use keys", width - Padding, 32, SKTextAlign.Right, font, countPaint);

            if (_rows.Count == 0)
            {
                canvas.DrawText("No folders yet. Add a folder to the library.", Padding, HeaderHeight + 24, SKTextAlign.Left, font, countPaint);
                return;
            }

            canvas.Save();
            canvas.ClipRect(SKRect.Create(0, HeaderHeight, width, height - HeaderHeight));

            for (int i = 0; i < _visible.Count; i++)
            {
                float top = HeaderHeight + i * RowHeight - _scroll;
                if (top + RowHeight < HeaderHeight || top > height)
                    continue;

                var row = _visible[i];
                bool selectedFolder = IsSelectedFolder(row);
                if (selectedFolder)
                    canvas.DrawRect(SKRect.Create(0, top, width, RowHeight), folderPaint);
                if (i == _cursor)
                    canvas.DrawRect(SKRect.Create(0, top, width, RowHeight), selectionPaint);

                string rel = row.Text.Trim().Trim('\\', '/');
                int depth = rel.Count(c => c == '\\' || c == '/');
                string name = rel.Length == 0 ? "<root>" : rel.Split('\\', '/').Last();
                if (_provider.TrashFolderIds.Contains(row.Id))
                    name += $" ({_deletedCount})";

                bool isPrivate = IsPrivateRow(row.Id);
                bool active = _provider.TrashFolderIds.Contains(row.Id) ? _provider.ShowDeleted : _group != null && _provider.FilterService.IsFilterActive(_group, row);
                float baseline = top + RowHeight * 0.7f;
                bool excluded = _provider.IsExcludedFolder(row.Id);
                if (excluded)
                    name += " (hidden)";
                bool allHidden = row.Count == 0 && !_provider.TrashFolderIds.Contains(row.Id);
                if (allHidden)
                    name += " (all hidden)";
                var namePaint = isPrivate ? privatePaint : excluded || allHidden ? excludedPaint : active ? activePaint : textPaint;
                float indent = Padding + depth * IndentStep;
                if (_parents.Contains(row.Id))
                {
                    canvas.DrawText(_collapsed.Contains(row.Id) ? "+" : "-", indent, baseline, SKTextAlign.Left, font, countPaint);
                    indent += IndentStep;
                }
                canvas.DrawText(name, indent, baseline, SKTextAlign.Left, font, namePaint);

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
                canvas.DrawText(_menuItems[i].Text, _menuX + 12, baseline, SKTextAlign.Left, font, _menuItems[i].Run == null ? disabled : text);
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

        /// <summary>
        /// Counts the known image files under each listed folder, including subfolders. Each top-level folder is
        /// walked once, and every file is added to each listed folder above it, so a deep library is read once.
        /// </summary>
        static Dictionary<string, int> CountImageFiles(List<(string Id, string Dir)> dirs)
        {
            var result = new Dictionary<string, int>();
            var idByDir = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in dirs)
            {
                result[item.Id] = 0;
                idByDir[item.Dir.TrimEnd('\\', '/')] = item.Id;
            }

            var roots = idByDir.Keys.Where(d => !idByDir.Keys.Any(o => o != d && IsInside(d, o))).ToList();
            foreach (var root in roots)
            {
                try
                {
                    if (!Directory.Exists(root))
                        continue;
                    foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    {
                        if (!ImageLoader.KnownEndings.Contains(Path.GetExtension(file).ToLowerInvariant()))
                            continue;
                        for (var dir = Path.GetDirectoryName(file); dir != null && dir.Length >= root.Length; dir = Path.GetDirectoryName(dir))
                            if (idByDir.TryGetValue(dir, out var id))
                                result[id]++;
                    }
                }
                catch (Exception)
                {
                    // unreadable folder: every listed folder under it shows as unknown
                    foreach (var pair in idByDir)
                        if (pair.Key == root || IsInside(pair.Key, root))
                            result[pair.Value] = -1;
                }
            }
            return result;
        }

        static bool IsInside(string dir, string parent) =>
            dir.Length > parent.Length && dir.StartsWith(parent, StringComparison.OrdinalIgnoreCase) && (dir[parent.Length] == '\\' || dir[parent.Length] == '/');

        /// <summary>True when the folder is highlighted: its filter is on, or it is the deleted-images folder while that view is open.</summary>
        bool IsSelectedFolder(FileFilter row) => _provider.TrashFolderIds.Contains(row.Id)
            ? _provider.ShowDeleted
            : _group != null && _provider.FilterService.IsFilterActive(_group, row);

        bool IsPrivateRow(string id) => PrivateFolderRules.IsPrivate(_provider.PrivateFolderIds, id);

        /// <summary>The rows that are shown: a folder under a collapsed folder is hidden. Also finds the folders that have subfolders.</summary>
        void RebuildVisible()
        {
            _parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in _rows)
                if (_rows.Any(other => IsChildOf(other.Id, row.Id)))
                    _parents.Add(row.Id);
            _visible = _rows.Where(r => !_collapsed.Any(c => IsChildOf(r.Id, c))).ToList();
        }

        /// <summary>True when the folder id lies inside the parent folder. A source root ("0/") holds every folder of that source.</summary>
        static bool IsChildOf(string id, string parent)
        {
            if (id.Length <= parent.Length || !id.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
                return false;
            return parent.EndsWith('/') || id[parent.Length] == '\\' || id[parent.Length] == '/';
        }

        static int DepthOf(FileFilter row) => row.Text.Trim().Trim('\\', '/').Count(c => c == '\\' || c == '/');

        float MaxScroll(int clientHeight) => Math.Max(0, _visible.Count * RowHeight - (clientHeight - HeaderHeight));

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
            if (index < 0 || index >= _visible.Count)
                return;
            var row = _visible[index];
            if (_provider.TrashFolderIds.Contains(row.Id))
            {
                _provider.SetShowDeleted(!_provider.ShowDeleted);
                return;
            }
            if (_group == null || row.Count == 0)
                return; // every photo in this folder is hidden, so there is nothing to show
            _provider.FilterService.ToggleFilters(_group, row);
        }

        int RowAt(float y)
        {
            if (y < HeaderHeight)
                return -1;
            int index = (int)((y - HeaderHeight + _scroll) / RowHeight);
            return index >= 0 && index < _visible.Count ? index : -1;
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
            _menuItems = BuildMenu(_visible[index]);
            float height = _menuItems.Count * RowHeight + 8;
            _menuX = Math.Max(0, Math.Min(x, ClientSize.X - MenuWidth));
            _menuY = Math.Max(0, Math.Min(y, ClientSize.Y - height));
            _menuOpen = true;
        }

        /// <summary>
        /// Menu for a right-click. If the clicked folder is highlighted, every highlighted folder is acted on;
        /// otherwise only the clicked folder is.
        /// </summary>
        List<(string Text, Action Run)> BuildMenu(FileFilter clicked)
        {
            var items = new List<(string Text, Action Run)>();
            void Add(string text, Action run) => items.Add((text, run));

            var highlighted = _rows.Where(IsSelectedFolder).ToList();
            var targets = highlighted.Contains(clicked) ? highlighted : new List<FileFilter> { clicked };
            var ids = targets.Select(t => t.Id).ToList();
            bool several = ids.Count > 1;

            var privateIds = ids.Where(IsPrivateRow).ToList();
            var toMark = ids.Where(id => !IsPrivateRow(id)).ToList();
            var directMarked = ids.Where(id => _provider.PrivateFolderIds.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();

            if (privateIds.Count > 0 && !_provider.PrivateUnlocked)
                Add("Unlock private folders...", UnlockFromWindow);
            if (toMark.Count > 0)
                Add(several ? $"Mark {toMark.Count} folders as private (recursive)" : "Mark as private (recursive)",
                    () => _provider.SetFoldersPrivate(toMark, true));
            if (directMarked.Count > 0 && _provider.PrivateUnlocked)
                Add(several ? $"Unmark {directMarked.Count} folders as private" : "Unmark private (recursive)",
                    () => _provider.SetFoldersPrivate(directMarked, false));
            if (!several && privateIds.Count == 1 && directMarked.Count == 0 && _provider.PrivateUnlocked)
                Add("Private through a parent folder", null);

            bool allHidden = ids.All(_provider.IsExcludedFolder);
            string hideText = allHidden
                ? (several ? $"Show {ids.Count} folders in browse and slideshows" : "Show in browse and slideshows")
                : (several ? $"Hide {ids.Count} folders from browse and slideshows" : "Hide from browse and slideshows (this folder only)");
            Add(hideText, () => _provider.SetFoldersExcluded(ids, !allHidden));

            return items;
        }

        void UnlockFromWindow()
        {
            NativeDialogs.PasswordPrompt?.Invoke("Enter password for private folders", password =>
            {
                if (password == null)
                    return; // cancelled
                if (!_provider.TryUnlockPrivate(password))
                    NativeDialogs.Show?.Invoke(new[] { "Wrong password" }, new[] { new MessageBoxButton() { Text = "OK", Action = () => { } } });
            });
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
                    _cursor = Math.Min(Math.Max(0, _visible.Count - 1), _cursor + 1);
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
            var row = _visible[index];
            float arrowX = Padding + DepthOf(row) * IndentStep;
            if (_parents.Contains(row.Id) && x >= arrowX - 4 && x <= arrowX + IndentStep)
            {
                if (!_collapsed.Remove(row.Id))
                    _collapsed.Add(row.Id);
                RebuildVisible();
                _cursor = Math.Clamp(_cursor, 0, Math.Max(0, _visible.Count - 1));
                return;
            }
            Toggle(index);
        }

        void HandleMouseWheel(MouseWheelEventArgs e)
        {
            _scroll = Math.Clamp(_scroll - e.OffsetY * RowHeight, 0, MaxScroll(ClientSize.Y));
        }
    }
}
