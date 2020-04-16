using System.Drawing;
using System.Drawing.Imaging;

namespace BufferDemo.Extensions
{
    public static class ImageExtensions
    {
        public static double CalculateAverageLightness(this Image image)
        {
            var lightness = 0d;

            using var tmpBmp = new Bitmap(image);

            var width = image.Width;
            var height = image.Height;
            var bppModifier = image.PixelFormat == PixelFormat.Format24bppRgb ? 3 : 4;

            var srcData = tmpBmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, image.PixelFormat);
            var stride = srcData.Stride;
            var scan0 = srcData.Scan0;

            //Luminance (standard, objective): (0.2126*R) + (0.7152*G) + (0.0722*B)
            //Luminance (perceived option 1): (0.299*R + 0.587*G + 0.114*B)
            //Luminance (perceived option 2, slower to calculate): sqrt( 0.241*R^2 + 0.691*G^2 + 0.068*B^2 )

            unsafe
            {
                var p = (byte*)(void*)scan0;
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var idx = (y * stride) + x * bppModifier;
                        lightness += (0.299 * p[idx + 2] + 0.587 * p[idx + 1] + 0.114 * p[idx]);
                    }
                }
            }

            tmpBmp.UnlockBits(srcData);

            var avgLum = lightness / (width * height);

            return avgLum / 255.0;
        }
    }
}
