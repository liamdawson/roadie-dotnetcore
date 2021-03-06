﻿using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Roadie.Library.Data
{
    [Table("genre")]
    public partial class Genre : EntityBase
    {
        [Column("name")]
        [MaxLength(100)]
        public string Name { get; set; }

        public ICollection<ReleaseGenre> Releases { get; set; }
        public ICollection<ArtistGenre> Artists { get; set; }

        public Genre()
        {
            this.Releases = new HashSet<ReleaseGenre>();
            this.Artists = new HashSet<ArtistGenre>();
        }
    }
}