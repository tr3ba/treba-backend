using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Categories
{
    public sealed class MoveCategoryRequest
    {
        public Guid? NewParentId { get; init; }
    }
}
