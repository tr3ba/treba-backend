using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class ReviewVote
    {
        public Guid Id { get; set; }

        public Guid ReviewId { get; set; }

        public Guid UserId { get; set; }

        public bool Helpful { get; set; }
    }
}
