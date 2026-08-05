using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shortener.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSmsMessagePatternFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PatternCode",
                table: "SmsMessages",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PatternTokensJson",
                table: "SmsMessages",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PatternCode",
                table: "SmsMessages");

            migrationBuilder.DropColumn(
                name: "PatternTokensJson",
                table: "SmsMessages");
        }
    }
}
