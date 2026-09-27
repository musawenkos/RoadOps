using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoadOps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotosAndSoftVoid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "voided_at",
                table: "paved_road_records",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "voided_by",
                table: "paved_road_records",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "photos",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    record_id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    uploaded_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attached_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_photos", x => x.id);
                    table.ForeignKey(
                        name: "fk_photos_paved_road_records_record_id",
                        column: x => x.record_id,
                        principalTable: "paved_road_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_paved_road_records_created_by_created_at",
                table: "paved_road_records",
                columns: new[] { "created_by", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_photos_record_id",
                table: "photos",
                column: "record_id");

            migrationBuilder.CreateIndex(
                name: "ix_photos_uploaded_by_uploaded_at",
                table: "photos",
                columns: new[] { "uploaded_by", "uploaded_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "photos");

            migrationBuilder.DropIndex(
                name: "ix_paved_road_records_created_by_created_at",
                table: "paved_road_records");

            migrationBuilder.DropColumn(
                name: "voided_at",
                table: "paved_road_records");

            migrationBuilder.DropColumn(
                name: "voided_by",
                table: "paved_road_records");
        }
    }
}
