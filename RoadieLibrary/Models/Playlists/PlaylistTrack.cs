﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Roadie.Library.Models.Playlists
{
    [Serializable]
    public class PlaylistTrack
    {
        public Track Track { get; set; }
        public int ListNumber { get; set; }
    }
}