using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vivarium.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConcurrencyTokens : Migration
    {
        // xmin é coluna de SISTEMA do Postgres (já existe em toda tabela) usada como
        // token de concorrência otimista. Não há DDL a aplicar — esta migration
        // existe só pra manter o snapshot do modelo em sincronia.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
