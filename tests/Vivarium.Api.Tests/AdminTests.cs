using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Vivarium.Api.Tests;

public class AdminTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public AdminTests(VivariumApiFactory factory) => _factory = factory;

    private async Task TornarAdmin(long userId)
    {
        await _factory.WithDbAsync(async db =>
        {
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            user.IsAdmin = true;
        });
    }

    [Fact]
    public async Task NaoAdmin_Retorna403()
    {
        var (client, _) = await _factory.RegisterAsync("naoadmin1");

        var response = await client.PostAsync("/api/admin/give-starter-fish-all", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_DaPeixeATodosOsAquariosComEspacoNaFila()
    {
        var (adminClient, adminId) = await _factory.RegisterAsync("admin1");
        await TornarAdmin(adminId);

        // Outro jogador que já coletou o peixe inicial (fila vazia) — deve ganhar +1.
        var (otherClient, _) = await _factory.RegisterAsync("outro1");
        var otherTank = await otherClient.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        await otherClient.PostAsync($"/api/game/collect/{otherTank!.Queue[0].Id}", null);

        var response = await adminClient.PostAsync("/api/admin/give-starter-fish-all", null);
        response.EnsureSuccessStatusCode();

        var adminTankAfter = await adminClient.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        var otherTankAfter = await otherClient.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");

        // Admin ainda tinha o peixe inicial pendente (fila cheia pro cap? não, cap=5, só 1
        // pendente) — deve ganhar +1 também (agora 2 na fila).
        Assert.Equal(2, adminTankAfter!.Queue.Count);
        // Outro já tinha coletado (fila vazia) — deve ganhar +1 (agora 1 na fila).
        Assert.Single(otherTankAfter!.Queue);
    }
}
