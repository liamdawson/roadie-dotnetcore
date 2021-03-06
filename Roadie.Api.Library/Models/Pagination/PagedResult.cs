﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Roadie.Library.Models.Pagination
{
    [Serializable]
    public class PagedResult<T>
    {

        public bool IsSuccess
        {
            get
            {
                return this.Message == OperationMessages.OkMessage;
            }
        }

        public IEnumerable<T> Rows { get; set; }

        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
        public int TotalCount { get; set; }


        public string Message { get; set; }

        public long OperationTime { get; set; }

        public PagedResult()
        {
            this.Message = OperationMessages.OkMessage;
        }
    }
}
