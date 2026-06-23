using System;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace Lunex.Services
{
    public static class ImageHelper
    {
        // Magic bytes for common image formats
        private static readonly byte[] JpegMagic1 = { 0xFF, 0xD8, 0xFF };
        private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        private static readonly byte[] WebpMagic1 = { 0x52, 0x49, 0x46, 0x46 }; // RIFF
        private static readonly byte[] WebpMagic2 = { 0x57, 0x45, 0x42, 0x50 }; // WEBP

        /// <summary>
        /// Reads the magic bytes of a file to verify if it is a supported image (JPEG, PNG, WEBP).
        /// </summary>
        public static bool VerifyImageMimeType(string filePath)
        {
            if (!File.Exists(filePath)) return false;

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                if (fs.Length < 12) return false;

                byte[] header = new byte[12];
                fs.Read(header, 0, 12);

                if (header.Take(3).SequenceEqual(JpegMagic1)) return true;
                if (header.Take(8).SequenceEqual(PngMagic)) return true;
                if (header.Take(4).SequenceEqual(WebpMagic1) && header.Skip(8).Take(4).SequenceEqual(WebpMagic2)) return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Resizes and compresses an image to a JPEG byte array.
        /// </summary>
        public static byte[] CompressAndResizeImage(string filePath, int maxWidth = 512, int maxHeight = 512, int qualityLevel = 80)
        {
            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.UriSource = new Uri(filePath);
            bitmapImage.EndInit();
            bitmapImage.Freeze();

            int decodeWidth = bitmapImage.PixelWidth;
            int decodeHeight = bitmapImage.PixelHeight;

            if (decodeWidth > maxWidth || decodeHeight > maxHeight)
            {
                double ratioX = (double)maxWidth / decodeWidth;
                double ratioY = (double)maxHeight / decodeHeight;
                double ratio = Math.Min(ratioX, ratioY);

                decodeWidth = (int)(decodeWidth * ratio);
                decodeHeight = (int)(decodeHeight * ratio);
            }

            var scaledBitmap = new TransformedBitmap(bitmapImage, new System.Windows.Media.ScaleTransform(
                (double)decodeWidth / bitmapImage.PixelWidth,
                (double)decodeHeight / bitmapImage.PixelHeight));

            var encoder = new JpegBitmapEncoder
            {
                QualityLevel = qualityLevel
            };
            
            encoder.Frames.Add(BitmapFrame.Create(scaledBitmap));

            using var memoryStream = new MemoryStream();
            encoder.Save(memoryStream);
            return memoryStream.ToArray();
        }
    }
}
