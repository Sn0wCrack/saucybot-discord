using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaucyBot.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddUuidIdToGuildConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "uuid",
                table: "guild_configurations",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE guild_configurations SET uuid = LOWER(CONCAT(
                    LPAD(HEX(UNIX_TIMESTAMP(created_at) * 1000), 12, '0'),
                    '-7',
                    LPAD(HEX(FLOOR(RAND() * 4095)), 3, '0'),
                    '-',
                    HEX(8 + FLOOR(RAND() * 4)),
                    LPAD(HEX(FLOOR(RAND() * 4095)), 3, '0'),
                    '-',
                    LPAD(HEX(FLOOR(RAND() * 65535)), 4, '0'),
                    LPAD(HEX(FLOOR(RAND() * 4294967295)), 8, '0')
                ))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn("uuid", "guild_configurations");
        }
    }
}
