using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimplerJiangAiAgent.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalFactAiFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiSentiment",
                table: "LocalStockNews",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AiTags",
                table: "LocalStockNews",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiTarget",
                table: "LocalStockNews",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAiProcessed",
                table: "LocalStockNews",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TranslatedTitle",
                table: "LocalStockNews",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiSentiment",
                table: "LocalSectorReports",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AiTags",
                table: "LocalSectorReports",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiTarget",
                table: "LocalSectorReports",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAiProcessed",
                table: "LocalSectorReports",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TranslatedTitle",
                table: "LocalSectorReports",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LocalStockNews_IsAiProcessed_Symbol_PublishTime",
                table: "LocalStockNews",
                columns: new[] { "IsAiProcessed", "Symbol", "PublishTime" });

            migrationBuilder.CreateIndex(
                name: "IX_LocalSectorReports_IsAiProcessed_Level_Symbol_PublishTime",
                table: "LocalSectorReports",
                columns: new[] { "IsAiProcessed", "Level", "Symbol", "PublishTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LocalStockNews_IsAiProcessed_Symbol_PublishTime",
                table: "LocalStockNews");

            migrationBuilder.DropIndex(
                name: "IX_LocalSectorReports_IsAiProcessed_Level_Symbol_PublishTime",
                table: "LocalSectorReports");

            migrationBuilder.DropColumn(
                name: "AiSentiment",
                table: "LocalStockNews");

            migrationBuilder.DropColumn(
                name: "AiTags",
                table: "LocalStockNews");

            migrationBuilder.DropColumn(
                name: "AiTarget",
                table: "LocalStockNews");

            migrationBuilder.DropColumn(
                name: "IsAiProcessed",
                table: "LocalStockNews");

            migrationBuilder.DropColumn(
                name: "TranslatedTitle",
                table: "LocalStockNews");

            migrationBuilder.DropColumn(
                name: "AiSentiment",
                table: "LocalSectorReports");

            migrationBuilder.DropColumn(
                name: "AiTags",
                table: "LocalSectorReports");

            migrationBuilder.DropColumn(
                name: "AiTarget",
                table: "LocalSectorReports");

            migrationBuilder.DropColumn(
                name: "IsAiProcessed",
                table: "LocalSectorReports");

            migrationBuilder.DropColumn(
                name: "TranslatedTitle",
                table: "LocalSectorReports");
        }
    }
}
