using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimplerJiangAiAgent.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFundamentalSnapshotCacheFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FundamentalFactsJson",
                table: "StockCompanyProfiles",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FundamentalUpdatedAt",
                table: "StockCompanyProfiles",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FundamentalFactsJson",
                table: "StockCompanyProfiles");

            migrationBuilder.DropColumn(
                name: "FundamentalUpdatedAt",
                table: "StockCompanyProfiles");
        }
    }
}
