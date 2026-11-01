using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using ImageMagick;
using ImageMagick.Configuration;
using Photos.Core;
using SkiaSharp;

namespace Photos.Desktop
{
    internal class DesktopImageLoader
    {
        static void LoadMagic(FullImageData data)
        {
            using var image = new MagickImage();
            MagickReadSettings settings = null;
            if (Path.GetExtension(data.FullPath)?.ToLowerInvariant() == ".svg")
                settings = new MagickReadSettings { Format = MagickFormat.Msvg };
            if (data.Directory.SourceId == 0)
                image.Read(data.FullPath, settings);
            else
                image.Read(data.Data.Span);
            data.Width = (int)image.Width;
            data.Height = (int)image.Height;

            using var unsafeBuf = image.GetPixelsUnsafe();
            var buf = unsafeBuf.ToByteArray(PixelMapping.RGBA);
            data.Bitmap = new SKBitmap((int)image.Width, (int)image.Height, SKColorType.Rgba8888, image.HasAlpha ? SKAlphaType.Unpremul : SKAlphaType.Opaque);
            var pix = data.Bitmap.GetPixels(out var len);
            Marshal.Copy(buf, 0, pix, (int)len);
        }

        static bool _registered;
        internal static readonly string[] KnownEndings = new[] { ".tif", ".tiff", ".svg" };

        internal static void Register()
        {
            if (!_registered)
            {
                _registered = true;
                var configFiles = ConfigurationFiles.Default;
                configFiles.Policy.Data = """
                <policymap>
                <policy domain="resource" name="disk" value="0"/>
                <policy domain="resource" name="map" value="0"/>
                <policy domain="resource" name="memory" value="1GiB"/>
                <policy domain="resource" name="area" value="1GiB"/>
                </policymap>
                """;

                //MagickNET.SetLogEvents(LogEvents.Resource); // LogEvents.Cache | LogEvents.Blob | 
                MagickNET.Initialize(configFiles);

                ImageLoader.ImageLoaderList.Add(LoadMagic);
            }
        }
    }
}
