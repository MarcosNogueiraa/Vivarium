using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Vivarium.Api.Data;
using Vivarium.Api.Services;
using Vivarium.Core.Domain;

// Ferramenta de manutenção local, sem endpoint HTTP nenhum — existe só pra ações
// administrativas pontuais (ex: resetar senha de uma conta de teste, listar contas
// suspeitas de teste, apagar contas de teste residuais) sem precisar abrir uma rota
// na API que ficaria exposta sempre que alguém rodar `dev.cmd` (que conecta no
// mesmo Neon de produção). Roda uma vez e sai.
//
// Uso:
//   dotnet run --project tools/Vivarium.AdminReset -- reset-password <email> <novaSenha>
//   dotnet run --project tools/Vivarium.AdminReset -- list-users
//   dotnet run --project tools/Vivarium.AdminReset -- check-cross-refs <id1,id2,...>
//   dotnet run --project tools/Vivarium.AdminReset -- delete-users <id1,id2,...>

if (args.Length == 0)
{
    Console.WriteLine("Uso:");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- reset-password <email> <novaSenha>");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- list-users");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- check-cross-refs <id1,id2,...>");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- delete-users <id1,id2,...>");
    return 1;
}

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

switch (args[0])
{
    case "reset-password":
    {
        if (args.Length != 3)
        {
            Console.WriteLine("Uso: dotnet run --project tools/Vivarium.AdminReset -- reset-password <email> <novaSenha>");
            return 1;
        }
        string email = args[1];
        string newPassword = args[2];
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
    }
    case "list-users":
    {
        // Só leitura — lista todo mundo com data de criação e um resumo do tanque
        // (nº de peixes + raridade total), pra ajudar a distinguir conta de teste
        // (residual de sessão dev, que sempre bate no mesmo Neon de produção) de
        // conta de jogador de verdade, sem apagar nada sozinho.
        var rows = await db.Users
            .OrderBy(u => u.CreatedAt)
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.Email,
                u.CreatedAt,
                u.IsAdmin,
                FishCount = db.CreatureInstances.Count(c => c.OwnerId == u.Id && c.HabitatId != null),
                RarityTotal = db.CreatureInstances
                    .Where(c => c.OwnerId == u.Id && c.HabitatId != null)
                    .Sum(c => (decimal?)c.RarityScore) ?? 0m,
            })
            .ToListAsync();

        Console.WriteLine($"{"Id",-6} {"Username",-24} {"Email",-34} {"CriadoEm",-20} {"Admin",-6} {"Peixes",-7} {"RarTotal",-9}");
        foreach (var r in rows)
        {
            Console.WriteLine($"{r.Id,-6} {r.Username,-24} {r.Email,-34} {r.CreatedAt:yyyy-MM-dd HH:mm,-20} {(r.IsAdmin ? "sim" : ""),-6} {r.FishCount,-7} {r.RarityTotal,-9:0.0}");
        }
        Console.WriteLine($"\nTotal: {rows.Count} usuário(s).");
        return 0;
    }
    case "check-cross-refs":
    {
        if (args.Length != 2)
        {
            Console.WriteLine("Uso: dotnet run --project tools/Vivarium.AdminReset -- check-cross-refs <id1,id2,...>");
            return 1;
        }
        var ids = args[1].Split(',').Select(long.Parse).ToArray();
        bool clean = await CheckCrossRefsAsync(db, ids);
        return clean ? 0 : 1;
    }
    case "delete-users":
    {
        if (args.Length != 2)
        {
            Console.WriteLine("Uso: dotnet run --project tools/Vivarium.AdminReset -- delete-users <id1,id2,...>");
            return 1;
        }
        var ids = args[1].Split(',').Select(long.Parse).ToArray();

        var users = await db.Users.Where(u => ids.Contains(u.Id)).ToListAsync();
        Console.WriteLine("Vai apagar:");
        foreach (var u in users)
            Console.WriteLine($"  #{u.Id} {u.Username} ({u.Email})");
        if (users.Count != ids.Length)
            Console.WriteLine($"AVISO: {ids.Length - users.Count} id(s) não encontrado(s) — ignorados.");

        if (!await CheckCrossRefsAsync(db, ids))
        {
            Console.WriteLine("\nAbortado — resolva as referências cruzadas acima antes de apagar.");
            return 1;
        }

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            foreach (long uid in ids)
            {
                await db.TransactionLogs.Where(t => t.FromUserId == uid || t.ToUserId == uid).ExecuteDeleteAsync();
                await db.MarketListings.Where(m => m.SellerId == uid || m.BuyerId == uid).ExecuteDeleteAsync();
                await db.BreedingSlots.Where(s => s.UserId == uid).ExecuteDeleteAsync();
                // Quebra o auto-relacionamento (ParentAId/ParentBId, FK Restrict) antes de apagar
                // as próprias criaturas — já confirmado acima que nada FORA desse conjunto de
                // usuários referencia essas criaturas como pai/mãe.
                await db.CreatureInstances.Where(c => c.OwnerId == uid)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.ParentAId, (long?)null).SetProperty(c => c.ParentBId, (long?)null));
                await db.CreatureInstances.Where(c => c.OwnerId == uid).ExecuteDeleteAsync();
                await db.UserInventories.Where(i => i.UserId == uid).ExecuteDeleteAsync();
                await db.WalletBalances.Where(w => w.UserId == uid).ExecuteDeleteAsync();
                var habitatIds = await db.Habitats.Where(h => h.UserId == uid).Select(h => h.Id).ToListAsync();
                await db.GenerationQueueItems.Where(q => habitatIds.Contains(q.HabitatId)).ExecuteDeleteAsync();
                await db.Habitats.Where(h => h.UserId == uid).ExecuteDeleteAsync();
                await db.VipSubscriptions.Where(v => v.UserId == uid).ExecuteDeleteAsync();
                await db.Users.Where(u => u.Id == uid).ExecuteDeleteAsync();
            }
            await tx.CommitAsync();
            Console.WriteLine($"\n{users.Count} conta(s) apagada(s) com sucesso.");
            return 0;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Console.WriteLine($"\nERRO, nada foi apagado (rollback): {ex.Message}");
            return 1;
        }
    }
    default:
        Console.WriteLine("Comando desconhecido. Use 'reset-password', 'list-users', 'check-cross-refs' ou 'delete-users'.");
        return 1;
}

