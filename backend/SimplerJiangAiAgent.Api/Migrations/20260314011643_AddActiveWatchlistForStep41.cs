using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimplerJiangAiAgent.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddActiveWatchlistForStep41 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActiveWatchlists",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SourceTag = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastQuoteSyncAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActiveWatchlists", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActiveWatchlists_IsEnabled_UpdatedAt",
                table: "ActiveWatchlists",
                columns: new[] { "IsEnabled", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ActiveWatchlists_Symbol",
                table: "ActiveWatchlists",
                column: "Symbol",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActiveWatchlists");
        }
    }
}
