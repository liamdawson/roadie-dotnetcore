﻿using Roadie.Library.SearchEngines.Imaging;
using Roadie.Library.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Roadie.Library.Imaging
{
    public static class ImageHelper
    {
        public static byte[] ConvertToJpegFormat(byte[] imageBytes)
        {
            if (imageBytes == null)
            {
                return null;
            }
            using (MemoryStream outStream = new MemoryStream())
            {
                IImageFormat imageFormat = null;
                using (Image<Rgba32> image = Image.Load(imageBytes, out imageFormat))
                {
                    image.Save(outStream, ImageFormats.Jpeg);
                }
                return outStream.ToArray();
            }
        }

        public static string[] GetFiles(string path, string[] patterns = null, SearchOption options = SearchOption.TopDirectoryOnly)
        {
            if (patterns == null || patterns.Length == 0)
            {
                return Directory.GetFiles(path, "*", options);
            }
            if (patterns.Length == 1)
            {
                return Directory.GetFiles(path, patterns[0], options);
            }
            return patterns.SelectMany(pattern => Directory.GetFiles(path, pattern, options)).Distinct().ToArray();
        }

        public static string[] ImageExtensions()
        {
            return new string[8] { "*.bmp", "*.jpeg", "*.jpe", "*.jpg", "*.png", "*.gif", "*.tif", "*.tiff" };
        }

        public static string[] ImageFilesInFolder(string folder)
        {
            return ImageHelper.GetFiles(folder, ImageHelper.ImageExtensions());
        }

        public static string[] ImageMimeTypes()
        {
            return new string[5] { "image/bmp", "image/jpeg", "image/png", "image/gif", "image/tiff" };
        }

        public static ImageSearchResult ImageSearchResultForImageUrl(string imageUrl)
        {
            if (!WebHelper.IsStringUrl(imageUrl))
            {
                return null;
            }
            var result = new ImageSearchResult();
            var imageBytes = WebHelper.BytesForImageUrl(imageUrl);
            IImageFormat imageFormat = null;
            using (Image<Rgba32> image = Image.Load(imageBytes, out imageFormat))
            {
                result.Height = image.Height.ToString();
                result.Width = image.Width.ToString();
                result.MediaUrl = imageUrl;
            }
            return result;
        }

        /// <summary>
        /// Resize a given image to given dimensions
        /// </summary>
        public static byte[] ResizeImage(byte[] imageBytes, int width, int height)
        {
            using (MemoryStream outStream = new MemoryStream())
            {
                IImageFormat imageFormat = null;
                using (Image<Rgba32> image = Image.Load(imageBytes, out imageFormat))
                {
                    image.Mutate(ctx => ctx.Resize(width, height));
                    image.Save(outStream, imageFormat);
                }
                return outStream.ToArray();
            }
        }
    }
}