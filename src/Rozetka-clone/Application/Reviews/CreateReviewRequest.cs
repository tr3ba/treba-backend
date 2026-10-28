using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Reviews
{
    public sealed class CreateReviewRequest
    {
        public Guid ProductId { get; init; }

        public Guid OrderItemId { get; init; }

        [System.ComponentModel.DataAnnotations.Range(1, 5)]
        public int Rating { get; init; }

        [System.ComponentModel.DataAnnotations.StringLength(255)]
        public string Title { get; init; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(10000)]
        public string Comment { get; init; } = string.Empty;
    }
}
