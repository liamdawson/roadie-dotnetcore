﻿using Roadie.Library.Caching;
using Roadie.Library.Imaging;
using Roadie.Library.Extensions;
using Roadie.Library.Processors;
using Roadie.Library.Utility;
using Roadie.Library.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Roadie.Library.Data;
using Microsoft.Extensions.Configuration;
using Roadie.Library.MetaData.Audio;

namespace Roadie.Library.Factories
{
    public sealed class ImageFactory : FactoryBase
    {
        private readonly ImageProcessor _imageProcessor = null;

        private ImageProcessor ImageProcessor
        {
            get
            {
                return this._imageProcessor;
            }
        }

        public ImageFactory(IConfiguration configuration, IRoadieDbContext context, ICacheManager cacheManager, ILogger logger) : base(configuration, context, cacheManager, logger)
        {
            this._imageProcessor = new ImageProcessor();
        }

        /// <summary>
        /// Get image data from all sources for either fileanme or MetaData
        /// </summary>
        /// <param name="filename">Name of the File (ie a CUE file)</param>
        /// <param name="metaData">Populated MetaData</param>
        /// <returns></returns>
        public AudioMetaDataImage GetPictureForMetaData(string filename, AudioMetaData metaData)
        {
            SimpleContract.Requires<ArgumentException>(!string.IsNullOrEmpty(filename), "Invalid Filename");
            SimpleContract.Requires<ArgumentException>(metaData != null, "Invalid MetaData");

            return this.ImageForFilename(filename);

        }

        /// <summary>
        /// Does image exist with the same filename
        /// </summary>
        /// <param name="filename">Name of the File (ie a CUE file)</param>
        /// <returns>Null if not found else populated image</returns>
        public AudioMetaDataImage ImageForFilename(string filename)
        {
            AudioMetaDataImage imageMetaData = null;

            if(string.IsNullOrEmpty(filename))
            {
                return imageMetaData;
            }
            try
            {
                var fileInfo = new FileInfo(filename);
                var ReleaseCover = Path.ChangeExtension(filename, "jpg");
                if (File.Exists(ReleaseCover))
                {
                    using (var processor = new ImageProcessor())
                    {
                        imageMetaData = new AudioMetaDataImage
                        {
                            Data = processor.Process(File.ReadAllBytes(ReleaseCover)),
                            Type = AudioMetaDataImageType.FrontCover,
                            MimeType = FileProcessor.DetermineFileType(fileInfo)
                        };
                    }
                }
                else
                {
                    // Is there a picture in filename folder (for the Release)
                    var pictures = fileInfo.Directory.GetFiles("*.jpg");
                    var tagImages = new List<AudioMetaDataImage>();
                    if (pictures != null && pictures.Any())
                    {
                        FileInfo picture = null;
                        // See if there is a "cover" or "front" jpg file if so use it
                        picture = pictures.FirstOrDefault(x => x.Name.Equals("cover", StringComparison.OrdinalIgnoreCase));
                        if(picture == null)
                        {
                            picture = pictures.FirstOrDefault(x => x.Name.Equals("front", StringComparison.OrdinalIgnoreCase));
                        }
                        if (picture == null)
                        {
                            picture = pictures.First();
                        }
                        if (picture != null)
                        {
                            using (var processor = new ImageProcessor())
                            {
                                imageMetaData = new AudioMetaDataImage
                                {
                                    Data = processor.Process(File.ReadAllBytes(picture.FullName)),
                                    Type = AudioMetaDataImageType.FrontCover,
                                    MimeType = FileProcessor.DetermineFileType(picture)
                                };
                            }
                        }
                    }
                }

            }
            catch(System.IO.FileNotFoundException)
            {
            }
            catch (Exception ex)
            {
                this.Logger.Error(ex, ex.Serialize());
            }
            return imageMetaData;
        }

    }
}