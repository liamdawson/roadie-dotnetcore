﻿using Mapster;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Roadie.Library.Caching;
using Roadie.Library.Configuration;
using Roadie.Library.Encoding;
using Roadie.Library.Extensions;
using Roadie.Library.Identity;
using Roadie.Library.Models;
using Roadie.Library.Models.Releases;
using Roadie.Library.Models.Users;
using Roadie.Library.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using data = Roadie.Library.Data;
using subsonic = Roadie.Library.Models.ThirdPartyApi.Subsonic;

namespace Roadie.Api.Services
{
    /// <summary>
    /// Subsonic API emulator for Roadie. Enables Subsonic clients to work with Roadie.
    /// <seealso cref="http://www.subsonic.org/pages/inc/api/schema/subsonic-rest-api-1.16.1.xsd">
    /// <seealso cref="http://www.subsonic.org/pages/api.jsp#getIndexes"/>
    /// <seealso cref="https://www.reddit.com/r/subsonic/comments/7c2n6j/database_table_schema/"/>
    /// <!-- Generated the classes from the schema above using 'xsd subsonic-rest-api-1.16.1.xsd /c /f /n:Roadie.Library.Models.Subsonic' from Visual Studio Command Prompt -->
    /// </summary>
    public class SubsonicService : ServiceBase, ISubsonicService
    {
        public const string SubsonicVersion = "1.16.1";

        private IArtistService ArtistService { get; }
        private IBookmarkService BookmarkService { get; }
        private ICollectionService CollectionService { get; }
        private IImageService ImageService { get; }
        private IPlayActivityService PlayActivityService { get; }
        private IPlaylistService PlaylistService { get; }
        private IReleaseService ReleaseService { get; }
        private ITrackService TrackService { get; }
        private UserManager<ApplicationUser> UserManger { get; }

        public SubsonicService(IRoadieSettings configuration,
                             IHttpEncoder httpEncoder,
                             IHttpContext httpContext,
                             data.IRoadieDbContext context,
                             ICacheManager cacheManager,
                             ILogger<SubsonicService> logger,
                             IArtistService artistService,
                             ITrackService trackService,
                             ICollectionService collectionService,
                             IPlaylistService playlistService,
                             IReleaseService releaseService,
                             IImageService imageService,
                             IBookmarkService bookmarkService,
                             IPlayActivityService playActivityService,
                             UserManager<ApplicationUser> userManager
                             )
            : base(configuration, httpEncoder, context, cacheManager, logger, httpContext)
        {
            this.ArtistService = artistService;
            this.BookmarkService = bookmarkService;
            this.CollectionService = collectionService;
            this.ImageService = imageService;
            this.PlaylistService = playlistService;
            this.PlayActivityService = playActivityService;
            this.ReleaseService = releaseService;
            this.TrackService = trackService;
            this.UserManger = userManager;
        }

        /// <summary>
        /// Adds a message to the chat log.
        /// </summary>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> AddChatMessage(subsonic.Request request, User roadieUser)
        {
            if (string.IsNullOrEmpty(request.Message))
            {
                return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.RequiredParameterMissing, $"Message is required");
            }
            var chatMessage = new data.ChatMessage
            {
                UserId = roadieUser.Id.Value,
                Message = request.Message
            };
            this.DbContext.ChatMessages.Add(chatMessage);
            await this.DbContext.SaveChangesAsync();

