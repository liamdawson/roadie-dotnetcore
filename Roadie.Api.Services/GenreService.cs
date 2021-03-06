﻿using Microsoft.Extensions.Logging;
using Roadie.Library.Caching;
using Roadie.Library.Configuration;
using Roadie.Library.Encoding;
using Roadie.Library.Models;
using Roadie.Library.Models.Pagination;
using Roadie.Library.Models.Users;
using Roadie.Library.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using data = Roadie.Library.Data;

namespace Roadie.Api.Services
{
    public class GenreService : ServiceBase, IGenreService
    {
        public GenreService(IRoadieSettings configuration,
                             IHttpEncoder httpEncoder,
                             IHttpContext httpContext,
                             data.IRoadieDbContext dbContext,
                             ICacheManager cacheManager,
                             ILogger<GenreService> logger)
            : base(configuration, httpEncoder, dbContext, cacheManager, logger, httpContext)
        {
        }

        public Task<Library.Models.Pagination.PagedResult<GenreList>> List(User roadieUser, PagedRequest request, bool? doRandomize = false)
        {
            var sw = new Stopwatch();
            sw.Start();

            if (!string.IsNullOrEmpty(request.Sort))
            {
                request.Sort = request.Sort.Replace("createdDate", "createdDateTime");
                request.Sort = request.Sort.Replace("lastUpdated", "lastUpdatedDateTime");
            }
            var result = (from g in this.DbContext.Genres
                          let releaseCount = (from rg in this.DbContext.ReleaseGenres
                                              where rg.GenreId == g.Id
                                              select rg.Id).Count()
                          let artistCount = (from rg in this.DbContext.ArtistGenres
                                             where rg.GenreId == g.Id
                                             select rg.Id).Count()
                          where (request.FilterValue.Length == 0 || (g.Name.Contains(request.FilterValue)))
                          select new GenreList
                          {
                              DatabaseId = g.Id,
                              Id = g.RoadieId,
                              Genre = new DataToken
                              {
                                  Text = g.Name,
                                  Value = g.RoadieId.ToString()
                              },
                              ReleaseCount = releaseCount,
                              ArtistCount = artistCount,
                              CreatedDate = g.CreatedDate,
                              LastUpdated = g.LastUpdated,
                          });

            GenreList[] rows = null;
            var rowCount = result.Count();
            if (doRandomize ?? false)
            {
                var randomLimit = roadieUser?.RandomReleaseLimit ?? 100;
                request.Limit = request.LimitValue > randomLimit ? randomLimit : request.LimitValue;
                rows = result.OrderBy(x => x.RandomSortId).Skip(request.SkipValue).Take(request.LimitValue).ToArray();
            }
            else
            {
                var sortBy = string.IsNullOrEmpty(request.Sort) ? request.OrderValue(new Dictionary<string, string> { { "Genre.Text", "ASC" } }) : request.OrderValue(null);
                rows = result.OrderBy(sortBy).Skip(request.SkipValue).Take(request.LimitValue).ToArray();
            }
            sw.Stop();
            return Task.FromResult(new Library.Models.Pagination.PagedResult<GenreList>
            {
                TotalCount = rowCount,
                CurrentPage = request.PageValue,
                TotalPages = (int)Math.Ceiling((double)rowCount / request.LimitValue),
                OperationTime = sw.ElapsedMilliseconds,
                Rows = rows
            });
        }
    }
}