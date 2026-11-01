using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Photos.Core
{
    /// <summary>
    /// Decodes images on a background thread and hands them over on the render thread. The image on screen stays
    /// until the next one is ready. A prefetched image is kept decoding for the photo most likely to be shown next.
    /// Results that are no longer needed are disposed when they finish.
    /// </summary>
    public sealed class AsyncImage : IDisposable
    {
        Task<SKImage> _latest;
        Task<SKImage> _prefetch;
        object _prefetchKey;
        readonly List<Task<SKImage>> _superseded = new List<Task<SKImage>>();

        /// <summary>The image on screen. Changes only in <see cref="Poll"/>.</summary>
        public SKImage Current { get; private set; }

        /// <summary>Starts producing the next image to show. A null result means no image.</summary>
        public void Request(Func<SKImage> produce)
        {
            if (_latest != null)
                _superseded.Add(_latest);
            _latest = Task.Run(produce);
        }

        /// <summary>Makes a prefetched image the next one to show. Returns false when that image was not prefetched.</summary>
        public bool TryPromote(object key)
        {
            if (!IsPrefetching(key))
                return false;
            if (_latest != null)
                _superseded.Add(_latest);
            _latest = _prefetch;
            _prefetch = null;
            _prefetchKey = null;
            return true;
        }

        /// <summary>True when the image for this key is already being decoded.</summary>
        public bool IsPrefetching(object key) => _prefetch != null && Equals(_prefetchKey, key);

        /// <summary>Starts decoding an image that is likely to be shown next.</summary>
        public void Prefetch(object key, Func<SKImage> produce)
        {
            if (_prefetch != null)
                _superseded.Add(_prefetch);
            _prefetchKey = key;
            _prefetch = Task.Run(produce);
        }

        /// <summary>The decode step for a loaded file. The file data is disposed when the step has run.</summary>
        public static Func<SKImage> Decoder(FullImageData data, int side) => () =>
        {
            if (data == null)
                return null;
            using (data)
            {
                ImageLoader.DecodeAndResizeImage(data, ImageLoader.ScaleToMinSide(side, side));
                return data.Bitmap == null ? null : SKImage.FromBitmap(data.Bitmap);
            }
        };

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
                else if (done.Exception != null)
                {
                    Utils.LogError(done.Exception); // the image is left out, but the failure is recorded
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
                _superseded.Add(_latest);
            if (_prefetch != null)
                _superseded.Add(_prefetch);
            _latest = null;
            _prefetch = null;
            _prefetchKey = null;
            foreach (var task in _superseded)
                task.ContinueWith(DisposeResult);
            _superseded.Clear();
        }
    }
}
