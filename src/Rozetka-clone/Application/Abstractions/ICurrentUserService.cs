using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Abstractions
{
    public interface ICurrentUserService
    {
        Guid? UserId { get; }

        bool IsAuthenticated { get; }
    }
}
