using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Attributes
{
    public interface IAttributeService
    {
        Task<IReadOnlyList<AttributeDto>> GetByCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);

        Task<AttributeDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        Task<AttributeDto> CreateAsync(CreateAttributeRequest request, CancellationToken cancellationToken = default);

        Task<AttributeDto?> UpdateAsync(Guid id, UpdateAttributeRequest request, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<AttributeOptionDto>> GetOptionsAsync(Guid attributeId, CancellationToken cancellationToken = default);

        Task<AttributeOptionDto> CreateOptionAsync(Guid attributeId, CreateAttributeOptionRequest request, CancellationToken cancellationToken = default);

        Task<AttributeOptionDto?> UpdateOptionAsync(Guid attributeId, Guid optionId, UpdateAttributeOptionRequest request, CancellationToken cancellationToken = default);
    }
}