            return new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok
                }
            };
        }

        /// <summary>
        /// Authenticate the given credentials and return the corresponding ApplicationUser
        /// </summary>
        public async Task<subsonic.SubsonicOperationResult<subsonic.SubsonicAuthenticateResponse>> Authenticate(subsonic.Request request)
        {
            if (request == null || string.IsNullOrEmpty(request?.u))
            {
                return new subsonic.SubsonicOperationResult<subsonic.SubsonicAuthenticateResponse>(subsonic.ErrorCodes.WrongUsernameOrPassword, $"Unknown Username");
            }
            try
            {
                var user = this.DbContext.Users
                                    .FirstOrDefault(x => x.UserName == request.u);
                if (user == null)
                {
                    this.Logger.LogInformation($"Unknown User [{ request.u }]");
                    return new subsonic.SubsonicOperationResult<subsonic.SubsonicAuthenticateResponse>(subsonic.ErrorCodes.WrongUsernameOrPassword, $"Unknown Username");
                }
                var password = request.Password;
                if (!string.IsNullOrEmpty(request.s))
                {
                    try
                    {
                        var token = HashHelper.MD5Hash((user.ApiToken ?? user.Email) + request.s);
                        if (!token.Equals(request.t, StringComparison.OrdinalIgnoreCase))
                        {
                            user = null;
                        }
                    }
                    catch
                    {
                    }
                }
                if (user != null && !string.IsNullOrEmpty(user.PasswordHash) && !string.IsNullOrEmpty(password))
                {
                    try
                    {
                        var hashCheck = this.UserManger.PasswordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
                        if (hashCheck == PasswordVerificationResult.Failed)
                        {
                            user = null;
                        }
                    }
                    catch
                    {
                    }
                }
                if (user != null)
                {
                    var now = DateTime.UtcNow;
                    user.LastUpdated = now;
                    user.LastApiAccess = now;
                    await this.DbContext.SaveChangesAsync();
                }
                if (user == null)
                {
                    this.Logger.LogInformation($"Invalid Credentials given for User [{ request.u }]");
                    return new subsonic.SubsonicOperationResult<subsonic.SubsonicAuthenticateResponse>(subsonic.ErrorCodes.WrongUsernameOrPassword, $"Unknown Username");
                }
                this.Logger.LogInformation($"Subsonic: Successfully Authenticated User [{ user.ToString() }] via Application [{ request.c }], Application Version [{ request.v }]");
                return new subsonic.SubsonicOperationResult<subsonic.SubsonicAuthenticateResponse>
                {
                    IsSuccess = true,
                    Data = new subsonic.SubsonicAuthenticateResponse
                    {
                        User = user
                    }
                };
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Subsonic.Authenticate, Error CheckPassword [" + JsonConvert.SerializeObject(request) + "]");
            }
            return null;
        }

        /// <summary>
        /// Creates or updates a bookmark (a position within a media file). Bookmarks are personal and not visible to other users.
        /// </summary>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> CreateBookmark(subsonic.Request request, User roadieUser, int position, string comment)
        {
            if (!request.TrackId.HasValue)
            {
                return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Track Id [{ request.id }]");
            }
            var track = this.GetTrack(request.TrackId.Value);
            if (track == null)
            {
                return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Track Id [{ request.TrackId.Value }]");
            }
            var userBookmark = this.DbContext.Bookmarks.FirstOrDefault(x => x.UserId == roadieUser.Id && x.BookmarkTargetId == track.Id && x.BookmarkType == Library.Enums.BookmarkType.Track);
            var createdBookmark = false;
            if (userBookmark == null)
            {
                userBookmark = new data.Bookmark
                {
                    BookmarkTargetId = track.Id,
                    BookmarkType = Library.Enums.BookmarkType.Track,
                    UserId = roadieUser.Id.Value,
                    Comment = comment,
                    Position = position
                };
                this.DbContext.Bookmarks.Add(userBookmark);
                createdBookmark = true;
            }
            else
            {
                userBookmark.LastUpdated = DateTime.UtcNow;
                userBookmark.Position = position;
                userBookmark.Comment = comment;
            }
            await this.DbContext.SaveChangesAsync();

            var user = this.GetUser(roadieUser.UserId);
            this.CacheManager.ClearRegion(user.CacheRegion);

            this.Logger.LogInformation($"{ (createdBookmark ? "Created" : "Updated") } Bookmark `{ userBookmark}` for User `{ roadieUser }`");
            return new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok
                }
            };
        }

        /// <summary>
        /// Creates (or updates) a playlist.
        /// </summary>
        /// <param name="request">Populated Subsonic Request</param>
        /// <param name="roadieUser">Populated Roadie User</param>
        /// <param name="name">The human-readable name of the playlist.</param>
        /// <param name="songIds">ID of a song in the playlist. Use one songId parameter for each song in the playlist.</param>
        /// <param name="playlistId">The playlist ID. (if updating else blank is adding)</param>
        /// <returns></returns>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> CreatePlaylist(subsonic.Request request, User roadieUser, string name, string[] songIds, string playlistId = null)
        {
            data.Playlist playlist = null;

            Guid?[] songRoadieIds = new Guid?[0];
            IQueryable<data.Track> submittedTracks = new data.Track[0].AsQueryable();

            if (songIds != null && songIds.Any())
            {
                songRoadieIds = songIds.Select(x => SafeParser.ToGuid(x)).ToArray();
                // Add (if not already) given tracks to Playlist
                submittedTracks = (from t in this.DbContext.Tracks
                                   where songRoadieIds.Contains(t.RoadieId)
                                   select t);
            }
            var didCreate = false;
            if (!string.IsNullOrEmpty(playlistId))
            {
                request.id = playlistId;
                playlist = this.DbContext.Playlists.Include(x => x.Tracks).FirstOrDefault(x => x.RoadieId == request.PlaylistId);
                if (playlist == null)
                {
                    return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid PlaylistId [{ playlistId }]");
                }
                // When Create is called again on an existing delete all existing tracks and add given
                if (playlist.Tracks != null && playlist.Tracks.Any())
                {
                    this.DbContext.PlaylistTracks.RemoveRange(playlist.Tracks);
                }
                var listNumber = playlist.Tracks != null && playlist.Tracks.Any() ? playlist.Tracks?.Max(x => x.ListNumber) ?? 0 : 0;
                foreach (var submittedTrack in submittedTracks)
                {
                    if (playlist.Tracks == null || !playlist.Tracks.Any(x => x.TrackId == submittedTrack.Id))
                    {
                        listNumber++;
                        this.DbContext.PlaylistTracks.Add(new data.PlaylistTrack
                        {
                            PlayListId = playlist.Id,
                            ListNumber = listNumber,
                            TrackId = submittedTrack.Id
                        });
                    }
                }
                playlist.Name = name ?? playlist.Name;
                playlist.LastUpdated = DateTime.UtcNow;
            }
            else
            {
                var tracks = new List<data.PlaylistTrack>();
                var listNumber = 0;
                foreach (var submittedTrack in submittedTracks)
                {
                    listNumber++;
                    tracks.Add(new data.PlaylistTrack
                    {
                        PlayListId = playlist.Id,
                        ListNumber = listNumber,
                        TrackId = submittedTrack.Id
                    });
                }

                playlist = new data.Playlist
                {
                    IsPublic = false,
                    Name = name,
                    UserId = roadieUser.Id,
                    Tracks = tracks
                };
                didCreate = true;
                this.DbContext.Playlists.Add(playlist);
            }
            await this.DbContext.SaveChangesAsync();
            this.Logger.LogInformation($"Subsonic: User `{ roadieUser }` { (didCreate ? "created" : "modified") } Playlist `{ playlist }` added [{ songRoadieIds.Count() }] Tracks.");
            request.id = subsonic.Request.PlaylistdIdentifier + playlist.RoadieId.ToString();
            return await this.GetPlaylist(request, roadieUser);
        }

        /// <summary>
        /// Deletes the bookmark for a given file.
        /// </summary>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> DeleteBookmark(subsonic.Request request, User roadieUser)
        {
            if (!request.TrackId.HasValue)
            {
                return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Track Id [{ request.id }]");
            }
            var track = this.GetTrack(request.TrackId.Value);
            if (track == null)
            {
                return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Track Id [{ request.TrackId.Value }]");
            }
            var userBookmark = this.DbContext.Bookmarks.FirstOrDefault(x => x.UserId == roadieUser.Id && x.BookmarkTargetId == track.Id && x.BookmarkType == Library.Enums.BookmarkType.Track);
            if (userBookmark != null)
            {
                this.DbContext.Bookmarks.Remove(userBookmark);
                await this.DbContext.SaveChangesAsync();

                var user = this.GetUser(roadieUser.UserId);
                this.CacheManager.ClearRegion(user.CacheRegion);

                this.Logger.LogInformation($"Subsonic: Deleted Bookmark `{ userBookmark}` for User `{ roadieUser }`");
            }
            return new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok
                }
            };
        }

        /// <summary>
        /// Deletes a saved playlist.
        /// </summary>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> DeletePlaylist(subsonic.Request request, User roadieUser)
        {
            if (!request.PlaylistId.HasValue)
            {
                return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Playlist Id [{ request.id }]");
            }
            var playlist = this.GetPlaylist(request.PlaylistId.Value);
            if (playlist == null)
            {
                return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Playlist Id [{ request.TrackId.Value }]");
            }
            if (playlist.UserId != roadieUser.Id && !roadieUser.IsAdmin)
            {
                return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.UserIsNotAuthorizedForGivenOperation, "User is not allowed to delete playlist.");
            }
            this.DbContext.Playlists.Remove(playlist);
            await this.DbContext.SaveChangesAsync();

            var user = this.GetUser(roadieUser.UserId);
            this.CacheManager.ClearRegion(user.CacheRegion);

            this.Logger.LogInformation($"Subsonic: Deleted Playlist `{ playlist}` for User `{ roadieUser }`");

            return new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok
                }
            };
        }

        /// <summary>
        /// Returns details for an album, including a list of songs. This method organizes music according to ID3 tags.
        /// </summary>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetAlbum(subsonic.Request request, User roadieUser)
        {
            try
            {
                var releaseId = SafeParser.ToGuid(request.id);
                if (!releaseId.HasValue)
                {
                    return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Release [{ request.ReleaseId}]");
                }
                var release = this.GetRelease(releaseId.Value);
                if (release == null)
                {
                    return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Release [{ request.ReleaseId}]");
                }
                var trackPagedRequest = request.PagedRequest;
                trackPagedRequest.Sort = "TrackNumber";
                trackPagedRequest.Order = "ASC";
                var releaseTracks = await this.TrackService.List(trackPagedRequest, roadieUser, false, releaseId);
                var userRelease = roadieUser == null ? null : this.DbContext.UserReleases.FirstOrDefault(x => x.ReleaseId == release.Id && x.UserId == roadieUser.Id);
                var genre = release.Genres.FirstOrDefault();
                return new subsonic.SubsonicOperationResult<subsonic.Response>
                {
                    IsSuccess = true,
                    Data = new subsonic.Response
                    {
                        version = SubsonicService.SubsonicVersion,
                        status = subsonic.ResponseStatus.ok,
                        ItemElementName = subsonic.ItemChoiceType.album,
                        Item = new subsonic.AlbumWithSongsID3
                        {
                            artist = release.Artist.Name,
                            artistId = subsonic.Request.ArtistIdIdentifier + release.Artist.RoadieId.ToString(),
                            coverArt = subsonic.Request.ReleaseIdIdentifier + release.RoadieId.ToString(),
                            created = release.CreatedDate,
                            duration = release.Duration.ToSecondsFromMilliseconds(),
                            genre = genre == null ? null : genre.Genre.Name,
                            id = subsonic.Request.ReleaseIdIdentifier + release.RoadieId.ToString(),
                            name = release.Title,
                            playCount = releaseTracks.Rows.Sum(x => x.PlayedCount) ?? 0,
                            playCountSpecified = releaseTracks.Rows.Any(),
                            songCount = releaseTracks.Rows.Count(),
                            starred = userRelease?.LastUpdated ?? userRelease?.CreatedDate ?? DateTime.UtcNow,
                            starredSpecified = userRelease?.IsFavorite ?? false,
                            year = release.ReleaseDate != null ? release.ReleaseDate.Value.Year : 0,
                            yearSpecified = release.ReleaseDate != null,
                            song = this.SubsonicChildrenForTracks(releaseTracks.Rows)
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "GetAlbum Request [{0}], User [{1}]", JsonConvert.SerializeObject(request), roadieUser.ToString());
            }
            return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Release [{ request.ReleaseId}]");
        }

        /// <summary>
        /// Returns album notes, image URLs etc, using data from last.fm.
        /// </summary>
        public Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetAlbumInfo(subsonic.Request request, User roadieUser, subsonic.AlbumInfoVersion version)
        {
            var releaseId = SafeParser.ToGuid(request.id);
            if (!releaseId.HasValue)
            {
                return Task.FromResult(new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Release [{ request.id }]"));
            }
            var release = this.GetRelease(releaseId.Value);
            if (release == null)
            {
                return Task.FromResult(new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Release [{ request.id }]"));
            }
            switch (version)
            {
                case subsonic.AlbumInfoVersion.One:
                case subsonic.AlbumInfoVersion.Two:
                    return Task.FromResult(new subsonic.SubsonicOperationResult<subsonic.Response>
                    {
                        IsSuccess = true,
                        Data = new subsonic.Response
                        {
                            version = SubsonicService.SubsonicVersion,
                            status = subsonic.ResponseStatus.ok,
                            ItemElementName = subsonic.ItemChoiceType.albumInfo,
                            Item = new subsonic.AlbumInfo
                            {
                                largeImageUrl = this.MakeImage(release.RoadieId, "release", this.Configuration.LargeImageSize).Url,
                                mediumImageUrl = this.MakeImage(release.RoadieId, "release", this.Configuration.MediumImageSize).Url,
                                smallImageUrl = this.MakeImage(release.RoadieId, "release", this.Configuration.SmallImageSize).Url,
                                lastFmUrl = this.MakeLastFmUrl(release.Artist.Name, release.Title),
                                musicBrainzId = release.MusicBrainzId,
                                notes = release.Profile
                            }
                        }
                    });

                default:
                    return Task.FromResult(new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.IncompatibleServerRestProtocolVersion, $"Unknown Album Info Version [{ request.Type}]"));
            }
        }

        /// <summary>
        /// Returns a list of random, newest, highest rated etc. albums. Similar to the album lists on the home page of the Subsonic web interface.
        /// </summary>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetAlbumList(subsonic.Request request, User roadieUser, subsonic.AlbumListVersions version)
        {
            var releaseResult = new Library.Models.Pagination.PagedResult<ReleaseList>();

            switch (request.Type)
            {
                case subsonic.ListType.Random:
                    releaseResult = await this.ReleaseService.List(roadieUser, request.PagedRequest, true);
                    break;

                case subsonic.ListType.Highest:
                case subsonic.ListType.Recent:
                case subsonic.ListType.Newest:
                case subsonic.ListType.Frequent:
                case subsonic.ListType.AlphabeticalByName:
                case subsonic.ListType.AlphabeticalByArtist:
                case subsonic.ListType.Starred:
                case subsonic.ListType.ByGenre:
                case subsonic.ListType.ByYear:
                    releaseResult = await this.ReleaseService.List(roadieUser, request.PagedRequest);
                    break;

                default:
                    return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.IncompatibleServerRestProtocolVersion, $"Unknown Album List Type [{ request.Type}]");
            }

            if (!releaseResult.IsSuccess)
            {
                return new subsonic.SubsonicOperationResult<subsonic.Response>(releaseResult.Message);
            }

            switch (version)
            {
                case subsonic.AlbumListVersions.One:
                    return new subsonic.SubsonicOperationResult<subsonic.Response>
                    {
                        IsSuccess = true,
                        Data = new subsonic.Response
                        {
                            version = SubsonicService.SubsonicVersion,
                            status = subsonic.ResponseStatus.ok,
                            ItemElementName = subsonic.ItemChoiceType.albumList,
                            Item = new subsonic.AlbumList
                            {
                                album = this.SubsonicChildrenForReleases(releaseResult.Rows, null)
                            }
                        }
                    };

                case subsonic.AlbumListVersions.Two:
                    return new subsonic.SubsonicOperationResult<subsonic.Response>
                    {
                        IsSuccess = true,
                        Data = new subsonic.Response
                        {
                            version = SubsonicService.SubsonicVersion,
                            status = subsonic.ResponseStatus.ok,
                            ItemElementName = subsonic.ItemChoiceType.albumList2,
                            Item = new subsonic.AlbumList2
                            {
                                album = this.SubsonicAlbumID3ForReleases(releaseResult.Rows)
                            }
                        }
                    };

                default:
                    return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.IncompatibleServerRestProtocolVersion, $"Unknown AlbumListVersions [{ version }]");
            }
        }

        /// <summary>
        /// Returns details for an artist, including a list of albums. This method organizes music according to ID3 tags.
        /// </summary>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetArtist(subsonic.Request request, User roadieUser)
        {
            var artistId = SafeParser.ToGuid(request.id);
            if (!artistId.HasValue)
            {
                return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Release [{ request.id }]");
            }
            var pagedRequest = request.PagedRequest;
            pagedRequest.Sort = "Id";
            pagedRequest.FilterToArtistId = artistId.Value;
            var artistResult = await this.ArtistService.List(roadieUser, pagedRequest);
            var artist = artistResult.Rows.Any() ? artistResult.Rows.First() : null;
            if (artist == null)
            {
                return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Release [{ request.id }]");
            }
            var artistReleaseResult = await this.ReleaseService.List(roadieUser, pagedRequest);
            return new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok,
                    ItemElementName = subsonic.ItemChoiceType.artist,
                    Item = this.SubsonicArtistWithAlbumsID3ForArtist(artist, this.SubsonicAlbumID3ForReleases(artistReleaseResult.Rows))
                }
            };
        }

        /// <summary>
        /// Returns artist info with biography, image URLs and similar artists, using data from last.fm.
        /// </summary>
        public Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetArtistInfo(subsonic.Request request, int? count, bool includeNotPresent, subsonic.ArtistInfoVersion version)
        {
            var artistId = SafeParser.ToGuid(request.id);
            if (!artistId.HasValue)
            {
                return Task.FromResult(new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid ArtistId [{ request.id }]"));
            }
            var artist = this.GetArtist(artistId.Value);
            if (artist == null)
            {
                return Task.FromResult(new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid ArtistId [{ request.id }]"));
            }

            switch (version)
            {
                case subsonic.ArtistInfoVersion.One:
                    return Task.FromResult(new subsonic.SubsonicOperationResult<subsonic.Response>
                    {
                        IsSuccess = true,
                        Data = new subsonic.Response
                        {
                            version = SubsonicService.SubsonicVersion,
                            status = subsonic.ResponseStatus.ok,
                            ItemElementName = subsonic.ItemChoiceType.artistInfo,
                            Item = this.SubsonicArtistInfoForArtist(artist)
                        }
                    });

                case subsonic.ArtistInfoVersion.Two:
                    return Task.FromResult(new subsonic.SubsonicOperationResult<subsonic.Response>
                    {
                        IsSuccess = true,
                        Data = new subsonic.Response
                        {
                            version = SubsonicService.SubsonicVersion,
                            status = subsonic.ResponseStatus.ok,
                            ItemElementName = subsonic.ItemChoiceType.artistInfo2,
                            Item = this.SubsonicArtistInfo2InfoForArtist(artist)
                        }
                    });

                default:
                    return Task.FromResult(new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.IncompatibleServerRestProtocolVersion, $"Unknown ArtistInfoVersion [{ version }]"));
            }
        }

        /// <summary>
        /// Similar to getIndexes, but organizes music according to ID3 tags.
        /// </summary>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetArtists(subsonic.Request request, User roadieUser)
        {
            var cacheKey = $"urn:subsonic_artists:{ roadieUser.UserName }";
            return await this.CacheManager.GetAsync<subsonic.SubsonicOperationResult<subsonic.Response>>(cacheKey, async () =>
            {
                return await this.GetArtistsAction(request, roadieUser);
            }, CacheManagerBase.SystemCacheRegionUrn);
        }

        /// <summary>
        /// Returns all bookmarks for this user. A bookmark is a position within a certain media file.
        /// </summary>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetBookmarks(subsonic.Request request, User roadieUser)
        {
            var pagedRequest = request.PagedRequest;
            pagedRequest.Sort = "LastUpdated";
            pagedRequest.Order = "DESC";
            var userBookmarkResult = await this.BookmarkService.List(roadieUser, pagedRequest, false, Library.Enums.BookmarkType.Track);
            pagedRequest.FilterToTrackIds = userBookmarkResult.Rows.Select(x => SafeParser.ToGuid(x.Bookmark.Value)).ToArray();
            var trackListResult = await this.TrackService.List(pagedRequest, roadieUser);
            return new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok,
                    ItemElementName = subsonic.ItemChoiceType.bookmarks,
                    Item = new subsonic.Bookmarks
                    {
                        bookmark = this.SubsonicBookmarksForBookmarks(userBookmarkResult.Rows, trackListResult.Rows)
                    }
                }
            };
        }

        /// <summary>
        /// Returns the current visible (non-expired) chat messages.
        /// </summary>
        public Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetChatMessages(subsonic.Request request, User roadieUser, long? since)
        {
            DateTime? messagesSince = since.HasValue ? (DateTime?)since.Value.FromUnixTime() : null;
            var chatMessages = (from cm in this.DbContext.ChatMessages
                                join u in this.DbContext.Users on cm.UserId equals u.Id
                                where messagesSince == null || cm.CreatedDate >= messagesSince
                                where cm.Status != Library.Enums.Statuses.Deleted
                                orderby cm.CreatedDate descending
                                select new subsonic.ChatMessage
                                {
                                    message = cm.Message,
                                    username = u.UserName,
                                    time = cm.CreatedDate.ToUnixTime()
                                }).ToArray();
            return Task.FromResult(new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok,
                    ItemElementName = subsonic.ItemChoiceType.chatMessages,
                    Item = new subsonic.ChatMessages
                    {
                        chatMessage = chatMessages.ToArray()
                    }
                }
            });
        }

        /// <summary>
        /// Returns a cover art image.
        /// </summary>
        public async Task<subsonic.SubsonicFileOperationResult<Roadie.Library.Models.Image>> GetCoverArt(subsonic.Request request, int? size)
        {
            var sw = Stopwatch.StartNew();
            var result = new subsonic.SubsonicFileOperationResult<Roadie.Library.Models.Image>
            {
                Data = new Roadie.Library.Models.Image()
            };

            if (request.ArtistId != null)
            {
                var artistImage = await this.ImageService.ArtistImage(request.ArtistId.Value, size, size);
                if (!artistImage.IsSuccess)
                {
                    return artistImage.Adapt<subsonic.SubsonicFileOperationResult<Image>>();
                }
                result.Data.Bytes = artistImage.Data.Bytes;
            }
            else if (request.TrackId != null)
            {
                var trackimage = await this.ImageService.TrackImage(request.TrackId.Value, size, size);
                if (!trackimage.IsSuccess)
                {
                    return trackimage.Adapt<subsonic.SubsonicFileOperationResult<Image>>();
                }
                result.Data.Bytes = trackimage.Data.Bytes;
            }
            else if (request.CollectionId != null)
            {
                var collectionImage = await this.ImageService.CollectionImage(request.CollectionId.Value, size, size);
                if (!collectionImage.IsSuccess)
                {
                    return collectionImage.Adapt<subsonic.SubsonicFileOperationResult<Image>>();
                }
                result.Data.Bytes = collectionImage.Data.Bytes;
            }
            else if (request.ReleaseId != null)
            {
                var releaseimage = await this.ImageService.ReleaseImage(request.ReleaseId.Value, size, size);
                if (!releaseimage.IsSuccess)
                {
                    return releaseimage.Adapt<subsonic.SubsonicFileOperationResult<Image>>();
                }
                result.Data.Bytes = releaseimage.Data.Bytes;
            }
            else if (request.PlaylistId != null)
            {
                var playlistImage = await this.ImageService.PlaylistImage(request.PlaylistId.Value, size, size);
                if (!playlistImage.IsSuccess)
                {
                    return playlistImage.Adapt<subsonic.SubsonicFileOperationResult<Image>>();
                }
                result.Data.Bytes = playlistImage.Data.Bytes;
            }
            else if (!string.IsNullOrEmpty(request.u))
            {
                var user = this.GetUser(request.u);
                if (user == null)
                {
                    return new subsonic.SubsonicFileOperationResult<Roadie.Library.Models.Image>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Username [{ request.u}]");
                }
                var userImage = await this.ImageService.UserImage(user.RoadieId, size, size);
                if (!userImage.IsSuccess)
                {
                    return userImage.Adapt<subsonic.SubsonicFileOperationResult<Image>>();
                }
                result.Data.Bytes = userImage.Data.Bytes;
            }
            result.IsSuccess = result.Data.Bytes != null;
            sw.Stop();
            return new subsonic.SubsonicFileOperationResult<Roadie.Library.Models.Image>(result.Messages)
            {
                Data = result.Data,
                ETag = result.ETag,
                LastModified = result.LastModified,
                ContentType = "image/jpeg",
                Errors = result?.Errors,
                IsSuccess = result.IsSuccess,
                OperationTime = sw.ElapsedMilliseconds
            };
        }

        /// <summary>
        /// Returns all genres
        /// </summary>
        public Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetGenres(subsonic.Request request)
        {
            var genres = (from g in this.DbContext.Genres
                          let albumCount = (from rg in this.DbContext.ReleaseGenres
                                            where rg.GenreId == g.Id
                                            select rg.Id).Count()
                          let songCount = (from rg in this.DbContext.ReleaseGenres
                                           join rm in this.DbContext.ReleaseMedias on rg.ReleaseId equals rm.ReleaseId
                                           join t in this.DbContext.Tracks on rm.ReleaseId equals t.ReleaseMediaId
                                           where rg.GenreId == g.Id
                                           select t.Id).Count()
                          select new subsonic.Genre
                          {
                              songCount = songCount,
                              albumCount = albumCount,
                              value = g.Name
                          }).OrderBy(x => x.value).ToArray();

            return Task.FromResult(new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok,
                    ItemElementName = subsonic.ItemChoiceType.genres,
                    Item = new subsonic.Genres
                    {
                        genre = genres.ToArray()
                    }
                }
            });
        }

        /// <summary>
        /// Returns an indexed structure of all artists.
        /// </summary>
        /// <param name="request">Query from application.</param>
        /// <param name="musicFolderId">If specified, only return artists in the music folder with the given ID.</param>
        /// <param name="ifModifiedSince">If specified, only return a result if the artist collection has changed since the given time (in milliseconds since 1 Jan 1970).</param>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetIndexes(subsonic.Request request, User roadieUser, long? ifModifiedSince = null)
        {
            var cacheKey = string.Format("urn:subsonic_indexes");
            return await this.CacheManager.GetAsync<subsonic.SubsonicOperationResult<subsonic.Response>>(cacheKey, async () =>
            {
                // Dont send the user to get index list as user data (likes, dislikes, etc.) aren't used in this list and dont need performance hit
                return await this.GetIndexesAction(request, null, ifModifiedSince);
            }, CacheManagerBase.SystemCacheRegionUrn);
        }

        /// <summary>
        /// Get details about the software license. Takes no extra parameters. Roadies gives everyone a premium 1 year license everytime they ask :)
        /// </summary>
        public subsonic.SubsonicOperationResult<subsonic.Response> GetLicense(subsonic.Request request)
        {
            return new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok,
                    ItemElementName = subsonic.ItemChoiceType.license,
                    Item = new subsonic.License
                    {
                        email = this.Configuration.SmtpFromAddress,
                        valid = true,
                        licenseExpires = DateTime.UtcNow.AddYears(1),
                        licenseExpiresSpecified = true
                    }
                }
            };
        }

        /// <summary>
        /// Searches for and returns lyrics for a given song
        /// </summary>
        public subsonic.SubsonicOperationResult<subsonic.Response> GetLyrics(subsonic.Request request, string artistId, string title)
        {
            return new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok,
                    ItemElementName = subsonic.ItemChoiceType.lyrics,
                    Item = new subsonic.Lyrics
                    {
                        artist = artistId,
                        title = title,
                        Text = new string[0]
                    }
                }
            };
        }

        /// <summary>
        /// Returns a listing of all files in a music directory. Typically used to get list of albums for an artist, or list of songs for an album.
        /// </summary>
        /// <param name="request">Query from application.</param>
        /// <param name="id">A string which uniquely identifies the music folder. Obtained by calls to getIndexes or getMusicDirectory.</param>
        /// <returns></returns>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetMusicDirectory(subsonic.Request request, User roadieUser)
        {
            var directory = new subsonic.Directory();
            var user = this.GetUser(roadieUser?.UserId);

            // Request to get albums for an Artist
            if (request.ArtistId != null)
            {
                var artistId = SafeParser.ToGuid(request.id);
                var artist = this.GetArtist(artistId.Value);
                if (artist == null)
                {
                    return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid ArtistId [{ request.id}]");
                }
                directory.id = subsonic.Request.ArtistIdIdentifier + artist.RoadieId.ToString();
                directory.name = artist.Name;
                var artistRating = user == null ? null : this.DbContext.UserArtists.FirstOrDefault(x => x.UserId == user.Id && x.ArtistId == artist.Id);
                if (artistRating?.IsFavorite ?? false)
                {
                    directory.starred = (artistRating.LastUpdated ?? artistRating.CreatedDate);
                    directory.starredSpecified = true;
                }
                var pagedRequest = request.PagedRequest;
                pagedRequest.FilterToArtistId = artist.RoadieId;
                pagedRequest.Sort = "Release.Text";
                var artistReleases = await this.ReleaseService.List(roadieUser, pagedRequest);
                directory.child = this.SubsonicChildrenForReleases(artistReleases.Rows, subsonic.Request.ArtistIdIdentifier + artist.RoadieId.ToString());
            }
            // Request to get albums for in a Collection
            else if (request.CollectionId != null)
            {
                var collectionId = SafeParser.ToGuid(request.id);
                var collection = this.GetCollection(collectionId.Value);
                if (collection == null)
                {
                    return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid CollectionId [{ request.id}]");
                }
                directory.id = subsonic.Request.CollectionIdentifier + collection.RoadieId.ToString();
                directory.name = collection.Name;
                var pagedRequest = request.PagedRequest;
                pagedRequest.FilterToCollectionId = collection.RoadieId;
                var collectionReleases = await this.ReleaseService.List(roadieUser, pagedRequest);
                directory.child = this.SubsonicChildrenForReleases(collectionReleases.Rows, subsonic.Request.CollectionIdentifier + collection.RoadieId.ToString());
            }
            // Request to get Tracks for an Album
            else if (request.ReleaseId.HasValue)
            {
                var releaseId = SafeParser.ToGuid(request.id);
                var release = this.GetRelease(releaseId.Value);
                if (release == null)
                {
                    return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid ReleaseId [{ request.id}]");
                }
                directory.id = subsonic.Request.ReleaseIdIdentifier + release.RoadieId.ToString();
                directory.name = release.Title;
                var releaseRating = user == null ? null : this.DbContext.UserReleases.FirstOrDefault(x =>x.UserId == user.Id && x.ReleaseId == release.Id);
                directory.averageRating = release.Rating ?? 0;
                directory.parent = subsonic.Request.ArtistIdIdentifier + release.Artist.RoadieId.ToString();
                if (releaseRating?.IsFavorite ?? false)
                {
                    directory.starred = releaseRating.LastUpdated ?? releaseRating.CreatedDate;
                    directory.starredSpecified = true;
                }
                var trackPagedRequest = request.PagedRequest;
                trackPagedRequest.Sort = "TrackNumber";
                trackPagedRequest.Order = "ASC";
                var songTracks = await this.TrackService.List(trackPagedRequest, roadieUser, false, release.RoadieId);
                directory.child = this.SubsonicChildrenForTracks(songTracks.Rows);
                directory.playCount = directory.child.Select(x => x.playCount).Sum();
            }
            else
            {
                return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Unknown GetMusicDirectory Type [{ JsonConvert.SerializeObject(request) }], id [{ request.id }]");
            }
            return new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok,
                    ItemElementName = subsonic.ItemChoiceType.directory,
                    Item = directory
                }
            };
        }

        /// <summary>
        /// Returns all configured top-level music folders. Takes no extra parameters.
        /// </summary>
        public Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetMusicFolders(subsonic.Request request)
        {
            return Task.FromResult(new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok,
                    ItemElementName = subsonic.ItemChoiceType.musicFolders,
                    Item = new subsonic.MusicFolders
                    {
                        musicFolder = this.MusicFolders().ToArray()
                    }
                }
            });
        }

        /// <summary>
        /// Returns what is currently being played by all users. Takes no extra parameters.
        /// </summary>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetNowPlaying(subsonic.Request request, User roadieUser)
        {
            var pagedRequest = request.PagedRequest;
            pagedRequest.Sort = "PlayedDateDateTime";
            pagedRequest.Order = "DESC";
            var playActivityResult = await this.PlayActivityService.List(pagedRequest, roadieUser, DateTime.UtcNow.AddDays(-1));

            pagedRequest.Sort = null;
            pagedRequest.Order = null;
            pagedRequest.FilterToTrackIds = playActivityResult.Rows.Select(x => SafeParser.ToGuid(x.Track.Value)).Distinct().ToArray();
            var playActivityTracksResult = await this.TrackService.List(pagedRequest, roadieUser);

            var playEntries = new List<subsonic.NowPlayingEntry>();
            var now = DateTime.UtcNow;
            foreach (var row in playActivityResult.Rows)
            {
                var rowTrack = playActivityTracksResult.Rows.FirstOrDefault(x => x.Track.Value == row.Track.Value);
                var playEntryTrackChild = this.SubsonicChildForTrack(rowTrack);
                var playEntry = playEntryTrackChild.Adapt<subsonic.NowPlayingEntry>();
                playEntry.username = row.User.Text;
                playEntry.minutesAgo = (int)(now - row.PlayedDateDateTime.Value).TotalMinutes;
                playEntry.playerId = 0;
                playEntry.playerName = string.Empty;
                playEntries.Add(playEntry);
            }

            return new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok,
                    ItemElementName = subsonic.ItemChoiceType.nowPlaying,
                    Item = new subsonic.NowPlaying
                    {
                        entry = playEntries.ToArray()
                    }
                }
            };
        }

        /// <summary>
        /// Returns a listing of files in a saved playlist.
        /// </summary>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetPlaylist(subsonic.Request request, User roadieUser)
        {
            var playListId = SafeParser.ToGuid(request.id);
            if (!playListId.HasValue)
            {
                return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid PlaylistId [{ request.id }]");
            }
            var pagedRequest = request.PagedRequest;
            pagedRequest.Sort = "Id";
            pagedRequest.FilterToPlaylistId = playListId.Value;
            var playlistResult = await this.PlaylistService.List(pagedRequest, roadieUser);
            var playlist = playlistResult.Rows.Any() ? playlistResult.Rows.First() : null;
            if (playlist == null)
            {
                return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid PlaylistId [{ request.id }]");
            }
            // For a playlist to show all the tracks in the playlist set the limit to the playlist size
            pagedRequest.Limit = playlist.PlaylistCount ?? pagedRequest.Limit;
            var tracksForPlaylist = await this.TrackService.List(pagedRequest, roadieUser);
            return new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok,
                    ItemElementName = subsonic.ItemChoiceType.playlist,
                    Item = this.SubsonicPlaylistForPlaylist(playlist, tracksForPlaylist.Rows)
                }
            };
        }

        /// <summary>
        /// Returns all playlists a user is allowed to play.
        /// </summary>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetPlaylists(subsonic.Request request, User roadieUser, string filterToUserName)
        {
            var pagedRequest = request.PagedRequest;
            pagedRequest.Sort = "Playlist.Text";
            pagedRequest.Order = "ASC";
            var playlistResult = await this.PlaylistService.List(pagedRequest, roadieUser);

            return new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok,
                    ItemElementName = subsonic.ItemChoiceType.playlists,
                    Item = new subsonic.Playlists
                    {
                        playlist = this.SubsonicPlaylistsForPlaylists(playlistResult.Rows)
                    }
                }
            };
        }

        /// <summary>
        /// Returns the state of the play queue for this user (as set by savePlayQueue). This includes the tracks in the play queue, the currently playing track, and the position within this track. Typically used to allow a user to move between different clients/apps while retaining the same play queue (for instance when listening to an audio book).
        /// </summary>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetPlayQueue(subsonic.Request request, User roadieUser)
        {
            var user = this.GetUser(roadieUser.UserId);

            subsonic.PlayQueue playQue = null;

            if (user.UserQues != null && user.UserQues.Any())
            {
                var current = user.UserQues.FirstOrDefault(x => x.IsCurrent ?? false) ?? user.UserQues.First();
                var pagedRequest = request.PagedRequest;
                pagedRequest.FilterToTrackIds = user.UserQues.Select(x => x.Track?.RoadieId).ToArray();
                var queTracksResult = await this.TrackService.List(pagedRequest, roadieUser);
                var queTrackRows = (from tt in queTracksResult.Rows
                                    join qt in user.UserQues on tt.DatabaseId equals qt.TrackId
                                    orderby qt.QueSortOrder
                                    select tt).ToArray();
                playQue = new subsonic.PlayQueue
                {
                    // I didnt specify current as it appears to be a Int and it blows up several client applications changing it to a string.
                    // current = subsonic.Request.TrackIdIdentifier + current.Track.RoadieId.ToString(),
                    changedBy = user.UserName,
                    changed = user.UserQues.OrderByDescending(x => x.CreatedDate).First().CreatedDate,
                    position = current.Position ?? 0,
                    positionSpecified = current.Position.HasValue,
                    username = user.UserName,
                    entry = this.SubsonicChildrenForTracks(queTrackRows)
                };
            }
            return new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                IsEmptyResponse = playQue == null,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok,
                    ItemElementName = subsonic.ItemChoiceType.playQueue,
                    Item = playQue
                }
            };
        }

        /// <summary>
        /// Returns all Podcast channels the server subscribes to, and (optionally) their episodes. This method can also be used to return details for only one channel - refer to the id parameter. A typical use case for this method would be to first retrieve all channels without episodes, and then retrieve all episodes for the single channel the user selects.
        /// </summary>
        public Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetPodcasts(subsonic.Request request)
        {
            return Task.FromResult(new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok,
                    ItemElementName = subsonic.ItemChoiceType.podcasts,
                    Item = new subsonic.Podcasts
                    {
                        channel = new subsonic.PodcastChannel[0]
                    }
                }
            });
        }

        /// <summary>
        /// Returns random songs matching the given criteria.
        /// </summary>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetRandomSongs(subsonic.Request request, User roadieUser)
        {
            var songs = new List<subsonic.Child>();

            var randomSongs = await this.TrackService.List(request.PagedRequest, roadieUser, true);

            return new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok,
                    ItemElementName = subsonic.ItemChoiceType.randomSongs,
                    Item = new subsonic.Songs
                    {
                        song = this.SubsonicChildrenForTracks(randomSongs.Rows)
                    }
                }
            };
        }

        /// <summary>
        /// Returns a random collection of songs from the given artist and similar artists, using data from last.fm. Typically used for artist radio features.
        /// </summary>
        public Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetSimliarSongs(subsonic.Request request, User roadieUser, subsonic.SimilarSongsVersion version, int? count = 50)
        {
            // TODO How to determine similiar songs? Perhaps by genre?

            switch (version)
            {
                case subsonic.SimilarSongsVersion.One:
                    return Task.FromResult(new subsonic.SubsonicOperationResult<subsonic.Response>
                    {
                        IsSuccess = true,
                        Data = new subsonic.Response
                        {
                            version = SubsonicService.SubsonicVersion,
                            status = subsonic.ResponseStatus.ok,
                            ItemElementName = subsonic.ItemChoiceType.similarSongs,
                            Item = new subsonic.SimilarSongs
                            {
                                song = new subsonic.Child[0]
                            }
                        }
                    });

                case subsonic.SimilarSongsVersion.Two:
                    return Task.FromResult(new subsonic.SubsonicOperationResult<subsonic.Response>
                    {
                        IsSuccess = true,
                        Data = new subsonic.Response
                        {
                            version = SubsonicService.SubsonicVersion,
                            status = subsonic.ResponseStatus.ok,
                            ItemElementName = subsonic.ItemChoiceType.similarSongs2,
                            Item = new subsonic.SimilarSongs2
                            {
                                song = new subsonic.Child[0]
                            }
                        }
                    });

                default:
                    return Task.FromResult(new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.IncompatibleServerRestProtocolVersion, $"Unknown SimilarSongsVersion [{ version }]"));
            }
        }

        /// <summary>
        /// Returns details for a song.
        /// </summary>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetSong(subsonic.Request request, User roadieUser)
        {
            if (!request.TrackId.HasValue)
            {
                return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Track [{ request.id }]");
            }
            var pagedRequest = request.PagedRequest;
            pagedRequest.FilterToTrackId = request.TrackId.Value;
            pagedRequest.Sort = "Id";
            var trackResult = await this.TrackService.List(pagedRequest, roadieUser);
            var track = trackResult.Rows.Any() ? trackResult.Rows.First() : null;
            if (track == null)
            {
                return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Track [{ request.id }]");
            }
            return new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok,
                    ItemElementName = subsonic.ItemChoiceType.song,
                    Item = this.SubsonicChildForTrack(track)
                }
            };
        }

        /// <summary>
        /// Returns songs in a given genre.
        /// </summary>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetSongsByGenre(subsonic.Request request, User roadieUser)
        {
            var pagedRequest = request.PagedRequest;
            pagedRequest.FilterByGenre = request.Genre;
            pagedRequest.Sort = "Id";
            var trackResult = await this.TrackService.List(pagedRequest, roadieUser);

            return new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok,
                    ItemElementName = subsonic.ItemChoiceType.songsByGenre,
                    Item = new subsonic.Songs
                    {
                        song = this.SubsonicChildrenForTracks(trackResult.Rows)
                    }
                }
            };
        }

        /// <summary>
        /// Returns starred songs, albums and artists.
        /// </summary>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetStarred(subsonic.Request request, User roadieUser, subsonic.StarredVersion version)
        {
            var pagedRequest = request.PagedRequest;
            pagedRequest.FilterFavoriteOnly = true;
            pagedRequest.Sort = "Id";

            var artistList = await this.ArtistService.List(roadieUser, pagedRequest);
            var releaseList = await this.ReleaseService.List(roadieUser, pagedRequest);
            var songList = await this.TrackService.List(pagedRequest, roadieUser);

            switch (version)
            {
                case subsonic.StarredVersion.One:
                    return new subsonic.SubsonicOperationResult<subsonic.Response>
                    {
                        IsSuccess = true,
                        Data = new subsonic.Response
                        {
                            version = SubsonicService.SubsonicVersion,
                            status = subsonic.ResponseStatus.ok,
                            ItemElementName = subsonic.ItemChoiceType.starred,
                            Item = new subsonic.Starred
                            {
                                album = this.SubsonicChildrenForReleases(releaseList.Rows, null),
                                artist = this.SubsonicArtistsForArtists(artistList.Rows),
                                song = this.SubsonicChildrenForTracks(songList.Rows)
                            }
                        }
                    };

                case subsonic.StarredVersion.Two:
                    return new subsonic.SubsonicOperationResult<subsonic.Response>
                    {
                        IsSuccess = true,
                        Data = new subsonic.Response
                        {
                            version = SubsonicService.SubsonicVersion,
                            status = subsonic.ResponseStatus.ok,
                            ItemElementName = subsonic.ItemChoiceType.starred2,
                            Item = new subsonic.Starred2
                            {
                                album = this.SubsonicAlbumID3ForReleases(releaseList.Rows),
                                artist = this.SubsonicArtistID3sForArtists(artistList.Rows),
                                song = this.SubsonicChildrenForTracks(songList.Rows)
                            }
                        }
                    };

                default:
                    return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.IncompatibleServerRestProtocolVersion, $"Unknown StarredVersion [{ version }]");
            }
        }

        /// <summary>
        /// Returns top songs for the given artist, using data from last.fm.
        /// </summary>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetTopSongs(subsonic.Request request, User roadieUser, int? count = 50)
        {
            data.Artist artist = null;
            if (!string.IsNullOrEmpty(request.ArtistName))
            {
                artist = base.GetArtist(request.ArtistName);
            }
            else if (request.ArtistId.HasValue)
            {
                artist = this.GetArtist(request.ArtistId.Value);
            }
            if (artist == null)
            {
                return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Unknown Artist [{ request.ArtistName }]");
            }
            var pagedRequest = request.PagedRequest;
            pagedRequest.FilterToArtistId = artist.RoadieId;
            pagedRequest.FilterTopPlayedOnly = true;
            pagedRequest.Sort = "PlayedCount";
            pagedRequest.Order = "DESC";
            var trackResult = await this.TrackService.List(pagedRequest, roadieUser);
            return new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok,
                    ItemElementName = subsonic.ItemChoiceType.topSongs,
                    Item = new subsonic.TopSongs
                    {
                        song = this.SubsonicChildrenForTracks(trackResult.Rows)
                    }
                }
            };
        }

        /// <summary>
        /// Get details about a given user, including which authorization roles and folder access it has. Can be used to enable/disable certain features in the client, such as jukebox control.
        /// </summary>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetUser(subsonic.Request request, string username)
        {
            var user = this.GetUser(username);
            if (user == null)
            {
                return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Username [{ username }]");
            }
            return new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok,
                    ItemElementName = subsonic.ItemChoiceType.user,
                    Item = await this.SubsonicUserForUser(user)
                }
            };
        }

        /// <summary>
        /// Returns all video files.
        /// </summary>
        public subsonic.SubsonicOperationResult<subsonic.Response> GetVideos(subsonic.Request request)
        {
            return new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok,
                    ItemElementName = subsonic.ItemChoiceType.videos,
                    Item = new subsonic.Videos
                    {
                        video = new subsonic.Child[0]
                    }
                }
            };
        }

        /// <summary>
        /// Used to test connectivity with the server. Takes no extra parameters.
        /// </summary>
        public subsonic.SubsonicOperationResult<subsonic.Response> Ping(subsonic.Request request)
        {
            return new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok
                }
            };
        }

        /// <summary>
        /// Saves the state of the play queue for this user. This includes the tracks in the play queue, the currently playing track, and the position within this track. Typically used to allow a user to move between different clients/apps while retaining the same play queue (for instance when listening to an audio book).
        /// </summary>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> SavePlayQueue(subsonic.Request request, User roadieUser, string current, long? position)
        {
            // Remove any existing Que for User
            var user = this.GetUser(roadieUser.UserId);
            if (user.UserQues != null && user.UserQues.Any())
            {
                this.DbContext.UserQues.RemoveRange(user.UserQues);
            }

            // Create a new UserQue for each posted TrackId in ids
            if (request.ids != null && request.ids.Any())
            {
                short queSortOrder = 0;
                var pagedRequest = request.PagedRequest;
                pagedRequest.FilterToTrackIds = request.ids.Select(x => SafeParser.ToGuid(x)).Where(x => x.HasValue).ToArray();
                var trackListResult = await this.TrackService.List(pagedRequest, roadieUser);
                var currentTrackId = SafeParser.ToGuid(current);
                foreach (var row in trackListResult.Rows)
                {
                    queSortOrder++;
                    this.DbContext.UserQues.Add(new data.UserQue
                    {
                        IsCurrent = row.Track.Value == currentTrackId?.ToString(),
                        Position = row.Track.Value == currentTrackId?.ToString() ? position : null,
                        QueSortOrder = queSortOrder,
                        TrackId = row.DatabaseId,
                        UserId = user.Id
                    });
                }
            }

            await this.DbContext.SaveChangesAsync();

            this.CacheManager.ClearRegion(user.CacheRegion);

            return new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok
                }
            };
        }

        /// <summary>
        /// Returns albums, artists and songs matching the given search criteria. Supports paging through the result.
        /// </summary>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> Search(subsonic.Request request, User roadieUser, subsonic.SearchVersion version)
        {
            var query = this.HttpEncoder.UrlDecode(request.Query).Replace("*", "").Replace("%", "").Replace(";", "");

            // Search artists with query returning ArtistCount skipping ArtistOffset
            var artistPagedRequest = request.PagedRequest;
            artistPagedRequest.Sort = "Artist.Text";
            artistPagedRequest.Limit = request.ArtistCount ?? artistPagedRequest.Limit;
            artistPagedRequest.SkipValue = request.ArtistOffset ?? artistPagedRequest.SkipValue;
            artistPagedRequest.Filter = query;
            var artistResult = await this.ArtistService.List(roadieUser, artistPagedRequest);

            // Search release with query returning RelaseCount skipping ReleaseOffset
            var releasePagedRequest = request.PagedRequest;
            releasePagedRequest.Sort = "Release.Text";
            releasePagedRequest.Limit = request.AlbumCount ?? releasePagedRequest.Limit;
            releasePagedRequest.SkipValue = request.AlbumOffset ?? releasePagedRequest.SkipValue;
            releasePagedRequest.Filter = query;
            var releaseResult = await this.ReleaseService.List(roadieUser, releasePagedRequest);

            // Search tracks with query returning SongCount skipping SongOffset
            var trackPagedRequest = request.PagedRequest;
            trackPagedRequest.Sort = "Track.Text";
            trackPagedRequest.Limit = request.SongCount ?? trackPagedRequest.Limit;
            trackPagedRequest.SkipValue = request.SongOffset ?? trackPagedRequest.SkipValue;
            trackPagedRequest.Filter = query;
            var songResult = await this.TrackService.List(trackPagedRequest, roadieUser);
            var songs = this.SubsonicChildrenForTracks(songResult.Rows);

            switch (version)
            {
                case subsonic.SearchVersion.One:
                    return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.IncompatibleClientRestProtocolVersion, "Deprecated since 1.4.0, use search2 instead.");

                case subsonic.SearchVersion.Two:
                    return new subsonic.SubsonicOperationResult<subsonic.Response>
                    {
                        IsSuccess = true,
                        Data = new subsonic.Response
                        {
                            version = SubsonicService.SubsonicVersion,
                            status = subsonic.ResponseStatus.ok,
                            ItemElementName = subsonic.ItemChoiceType.searchResult2,
                            Item = new subsonic.SearchResult2
                            {
                                artist = this.SubsonicArtistsForArtists(artistResult.Rows),
                                album = this.SubsonicChildrenForReleases(releaseResult.Rows, null),
                                song = songs.ToArray()
                            }
                        }
                    };

                case subsonic.SearchVersion.Three:
                    return new subsonic.SubsonicOperationResult<subsonic.Response>
                    {
                        IsSuccess = true,
                        Data = new subsonic.Response
                        {
                            version = SubsonicService.SubsonicVersion,
                            status = subsonic.ResponseStatus.ok,
                            ItemElementName = subsonic.ItemChoiceType.searchResult3,
                            Item = new subsonic.SearchResult3
                            {
                                artist = this.SubsonicArtistID3sForArtists(artistResult.Rows),
                                album = this.SubsonicAlbumID3ForReleases(releaseResult.Rows),
                                song = songs.ToArray()
                            }
                        }
                    };

                default:
                    return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.IncompatibleServerRestProtocolVersion, $"Unknown SearchVersion [{ version }]");
            }
        }

        /// <summary>
        /// Sets the rating for a music file. If rating is zero then remove rating.
        /// </summary>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> SetRating(subsonic.Request request, User roadieUser, short rating)
        {
            var user = this.GetUser(roadieUser.UserId);
            if (user == null)
            {
                return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.UserIsNotAuthorizedForGivenOperation, $"Invalid User [{ roadieUser }]");
            }

            // Id can be a song, album or artist
            if (request.TrackId.HasValue)
            {
                var starResult = await this.SetTrackRating(request.TrackId.Value, user, rating);
                if (starResult.IsSuccess)
                {
                    return new subsonic.SubsonicOperationResult<subsonic.Response>
                    {
                        IsSuccess = true,
                        Data = new subsonic.Response()
                    };
                }
            }
            else if (request.ReleaseId.HasValue)
            {
                var starResult = await this.SetReleaseRating(request.ReleaseId.Value, user, rating);
                if (starResult.IsSuccess)
                {
                    return new subsonic.SubsonicOperationResult<subsonic.Response>
                    {
                        IsSuccess = true,
                        Data = new subsonic.Response()
                    };
                }
            }
            else if (request.ArtistId.HasValue)
            {
                var starResult = await this.SetArtistRating(request.ArtistId.Value, user, rating);
                if (starResult.IsSuccess)
                {
                    return new subsonic.SubsonicOperationResult<subsonic.Response>
                    {
                        IsSuccess = true,
                        Data = new subsonic.Response()
                    };
                }
            }
            return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Unknown Star Id [{ JsonConvert.SerializeObject(request) }]");
        }

        /// <summary>
        /// Attaches a star to a song, album or artist.
        /// </summary>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> ToggleStar(subsonic.Request request, User roadieUser, bool star, string[] albumIds = null, string[] artistIds = null)
        {
            var user = this.GetUser(roadieUser.UserId);
            if (user == null)
            {
                return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.UserIsNotAuthorizedForGivenOperation, $"Invalid User [{ roadieUser }]");
            }

            // Id can be a song, album or artist
            if (request.TrackId.HasValue)
            {
                var starResult = await this.ToggleTrackStar(request.TrackId.Value, user, star);
                if (starResult.IsSuccess)
                {
                    return new subsonic.SubsonicOperationResult<subsonic.Response>
                    {
                        IsSuccess = true,
                        Data = new subsonic.Response()
                    };
                }
            }
            else if (request.ReleaseId.HasValue)
            {
                var starResult = await this.ToggleReleaseStar(request.ReleaseId.Value, user, star);
                if (starResult.IsSuccess)
                {
                    return new subsonic.SubsonicOperationResult<subsonic.Response>
                    {
                        IsSuccess = true,
                        Data = new subsonic.Response()
                    };
                }
            }
            else if (request.ArtistId.HasValue)
            {
                var starResult = await this.ToggleArtistStar(request.ArtistId.Value, user, star);
                if (starResult.IsSuccess)
                {
                    return new subsonic.SubsonicOperationResult<subsonic.Response>
                    {
                        IsSuccess = true,
                        Data = new subsonic.Response()
                    };
                }
            }
            else if (albumIds != null && albumIds.Any())
            {
                foreach (var rId in albumIds)
                {
                    var releaseId = SafeParser.ToGuid(rId);
                    if (releaseId.HasValue)
                    {
                        var starResult = await this.ToggleReleaseStar(releaseId.Value, user, star);
                        if (!starResult.IsSuccess)
                        {
                            return new subsonic.SubsonicOperationResult<subsonic.Response>(starResult.ErrorCode.Value, starResult.Messages.FirstOrDefault());
                        }
                    }
                }
            }
            else if (artistIds != null && artistIds.Any())
            {
                foreach (var aId in artistIds)
                {
                    var artistId = SafeParser.ToGuid(aId);
                    if (artistId.HasValue)
                    {
                        var starResult = await this.ToggleReleaseStar(artistId.Value, user, star);
                        if (!starResult.IsNotFoundResult)
                        {
                            return new subsonic.SubsonicOperationResult<subsonic.Response>(starResult.ErrorCode.Value, starResult.Messages.FirstOrDefault());
                        }
                    }
                }
            }
            return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Unknown Star Id [{ JsonConvert.SerializeObject(request) }]");
        }

        /// <summary>
        /// Updates a playlist. Only the owner of a playlist is allowed to update it.
        /// </summary>
        /// <param name="request">Populated Subsonic Request</param>
        /// <param name="roadieUser">Populated Roadie User</param>
        /// <param name="name">The human-readable name of the playlist.</param>
        /// <param name="comment">The playlist comment.</param>
        /// <param name="isPublic">true if the playlist should be visible to all users, false otherwise.</param>
        /// <param name="songIdsToAdd">Add this song with this ID to the playlist. Multiple parameters allowed</param>
        /// <param name="songIndexesToRemove">Remove the song at this position in the playlist. Multiple parameters allowed.</param>
        public async Task<subsonic.SubsonicOperationResult<subsonic.Response>> UpdatePlaylist(subsonic.Request request, User roadieUser, string playListId, string name = null, string comment = null, bool? isPublic = null, string[] songIdsToAdd = null, int[] songIndexesToRemove = null)
        {
            request.id = playListId ?? request.id;
            if (!request.PlaylistId.HasValue)
            {
                return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Playlist Id [{ request.id }]");
            }
            var playlist = this.GetPlaylist(request.PlaylistId.Value);
            if (playlist == null)
            {
                return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Playlist Id [{ request.TrackId.Value }]");
            }
            if (playlist.UserId != roadieUser.Id && !roadieUser.IsAdmin)
            {
                return new subsonic.SubsonicOperationResult<subsonic.Response>(subsonic.ErrorCodes.UserIsNotAuthorizedForGivenOperation, "User is not allowed to update playlist.");
            }

            playlist.Name = name ?? playlist.Name;
            playlist.IsPublic = isPublic ?? playlist.IsPublic;
            playlist.LastUpdated = DateTime.UtcNow;

            if (songIdsToAdd != null && songIdsToAdd.Any())
            {
                // Add new if not already on Playlist
                var songIdsToAddRoadieIds = songIdsToAdd.Select(x => SafeParser.ToGuid(x)).ToArray();
                var submittedTracks = (from t in this.DbContext.Tracks
                                       where songIdsToAddRoadieIds.Contains(t.RoadieId)
                                       select t);

                var listNumber = playlist.Tracks?.Max(x => x.ListNumber) ?? 0;
                foreach (var submittedTrack in submittedTracks)
                {
                    if (playlist.Tracks == null || playlist.Tracks == null || !playlist.Tracks.Any(x => x.TrackId == submittedTrack.Id))
                    {
                        listNumber++;
                        this.DbContext.PlaylistTracks.Add(new data.PlaylistTrack
                        {
                            PlayListId = playlist.Id,
                            ListNumber = listNumber,
                            TrackId = submittedTrack.Id
                        });
                    }
                }
            }
            if (songIndexesToRemove != null && songIndexesToRemove.Any())
            {
                // Remove tracks from playlist
                // Not clear from API documentation if this is zero based, wait until someone calls it to get values passed.
                throw new NotImplementedException($"Request [{ JsonConvert.SerializeObject(request) }]");
            }

            await this.DbContext.SaveChangesAsync();

            var user = this.GetUser(roadieUser.UserId);
            this.CacheManager.ClearRegion(user.CacheRegion);

            return new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok
                }
            };
        }

        #region Privates

        private string[] AllowedUsers()
        {
            return this.CacheManager.Get<string[]>(CacheManagerBase.SystemCacheRegionUrn + ":active_usernames", () =>
            {
                return this.DbContext.Users.Where(x => x.IsActive ?? false).Select(x => x.UserName).ToArray();
            }, CacheManagerBase.SystemCacheRegionUrn);
        }

        private subsonic.MusicFolder CollectionMusicFolder()
        {
            return this.MusicFolders().First(x => x.id == 1);
        }

        private async Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetArtistsAction(subsonic.Request request, User roadieUser)
        {
            var indexes = new List<subsonic.IndexID3>();
            var musicFolder = this.MusicFolders().FirstOrDefault(x => x.id == (request.MusicFolderId ?? 2));
            var pagedRequest = request.PagedRequest;
            if (musicFolder == this.CollectionMusicFolder())
            {
                // Indexes for "Collection" Artists alphabetically
            }
            else
            {
                // Indexes for "Music" Artists alphabetically
                pagedRequest.SkipValue = 0;
                pagedRequest.Limit = short.MaxValue;
                pagedRequest.Sort = "Artist.Text";
                var artistList = await this.ArtistService.List(roadieUser: roadieUser,
                                                               request: pagedRequest,
                                                               doRandomize: false,
                                                               onlyIncludeWithReleases: true);
                foreach (var artistGroup in artistList.Rows.GroupBy(x => x.Artist.Text.Substring(0, 1)))
                {
                    indexes.Add(new subsonic.IndexID3
                    {
                        name = artistGroup.Key,
                        artist = this.SubsonicArtistID3sForArtists(artistGroup)
                    });
                };
            }
            return new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok,
                    ItemElementName = subsonic.ItemChoiceType.artists,
                    Item = new subsonic.ArtistsID3
                    {
                        index = indexes.ToArray()
                    }
                }
            };
        }

        private async Task<subsonic.SubsonicOperationResult<subsonic.Response>> GetIndexesAction(subsonic.Request request, User roadieUser, long? ifModifiedSince = null)
        {
            var modifiedSinceFilter = ifModifiedSince.HasValue ? (DateTime?)ifModifiedSince.Value.FromUnixTime() : null;
            subsonic.MusicFolder musicFolderFilter = !request.MusicFolderId.HasValue ? new subsonic.MusicFolder() : this.MusicFolders().FirstOrDefault(x => x.id == request.MusicFolderId.Value);
            var indexes = new List<subsonic.Index>();

            if (musicFolderFilter.id == this.CollectionMusicFolder().id)
            {
                // Collections for Music Folders by Alpha First
                foreach (var collectionFirstLetter in (from c in this.DbContext.Collections
                                                       let first = c.Name.Substring(0, 1)
                                                       orderby first
                                                       select first).Distinct().ToArray())
                {
                    indexes.Add(new subsonic.Index
                    {
                        name = collectionFirstLetter,
                        artist = (from c in this.DbContext.Collections
                                  where c.Name.Substring(0, 1) == collectionFirstLetter
                                  where modifiedSinceFilter == null || c.LastUpdated >= modifiedSinceFilter
                                  orderby c.SortName, c.Name
                                  select new subsonic.Artist
                                  {
                                      id = subsonic.Request.CollectionIdentifier + c.RoadieId.ToString(),
                                      name = c.Name,
                                      artistImageUrl = this.MakeCollectionThumbnailImage(c.RoadieId).Url,
                                      averageRating = 0,
                                      userRating = 0
                                  }).ToArray()
                    });
                }
            }
            else
            {
                // Indexes for Artists alphabetically
                var pagedRequest = request.PagedRequest;
                pagedRequest.SkipValue = 0;
                pagedRequest.Limit = short.MaxValue;
                pagedRequest.Sort = "Artist.Text";
                var artistList = await this.ArtistService.List(roadieUser: roadieUser,
                                                               request: pagedRequest,
                                                               doRandomize: false,
                                                               onlyIncludeWithReleases: true);
                foreach (var artistGroup in artistList.Rows.GroupBy(x => x.Artist.Text.Substring(0, 1)))
                {
                    indexes.Add(new subsonic.Index
                    {
                        name = artistGroup.Key,
                        artist = this.SubsonicArtistsForArtists(artistGroup)
                    });
                };
            }
            return new subsonic.SubsonicOperationResult<subsonic.Response>
            {
                IsSuccess = true,
                Data = new subsonic.Response
                {
                    version = SubsonicService.SubsonicVersion,
                    status = subsonic.ResponseStatus.ok,
                    ItemElementName = subsonic.ItemChoiceType.indexes,
                    Item = new subsonic.Indexes
                    {
                        lastModified = DateTime.UtcNow.ToUnixTime(),
                        index = indexes.ToArray()
                    }
                }
            };
        }

        private List<subsonic.MusicFolder> MusicFolders()
        {
            return new List<subsonic.MusicFolder>
            {
                new subsonic.MusicFolder { id = 1, name = "Collections"},
                new subsonic.MusicFolder { id = 2, name = "Music"}
            };
        }

        private subsonic.MusicFolder MusicMusicFolder()
        {
            return this.MusicFolders().First(x => x.id == 2);
        }

        private new async Task<subsonic.SubsonicOperationResult<bool>> SetArtistRating(Guid artistId, ApplicationUser user, short rating)
        {
            var r = await base.SetArtistRating(artistId, user, rating);
            if (r.IsNotFoundResult)
            {
                return new subsonic.SubsonicOperationResult<bool>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Artist Id [{ artistId }]");
            }
            return new subsonic.SubsonicOperationResult<bool>
            {
                IsSuccess = r.IsSuccess,
                Data = r.IsSuccess
            };
        }

        private new async Task<subsonic.SubsonicOperationResult<bool>> SetReleaseRating(Guid releaseId, ApplicationUser user, short rating)
        {
            var r = await base.SetReleaseRating(releaseId, user, rating);
            if (r.IsNotFoundResult)
            {
                return new subsonic.SubsonicOperationResult<bool>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Release Id [{ releaseId }]");
            }
            return new subsonic.SubsonicOperationResult<bool>
            {
                IsSuccess = r.IsSuccess,
                Data = r.IsSuccess
            };
        }

        private new async Task<subsonic.SubsonicOperationResult<bool>> SetTrackRating(Guid trackId, ApplicationUser user, short rating)
        {
            var r = await base.SetTrackRating(trackId, user, rating);
            if (r.IsNotFoundResult)
            {
                return new subsonic.SubsonicOperationResult<bool>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Track Id [{ trackId }]");
            }
            return new subsonic.SubsonicOperationResult<bool>
            {
                IsSuccess = r.IsSuccess,
                Data = r.IsSuccess
            };
        }

        private subsonic.AlbumID3 SubsonicAlbumID3ForRelease(ReleaseList r)
        {
            return new subsonic.AlbumID3
            {
                id = subsonic.Request.ReleaseIdIdentifier + r.Id.ToString(),
                artistId = r.Artist.Value,
                name = r.Release.Text,
                songCount = r.TrackCount ?? 0,
                duration = r.Duration.ToSecondsFromMilliseconds(),
                artist = r.Artist.Text,
                coverArt = subsonic.Request.ReleaseIdIdentifier + r.Id.ToString(),
                created = r.CreatedDate.Value,
                genre = r.Genre.Text,
                playCount = r.TrackPlayedCount ?? 0,
                playCountSpecified = true,
                starred = r.UserRating?.RatedDate ?? DateTime.UtcNow,
                starredSpecified = r.UserRating?.IsFavorite ?? false,
                year = SafeParser.ToNumber<int>(r.ReleaseYear),
                yearSpecified = true
            };
        }

        private subsonic.AlbumID3[] SubsonicAlbumID3ForReleases(IEnumerable<ReleaseList> r)
        {
            if (r == null || !r.Any())
            {
                return new subsonic.AlbumID3[0];
            }
            return r.Select(x => this.SubsonicAlbumID3ForRelease(x)).ToArray();
        }

        private subsonic.Artist SubsonicArtistForArtist(ArtistList artist)
        {
            return new subsonic.Artist
            {
                id = subsonic.Request.ArtistIdIdentifier + artist.Artist.Value.ToString(),
                name = artist.Artist.Text,
                artistImageUrl = this.MakeArtistThumbnailImage(artist.Id).Url,
                averageRating = artist.Rating ?? 0,
                averageRatingSpecified = true,
                starred = artist.UserRating?.RatedDate ?? DateTime.UtcNow,
                starredSpecified = artist.UserRating?.IsFavorite ?? false,
                userRating = artist.UserRating != null ? artist.UserRating.Rating ?? 0 : 0,
                userRatingSpecified = artist.UserRating != null && artist.UserRating.Rating != null
            };
        }

        private subsonic.ArtistID3 SubsonicArtistID3ForArtist(ArtistList artist)
        {
            var artistImageUrl = this.MakeArtistThumbnailImage(artist.Id).Url;
            return new subsonic.ArtistID3
            {
                id = subsonic.Request.ArtistIdIdentifier + artist.Artist.Value.ToString(),
                name = artist.Artist.Text,
                albumCount = artist.ReleaseCount ?? 0,
                coverArt = artistImageUrl,
                artistImageUrl = artistImageUrl,
                starred = artist.UserRating?.RatedDate ?? DateTime.UtcNow,
                starredSpecified = artist.UserRating?.IsFavorite ?? false
            };
        }

        private subsonic.ArtistID3[] SubsonicArtistID3sForArtists(IEnumerable<ArtistList> artists)
        {
            if (artists == null || !artists.Any())
            {
                return new subsonic.ArtistID3[0];
            }
            return artists.Select(x => this.SubsonicArtistID3ForArtist(x)).ToArray();
        }

        private subsonic.ArtistInfo2 SubsonicArtistInfo2InfoForArtist(data.Artist artist)
        {
            return new subsonic.ArtistInfo2
            {
                biography = artist.BioContext,
                largeImageUrl = this.MakeImage(artist.RoadieId, "artist", this.Configuration.LargeImageSize).Url,
                mediumImageUrl = this.MakeImage(artist.RoadieId, "artist", this.Configuration.MediumImageSize).Url,
                musicBrainzId = artist.MusicBrainzId,
                similarArtist = new subsonic.ArtistID3[0],
                smallImageUrl = this.MakeImage(artist.RoadieId, "artist", this.Configuration.SmallImageSize).Url
            };
        }

        private subsonic.ArtistInfo SubsonicArtistInfoForArtist(data.Artist artist)
        {
            return new subsonic.ArtistInfo
            {
                biography = artist.BioContext,
                largeImageUrl = this.MakeImage(artist.RoadieId, "artist", this.Configuration.LargeImageSize).Url,
                mediumImageUrl = this.MakeImage(artist.RoadieId, "artist", this.Configuration.MediumImageSize).Url,
                musicBrainzId = artist.MusicBrainzId,
                similarArtist = new subsonic.Artist[0],
                smallImageUrl = this.MakeImage(artist.RoadieId, "artist", this.Configuration.SmallImageSize).Url
            };
        }

        private subsonic.Artist[] SubsonicArtistsForArtists(IEnumerable<ArtistList> artists)
        {
            if (artists == null || !artists.Any())
            {
                return new subsonic.Artist[0];
            }
            return artists.Select(x => this.SubsonicArtistForArtist(x)).ToArray();
        }

        private subsonic.ArtistWithAlbumsID3 SubsonicArtistWithAlbumsID3ForArtist(ArtistList artist, subsonic.AlbumID3[] releases)
        {
            var artistImageUrl = this.MakeArtistThumbnailImage(artist.Id).Url;
            return new subsonic.ArtistWithAlbumsID3
            {
                id = subsonic.Request.ArtistIdIdentifier + artist.Artist.Value.ToString(),
                album = releases,
                albumCount = releases.Count(),
                artistImageUrl = artistImageUrl,
                coverArt = artistImageUrl,
                name = artist.Artist.Text,
                starred = artist.UserRating?.RatedDate ?? DateTime.UtcNow,
                starredSpecified = artist.UserRating?.IsFavorite ?? false
            };
        }

        private subsonic.Bookmark SubsonicBookmarkForBookmark(BookmarkList b, subsonic.Child entry)
        {
            return new subsonic.Bookmark
            {
                changed = b.LastUpdated ?? b.CreatedDate.Value,
                comment = b.Comment,
                created = b.CreatedDate.Value,
                position = b.Position ?? 0,
                username = b.User.Text,
                entry = entry
            };
        }

        private subsonic.Bookmark[] SubsonicBookmarksForBookmarks(IEnumerable<BookmarkList> bb, IEnumerable<TrackList> childTracks)
        {
            if (bb == null || !bb.Any())
            {
                return new subsonic.Bookmark[0];
            }
            var result = new List<subsonic.Bookmark>();
            foreach (var bookmark in bb)
            {
                subsonic.Child child = null;
                switch (bookmark.Type.Value)
                {
                    case Library.Enums.BookmarkType.Track:
                        child = this.SubsonicChildForTrack(childTracks.FirstOrDefault(x => x.Id == SafeParser.ToGuid(bookmark.Bookmark.Value)));
                        break;

                    default:
                        throw new NotImplementedException("Wrong Bookmark type to convert to Subsonic media Bookmark");
                }
                result.Add(this.SubsonicBookmarkForBookmark(bookmark, child));
            }
            return result.ToArray();
        }

        private subsonic.Child SubsonicChildForRelease(ReleaseList r, string parent, string path)
        {
            return new subsonic.Child
            {
                id = subsonic.Request.ReleaseIdIdentifier + r.Id.ToString(),
                album = r.Release.Text,
                albumId = subsonic.Request.ReleaseIdIdentifier + r.Id.ToString(),
                artist = r.Artist.Text,
                averageRating = r.Rating ?? 0,
                averageRatingSpecified = true,
                coverArt = subsonic.Request.ReleaseIdIdentifier + r.Id.ToString(),
                created = r.CreatedDate.Value,
                createdSpecified = true,
                genre = r.Genre.Text,
                isDir = true,
                parent = parent ?? subsonic.Request.ArtistIdIdentifier + r.Artist.Value,
                path = path,
                playCount = r.TrackPlayedCount ?? 0,
                playCountSpecified = true,
                starred = r.UserRating?.RatedDate ?? DateTime.UtcNow,
                starredSpecified = r.UserRating?.IsFavorite ?? false,
                title = r.Release.Text,
                userRating = r.UserRating != null ? r.UserRating.Rating ?? 0 : 0,
                userRatingSpecified = r.UserRating != null && r.UserRating.Rating != null,
                year = SafeParser.ToNumber<int>(r.ReleaseYear),
                yearSpecified = true
            };
        }

        private subsonic.Child SubsonicChildForTrack(TrackList t)
        {
            return new subsonic.Child
            {
                id = subsonic.Request.TrackIdIdentifier + t.Id.ToString(),
                album = t.Release.Release.Text,
                albumId = subsonic.Request.ReleaseIdIdentifier + t.Release.Release.Value,
                artist = t.Artist.Artist.Text,
                artistId = subsonic.Request.ArtistIdIdentifier + t.Artist.Artist.Value,
                averageRating = t.Rating ?? 0,
                averageRatingSpecified = true,
                bitRate = 320,
                bitRateSpecified = true,
                contentType = "audio/mpeg",
                coverArt = subsonic.Request.TrackIdIdentifier + t.Id.ToString(),
                created = t.CreatedDate.Value,
                createdSpecified = true,
                discNumber = t.MediaNumber ?? 0,
                discNumberSpecified = true,
                duration = t.Duration.ToSecondsFromMilliseconds(),
                durationSpecified = true,
                isDir = false,
                parent = subsonic.Request.ReleaseIdIdentifier + t.Release.Release.Value,
                path = $"{ t.Artist.Artist.Text }/{ t.Release.Release.Text }/{ t.TrackNumber } - { t.Title }.mp3",
                playCountSpecified = true,
                size = t.FileSize ?? 0,
                sizeSpecified = true,
                starred = t.UserRating?.RatedDate ?? DateTime.UtcNow,
                starredSpecified = t.UserRating?.IsFavorite ?? false,
                suffix = "mp3",
                title = t.Title,
                track = t.TrackNumber ?? 0,
                trackSpecified = t.TrackNumber.HasValue,
                type = subsonic.MediaType.music,
                typeSpecified = true,
                userRating = t.UserRating != null ? t.UserRating.Rating ?? 0 : 0,
                userRatingSpecified = t.UserRating != null,
                year = t.Year ?? 0,
                yearSpecified = t.Year.HasValue,
                transcodedContentType = "audio/mpeg",
                transcodedSuffix = "mp3",
                isVideo = false,
                isVideoSpecified = true,
                playCount = t.PlayedCount ?? 0
            };
        }

        private subsonic.Child[] SubsonicChildrenForReleases(IEnumerable<ReleaseList> r, string parent)
        {
            if (r == null || !r.Any())
            {
                return new subsonic.Child[0];
            }
            return r.Select(x => this.SubsonicChildForRelease(x, parent, $"{ x.Artist.Text}/{ x.Release.Text}/")).ToArray();
        }

        private subsonic.Child[] SubsonicChildrenForTracks(IEnumerable<TrackList> tracks)
        {
            if (tracks == null || !tracks.Any())
            {
                return new subsonic.Child[0];
            }
            return tracks.Select(x => this.SubsonicChildForTrack(x)).ToArray();
        }

        private subsonic.Playlist SubsonicPlaylistForPlaylist(Library.Models.Playlists.PlaylistList playlist, IEnumerable<TrackList> playlistTracks = null)
        {
            return new subsonic.PlaylistWithSongs
            {
                coverArt = this.MakePlaylistThumbnailImage(playlist.Id).Url,
                allowedUser = playlist.IsPublic ? this.AllowedUsers() : null,
                changed = playlist.LastUpdated ?? playlist.CreatedDate ?? DateTime.UtcNow,
                created = playlist.CreatedDate ?? DateTime.UtcNow,
                duration = playlist.Duration.ToSecondsFromMilliseconds(),
                id = subsonic.Request.PlaylistdIdentifier + playlist.Id.ToString(),
                name = playlist.Playlist.Text,
                owner = playlist.User.Text,
                @public = playlist.IsPublic,
                publicSpecified = true,
                songCount = playlist.PlaylistCount ?? 0,
                entry = this.SubsonicChildrenForTracks(playlistTracks)
            };
        }

        private subsonic.Playlist[] SubsonicPlaylistsForPlaylists(IEnumerable<Library.Models.Playlists.PlaylistList> playlists)
        {
            if (playlists == null || !playlists.Any())
            {
                return new subsonic.Playlist[0];
            }
            return playlists.Select(x => this.SubsonicPlaylistForPlaylist(x)).ToArray();
        }

        private async Task<subsonic.User> SubsonicUserForUser(Library.Identity.ApplicationUser user)
        {
            var isAdmin = await this.UserManger.IsInRoleAsync(user, "Admin");
            var isEditor = await this.UserManger.IsInRoleAsync(user, "Editor");
            return new subsonic.User
            {
                adminRole = false, // disabling this as we dont want Roadie user management done via Subsonic API
                avatarLastChanged = user.LastUpdated ?? user.CreatedDate ?? DateTime.UtcNow,
                avatarLastChangedSpecified = user.LastUpdated.HasValue,
                commentRole = true,
                coverArtRole = isEditor || isAdmin,
                downloadRole = isEditor || isAdmin,
                email = user.Email,
                jukeboxRole = false, // Jukebox disabled (what is jukebox?)
                maxBitRate = 320,
                maxBitRateSpecified = true,
                playlistRole = isEditor || isAdmin,
                podcastRole = false, // Disable podcast nonsense
                scrobblingEnabled = false, // Disable scrobbling
                settingsRole = isAdmin,
                shareRole = false,
                streamRole = true,
                uploadRole = true,
                username = user.UserName,
                videoConversionRole = false, // Disable video nonsense
                folder = this.MusicFolders().Select(x => x.id).ToArray()
            };
        }

        private async Task<subsonic.SubsonicOperationResult<bool>> ToggleArtistStar(Guid artistId, ApplicationUser user, bool starred)
        {
            var r = await base.ToggleArtistFavorite(artistId, user, starred);
            if (r.IsNotFoundResult)
            {
                return new subsonic.SubsonicOperationResult<bool>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Artist Id [{ artistId }]");
            }
            return new subsonic.SubsonicOperationResult<bool>
            {
                IsSuccess = r.IsSuccess,
                Data = r.IsSuccess
            };
        }

        private async Task<subsonic.SubsonicOperationResult<bool>> ToggleReleaseStar(Guid releaseId, ApplicationUser user, bool starred)
        {
            var r = await base.ToggleReleaseFavorite(releaseId, user, starred);
            if (r.IsNotFoundResult)
            {
                return new subsonic.SubsonicOperationResult<bool>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Release Id [{ releaseId }]");
            }
            return new subsonic.SubsonicOperationResult<bool>
            {
                IsSuccess = r.IsSuccess,
                Data = r.IsSuccess
            };
        }

        private async Task<subsonic.SubsonicOperationResult<bool>> ToggleTrackStar(Guid trackId, ApplicationUser user, bool starred)
        {
            var r = await base.ToggleTrackFavorite(trackId, user, starred);
            if (r.IsNotFoundResult)
            {
                return new subsonic.SubsonicOperationResult<bool>(subsonic.ErrorCodes.TheRequestedDataWasNotFound, $"Invalid Track Id [{ trackId }]");
            }
            return new subsonic.SubsonicOperationResult<bool>
            {
                IsSuccess = r.IsSuccess,
                Data = r.IsSuccess
            };
        }

        #endregion Privates
    }
}