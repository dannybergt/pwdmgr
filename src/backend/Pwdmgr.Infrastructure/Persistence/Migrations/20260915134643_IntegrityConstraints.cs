using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pwdmgr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IntegrityConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "ix_wrapped_keys_tenant_id_resource_type_resource_id_recipient_",
                table: "wrapped_keys",
                newName: "ux_wrapped_keys_resource_recipient_version");

            migrationBuilder.CreateIndex(
                name: "ix_wrapped_keys_tenant_id_recipient_id",
                table: "wrapped_keys",
                columns: new[] { "tenant_id", "recipient_id" });

            migrationBuilder.CreateIndex(
                name: "ix_wrapped_keys_tenant_id_resource_id",
                table: "wrapped_keys",
                columns: new[] { "tenant_id", "resource_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_wrapped_keys_ciphertext_len",
                table: "wrapped_keys",
                sql: "octet_length(ciphertext) BETWEEN 60 AND 1024");

            migrationBuilder.AddCheckConstraint(
                name: "ck_wrapped_keys_types",
                table: "wrapped_keys",
                sql: "resource_type = 1 AND recipient_type = 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vaults_name_len",
                table: "vaults",
                sql: "octet_length(name_ciphertext) BETWEEN 28 AND 1024");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_keyrings_kdf_range",
                table: "user_keyrings",
                sql: "kdf_memory_kib BETWEEN 19456 AND 1048576 AND kdf_iterations BETWEEN 2 AND 16 AND kdf_parallelism BETWEEN 1 AND 16");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_keyrings_kdf_salt_len",
                table: "user_keyrings",
                sql: "octet_length(kdf_salt) BETWEEN 16 AND 64");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_keyrings_private_key_len",
                table: "user_keyrings",
                sql: "octet_length(encrypted_private_key) BETWEEN 28 AND 4096");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_keyrings_public_key_len",
                table: "user_keyrings",
                sql: "octet_length(public_key) = 32");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_revoked_at",
                table: "sessions",
                column: "revoked_at",
                filter: "revoked_at IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sessions_token_hash_len",
                table: "sessions",
                sql: "octet_length(token_hash) = 32");

            migrationBuilder.AddCheckConstraint(
                name: "ck_secrets_name_len",
                table: "secrets",
                sql: "octet_length(name_ciphertext) BETWEEN 28 AND 1024");

            migrationBuilder.AddCheckConstraint(
                name: "ck_secret_versions_aad_hash_len",
                table: "secret_versions",
                sql: "octet_length(aad_hash) = 32");

            migrationBuilder.AddCheckConstraint(
                name: "ck_secret_versions_payload_len",
                table: "secret_versions",
                sql: "octet_length(payload_ciphertext) BETWEEN 28 AND 65536");

            migrationBuilder.AddCheckConstraint(
                name: "ck_secret_versions_version_no",
                table: "secret_versions",
                sql: "version_no >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_secret_versions_wrapped_dek_len",
                table: "secret_versions",
                sql: "octet_length(wrapped_dek) BETWEEN 60 AND 256");

            migrationBuilder.AddForeignKey(
                name: "fk_wrapped_keys_users_tenant_id_recipient_id",
                table: "wrapped_keys",
                columns: new[] { "tenant_id", "recipient_id" },
                principalTable: "users",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_wrapped_keys_vaults_tenant_id_resource_id",
                table: "wrapped_keys",
                columns: new[] { "tenant_id", "resource_id" },
                principalTable: "vaults",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_wrapped_keys_users_tenant_id_recipient_id",
                table: "wrapped_keys");

            migrationBuilder.DropForeignKey(
                name: "fk_wrapped_keys_vaults_tenant_id_resource_id",
                table: "wrapped_keys");

            migrationBuilder.DropIndex(
                name: "ix_wrapped_keys_tenant_id_recipient_id",
                table: "wrapped_keys");

            migrationBuilder.DropIndex(
                name: "ix_wrapped_keys_tenant_id_resource_id",
                table: "wrapped_keys");

            migrationBuilder.DropCheckConstraint(
                name: "ck_wrapped_keys_ciphertext_len",
                table: "wrapped_keys");

            migrationBuilder.DropCheckConstraint(
                name: "ck_wrapped_keys_types",
                table: "wrapped_keys");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vaults_name_len",
                table: "vaults");

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_keyrings_kdf_range",
                table: "user_keyrings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_keyrings_kdf_salt_len",
                table: "user_keyrings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_keyrings_private_key_len",
                table: "user_keyrings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_keyrings_public_key_len",
                table: "user_keyrings");

            migrationBuilder.DropIndex(
                name: "ix_sessions_revoked_at",
                table: "sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sessions_token_hash_len",
                table: "sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_secrets_name_len",
                table: "secrets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_secret_versions_aad_hash_len",
                table: "secret_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_secret_versions_payload_len",
                table: "secret_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_secret_versions_version_no",
                table: "secret_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_secret_versions_wrapped_dek_len",
                table: "secret_versions");

            migrationBuilder.RenameIndex(
                name: "ux_wrapped_keys_resource_recipient_version",
                table: "wrapped_keys",
                newName: "ix_wrapped_keys_tenant_id_resource_type_resource_id_recipient_");
        }
    }
}
