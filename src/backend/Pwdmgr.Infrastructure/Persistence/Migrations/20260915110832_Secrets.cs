using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pwdmgr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Secrets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "secrets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vault_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name_ciphertext = table.Column<byte[]>(type: "bytea", maxLength: 1024, nullable: false),
                    latest_version_no = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_secrets", x => x.id);
                    table.UniqueConstraint("ak_secrets_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_secrets_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_secrets_vaults_tenant_id_vault_id",
                        columns: x => new { x.tenant_id, x.vault_id },
                        principalTable: "vaults",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "secret_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    secret_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    payload_ciphertext = table.Column<byte[]>(type: "bytea", maxLength: 65536, nullable: false),
                    wrapped_dek = table.Column<byte[]>(type: "bytea", maxLength: 256, nullable: false),
                    aad_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    crypto_version = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_secret_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_secret_versions_secrets_tenant_id_secret_id",
                        columns: x => new { x.tenant_id, x.secret_id },
                        principalTable: "secrets",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_secret_versions_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_secret_versions_tenant_id_secret_id_version_no",
                table: "secret_versions",
                columns: new[] { "tenant_id", "secret_id", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_secrets_tenant_id_vault_id_status",
                table: "secrets",
                columns: new[] { "tenant_id", "vault_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "secret_versions");

            migrationBuilder.DropTable(
                name: "secrets");
        }
    }
}
