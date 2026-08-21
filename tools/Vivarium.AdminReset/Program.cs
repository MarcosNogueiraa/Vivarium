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
//   dotnet run --project tools/Vivarium.AdminReset -- band-distribution
//   dotnet run --project tools/Vivarium.AdminReset -- check-cross-refs <id1,id2,...>
//   dotnet run --project tools/Vivarium.AdminReset -- delete-users <id1,id2,...>
//   dotnet run --project tools/Vivarium.AdminReset -- list-creatures <email>
//   dotnet run --project tools/Vivarium.AdminReset -- reset-account <email>
//   dotnet run --project tools/Vivarium.AdminReset -- give-seed <username-ou-email> <seed>
//   dotnet run --project tools/Vivarium.AdminReset -- give-premium-all <quantidade>
//   dotnet run --project tools/Vivarium.AdminReset -- delete-creature <id>
//   dotnet run --project tools/Vivarium.AdminReset -- reset-daily-reward-all

if (args.Length == 0)
{
    Console.WriteLine("Uso:");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- reset-password <email> <novaSenha>");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- list-users");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- check-cross-refs <id1,id2,...>");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- delete-users <id1,id2,...>");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- list-creatures <email>");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- dump-traits <id1,id2,...>");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- backfill-traits");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- audit-ancestry");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- finish-all-breeding");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- reset-account <email>");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- give-seed <username-ou-email> <seed>");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- give-premium-all <quantidade>");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- delete-creature <id>");
    Console.WriteLine("  dotnet run --project tools/Vivarium.AdminReset -- reset-daily-reward-all");
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
    case "tank-income":
    {
        // Só leitura — breakdown REAL de renda do tanque de um jogador (raridade + sinergia por
        // parte), pra investigar relato de "sinergia ainda tá muito forte" com dados de produção
        // em vez de só simulação sintética. Reusa IncomeCalculator/PartColorsResolver (mesmo
        // motor do jogo), não reimplementa nada.
        if (args.Length != 2)
        {
            Console.WriteLine("Uso: dotnet run --project tools/Vivarium.AdminReset -- tank-income <email>");
            return 1;
        }
        string tiEmail = args[1];
        var tiUser = await db.Users.FirstOrDefaultAsync(u => u.Email == tiEmail);
        if (tiUser is null) { Console.WriteLine($"Usuário com email '{tiEmail}' não encontrado."); return 1; }

        int tiAquariumTypeId = await db.HabitatTypes.Where(h => h.Code == "Aquarium").Select(h => h.Id).FirstAsync();
        var tiHabitat = await db.Habitats.FirstAsync(h => h.UserId == tiUser.Id && h.HabitatTypeId == tiAquariumTypeId);
        var tiFish = await db.CreatureInstances
            .Where(c => c.HabitatId == tiHabitat.Id && !c.IsDead)
            .ToListAsync();

        var tiConfig = new TickConfig();
        var incomes = tiFish.Select(c =>
        {
            var (tail, dorsal, pectoral) = PartColorsResolver.Of(c);
            return new FishIncome(c.RarityScore, tail, dorsal, pectoral);
        }).ToList();

        var tailCounts = incomes.GroupBy(f => f.TailColor).ToDictionary(g => g.Key, g => g.Count());
        var dorsalCounts = incomes.GroupBy(f => f.DorsalColor).ToDictionary(g => g.Key, g => g.Count());
        var pectoralCounts = incomes.GroupBy(f => f.PectoralColor).ToDictionary(g => g.Key, g => g.Count());

        Console.WriteLine($"Usuário #{tiUser.Id} {tiUser.Username} — {tiFish.Count} peixe(s) no tanque, água={tiHabitat.MaintenanceLevel:0}%\n");
        double totalBase = 0, totalWithSynergy = 0;
        for (int i = 0; i < tiFish.Count; i++)
        {
            var c = tiFish[i];
            var f = incomes[i];
            double baseRate = IncomeCalculator.CoinsPerHour(f.RarityScore, tiConfig);
            double synergy = IncomeCalculator.SynergyMultiplier(
                tailCounts[f.TailColor], dorsalCounts[f.DorsalColor], pectoralCounts[f.PectoralColor], tiConfig);
            double finalRate = baseRate * synergy;
            totalBase += baseRate;
            totalWithSynergy += finalRate;
            Console.WriteLine($"#{c.Id,-6} score={f.RarityScore,6:0.00}  base={baseRate,7:0.00}/h  " +
                $"tail={f.TailColor}({tailCounts[f.TailColor]}) dorsal={f.DorsalColor}({dorsalCounts[f.DorsalColor]}) pectoral={f.PectoralColor}({pectoralCounts[f.PectoralColor]})  " +
                $"sinergia=×{synergy:0.000} ({(synergy - 1) * 100:+0.0;-0.0}%)  final={finalRate,7:0.00}/h");
        }
        double water = IncomeCalculator.WaterFactor(tiHabitat.MaintenanceLevel, tiConfig);
        Console.WriteLine($"\nTotal base (sem sinergia): {totalBase:0.00}/h");
        Console.WriteLine($"Total com sinergia:        {totalWithSynergy:0.00}/h  (+{(totalWithSynergy / totalBase - 1) * 100:0.0}% sobre o base)");
        Console.WriteLine($"Total real (com água {tiHabitat.MaintenanceLevel:0}%, fator {water:0.000}): {totalWithSynergy * water:0.00}/h");
        return 0;
    }
    case "band-distribution":
    {
        // Só leitura — distribuição REAL de faixa de raridade na população viva (exclui mortos
        // e vendidos ao NPC, que já saíram do jogo). Cortes espelham BANDS (fishRenderer.js) e
        // MarketService.BandNameOf — atualizar aqui junto se a pirâmide mudar de novo.
        // Argumento opcional <desde> (yyyy-MM-dd[THH:mm]) filtra por CreatedAt — útil pra
        // comparar a população NOVA (nascida depois de um rebalanceamento) contra o legado
        // (traits congelados no nascimento — peixe antigo nunca migra de faixa sozinho).
        DateTime? since = null;
        if (args.Length > 1 && DateTime.TryParse(args[1], out var parsed))
            since = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);

        var query = db.CreatureInstances.Where(c => !c.IsDead && c.SoldAt == null);
        if (since is not null) query = query.Where(c => c.CreatedAt >= since);
        var rows = await query.Select(c => new { c.RarityScore, IsBred = c.ParentAId != null }).ToListAsync();

        (string Name, decimal Max)[] bands =
        [
            ("Comum", 5.45m), ("Incomum", 12.04m), ("Raro", 13.78m), ("Épico", 16.60m), ("Lendário", decimal.MaxValue),
        ];
        void PrintTable(string label, IReadOnlyList<decimal> scores)
        {
            decimal lo = decimal.MinValue;
            Console.WriteLine($"\n{label} — {scores.Count} criatura(s)");
            Console.WriteLine($"{"Faixa",-10} {"Peixes",-8} {"%",-8}");
            foreach (var (name, hi) in bands)
            {
                int n = scores.Count(s => s >= lo && s < hi);
                double pct = scores.Count == 0 ? 0 : n / (double)scores.Count * 100;
                Console.WriteLine($"{name,-10} {n,-8} {pct,-8:0.00}%");
                lo = hi;
            }
        }
        Console.WriteLine(since is null ? "Todas as criaturas vivas" : $"Criaturas vivas desde {since:yyyy-MM-dd HH:mm} UTC");
        // Fresco (Generate direto do seed) vs filhote (BreedTraits, herança com viés de raridade
        // — pode manter a raridade dos pais mesmo com os pesos de geração nova bem mais raros;
        // ver CLAUDE.md §8.8) — separar ajuda a achar a causa real de um desequilíbrio percebido.
        PrintTable("TODAS", rows.Select(r => r.RarityScore).ToList());
        PrintTable("Só peixe FRESCO (não-filhote)", rows.Where(r => !r.IsBred).Select(r => r.RarityScore).ToList());
        PrintTable("Só FILHOTE (cruzamento)", rows.Where(r => r.IsBred).Select(r => r.RarityScore).ToList());
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
        // Só leitura — desde 13/08/2026 os traits já vêm congelados em TraitsJson no
        // nascimento, então isso é só uma leitura direta, sem motor de geração envolvido
        // (ver backfill-traits/audit-ancestry abaixo pro que substituiu diff-scores/fix-scores).
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
            if (string.IsNullOrEmpty(c.TraitsJson))
            {
                Console.WriteLine($"#{c.Id}: sem TraitsJson (rodar backfill-traits primeiro)");
                continue;
            }
            var traits = Vivarium.Core.Generation.TraitsSerialization.DeserializeTraits(c.TraitsJson);
            Console.WriteLine($"#{c.Id} {(c.ParentAId != null ? "(FILHOTE)" : "(normal)")} Seed={c.Seed} RarityScore no banco={c.RarityScore}");
            Console.WriteLine($"    Shimmer: tier={traits.ShimmerTier} cor={traits.ShimmerColor} opacidade={traits.ShimmerOpacity:0.0}");
            Console.WriteLine($"    Cauda:   cor={traits.Tail.Color} padrão={traits.Tail.Pattern} corPadrão={traits.Tail.PatternColor} tam={traits.Tail.PatternSize:0.0} op={traits.Tail.PatternOpacity:0.0}");
            Console.WriteLine($"    Dorsal:  cor={traits.Dorsal.Color} padrão={traits.Dorsal.Pattern} corPadrão={traits.Dorsal.PatternColor} tam={traits.Dorsal.PatternSize:0.0} op={traits.Dorsal.PatternOpacity:0.0}");
            Console.WriteLine($"    Peitoral:cor={traits.Pectoral.Color} padrão={traits.Pectoral.Pattern} corPadrão={traits.Pectoral.PatternColor} tam={traits.Pectoral.PatternSize:0.0} op={traits.Pectoral.PatternOpacity:0.0}");
            Console.WriteLine($"    Movimento: caudaSpeed={traits.Movement.TailSpeed:0.0} finSpeed={traits.Movement.FinSpeed:0.0}");
            Console.WriteLine();
        }
        return 0;
    }
    case "backfill-traits":
    {
        // Congela TraitsJson pra TODA criatura já existente (inclusive mortas/vendidas —
        // preserva histórico) — passo único de migração antes do deploy do motor novo
        // (traits congelados no nascimento, ver CLAUDE.md §8.19.1/plano "traits congelados").
        //
        // Processa em ordem de criação (mais antigos primeiro) e resolve filhotes lendo os
        // traits JÁ CALCULADOS dos pais nesta mesma passada (dicionário em memória por Id) —
        // não a reconstrução antiga por ancestralidade limitada a 2 gerações. Isso corrige de
        // vez qualquer divergência de profundidade (o próprio bug que motivou esta migração),
        // porque cada filhote deriva do valor CONGELADO real do pai, sem limite de gerações.
        var all = await db.CreatureInstances.OrderBy(c => c.CreatedAt).ToListAsync();
        var resolved = new Dictionary<long, Vivarium.Core.Generation.CreatureTraits>();
        int count = 0;
        foreach (var c in all)
        {
            Vivarium.Core.Generation.CreatureTraits traits;
            if (c.ParentAId is { } paId && c.ParentBId is { } pbId
                && resolved.TryGetValue(paId, out var ownA) && resolved.TryGetValue(pbId, out var ownB))
            {
                (traits, _) = Vivarium.Core.Generation.TraitGenerator.BreedTraits(
                    c.Seed, ownA, ownB,
                    BreedingDefaults.MutationChance, BreedingDefaults.RarityBiasStrength,
                    BreedingDefaults.MutationRarityBiasStrength, BreedingDefaults.AntiDuplicationDecay, BreedingDefaults.AntiDuplicationMaxPenalty);
            }
            else
            {
                traits = Vivarium.Core.Generation.TraitGenerator.Generate(c.Seed, Vivarium.Core.Generation.TraitConfigV1.Version);
            }
            c.TraitsJson = Vivarium.Core.Generation.TraitsSerialization.Serialize(traits);
            c.TraitConfigVersion = Vivarium.Core.Generation.TraitConfigV1.Version;
            decimal newScore = (decimal)traits.RarityScore;
            if (newScore != c.RarityScore)
                Console.WriteLine($"  #{c.Id,-5} RarityScore {c.RarityScore,7:0.00} -> {newScore,7:0.00}");
            c.RarityScore = newScore;
            resolved[c.Id] = traits;
            count++;
        }
        await db.SaveChangesAsync();
        Console.WriteLine($"\n{count} criatura(s) com TraitsJson preenchido.");
        return 0;
    }
    case "audit-ancestry":
    {
        // Só leitura — verifica, pra TODA criatura, se TraitsJson bate com o que o motor
        // produziria a partir do Seed (fresco) ou de BreedTraits(seed, traits-do-pai-A,
        // traits-do-pai-B) (filhote, usando os traits DO PAI já verificados na mesma
        // passada). Percorre a ancestralidade real via ParentAId/ParentBId, sem limite de
        // profundidade — detecta tanto bug futuro quanto adulteração direta no banco.
        var all = await db.CreatureInstances.OrderBy(c => c.CreatedAt).ToListAsync();
        var verified = new Dictionary<long, Vivarium.Core.Generation.CreatureTraits>();
        int mismatches = 0;
        foreach (var c in all)
        {
            Vivarium.Core.Generation.CreatureTraits expected;
            if (c.ParentAId is { } paId && c.ParentBId is { } pbId
                && verified.TryGetValue(paId, out var ownA) && verified.TryGetValue(pbId, out var ownB))
            {
                (expected, _) = Vivarium.Core.Generation.TraitGenerator.BreedTraits(
                    c.Seed, ownA, ownB,
                    BreedingDefaults.MutationChance, BreedingDefaults.RarityBiasStrength,
                    BreedingDefaults.MutationRarityBiasStrength, BreedingDefaults.AntiDuplicationDecay, BreedingDefaults.AntiDuplicationMaxPenalty);
            }
            else
            {
                expected = Vivarium.Core.Generation.TraitGenerator.Generate(c.Seed, Vivarium.Core.Generation.TraitConfigV1.Version);
            }

            if (string.IsNullOrEmpty(c.TraitsJson))
            {
                Console.WriteLine($"#{c.Id}: TraitsJson vazio (rodar backfill-traits)");
                mismatches++;
                verified[c.Id] = expected;
                continue;
            }
            var stored = Vivarium.Core.Generation.TraitsSerialization.DeserializeTraits(c.TraitsJson);
            if (!stored.Equals(expected))
            {
                Console.WriteLine($"#{c.Id}: TraitsJson NÃO bate com o esperado (gravado score={stored.RarityScore:0.0000}, esperado={expected.RarityScore:0.0000})");
                mismatches++;
            }
            // Segue a cadeia com o valor GRAVADO (é o que realmente vale pros filhos) — assim
            // uma divergência num ancestral não mascara nem duplica divergências nos filhos.
            verified[c.Id] = stored;
        }
        Console.WriteLine($"\n{all.Count} criatura(s) verificada(s), {mismatches} divergente(s).");
        return mismatches == 0 ? 0 : 1;
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
    case "delete-creature":
    {
        // Apaga UMA criatura pontual (ex: um peixe de teste criado por give-seed). Bloqueia
        // se ela for pai/mãe de alguém (FK Restrict em ParentAId/BId — mesma checagem de
        // delete-users), se estiver listada no mercado, ou presa numa gestação ativa.
        if (args.Length != 2 || !long.TryParse(args[1], out long creatureId))
        {
            Console.WriteLine("Uso: dotnet run --project tools/Vivarium.AdminReset -- delete-creature <id>");
            return 1;
        }
        var creature = await db.CreatureInstances.FindAsync(creatureId);
        if (creature is null)
        {
            Console.WriteLine($"Criatura #{creatureId} não encontrada.");
            return 1;
        }
        bool isParent = await db.CreatureInstances.AnyAsync(c => c.ParentAId == creatureId || c.ParentBId == creatureId);
        bool listed = await db.MarketListings.AnyAsync(m => m.CreatureInstanceId == creatureId && m.Status == ListingStatus.Active);
        bool breeding = await db.BreedingSlots.AnyAsync(s => (s.ParentAId == creatureId || s.ParentBId == creatureId) && s.Status == BreedingStatus.InProgress);
        if (isParent || listed || breeding)
        {
            Console.WriteLine($"Abortado — criatura #{creatureId} tem referência ativa (pai de outra: {isParent}, listada: {listed}, em gestação: {breeding}).");
            return 1;
        }
        db.CreatureInstances.Remove(creature);
        await db.SaveChangesAsync();
        Console.WriteLine($"Criatura #{creatureId} (seed={creature.Seed}) apagada.");
        return 0;
    }
    case "reset-account":
    {
        // Reseta uma conta pro estado de recém-registrada (SEM apagar a conta em si — login,
        // senha, IsAdmin continuam). Apaga todo o progresso de jogo (criaturas, mochila, fila,
        // gestação, inventário) e reseta carteira/tanque pros mesmos valores que AuthEndpoints
        // usa no registro (HabitatDefaults/EconomyDefaults), incluindo o peixe inicial já
        // pronto pra coletar (8.13) — pra ficar indistinguível de uma conta nova de verdade.
        if (args.Length != 2)
        {
            Console.WriteLine("Uso: dotnet run --project tools/Vivarium.AdminReset -- reset-account <email>");
            return 1;
        }
        string email = args[1];
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
        {
            Console.WriteLine($"Usuário com email '{email}' não encontrado.");
            return 1;
        }

        var creatureIds = await db.CreatureInstances.Where(c => c.OwnerId == user.Id).Select(c => c.Id).ToListAsync();
        var listingCount = await db.MarketListings.CountAsync(m => m.SellerId == user.Id);
        var fishCount = await db.CreatureInstances.CountAsync(c => c.OwnerId == user.Id);
        Console.WriteLine($"Vai resetar: #{user.Id} {user.Username} ({user.Email}) — {fishCount} criatura(s), {listingCount} listagem(ns) como vendedor.");

        // Mesmo cuidado do delete-users: uma criatura de OUTRO usuário (transferida/vendida no
        // passado) pode ter essa conta como pai/mãe na linhagem (FK Restrict) — apagar sem
        // checar quebraria a árvore genealógica de quem recebeu.
        if (!await CheckCrossRefsAsync(db, [user.Id]))
        {
            Console.WriteLine("\nAbortado — resolva as referências cruzadas acima antes de resetar.");
            return 1;
        }

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            await db.TransactionLogs.Where(t => t.FromUserId == user.Id || t.ToUserId == user.Id).ExecuteDeleteAsync();
            await db.MarketListings.Where(m => m.SellerId == user.Id).ExecuteDeleteAsync();
            await db.BreedingSlots.Where(s => s.UserId == user.Id).ExecuteDeleteAsync();
            await db.CreatureInstances.Where(c => c.OwnerId == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ParentAId, (long?)null).SetProperty(c => c.ParentBId, (long?)null));
            await db.CreatureInstances.Where(c => c.OwnerId == user.Id).ExecuteDeleteAsync();
            await db.UserInventories.Where(i => i.UserId == user.Id).ExecuteDeleteAsync();

            var currencies = await db.CurrencyTypes.ToDictionaryAsync(c => c.Code, c => c.Id);
            var softWallet = await db.WalletBalances.FirstAsync(w => w.UserId == user.Id && w.CurrencyTypeId == currencies["SOFT"]);
            softWallet.Amount = EconomyDefaults.StartingSoftBalance;
            var premiumWallet = await db.WalletBalances.FirstAsync(w => w.UserId == user.Id && w.CurrencyTypeId == currencies["PREMIUM"]);
            premiumWallet.Amount = EconomyDefaults.StartingPremiumBalance;

            var now = DateTime.UtcNow;
            int aquariumTypeId = await db.HabitatTypes.Where(h => h.Code == "Aquarium").Select(h => h.Id).FirstAsync();
            int breedingTypeId = await db.HabitatTypes.Where(h => h.Code == "Breeding").Select(h => h.Id).FirstAsync();
            var aquarium = await db.Habitats.FirstAsync(h => h.UserId == user.Id && h.HabitatTypeId == aquariumTypeId);
            await db.GenerationQueueItems.Where(q => q.HabitatId == aquarium.Id).ExecuteDeleteAsync();
            aquarium.Capacity = HabitatDefaults.Capacity;
            aquarium.MaintenanceLevel = HabitatDefaults.MaintenanceLevel;
            aquarium.QueueCap = HabitatDefaults.QueueCap;
            aquarium.GenerationIntervalMinutes = HabitatDefaults.GenerationIntervalMinutes;
            aquarium.OnlineGenerationRate = HabitatDefaults.OnlineGenerationRate;
            aquarium.OfflineGenerationRate = HabitatDefaults.OfflineGenerationRate;
            aquarium.GenerationProgressMinutes = 0;
            aquarium.CoinAccrual = 0;
            aquarium.LastTickAt = now;
            aquarium.LastHeartbeatAt = null;

            int aquariumSpeciesId = await db.Species
                .Where(s => s.HabitatTypeId == aquariumTypeId).Select(s => s.Id).FirstAsync();
            db.GenerationQueueItems.Add(new GenerationQueueItem
            {
                HabitatId = aquarium.Id, SpeciesId = aquariumSpeciesId,
                ReadyAt = now, Status = QueueItemStatus.Pending, IsSick = false,
            });

            var breedingHabitat = await db.Habitats.FirstOrDefaultAsync(h => h.UserId == user.Id && h.HabitatTypeId == breedingTypeId);
            if (breedingHabitat is not null)
                breedingHabitat.LastTickAt = now;

            user.LastDailyRewardAt = null;

            await db.SaveChangesAsync();
            await tx.CommitAsync();
            Console.WriteLine($"\nConta #{user.Id} {user.Username} resetada — {creatureIds.Count} criatura(s) apagada(s), carteira/tanque/mochila/gestação voltaram ao estado de conta nova (com 1 peixe pronto na fila).");
            return 0;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Console.WriteLine($"\nERRO, nada foi alterado (rollback): {ex.Message}");
            return 1;
        }
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
    case "give-seed":
    {
        // Insere um CreatureInstance com um seed ESCOLHIDO (não sorteado na coleta) direto na
        // mochila do usuário (HabitatId=null) — pra dar um peixe específico (ex: o de maior
        // RarityScore achado por `Vivarium.Simulation best`, CLAUDE.md). Não fabrica traits: o
        // seed é real, os traits continuam 100% derivados dele pelo motor normal
        // (Vivarium.Core.Generation.TraitGenerator), só a ESCOLHA de qual seed foi manual em vez
        // de aleatória. Uso pontual — não expõe endpoint HTTP nenhum, mesma filosofia da ferramenta.
        if (args.Length != 3 || !long.TryParse(args[2], out long seed))
        {
            Console.WriteLine("Uso: dotnet run --project tools/Vivarium.AdminReset -- give-seed <username-ou-email> <seed>");
            return 1;
        }
        string identifier = args[1];
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == identifier || u.Email == identifier);
        if (user is null)
        {
            Console.WriteLine($"Usuário '{identifier}' não encontrado.");
            return 1;
        }

        var traits = Vivarium.Core.Generation.TraitGenerator.Generate(seed, Vivarium.Core.Generation.TraitConfigV1.Version);

        int aquariumTypeId = await db.HabitatTypes.Where(h => h.Code == "Aquarium").Select(h => h.Id).FirstAsync();
        int aquariumSpeciesId = await db.Species.Where(s => s.HabitatTypeId == aquariumTypeId).Select(s => s.Id).FirstAsync();

        var creature = new CreatureInstance
        {
            SpeciesId = aquariumSpeciesId,
            OwnerId = user.Id,
            OriginalOwnerId = user.Id,
            HabitatId = null, // mochila — jogador decide se quer colocar no tanque
            Seed = seed,
            TraitConfigVersion = Vivarium.Core.Generation.TraitConfigV1.Version,
            RarityScore = (decimal)traits.RarityScore,
            TraitsJson = Vivarium.Core.Generation.TraitsSerialization.Serialize(traits),
            CreatedAt = DateTime.UtcNow,
        };
        db.CreatureInstances.Add(creature);
        await db.SaveChangesAsync();

        Console.WriteLine($"Peixe #{creature.Id} criado na mochila de #{user.Id} {user.Username} — seed={seed}, RarityScore={creature.RarityScore:0.0000}");
        Console.WriteLine($"  Shimmer: {traits.ShimmerTier} ({traits.ShimmerColor?.ToString() ?? "-"})");
        Console.WriteLine($"  Cauda: {traits.Tail.Color}/{traits.Tail.Pattern}   Dorsal: {traits.Dorsal.Color}/{traits.Dorsal.Pattern}   Peitoral: {traits.Pectoral.Color}/{traits.Pectoral.Pattern}");
        return 0;
    }
    case "give-premium-all":
    {
        // Credita `quantidade` de moeda PREMIUM na carteira de TODO usuário — ação pontual
        // pra testes com jogadores reais (ex: validar o fluxo de VIP/rush/seguro sem precisar
        // de um pagamento de verdade). Um TransactionLog.AdminGrant por usuário, auditado
        // igual a qualquer outro crédito de moeda (mesmo princípio de TransactionLog único,
        // CLAUDE.md §9.1). Não mexe em SOFT nem em nada além da carteira PREMIUM.
        if (args.Length != 2 || !decimal.TryParse(args[1], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal amount) || amount <= 0)
        {
            Console.WriteLine("Uso: dotnet run --project tools/Vivarium.AdminReset -- give-premium-all <quantidade>");
            return 1;
        }

        int premiumId = await db.CurrencyTypes.Where(c => c.Code == "PREMIUM").Select(c => c.Id).FirstAsync();
        var wallets = await db.WalletBalances.Where(w => w.CurrencyTypeId == premiumId).ToListAsync();

        Console.WriteLine($"Vai creditar {amount:0} premium em {wallets.Count} carteira(s).");

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var now = DateTime.UtcNow;
            foreach (var w in wallets)
            {
                w.Amount += amount;
                db.TransactionLogs.Add(new TransactionLog
                {
                    Type = TransactionType.AdminGrant,
                    ToUserId = w.UserId,
                    CurrencyTypeId = premiumId,
                    Amount = amount,
                    CreatedAt = now,
                });
            }
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            Console.WriteLine($"\n{wallets.Count} carteira(s) creditada(s) com {amount:0} premium cada.");
            return 0;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Console.WriteLine($"\nERRO, nada foi alterado (rollback): {ex.Message}");
            return 1;
        }
    }
    case "reset-daily-reward-all":
    {
        // Zera `LastDailyRewardAt` de TODO usuário — ação pontual pra liberar o resgate de
        // novo pra todo mundo testar o redesenho da recompensa diária (roleta/streak/ovo,
        // CLAUDE.md §7.10) sem esperar virar o dia UTC. Não mexe em `DailyRewardStreak`: como
        // `DailyRewardCalculator.NextStreak` trata "sem resgate anterior" como dia 1 de
        // qualquer forma, o campo de streak fica órfão até o próximo claim recalculá-lo — não
        // precisa zerar à parte. Não credita nem debita nada (não é uma ação de moeda).
        var users = await db.Users.Where(u => u.LastDailyRewardAt != null).ToListAsync();

        Console.WriteLine($"Vai liberar o resgate de hoje pra {users.Count} usuário(s).");

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            foreach (var u in users)
                u.LastDailyRewardAt = null;

            await db.SaveChangesAsync();
            await tx.CommitAsync();
            Console.WriteLine($"\n{users.Count} usuário(s) liberado(s) pra resgatar a recompensa diária de novo.");
            return 0;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Console.WriteLine($"\nERRO, nada foi alterado (rollback): {ex.Message}");
            return 1;
        }
    }
    case "db-usage":
    {
        // Só leitura — diagnóstico pontual (20/08/2026, usuário perto do teto de 5GB de
        // "Network Transfer" do Neon free tier, avaliando se compensa migrar de banco antes
        // de trocar de provedor às cegas). Mede tamanho das tabelas (o que pesa por linha
        // trafegada) e conta usuários ativos (proxy pro volume de polling: heartbeat 60s +
        // refresh do tanque 30s por cliente, App.jsx) — os dois multiplicam pra formar o
        // tráfego real, não dá pra culpar só um lado sem medir.
        Console.WriteLine("Tamanho das tabelas (maiores primeiro):\n");
        await using (var cmd = db.Database.GetDbConnection().CreateCommand())
        {
            await db.Database.OpenConnectionAsync();
            cmd.CommandText = @"
                SELECT relname AS tabela,
                       pg_size_pretty(pg_total_relation_size(relid)) AS tamanho_total,
                       pg_size_pretty(pg_relation_size(relid)) AS tamanho_dados,
                       n_live_tup AS linhas
                FROM pg_stat_user_tables
                ORDER BY pg_total_relation_size(relid) DESC
                LIMIT 15;";
            await using var reader = await cmd.ExecuteReaderAsync();
            Console.WriteLine($"{"Tabela",-28} {"Total",-12} {"Dados",-12} {"Linhas",-10}");
            while (await reader.ReadAsync())
                Console.WriteLine($"{reader.GetString(0),-28} {reader.GetString(1),-12} {reader.GetString(2),-12} {reader.GetInt64(3),-10}");
        }

        Console.WriteLine("\nTamanho total do banco:");
        await using (var cmd2 = db.Database.GetDbConnection().CreateCommand())
        {
            cmd2.CommandText = "SELECT pg_size_pretty(pg_database_size(current_database()));";
            var size = await cmd2.ExecuteScalarAsync();
            Console.WriteLine($"  {size}");
        }

        int totalUsers = await db.Users.CountAsync();
        var since30d = DateTime.UtcNow.AddDays(-30);
        int activeHeartbeat30d = await db.Habitats.CountAsync(h => h.LastHeartbeatAt != null && h.LastHeartbeatAt >= since30d);
        Console.WriteLine($"\nUsuários totais: {totalUsers}");
        Console.WriteLine($"Habitats com heartbeat nos últimos 30 dias (proxy de jogador ativo/polling): {activeHeartbeat30d}");

        var traitsLens = await db.CreatureInstances
            .Where(c => c.TraitsJson != null)
            .Select(c => c.TraitsJson!.Length)
            .ToListAsync();
        if (traitsLens.Count > 0)
        {
            double avgLen = traitsLens.Average();
            Console.WriteLine($"\nTraitsJson: {traitsLens.Count} criatura(s), tamanho médio {avgLen:0} bytes, total ~{pg_size(traitsLens.Sum())}");
        }

        return 0;

        static string pg_size(long bytes) => bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
            _ => $"{bytes / 1024.0 / 1024.0:0.0} MB",
        };
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
