using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaucyBot.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddGuildConfigurationDisabledSiteTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "guild_configuration_disabled_sites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid(36)", nullable: false, collation: "ascii_general_ci"),
                    guild_configuration_id = table.Column<Guid>(type: "uuid(36)", nullable: false, collation: "ascii_general_ci"),
                    site = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_configuration_disabled_sites", x => x.id);
                    table.ForeignKey(
                        name: "fk_guild_configuration_disabled_sites_guild_configurations_guil",
                        column: x => x.guild_configuration_id,
                        principalTable: "guild_configurations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_guild_configuration_disabled_sites_guild_configuration_id",
                table: "guild_configuration_disabled_sites",
                column: "guild_configuration_id");

            migrationBuilder.CreateIndex(
                name: "ix_guild_configuration_disabled_sites_guild_configuration_id_si",
                table: "guild_configuration_disabled_sites",
                columns: new[] { "guild_configuration_id", "site" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guild_configuration_disabled_sites");
        }
    }
}
