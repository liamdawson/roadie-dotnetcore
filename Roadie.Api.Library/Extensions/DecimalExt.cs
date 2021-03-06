﻿using Roadie.Library.Utility;
using System;
using System.Collections.Generic;
using System.Text;

namespace Roadie.Library.Extensions
{
    public static class DecimalExt
    {
        public static int ToSecondsFromMilliseconds(this decimal? value)
        {
            if (value > 0)
            {
                return (int)new TimeInfo(value.Value).Seconds;
            }
            return 0;
        }

        public static TimeSpan? ToTimeSpan(this decimal? value)
        {
            if(!value.HasValue)
            {
                return null;
            }
            return TimeSpan.FromSeconds((double)value);
        }
    }
}
