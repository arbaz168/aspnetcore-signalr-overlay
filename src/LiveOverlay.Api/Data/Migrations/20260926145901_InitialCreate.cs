using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LiveOverlay.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    DashboardKeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OverlayTokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    LastSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    GoalTitle = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    GoalTargetMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    GoalCurrentMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Channels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChannelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: true),
                    Message = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    GoalTitle = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    GoalTargetMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    GoalCurrentMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Events_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Channels_DashboardKeyHash",
                table: "Channels",
                column: "DashboardKeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Channels_OverlayTokenHash",
                table: "Channels",
                column: "OverlayTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_ChannelId_IdempotencyKey",
                table: "Events",
                columns: new[] { "ChannelId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_ChannelId_Sequence",
                table: "Events",
                columns: new[] { "ChannelId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "Channels");
        }
    }
}
