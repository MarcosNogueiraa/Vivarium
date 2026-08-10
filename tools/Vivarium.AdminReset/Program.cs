using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Vivarium.Api.Data;
using Vivarium.Api.Services;
using Vivarium.Core.Domain;
using Vivarium.Core.Gameplay;

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
//   dotnet run --project tools/Vivarium.AdminReset -- list-creatures <email>

if (args.Length == 0)
{
    Console.WriteLine("Uso:");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- reset-password <email> <novaSenha>");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- list-users");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- check-cross-refs <id1,id2,...>");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- delete-users <id1,id2,...>");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- list-creatures <email>");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- diff-scores [email]");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- fix-scores");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- finish-all-breeding");
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
    case "dump-traits":
    {
        if (args.Length != 2)
        {
            Console.WriteLine("Uso: dotnet run --project tools/Vivarium.AdminReset -- dump-traits <id1,id2,...>");
            return 1;
        }
        var ids = args[1].Split(',').Select(long.Parse).ToArray();
        var creatures = await db.CreatureInstances.Where(c => ids.Contains(c.Id)).ToListAsync();
        foreach (long id in ids)
        {
            var c = creatures.FirstOrDefault(x => x.Id == id);
            if (c is null) { Console.WriteLine($"#{id}: não encontrada"); continue; }

            Vivarium.Core.Generation.CreatureTraits traits;
            if (c.ParentASeed is { } pa && c.ParentBSeed is { } pb)
            {
                var ancestryA = new Vivarium.Core.Generation.TraitGenerator.ParentAncestry(pa, c.ParentAGrandparentASeed, c.ParentAGrandparentBSeed);
                var ancestryB = new Vivarium.Core.Generation.TraitGenerator.ParentAncestry(pb, c.ParentBGrandparentASeed, c.ParentBGrandparentBSeed);
                traits = Vivarium.Core.Generation.TraitGenerator.BreedTraits(
                    c.Seed, ancestryA, ancestryB, c.TraitConfigVersion,
                    BreedingDefaults.MutationChance, BreedingDefaults.RarityBiasStrength, BreedingDefaults.GrandparentReachChance);
                Console.WriteLine($"#{c.Id} (FILHOTE) Seed={c.Seed} ParentASeed={pa} ParentBSeed={pb} ParentAGpA={c.ParentAGrandparentASeed} ParentAGpB={c.ParentAGrandparentBSeed} ParentBGpA={c.ParentBGrandparentASeed} ParentBGpB={c.ParentBGrandparentBSeed}");
            }
            else
            {
                traits = Vivarium.Core.Generation.TraitGenerator.Generate(c.Seed, c.TraitConfigVersion);
                Console.WriteLine($"#{c.Id} (normal) Seed={c.Seed}");
            }
            Console.WriteLine($"    RarityScore no banco={c.RarityScore}  Recalculado agora={traits.RarityScore}");
            Console.WriteLine($"    Shimmer: tier={traits.ShimmerTier} cor={traits.ShimmerColor} opacidade={traits.ShimmerOpacity:0.0}");
            Console.WriteLine($"    Cauda:   cor={traits.Tail.Color} padrão={traits.Tail.Pattern} corPadrão={traits.Tail.PatternColor} tam={traits.Tail.PatternSize:0.0} op={traits.Tail.PatternOpacity:0.0}");
            Console.WriteLine($"    Dorsal:  cor={traits.Dorsal.Color} padrão={traits.Dorsal.Pattern} corPadrão={traits.Dorsal.PatternColor} tam={traits.Dorsal.PatternSize:0.0} op={traits.Dorsal.PatternOpacity:0.0}");
            Console.WriteLine($"    Peitoral:cor={traits.Pectoral.Color} padrão={traits.Pectoral.Pattern} corPadrão={traits.Pectoral.PatternColor} tam={traits.Pectoral.PatternSize:0.0} op={traits.Pectoral.PatternOpacity:0.0}");
            Console.WriteLine($"    Movimento: caudaSpeed={traits.Movement.TailSpeed:0.0} finSpeed={traits.Movement.FinSpeed:0.0}");
            Console.WriteLine();
        }
        return 0;
    }
    case "diff-scores":
    {
        // Só leitura — mede o estrago do bug de versionamento (TraitConfigVersion nunca
        // incrementado apesar de mudanças de peso já terem acontecido): recalcula o score de
        // TODA criatura viva (não vendida, não morta) com o motor ATUAL e compara com o que
        // está gravado. Não escreve nada. Segundo argumento opcional filtra por email (detalhe).
        string? filterEmail = args.Length >= 2 ? args[1] : null;
        long? filterUserId = null;
        if (filterEmail != null)
        {
            var fu = await db.Users.FirstOrDefaultAsync(u => u.Email == filterEmail);
            if (fu is null) { Console.WriteLine($"Usuário '{filterEmail}' não encontrado."); return 1; }
            filterUserId = fu.Id;
        }
        var all = await db.CreatureInstances
            .Where(c => !c.IsDead && c.SoldAt == null && (filterUserId == null || c.OwnerId == filterUserId))
            .ToListAsync();
        int changed = 0;
        double maxDelta = 0;
        long maxDeltaId = 0;
        var byUser = new Dictionary<long, (int Count, decimal TotalDelta)>();
        foreach (var c in all)
        {
            double recalculated;
            if (c.ParentASeed is { } pa && c.ParentBSeed is { } pb)
            {
                var ancestryA = new Vivarium.Core.Generation.TraitGenerator.ParentAncestry(pa, c.ParentAGrandparentASeed, c.ParentAGrandparentBSeed);
                var ancestryB = new Vivarium.Core.Generation.TraitGenerator.ParentAncestry(pb, c.ParentBGrandparentASeed, c.ParentBGrandparentBSeed);
                recalculated = Vivarium.Core.Generation.TraitGenerator.BreedTraits(
                    c.Seed, ancestryA, ancestryB, c.TraitConfigVersion,
                    BreedingDefaults.MutationChance, BreedingDefaults.RarityBiasStrength, BreedingDefaults.GrandparentReachChance).RarityScore;
            }
            else
            {
                recalculated = Vivarium.Core.Generation.TraitGenerator.Generate(c.Seed, c.TraitConfigVersion).RarityScore;
            }
            decimal delta = (decimal)recalculated - c.RarityScore;
            if (Math.Abs((double)delta) > 0.05)
            {
                changed++;
                (int Count, decimal TotalDelta) cur = byUser.TryGetValue(c.OwnerId, out var v) ? v : (0, 0m);
                byUser[c.OwnerId] = (cur.Count + 1, cur.TotalDelta + delta);
                if (Math.Abs((double)delta) > Math.Abs(maxDelta)) { maxDelta = (double)delta; maxDeltaId = c.Id; }
                if (filterUserId != null)
                    Console.WriteLine($"  #{c.Id,-5} HabitatId={c.HabitatId,-5} gravado={c.RarityScore,7:0.00}  recalculado={recalculated,7:0.00}  delta={delta,7:+0.00;-0.00}  {(c.ParentAId != null ? "(filhote)" : "")}");
            }
        }
        Console.WriteLine($"Total de criaturas vivas/ativas: {all.Count}");
        Console.WriteLine($"Com score divergente (>0.05): {changed} ({100.0 * changed / Math.Max(1, all.Count):0.0}%)");
        Console.WriteLine($"Maior divergência: criatura #{maxDeltaId}, delta={maxDelta:0.00}");
        Console.WriteLine();
        var userIds = byUser.Keys.ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username);
        foreach (var kv in byUser.OrderByDescending(kv => Math.Abs(kv.Value.TotalDelta)))
            Console.WriteLine($"  {users.GetValueOrDefault(kv.Key, $"#{kv.Key}"),-20} {kv.Value.Count,4} peixe(s) divergente(s), delta total={kv.Value.TotalDelta:0.0}");
        return 0;
    }
    case "fix-scores":
    {
        // Corrige o bug de RarityScore desatualizado (TraitConfigVersion nunca incrementado
        // apesar de mudanças de peso já terem acontecido — ver diff-scores): recalcula com o
        // motor ATUAL e sobrescreve o campo gravado pra TODA criatura viva (não vendida, não
        // morta) cujo score divergir. Não muda Seed/traits/ancestralidade — só sincroniza o
        // número com o que já está sendo renderizado/usado ao vivo. Dentro de uma transação.
        var all = await db.CreatureInstances.Where(c => !c.IsDead && c.SoldAt == null).ToListAsync();
        int fixedCount = 0;
        decimal totalDelta = 0;
        foreach (var c in all)
        {
            double recalculated;
            if (c.ParentASeed is { } pa && c.ParentBSeed is { } pb)
            {
                var ancestryA = new Vivarium.Core.Generation.TraitGenerator.ParentAncestry(pa, c.ParentAGrandparentASeed, c.ParentAGrandparentBSeed);
                var ancestryB = new Vivarium.Core.Generation.TraitGenerator.ParentAncestry(pb, c.ParentBGrandparentASeed, c.ParentBGrandparentBSeed);
                recalculated = Vivarium.Core.Generation.TraitGenerator.BreedTraits(
                    c.Seed, ancestryA, ancestryB, c.TraitConfigVersion,
                    BreedingDefaults.MutationChance, BreedingDefaults.RarityBiasStrength, BreedingDefaults.GrandparentReachChance).RarityScore;
            }
            else
            {
                recalculated = Vivarium.Core.Generation.TraitGenerator.Generate(c.Seed, c.TraitConfigVersion).RarityScore;
            }
            decimal delta = (decimal)recalculated - c.RarityScore;
            if (Math.Abs((double)delta) > 0.05)
            {
                Console.WriteLine($"  #{c.Id,-5} {c.RarityScore,7:0.00} -> {(decimal)recalculated,7:0.00}  (delta {delta,7:+0.00;-0.00})");
                c.RarityScore = (decimal)recalculated;
                fixedCount++;
                totalDelta += delta;
            }
        }
        await db.SaveChangesAsync();
        Console.WriteLine($"\n{fixedCount} criatura(s) corrigida(s). Delta total={totalDelta:0.0}.");
        return 0;
    }
    case "list-creatures":
    {
        if (args.Length != 2)
        {
            Console.WriteLine("Uso: dotnet run --project tools/Vivarium.AdminReset -- list-creatures <email>");
            return 1;
        }
        string email = args[1];
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
        {
            Console.WriteLine($"Usuário com email '{email}' não encontrado.");
            return 1;
        }
        // Só leitura — dump completo (sem derivar traits, só o que está gravado) pra
        // investigar discrepância de exibição entre telas (score/renda divergentes
        // pra peixes que parecem "iguais" na UI).
        var creatures = await db.CreatureInstances
            .Where(c => c.OwnerId == user.Id)
            .OrderBy(c => c.Id)
            .ToListAsync();
        Console.WriteLine($"Usuário #{user.Id} {user.Username} — {creatures.Count} criatura(s):\n");
        foreach (var c in creatures)
        {
            Console.WriteLine($"#{c.Id}  Seed={c.Seed}  RarityScore={c.RarityScore}  TraitConfigVersion={c.TraitConfigVersion}  HabitatId={c.HabitatId}  IsDead={c.IsDead}  SoldAt={c.SoldAt}");
            Console.WriteLine($"    ParentAId={c.ParentAId} ParentBId={c.ParentBId} ParentASeed={c.ParentASeed} ParentBSeed={c.ParentBSeed}");
            Console.WriteLine($"    CreatedAt={c.CreatedAt:yyyy-MM-dd HH:mm:ss}");
        }
        return 0;
    }
    case "finish-all-breeding":
    {
        // Zera o tempo restante de TODA gestação em andamento (Status=InProgress, ReadyAt no
        // futuro) — gesto único pra quem já tinha cruzamento rodando poder aproveitar o corte
        // de 10x na gestação (CLAUDE.md §8.11) sem esperar o prazo antigo, mais lento, que já
        // estava travado no ReadyAt calculado no momento do Start (não muda sozinho quando a
        // config muda). Não mexe em pais/filhote/risco — só antecipa a data de coleta.
        var now = DateTime.UtcNow;
        var slots = await db.BreedingSlots
            .Where(s => s.Status == BreedingStatus.InProgress && s.ReadyAt > now)
            .ToListAsync();
        foreach (var s in slots)
        {
            Console.WriteLine($"  slot #{s.Id} (usuário #{s.UserId}): ReadyAt {s.ReadyAt:yyyy-MM-dd HH:mm:ss} -> agora");
            s.ReadyAt = now;
        }
        await db.SaveChangesAsync();
        Console.WriteLine($"\n{slots.Count} gestação(ões) em andamento liberada(s) pra coleta imediata.");
        return 0;
    }
    default:
        Console.WriteLine("Comando desconhecido. Use 'reset-password', 'list-users', 'check-cross-refs', 'delete-users' ou 'list-creatures'.");
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
