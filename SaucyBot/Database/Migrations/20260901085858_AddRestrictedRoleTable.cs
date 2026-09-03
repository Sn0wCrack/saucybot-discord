using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaucyBot.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddRestrictedRoleTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey("pk_guild_configurations", "guild_configurations");

            migrationBuilder.AlterColumn<Guid>(
                name: "uuid",
                table: "guild_configurations",
                type: "uuid",
                nullable: false);

            migrationBuilder.DropColumn(
                name: "Id",
                table: "guild_configurations");

            migrationBuilder.RenameColumn(
                name: "uuid",
                table: "guild_configurations",
                newName: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_guild_configurations",
                table: "guild_configurations",
                column: "id");

            migrationBuilder.AddColumn<bool>(
                name: "restrict_to_roles",
                table: "guild_configurations",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "guild_configuration_restricted_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    guild_configuration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_configuration_restricted_roles", x => x.id);
                    table.ForeignKey(
                        name: "fk_guild_configuration_restricted_roles_guild_configurations_gu",
                        column: x => x.guild_configuration_id,
                        principalTable: "guild_configurations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_guild_configuration_restricted_roles_guild_configuration_id",
                table: "guild_configuration_restricted_roles",
                column: "guild_configuration_id");

            migrationBuilder.CreateIndex(
                name: "ix_guild_configuration_restricted_roles_guild_configuration_id_",
                table: "guild_configuration_restricted_roles",
                columns: new[] { "guild_configuration_id", "role_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guild_configuration_restricted_roles");

            migrationBuilder.DropColumn(
                name: "restrict_to_roles",
                table: "guild_configurations");

            migrationBuilder.AlterColumn<uint>(
                name: "id",
                table: "guild_configurations",
                type: "int unsigned",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid")
                .OldAnnotation("Relational:Collation", "ascii_general_ci");
        }
    }
}
