using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoadOps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workspaces",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    assessment_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspaces", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "road_sections",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    section_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    workspace_id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_road_sections", x => x.id);
                    table.ForeignKey(
                        name: "fk_road_sections_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "paved_road_records",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    workspace_id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    section_id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    chainage_from = table.Column<double>(type: "double precision", nullable: false),
                    chainage_to = table.Column<double>(type: "double precision", nullable: false),
                    surface_type = table.Column<int>(type: "integer", nullable: false),
                    distress_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    degree = table.Column<int>(type: "integer", nullable: false),
                    extent = table.Column<int>(type: "integer", nullable: false),
                    rut_depth_mm = table.Column<double>(type: "double precision", nullable: false),
                    riding_quality = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    skid_resistance = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    std_ref = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    recommended_action = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    image_paths = table.Column<string[]>(type: "text[]", nullable: false),
                    created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_paved_road_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_paved_road_records_road_sections_section_id",
                        column: x => x.section_id,
                        principalTable: "road_sections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_paved_road_records_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_paved_road_records_chainage_from",
                table: "paved_road_records",
                column: "chainage_from");

            migrationBuilder.CreateIndex(
                name: "ix_paved_road_records_created_at",
                table: "paved_road_records",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_paved_road_records_distress_type",
                table: "paved_road_records",
                column: "distress_type");

            migrationBuilder.CreateIndex(
                name: "ix_paved_road_records_section_id_chainage_from",
                table: "paved_road_records",
                columns: new[] { "section_id", "chainage_from" });

            migrationBuilder.CreateIndex(
                name: "ix_paved_road_records_workspace_id_chainage_from",
                table: "paved_road_records",
                columns: new[] { "workspace_id", "chainage_from" });

            migrationBuilder.CreateIndex(
                name: "ix_road_sections_section_name",
                table: "road_sections",
                column: "section_name");

            migrationBuilder.CreateIndex(
                name: "ix_road_sections_workspace_id_created_at",
                table: "road_sections",
                columns: new[] { "workspace_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_workspaces_created_at",
                table: "workspaces",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_workspaces_name",
                table: "workspaces",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_workspaces_status",
                table: "workspaces",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "paved_road_records");

            migrationBuilder.DropTable(
                name: "road_sections");

            migrationBuilder.DropTable(
                name: "workspaces");
        }
    }
}
