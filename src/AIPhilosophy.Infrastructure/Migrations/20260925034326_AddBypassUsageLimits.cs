using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIPhilosophy.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBypassUsageLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BypassUsageLimits",
                table: "Tenants",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BypassUsageLimits",
                table: "Tenants");
        }
    }
}
