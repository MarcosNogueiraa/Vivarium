using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vivarium.Api.Data;

namespace Vivarium.Api.Tests;

/// <summary>
/// API completa rodando contra SQLite in-memory (uma conexão viva por factory,
/// senão o banco some). Postgres fica pra staging; aqui validamos o fluxo HTTP.
/// </summary>
public class VivariumApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Jwt:Key", "chave-de-teste-suficientemente-longa-0123456789");
        builder.UseSetting("Jwt:Issuer", "vivarium");
        builder.UseSetting("Jwt:Audience", "vivarium");
        builder.UseSetting("ConnectionStrings:Vivarium", "Host=ignorado");

        builder.ConfigureServices(services =>
        {
            var descriptors = services
                .Where(d => d.ServiceType.Name.Contains("DbContextOptions"))
                .ToList();
            foreach (var d in descriptors)
                services.Remove(d);

            _connection.Open();
            services.AddDbContext<VivariumDbContext>(o => o.UseSqlite(_connection));

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<VivariumDbContext>().Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection.Dispose();
    }

    /// <summary>Executa uma ação com acesso direto ao banco (pra montar cenários de teste).</summary>
    public async Task WithDbAsync(Func<VivariumDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VivariumDbContext>();
        await action(db);
        await db.SaveChangesAsync();
    }

    public async Task<(HttpClient Client, long UserId)> RegisterAsync(string username)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            username,
            email = $"{username}@teste.com",
            password = "senha-forte-123",
        });
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return (client, auth.UserId);
    }

    public record AuthDto(long UserId, string Username, string Token);
}
