using Photos.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Photos.Desktop
{
    /// <summary>
    /// Opens the folder list and the preview on their displays and draws them from the main window's
    /// render loop, so every window is drawn on the same thread as the library.
    /// A failing window is logged and removed without stopping the others or the main window.
    /// </summary>
    internal sealed class MonitorLayout
    {
        readonly PhotoProvider _provider;
        readonly Action _restoreMainContext;
        readonly List<SkiaWindow> _windows = new List<SkiaWindow>();

        public MonitorLayout(PhotoProvider provider, Action restoreMainContext)
        {
            _provider = provider;
            _restoreMainContext = restoreMainContext;
            Roles = MonitorRoles.Assign(ScreenDetection.GetDisplayBounds());
        }

        public MonitorRoles Roles { get; }

        public bool IsRunning => _windows.Count > 0;

        public void Start()
        {
            if (IsRunning)
                return;

            if (Roles.Folders is Rectangle folders)
                TryAdd(new FolderWindow(_provider, folders));
            if (Roles.Preview is Rectangle preview)
                TryAdd(new PreviewWindow(_provider, preview));

            _restoreMainContext();
            Core.Utils.Log($"Monitor layout started: {_windows.Count} window(s)");
        }

        void TryAdd(SkiaWindow window)
        {
            _windows.Add(window); // added first, so Stop disposes it if Initialize fails
            try
            {
                window.Initialize();
            }
            catch (Exception ex)
            {
                Core.Utils.LogError(ex);
                _windows.Remove(window);
                try { window.Dispose(); } catch { }
            }
        }

        bool _previewHiddenForPhoto;
        PreviewWindow _hiddenPreview;

        /// <summary>Closes the preview because a photo opened in the main window. It comes back when browse is shown again.</summary>
        public void HidePreviewForPhoto()
        {
            foreach (var window in _windows)
                if (window is PreviewWindow preview && !preview.IsExiting)
                {
                    _previewHiddenForPhoto = true;
                    _hiddenPreview = preview;
                    preview.Close(); // removed and disposed on the next Tick
                }
        }

        /// <summary>Brings the preview back after it was hidden for a photo. A preview closed with Escape stays closed.</summary>
        public void ShowPreviewIfHidden()
        {
            if (!_previewHiddenForPhoto)
                return;
            _previewHiddenForPhoto = false;

            if (IsRunning && Roles.Preview is Rectangle preview && !_windows.Any(w => w is PreviewWindow p && !p.IsExiting))
            {
                TryAdd(new PreviewWindow(_provider, preview));
                _restoreMainContext();
            }
        }
        public void Stop()
        {
            if (_windows.Count == 0)
                return;

            foreach (var window in _windows)
            {
                try
                {
                    window.Dispose();
                }
                catch (Exception ex)
                {
                    Core.Utils.LogError(ex);
                }
            }
            _windows.Clear();
            _restoreMainContext();
            Core.Utils.Log("Monitor layout stopped");
        }

        /// <summary>Draws every role window. Called once per frame of the main window.</summary>
        public void Tick()
        {
            for (int i = _windows.Count - 1; i >= 0; i--)
            {
                var window = _windows[i];
                try
                {
                    if (window.IsExiting)
                    {
                        if (window is PreviewWindow && window != _hiddenPreview)
                            _previewHiddenForPhoto = false; // closed with Escape: do not bring it back
                        window.Dispose();
                        _windows.RemoveAt(i);
                        continue;
                    }

                    window.DrawAndPresent();
                }
                catch (Exception ex)
                {
                    Core.Utils.LogError(ex);
                    try { window.Dispose(); } catch { }
                    _windows.RemoveAt(i);
                }
            }

            _restoreMainContext();
        }
    }
}
