﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Roadie.Library.Caching;
using Roadie.Library.Logging;
using Roadie.Library.Utility;
using Roadie.Library.Factories;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Roadie.Library.MetaData.ID3Tags;

namespace Roadie.Library.FilePlugins
{
    public abstract class PluginBase : IFilePlugin
    {
        protected readonly IConfiguration _configuration = null;
        protected readonly ICacheManager _cacheManager = null;
        protected readonly ILogger _loggingService = null;
        protected readonly ArtistFactory _artistFactory = null;
        protected readonly ReleaseFactory _releaseFactory = null;
        protected readonly ImageFactory _imageFactory = null;
        protected ID3TagsHelper _id3TagsHelper = null;
        protected Audio _audioPlugin = null;

        protected IConfiguration Configuration
        {
            get
            {
                return this._configuration;
            }
        }

        protected ICacheManager CacheManager
        {
            get
            {
                return this._cacheManager;
            }
        }

        protected ILogger Logger
        {
            get
            {
                return this._loggingService;
            }
        }

        protected ArtistFactory ArtistFactory
        {
            get
            {
                return this._artistFactory;
            }
        }

        protected ImageFactory ImageFactory
        {
            get
            {
                return this._imageFactory;
            }
        }

        protected ReleaseFactory ReleaseFactory
        {
            get
            {
                return this._releaseFactory;
            }
        }

        protected ID3TagsHelper ID3TagsHelper
        {
            get
            {
                return this._id3TagsHelper ?? (this._id3TagsHelper = new ID3TagsHelper(this.Configuration, this.CacheManager, this.Logger));
            }
            set
            {
                this._id3TagsHelper = value;
            }
        }

        protected Audio AudioPlugin
        {
            get
            {
                return this._audioPlugin ?? (this._audioPlugin = new Audio(this.ArtistFactory, this.ReleaseFactory, this.ImageFactory, this.CacheManager, this.Logger));
            }
            set
            {
                this._audioPlugin = value;
            }
        }

        public PluginBase(IConfiguration configuration, ArtistFactory artistFactory, ReleaseFactory releaseFactory, ImageFactory imageFactory, ICacheManager cacheManager, ILogger logger)
        {
            this._configuration = configuration;
            this._artistFactory = artistFactory;
            this._releaseFactory = releaseFactory;
            this._imageFactory = imageFactory;
            this._cacheManager = cacheManager;
            this._loggingService = logger;
        }

        public abstract string[] HandlesTypes { get; }
        public abstract Task<OperationResult<bool>> Process(string destinationRoot, FileInfo fileInfo, bool doJustInfo, int? submissionId);

        /// <summary>
        /// Check if exists if not make given folder
        /// </summary>
        /// <param name="folder">Folder To Check</param>
        /// <returns>False if Exists, True if Made</returns>
        public static bool CheckMakeFolder(string folder)
        {
            SimpleContract.Requires<ArgumentException>(!string.IsNullOrEmpty(folder), "Invalid Folder");

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                Trace.WriteLine(string.Format("Created Directory [{0}]", folder));
                return true;
            }
            return false;
        }

        public int MinWeightToDelete
        {
            get
            {
                return SafeParser.ToNumber<int>(this.Configuration.GetValue<int>("FilePlugins:MinWeightToDelete", 0));
            }
        }

        protected virtual bool IsFileLocked(FileInfo file)
        {
            FileStream stream = null;

            try
            {
                stream = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                return true;
            }
            finally
            {
                if (stream != null)
                    stream.Close();
            }

            //file is not locked
            return false;
        }
    }
}
