﻿using Roadie.Library.Enums;
using Roadie.Library.Models;
using Roadie.Library.Models.Pagination;
using Roadie.Library.Models.Users;
using System.Threading.Tasks;

namespace Roadie.Api.Services
{
    public interface IBookmarkService
    {
        Task<PagedResult<BookmarkList>> List(User roadieUser, PagedRequest request, bool? doRandomize = false, BookmarkType? filterType = null);
    }
}