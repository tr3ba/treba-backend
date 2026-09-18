using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Brands
{
    public interface IBrandService
    {
        Task<IReadOnlyList<BrandDto>> GetAllAsync(CancellationToken cancellationToken = default);

        Task<BrandDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        Task<BrandDto> CreateAsync(CreateBrandRequest request, CancellationToken cancellationToken = default);

        Task<BrandDto?> UpdateAsync(Guid id, UpdateBrandRequest request, CancellationToken cancellationToken = default);
    }
}
