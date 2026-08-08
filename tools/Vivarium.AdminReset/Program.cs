using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Vivarium.Api.Data;
using Vivarium.Api.Services;

// Ferramenta de manutenção local, sem endpoint HTTP nenhum — existe só pra ações
// administrativas pontuais (ex: resetar senha de uma conta de teste) sem precisar
// abrir uma rota na API que ficaria exposta sempre que alguém rodar `dev.cmd`
// (que conecta no mesmo Neon de produção). Roda uma vez e sai.
//
// Uso: dotnet run --project tools/Vivarium.AdminReset -- reset-password <email> <novaSenha>

if (args.Length != 3 || args[0] != "reset-password")
{
    Console.WriteLine("Uso: dotnet run --project tools/Vivarium.AdminReset -- reset-password <email> <novaSenha>");
    return 1;
}

string email = args[1];
string newPassword = args[2];

var config = new ConfigurationBuilder()
    .AddUserSecrets<Vivarium.Api.Data.VivariumDbContext>(optional: true)
    .AddEnvironmentVariables()
    .Build();

string? connectionString = config.GetConnectionString("Vivarium");
if (string.IsNullOrEmpty(connectionString))
{
    Console.WriteLine("Connection string 'Vivarium' não encontrada (user-secrets do Vivarium.Api ou env var ConnectionStrings__Vivarium).");
    return 1;
}

var options = new DbContextOptionsBuilder<VivariumDbContext>()
    .UseNpgsql(connectionString)
    .Options;

await using var db = new VivariumDbContext(options);
var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
if (user is null)
{
    Console.WriteLine($"Usuário com email '{email}' não encontrado.");
    return 1;
}

user.PasswordHash = PasswordHasher.Hash(newPassword);
await db.SaveChangesAsync();

Console.WriteLine($"Senha redefinida com sucesso: {user.Username} ({user.Email})");
return 0;
