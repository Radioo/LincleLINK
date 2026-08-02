using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LincleLINK.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Instances",
                columns: table => new
                {
                    InstanceName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    NameKey = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    TotalFileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalFileCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalFileSizeString = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Instances", x => x.InstanceName);
                });

            migrationBuilder.CreateTable(
                name: "InstanceDirectories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InstanceName = table.Column<string>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstanceDirectories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstanceDirectories_Instances_InstanceName",
                        column: x => x.InstanceName,
                        principalTable: "Instances",
                        principalColumn: "InstanceName",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InstanceFiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InstanceName = table.Column<string>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    HashedFileName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstanceFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstanceFiles_Instances_InstanceName",
                        column: x => x.InstanceName,
                        principalTable: "Instances",
                        principalColumn: "InstanceName",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InstanceDirectories_InstanceName",
                table: "InstanceDirectories",
                column: "InstanceName");

            migrationBuilder.CreateIndex(
                name: "IX_InstanceFiles_InstanceName",
                table: "InstanceFiles",
                column: "InstanceName");

            migrationBuilder.CreateIndex(
                name: "IX_Instances_NameKey",
                table: "Instances",
                column: "NameKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InstanceDirectories");

            migrationBuilder.DropTable(
                name: "InstanceFiles");

            migrationBuilder.DropTable(
                name: "Instances");
        }
    }
}
