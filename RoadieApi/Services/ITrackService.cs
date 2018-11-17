﻿using Roadie.Library;
using Roadie.Library.Models;
using Roadie.Library.Models.Pagination;
using Roadie.Library.Models.Users;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Roadie.Api.Services
{
    public interface ITrackService
    {
        Task<OperationResult<Track>> ById(User roadieUser, Guid id, IEnumerable<string> includes);

        Task<Library.Models.Pagination.PagedResult<TrackList>> List(User roadieUser, PagedRequest request, bool? doRandomize = false, Guid? releaseId = null);

        Task<OperationResult<TrackStreamInfo>> TrackStreamInfo(Guid trackId, long beginBytes, long endBytes);
    }
}