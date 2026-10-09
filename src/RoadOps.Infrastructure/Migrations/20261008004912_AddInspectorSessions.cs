using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoadOps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInspectorSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inspector_sessions",
                columns: table => new
                {
                    inspector = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    workspace_id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    section_id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    km = table.Column<double>(type: "double precision", nullable: true),
                    position_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    follow_ups = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inspector_sessions", x => x.inspector);
                    table.ForeignKey(
                        name: "fk_inspector_sessions_road_sections_section_id",
                        column: x => x.section_id,
                        principalTable: "road_sections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_inspector_sessions_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inspector_sessions_section_id",
                table: "inspector_sessions",
                column: "section_id");

            migrationBuilder.CreateIndex(
                name: "ix_inspector_sessions_workspace_id",
                table: "inspector_sessions",
                column: "workspace_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inspector_sessions");
        }
    }
}
