using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Photos.Desktop
{
    /// <summary>
    /// Decodes an image on a background thread and hands it over on the render thread.
    /// The previous image stays on screen until the new one is ready. Results of superseded
    /// requests are disposed when they finish.
    /// </summary>
    internal sealed class AsyncImage : IDisposable
    {
        Task<SKImage> _latest;
        readonly List<Task<SKImage>> _superseded = new List<Task<SKImage>>();

        /// <summary>The image on screen. Changes only in <see cref="Poll"/>.</summary>
        public SKImage Current { get; private set; }

        /// <summary>Starts producing the next image. A null result means no image.</summary>
        public void Request(Func<SKImage> produce)
        {
            if (_latest != null)
                _superseded.Add(_latest);
            _latest = Task.Run(produce);
        }

        /// <summary>Takes over a finished image and disposes superseded ones. Call on the render thread.</summary>
        public void Poll()
        {
            for (int i = _superseded.Count - 1; i >= 0; i--)
            {
                if (_superseded[i].IsCompleted)
                {
                    DisposeResult(_superseded[i]);
                    _superseded.RemoveAt(i);
                }
            }

            if (_latest != null && _latest.IsCompleted)
            {
                var done = _latest;
                _latest = null;
                if (done.Status == TaskStatus.RanToCompletion)
                {
                    Current?.Dispose();
                    Current = done.Result;
                }
            }
        }

        static void DisposeResult(Task<SKImage> task)
        {
            if (task.Status == TaskStatus.RanToCompletion)
                task.Result?.Dispose();
        }

        public void Dispose()
        {
            Current?.Dispose();
            Current = null;
            if (_latest != null)
            {
                _superseded.Add(_latest);
                _latest = null;
            }
            foreach (var task in _superseded)
                task.ContinueWith(DisposeResult);
            _superseded.Clear();
        }
    }
}
