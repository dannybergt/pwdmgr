using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pwdmgr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CryptoMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_keyrings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    crypto_version = table.Column<int>(type: "integer", nullable: false),
                    kdf_algorithm = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    kdf_memory_kib = table.Column<int>(type: "integer", nullable: false),
                    kdf_iterations = table.Column<int>(type: "integer", nullable: false),
                    kdf_parallelism = table.Column<int>(type: "integer", nullable: false),
                    kdf_salt = table.Column<byte[]>(type: "bytea", maxLength: 64, nullable: false),
                    public_key = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    encrypted_private_key = table.Column<byte[]>(type: "bytea", maxLength: 4096, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_keyrings", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_keyrings_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_keyrings_users_tenant_id_user_id",
                        columns: x => new { x.tenant_id, x.user_id },
                        principalTable: "users",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vaults",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    name_ciphertext = table.Column<byte[]>(type: "bytea", maxLength: 1024, nullable: false),
                    crypto_version = table.Column<int>(type: "integer", nullable: false),
                    key_version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vaults", x => x.id);
                    table.UniqueConstraint("ak_vaults_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_vaults_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wrapped_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_type = table.Column<int>(type: "integer", nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_type = table.Column<int>(type: "integer", nullable: false),
                    recipient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_version = table.Column<int>(type: "integer", nullable: false),
                    crypto_version = table.Column<int>(type: "integer", nullable: false),
                    ciphertext = table.Column<byte[]>(type: "bytea", maxLength: 1024, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wrapped_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_wrapped_keys_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_keyrings_tenant_id_user_id",
                table: "user_keyrings",
                columns: new[] { "tenant_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_keyrings_user_id",
                table: "user_keyrings",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vaults_tenant_id_status",
                table: "vaults",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_wrapped_keys_tenant_id_recipient_type_recipient_id",
                table: "wrapped_keys",
                columns: new[] { "tenant_id", "recipient_type", "recipient_id" });

            migrationBuilder.CreateIndex(
                name: "ix_wrapped_keys_tenant_id_resource_type_resource_id_recipient_",
                table: "wrapped_keys",
                columns: new[] { "tenant_id", "resource_type", "resource_id", "recipient_type", "recipient_id", "key_version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_keyrings");

            migrationBuilder.DropTable(
                name: "vaults");

            migrationBuilder.DropTable(
                name: "wrapped_keys");
        }
    }
}
