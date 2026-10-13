using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class ReviewImage
    {
        public Guid Id { get; set; }

        public Guid ReviewId { get; set; }

        public string ImageUrl { get; set; } = string.Empty;

        public int SortOrder { get; set; }
    }
}
