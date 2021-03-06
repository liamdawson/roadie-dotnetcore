﻿using Microsoft.Extensions.Logging;
using Roadie.Library.Caching;
using Roadie.Library.Configuration;
using Roadie.Library.Data;
using Roadie.Library.Encoding;
using Roadie.Library.Engines;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Roadie.Library.Factories
{
    public class PlaylistFactory : FactoryBase, IPlaylistFactory
    {
        public PlaylistFactory(IRoadieSettings configuration, IHttpEncoder httpEncoder, IRoadieDbContext context, ICacheManager cacheManager, ILogger logger, IArtistLookupEngine artistLookupEngine, IReleaseLookupEngine releaseLookupEngine)
            : base(configuration, context, cacheManager, logger, httpEncoder, artistLookupEngine, releaseLookupEngine)
        {
        }

        [Obsolete("Use PlaylistService")]
        public async Task<OperationResult<bool>> AddTracksToPlaylist(Playlist playlist, IEnumerable<Guid> trackIds)
        {
            var sw = new Stopwatch();
            sw.Start();

            var result = false;
            var now = DateTime.UtcNow;

            var existingTracksForPlaylist = (from plt in this.DbContext.PlaylistTracks
                                             join t in this.DbContext.Tracks on plt.TrackId equals t.Id
                                             where plt.PlayListId == playlist.Id
                                             select t);
            var newTracksForPlaylist = (from t in this.DbContext.Tracks
                                        where (from x in trackIds select x).Contains(t.RoadieId)
                                        where !(from x in existingTracksForPlaylist select x.RoadieId).Contains(t.RoadieId)
                                        select t).ToArray();
            foreach (var newTrackForPlaylist in newTracksForPlaylist)
            {
                this.DbContext.PlaylistTracks.Add(new PlaylistTrack
                {
                    TrackId = newTrackForPlaylist.Id,
                    PlayListId = playlist.Id,
                    CreatedDate = now
                });
            }
            playlist.LastUpdated = now;
            await this.DbContext.SaveChangesAsync();
            result = true;

            var r = await this.ReorderPlaylist(playlist);
            result = result && r.IsSuccess;

            return new OperationResult<bool>
            {
                Data = result
            };
        }

        [Obsolete("Use PlaylistService")]
        public async Task<OperationResult<bool>> ReorderPlaylist(Playlist playlist)
        {
            var sw = new Stopwatch();
            sw.Start();

            var result = false;
            var now = DateTime.UtcNow;

            if (playlist != null)
            {
                var looper = 0;
                foreach (var playlistTrack in this.DbContext.PlaylistTracks.Where(x => x.PlayListId == playlist.Id).OrderBy(x => x.CreatedDate))
                {
                    looper++;
                    playlistTrack.ListNumber = looper;
                    playlistTrack.LastUpdated = now;
                }
                await this.DbContext.SaveChangesAsync();
                result = true;
            }

            return new OperationResult<bool>
            {
                IsSuccess = result,
                Data = result
            };
        }
    }
}