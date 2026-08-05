using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LincleLINK.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGameDetection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomLogoSource",
                table: "Instances",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DateCode",
                table: "Instances",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayTitle",
                table: "Instances",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GameCode",
                table: "Instances",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GameTitle",
                table: "Instances",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoKey",
                table: "Instances",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PeIdentifier",
                table: "Instances",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomLogoSource",
                table: "Instances");

            migrationBuilder.DropColumn(
                name: "DateCode",
                table: "Instances");

            migrationBuilder.DropColumn(
                name: "DisplayTitle",
                table: "Instances");

            migrationBuilder.DropColumn(
                name: "GameCode",
                table: "Instances");

            migrationBuilder.DropColumn(
                name: "GameTitle",
                table: "Instances");

            migrationBuilder.DropColumn(
                name: "LogoKey",
                table: "Instances");

            migrationBuilder.DropColumn(
                name: "PeIdentifier",
                table: "Instances");
        }
    }
}
