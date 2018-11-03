﻿using System.Collections.Generic;

namespace Roadie.Library.Setttings
{
    public interface IRoadieSettings
    {
        List<ApiKey> ApiKeys { get; set; }
        Dictionary<string, List<string>> ArtistNameReplace { get; set; }
        Converting Converting { get; set; }
        string DefaultTimeZone { get; set; }
        string DiagnosticsPassword { get; set; }
        IEnumerable<string> DontDoMetaDataProvidersSearchArtists { get; set; }
        IEnumerable<string> FileExtensionsToDelete { get; set; }
        string InboundFolder { get; set; }
        Integrations Integrations { get; set; }
        string LibraryFolder { get; set; }
        Processing Processing { get; set; }
        bool RecordNoResultSearches { get; set; }
        RedisCache Redis { get; set; }
        string SecretKey { get; set; }
        string SingleArtistHoldingFolder { get; set; }
        string SiteName { get; set; }
        string SmtpFromAddress { get; set; }
        string SmtpHost { get; set; }
        string SmtpPassword { get; set; }
        int SmtpPort { get; set; }
        string SmtpUsername { get; set; }
        bool SmtpUseSSl { get; set; }
        Thumbnails Thumbnails { get; set; }
        Dictionary<string, string> TrackPathReplace { get; set; }
        bool UseSSLBehindProxy { get; set; }
        string WebsocketAddress { get; set; }

        void MergeWith(RoadieSettings secondary);
    }
}