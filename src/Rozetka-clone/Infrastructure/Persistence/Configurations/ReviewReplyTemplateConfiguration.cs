using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class ReviewReplyTemplateConfiguration : IEntityTypeConfiguration<ReviewReplyTemplate>
{
    public void Configure(EntityTypeBuilder<ReviewReplyTemplate> builder)
    {
        builder.ToTable("review_reply_templates");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Name).HasMaxLength(80).IsRequired();
        builder.Property(row => row.TextUk).HasMaxLength(2000).IsRequired();
        builder.Property(row => row.TextEn).HasMaxLength(2000).IsRequired();
        builder.Property(row => row.Version).IsConcurrencyToken();
        builder.HasIndex(row => row.Enabled);
        var updatedAt = new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
        builder.HasData(
            new ReviewReplyTemplate { Id = Guid.Parse("a1000000-0000-4000-8000-000000000001"), Name = "Подяка за відгук", TextUk = "{customer}, дякуємо за ваш відгук! Раді, що ви поділилися враженнями про {product}.", TextEn = "{customer}, thank you for your review! We appreciate you sharing your experience with {product}.", Version = Guid.Parse("a1000000-0000-4000-8000-000000000011"), UpdatedAt = updatedAt },
            new ReviewReplyTemplate { Id = Guid.Parse("a1000000-0000-4000-8000-000000000002"), Name = "Раді вдалим покупкам", TextUk = "Дякуємо, що обираєте TREBA! Приємно знати, що {product} виправдав ваші очікування. Будемо раді бачити вас знову!", TextEn = "Thank you for choosing TREBA! We are glad that {product} met your expectations. We look forward to seeing you again!", Version = Guid.Parse("a1000000-0000-4000-8000-000000000012"), UpdatedAt = updatedAt },
            new ReviewReplyTemplate { Id = Guid.Parse("a1000000-0000-4000-8000-000000000003"), Name = "Допомога з покупкою", TextUk = "{customer}, дякуємо за зворотний зв’язок. Нам шкода, що ви зіткнулися з труднощами. Будь ласка, зверніться до підтримки TREBA з номером замовлення, щоб ми могли допомогти.", TextEn = "{customer}, thank you for your feedback. We are sorry you experienced difficulties. Please contact TREBA support with your order number so we can help.", Version = Guid.Parse("a1000000-0000-4000-8000-000000000013"), UpdatedAt = updatedAt }
        );
    }
}