static async Task<bool> CheckCrossRefsAsync(VivariumDbContext db, long[] ids)
{
    bool clean = true;

    // Peixe de fora do conjunto que tem um pai/mãe pertencente a alguém do conjunto —
    // apagar o pai quebraria a linhagem de uma conta que NÃO está sendo removida.
    var crossParentage = await db.CreatureInstances
        .Where(c => !ids.Contains(c.OwnerId) &&
            ((c.ParentAId != null && db.CreatureInstances.Any(p => p.Id == c.ParentAId && ids.Contains(p.OwnerId))) ||
             (c.ParentBId != null && db.CreatureInstances.Any(p => p.Id == c.ParentBId && ids.Contains(p.OwnerId)))))
        .Select(c => new { c.Id, c.OwnerId })
        .ToListAsync();
    if (crossParentage.Count > 0)
    {
        clean = false;
        Console.WriteLine($"BLOQUEIO: {crossParentage.Count} criatura(s) de OUTRO usuário têm pai/mãe pertencente a uma das contas a apagar:");
        foreach (var c in crossParentage)
            Console.WriteLine($"  criatura #{c.Id} (dono #{c.OwnerId})");
    }

    // Listagem de mercado envolvendo alguém de fora do conjunto (comprador ou vendedor real).
    var crossListings = await db.MarketListings
        .Where(m => (ids.Contains(m.SellerId) && !ids.Contains(m.BuyerId ?? -1) && m.BuyerId != null)
                 || (!ids.Contains(m.SellerId) && m.BuyerId != null && ids.Contains(m.BuyerId.Value)))
        .Select(m => new { m.Id, m.SellerId, m.BuyerId })
        .ToListAsync();
    if (crossListings.Count > 0)
    {
        clean = false;
        Console.WriteLine($"BLOQUEIO: {crossListings.Count} listagem(ns) de mercado envolvem comprador/vendedor de fora do conjunto:");
        foreach (var m in crossListings)
            Console.WriteLine($"  listagem #{m.Id} vendedor #{m.SellerId} comprador #{m.BuyerId}");
    }

    // Transferência direta (TransactionLog) de/pra alguém de fora do conjunto.
    var crossTx = await db.TransactionLogs
        .Where(t => (t.FromUserId != null && ids.Contains(t.FromUserId.Value) && t.ToUserId != null && !ids.Contains(t.ToUserId.Value))
                 || (t.ToUserId != null && ids.Contains(t.ToUserId.Value) && t.FromUserId != null && !ids.Contains(t.FromUserId.Value)))
        .Select(t => new { t.Id, t.Type, t.FromUserId, t.ToUserId })
        .ToListAsync();
    if (crossTx.Count > 0)
    {
        clean = false;
        Console.WriteLine($"BLOQUEIO: {crossTx.Count} transação(ões) envolvem usuário de fora do conjunto:");
        foreach (var t in crossTx)
            Console.WriteLine($"  transação #{t.Id} ({t.Type}) de #{t.FromUserId} pra #{t.ToUserId}");
    }

    if (clean)
        Console.WriteLine("Sem referências cruzadas com contas de fora do conjunto — seguro apagar.");
    return clean;
}
