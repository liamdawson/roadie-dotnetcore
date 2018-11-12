﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Roadie.Library.Models.Pagination;
using Roadie.Library.Models.Releases;
using Roadie.Library.Models.Users;

namespace Roadie.Api.Services
{
    public interface IReleaseService
    {
        Task<PagedResult<ReleaseList>> ReleaseList(User user, PagedRequest request, bool? doRandomize = false, IEnumerable<string> includes = null);
    }
}