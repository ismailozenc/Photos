using Photos.Core;
using System;
using System.Collections.Generic;

namespace Photos.Desktop
{
    /// <summary>
    /// Runs one slideshow window per display. It is driven from the main window's render loop,
    /// so every window is drawn on the same thread.
    /// </summary>
    internal sealed class SlideshowController
    {
        readonly PhotoProvider _provider;
        readonly Action _restoreMainContext;
        readonly List<SlideshowWindow> _windows = new List<SlideshowWindow>();

        public SlideshowController(PhotoProvider provider, Action restoreMainContext)
        {
            _provider = provider;
            _restoreMainContext = restoreMainContext;
        }

        public bool IsRunning => _windows.Count > 0;

        /// <summary>Raised when the last slideshow window closes on its own (not via Stop).</summary>
        public event Action Finished;

        public void Start()
        {
            if (IsRunning)
                return;
            if (_provider.DisplayedCount == 0)
            {
                Core.Utils.Log("Screen slideshow: no photos to show");
                return;
            }

            var displays = ScreenDetection.GetDisplayBounds();
            var starts = SlideshowPlanner.StartIndices(_provider.DisplayedCount, displays.Count);
            try
            {
                for (int i = 0; i < displays.Count; i++)
                {
                    var d = displays[i];
                    var window = new SlideshowWindow(_provider, d.X, d.Y, d.Width, d.Height, starts[i]);
                    _windows.Add(window); // added first, so Stop disposes it if Initialize fails
                    window.Initialize();
                }
            }
            catch (Exception ex)
            {
                Core.Utils.LogError(ex);
                Stop();
                return;
            }

            Core.Utils.Log($"Screen slideshow started on {displays.Count} display(s)");
            _restoreMainContext();
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
            Core.Utils.Log("Screen slideshow stopped");
        }

        /// <summary>Draws every slideshow window. Called once per frame of the main window.</summary>
        public void Tick()
        {
            bool hadWindows = _windows.Count > 0;
            for (int i = _windows.Count - 1; i >= 0; i--)
            {
                var window = _windows[i];
                try
                {
                    if (window.IsExiting)
                    {
                        window.Dispose();
                        _windows.RemoveAt(i);
                        continue;
                    }

                    window.RenderFrame();
                }
                catch (Exception ex)
                {
                    // One failing display must not stop the others or the library window.
                    Core.Utils.LogError(ex);
                    try { window.Dispose(); } catch { }
                    _windows.RemoveAt(i);
                }
            }

            _restoreMainContext();
            if (hadWindows && _windows.Count == 0)
                Finished?.Invoke();
        }
    }
}
