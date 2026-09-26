using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoadOps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSectionChainageCorridorAndMeasurements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "corridor",
                table: "workspaces",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "survey_year",
                table: "workspaces",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "chainage_from",
                table: "road_sections",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "chainage_to",
                table: "road_sections",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "depth_mm",
                table: "paved_road_records",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "length_m",
                table: "paved_road_records",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "paved_road_records",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "width_m",
                table: "paved_road_records",
                type: "double precision",
                nullable: true);

            // Backfill existing rows. Until now corridor, year and km range only existed inside the names,
            // e.g. "N1 Pretoria – Polokwane · VCI 2026" and "N1 S03: km 40.0–60.0 (Hammanskraal)".
            migrationBuilder.Sql("""
                UPDATE workspaces
                SET corridor = CASE
                        WHEN upper(split_part(trim(name), ' ', 1)) ~ '^[A-Z0-9]{1,12}$' THEN upper(split_part(trim(name), ' ', 1))
                        ELSE 'UNKNOWN'
                    END,
                    survey_year = COALESCE(
                        (regexp_match(name, '((?:19|20)[0-9]{2})'))[1]::int,
                        extract(year FROM created_at)::int);
                """);

            migrationBuilder.Sql("""
                UPDATE road_sections
                SET chainage_from = m[1]::double precision,
                    chainage_to = m[2]::double precision
                FROM (
                    SELECT id AS section_id,
                           regexp_match(section_name, 'km\s*([0-9]+(?:\.[0-9]+)?)\s*[–-]\s*([0-9]+(?:\.[0-9]+)?)') AS m
                    FROM road_sections
                ) parsed
                WHERE road_sections.id = parsed.section_id
                  AND m IS NOT NULL
                  AND m[1]::double precision < m[2]::double precision;
                """);

            // Sections whose name has no km range take the extent of their records, and may extend past the name when
            // records overrun it.
            migrationBuilder.Sql("""
                UPDATE road_sections
                SET chainage_from = CASE WHEN road_sections.chainage_to > road_sections.chainage_from
                                         THEN LEAST(road_sections.chainage_from, extent.min_from) ELSE extent.min_from END,
                    chainage_to = GREATEST(road_sections.chainage_to, extent.max_to)
                FROM (
                    SELECT section_id, MIN(chainage_from) AS min_from, MAX(chainage_to) AS max_to
                    FROM paved_road_records
                    GROUP BY section_id
                ) extent
                WHERE road_sections.id = extent.section_id;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_workspaces_corridor_survey_year",
                table: "workspaces",
                columns: new[] { "corridor", "survey_year" });

            migrationBuilder.CreateIndex(
                name: "ix_road_sections_workspace_id_chainage_from",
                table: "road_sections",
                columns: new[] { "workspace_id", "chainage_from" });

            migrationBuilder.CreateIndex(
                name: "ix_paved_road_records_latitude_longitude",
                table: "paved_road_records",
                columns: new[] { "latitude", "longitude" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_workspaces_corridor_survey_year",
                table: "workspaces");

            migrationBuilder.DropIndex(
                name: "ix_road_sections_workspace_id_chainage_from",
                table: "road_sections");

            migrationBuilder.DropIndex(
                name: "ix_paved_road_records_latitude_longitude",
                table: "paved_road_records");

            migrationBuilder.DropColumn(
                name: "corridor",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "survey_year",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "chainage_from",
                table: "road_sections");

            migrationBuilder.DropColumn(
                name: "chainage_to",
                table: "road_sections");

            migrationBuilder.DropColumn(
                name: "depth_mm",
                table: "paved_road_records");

            migrationBuilder.DropColumn(
                name: "length_m",
                table: "paved_road_records");

            migrationBuilder.DropColumn(
                name: "notes",
                table: "paved_road_records");

            migrationBuilder.DropColumn(
                name: "width_m",
                table: "paved_road_records");
        }
    }
}
