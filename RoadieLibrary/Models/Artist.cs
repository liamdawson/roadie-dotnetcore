﻿using Newtonsoft.Json;
using Roadie.Library.Models.Releases;
using Roadie.Library.Models.Statistics;
using Roadie.Library.Models.Users;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Roadie.Library.Models
{
    [Serializable]
    public class Artist : EntityModelBase
    {
        public IEnumerable<ReleaseList> ArtistContributionReleases;
        public IEnumerable<LabelList> ArtistLabels;

        [MaxLength(100)]
        public string AmgId { get; set; }

        public string ArtistType { get; set; }

        public IEnumerable<DataToken> AssociatedArtists { get; set; }
        public IEnumerable<DataToken> AssociatedWithArtists { get; set; }
        public string BandStatus { get; set; }

        [MaxLength(65535)]
        public string BioContext { get; set; }

        public DateTime? BirthDate { get; set; }

        [MaxLength(50)]
        public string DiscogsId { get; set; }

        public IEnumerable<DataToken> Genres { get; set; }

        public IEnumerable<Image> Images { get; set; }

        [MaxLength(65535)]
        [JsonIgnore]
        [IgnoreDataMember]
        public string ISNIList { get; set; }

        [JsonProperty("isniList")]
        public IEnumerable<string> ISNIListList
        {
            get
            {
                if (string.IsNullOrEmpty(this.ISNIList))
                {
                    return null;
                }
                return this.ISNIList.Split('|');
            }
        }

        [MaxLength(100)]
        public string ITunesId { get; set; }

        [MaxLength(100)]
        public string MusicBrainzId { get; set; }

        [MaxLength(250)]
        public string Name { get; set; }

        [MaxLength(65535)]
        public string Profile { get; set; }

        public short? Rating { get; set; }

        [MaxLength(500)]
        public string RealName { get; set; }

        public IEnumerable<Releases.ReleaseList> Releases { get; set; }

        [MaxLength(100)]
        public string SpotifyId { get; set; }

        public CollectionStatistics Statistics { get; set; }
        public Image Thumbnail { get; set; }

        public string Tooltip
        {
            get
            {
                return this.Name;
            }
        }

        public UserArtist UserArtist { get; set; }

        public Artist()
        {
            this.BandStatus = "1";
        }
    }
}