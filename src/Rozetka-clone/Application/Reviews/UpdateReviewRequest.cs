using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Reviews
{
    public sealed class UpdateReviewRequest
    {
        public int Rating { get; init; }

        public string Title { get; init; } = string.Empty;

        public string Comment { get; init; } = string.Empty;
    }
}
