﻿using Roadie.Library.Imaging;
using System;
using System.Linq;

namespace Roadie.Library.Data
{
    public partial class Image
    {
        public string Etag
        {
            get
            {
                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    return String.Concat(md5.ComputeHash(System.Text.Encoding.Default.GetBytes(string.Format("{0}{1}", this.RoadieId, this.LastUpdated))).Select(x => x.ToString("D2")));
                }
            }
        }

        public string GenerateSignature()
        {
            if (this.Bytes == null || !this.Bytes.Any())
            {
                return null;
            }
            return ImageHasher.AverageHash(this.Bytes).ToString();
        }
    }
}