using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewRepliesAndTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "StaffRepliedAt",
                table: "reviews",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StaffRepliedBy",
                table: "reviews",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StaffReply",
                table: "reviews",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "review_reply_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TextUk = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    TextEn = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_reply_templates", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "review_reply_templates",
                columns: new[] { "Id", "Enabled", "Name", "TextEn", "TextUk", "UpdatedAt", "UpdatedBy", "Version" },
                values: new object[,]
                {
                    { new Guid("a1000000-0000-4000-8000-000000000001"), true, "Подяка за відгук", "{customer}, thank you for your review! We appreciate you sharing your experience with {product}.", "{customer}, дякуємо за ваш відгук! Раді, що ви поділилися враженнями про {product}.", new DateTimeOffset(new DateTime(2026, 10, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), new Guid("a1000000-0000-4000-8000-000000000011") },
                    { new Guid("a1000000-0000-4000-8000-000000000002"), true, "Раді вдалим покупкам", "Thank you for choosing TREBA! We are glad that {product} met your expectations. We look forward to seeing you again!", "Дякуємо, що обираєте TREBA! Приємно знати, що {product} виправдав ваші очікування. Будемо раді бачити вас знову!", new DateTimeOffset(new DateTime(2026, 10, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), new Guid("a1000000-0000-4000-8000-000000000012") },
                    { new Guid("a1000000-0000-4000-8000-000000000003"), true, "Допомога з покупкою", "{customer}, thank you for your feedback. We are sorry you experienced difficulties. Please contact TREBA support with your order number so we can help.", "{customer}, дякуємо за зворотний зв’язок. Нам шкода, що ви зіткнулися з труднощами. Будь ласка, зверніться до підтримки TREBA з номером замовлення, щоб ми могли допомогти.", new DateTimeOffset(new DateTime(2026, 10, 30, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), new Guid("a1000000-0000-4000-8000-000000000013") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_review_reply_templates_Enabled",
                table: "review_reply_templates",
                column: "Enabled");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "review_reply_templates");

            migrationBuilder.DropColumn(
                name: "StaffRepliedAt",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "StaffRepliedBy",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "StaffReply",
                table: "reviews");
        }
    }
}
