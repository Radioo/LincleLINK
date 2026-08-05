using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LincleLINK.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDetectionConfidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Confidence",
                table: "Instances",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Confidence",
                table: "Instances");
        }
    }
}
