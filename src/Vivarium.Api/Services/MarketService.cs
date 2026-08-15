using Microsoft.EntityFrameworkCore;
using Vivarium.Api.Contracts;
using Vivarium.Api.Data;
using Vivarium.Api.Http;
using Vivarium.Core.Domain;
using Vivarium.Core.Gameplay;
using Vivarium.Core.Generation;

namespace Vivarium.Api.Services;

/// <summary>
/// Regras do mercado interno (listar, cancelar, comprar). Lógica de negócio fora
/// dos handlers HTTP; devolve <see cref="ServiceResult"/> que o endpoint traduz.
/// Sem taxa de mercado no MVP; compra é transacional + auditada no TransactionLog.
/// </summary>
public class MarketService(VivariumDbContext db)
{
    // Cortes de raridade espelhados de `frontend/src/lib/fishRenderer.js` BANDS (CLAUDE.md §5) —
    // não existe uma representação de "banda" no backend hoje, só o RarityScore cru. Se os
    // cortes mudarem lá (recalibração via Vivarium.Simulation), espelhar aqui também.
    // 14/08/2026: pirâmide "Íngreme" (Lendário 1/5.000), sincronizado com BANDS (fishRenderer.js).
    // 15/08/2026: Raro estava comum demais/Épico raro demais na prática — ajustado pra manter
    // Raro em ~1,00% e Épico em ~0,30% (12.24→12.04, 14.85→13.78, Incomum doou o espaço);
    // ver o bloco "CORTES ÍNGREME AJUSTADO" em Vivarium.Simulation.
    private static string BandNameOf(decimal score) => score switch
    {
        < 5.45m => "Comum",
        < 12.04m => "Incomum",
        < 13.78m => "Raro",
        < 16.60m => "Épico",
        _ => "Lendário",
    };

    // Cada valor pode ser uma lista separada por vírgula (ex: "Red,Green") — dentro do MESMO
    // atributo/parte, os valores são OU (qualquer um serve); entre partes/atributos diferentes
    // continua E (precisa bater em todos os filtros ativos). Vazio/null = sem filtro nesse campo.
    private static bool MatchesAny(string value, string? csv)
    {
        if (string.IsNullOrEmpty(csv) || csv == "all") return true;
        foreach (var candidate in csv.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (candidate == value) return true;
        return false;
    }

    private static bool MatchesPart(CreatureTraits traits, string part, string? colors, string? patterns)
    {
        var p = part switch { "tail" => traits.Tail, "dorsal" => traits.Dorsal, "pectoral" => traits.Pectoral, _ => traits.Tail };
        return MatchesAny(p.Color.ToString(), colors) && MatchesAny(p.Pattern.ToString(), patterns);
    }

    public async Task<MarketListingsResponse> ListingsAsync(
        long userId, int skip, int take, string sort,
        string? band, string? tailColor, string? tailPattern,
        string? dorsalColor, string? dorsalPattern, string? pectoralColor, string? pectoralPattern)
    {
        take = Math.Clamp(take, 1, 100);
        skip = Math.Max(0, skip);

        var all = (await db.MarketListings
            .Where(m => m.Status == ListingStatus.Active)
            .Select(m => new
            {
                m.Id, m.PriceSoft, m.SellerId, SellerName = m.Seller!.Username, m.CreatedAt,
                CreatureId = m.CreatureInstanceId, m.CreatureInstance!.SpeciesId,
                m.CreatureInstance.Seed, m.CreatureInstance.TraitConfigVersion,
                m.CreatureInstance.RarityScore, m.CreatureInstance.ParentAId,
                m.CreatureInstance.ParentASeed, m.CreatureInstance.ParentBSeed,
                m.CreatureInstance.ParentAGrandparentASeed, m.CreatureInstance.ParentAGrandparentBSeed,
                m.CreatureInstance.ParentBGrandparentASeed, m.CreatureInstance.ParentBGrandparentBSeed,
                m.CreatureInstance.TraitsJson, m.CreatureInstance.BreedingSourceJson,
            })
            .ToListAsync())
            .Select(m => new
            {
                Listing = new ListingDto(
                    m.Id, m.PriceSoft, m.SellerId, m.SellerName,
                    m.CreatureId, m.SpeciesId, m.Seed.ToString(),
                    m.TraitConfigVersion, m.RarityScore, m.ParentAId.HasValue,
                    m.ParentASeed?.ToString(), m.ParentBSeed?.ToString(),
                    m.ParentAGrandparentASeed?.ToString(), m.ParentAGrandparentBSeed?.ToString(),
                    m.ParentBGrandparentASeed?.ToString(), m.ParentBGrandparentBSeed?.ToString(),
                    m.TraitsJson is not null ? TraitsSerialization.DeserializeTraits(m.TraitsJson) : null,
                    m.BreedingSourceJson is not null ? TraitsSerialization.DeserializeSource(m.BreedingSourceJson) : null),
                m.CreatedAt,
                Traits = m.TraitsJson is not null ? TraitsSerialization.DeserializeTraits(m.TraitsJson) : null,
            })
            .ToList();

        bool PassesFilters(ListingDto l, CreatureTraits? traits) =>
            (string.IsNullOrEmpty(band) || band == "all" || BandNameOf(l.RarityScore) == band)
            && (traits is null || (
                MatchesPart(traits, "tail", tailColor, tailPattern)
                && MatchesPart(traits, "dorsal", dorsalColor, dorsalPattern)
                && MatchesPart(traits, "pectoral", pectoralColor, pectoralPattern)));

        var filtered = all.Where(m => PassesFilters(m.Listing, m.Traits)).ToList();

        // Painel "meus anúncios": todos os que passam nos MESMOS filtros, sem paginação — o
        // dono precisa ver o conjunto inteiro pra ter controle real, não uma fatia.
        var mine = filtered.Where(m => m.Listing.SellerId == userId)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => m.Listing)
            .ToList();

        var restQuery = filtered.Where(m => m.Listing.SellerId != userId).AsEnumerable();
        restQuery = sort switch
        {
            "price-asc" => restQuery.OrderBy(m => m.Listing.PriceSoft),
            "price-desc" => restQuery.OrderByDescending(m => m.Listing.PriceSoft),
            "rarity-desc" => restQuery.OrderByDescending(m => m.Listing.RarityScore),
            "rarity-asc" => restQuery.OrderBy(m => m.Listing.RarityScore),
            "oldest" => restQuery.OrderBy(m => m.CreatedAt),
            _ => restQuery.OrderByDescending(m => m.CreatedAt), // "newest" (default)
        };
        var rest = restQuery.ToList();
        var page = rest.Skip(skip).Take(take).Select(m => m.Listing).ToList();

        // Contador do limite reflete o total REAL (sem filtro de aparência/banda) — um filtro
        // escondendo anúncios não pode fazer o "X/Y" mentir sobre quanto do teto já foi usado.
        int myActiveTotal = await db.MarketListings.CountAsync(m => m.SellerId == userId && m.Status == ListingStatus.Active);

        return new MarketListingsResponse(page, rest.Count, mine, myActiveTotal, MarketDefaults.MaxActiveListingsPerSeller);
    }

    public async Task<ServiceResult> CreateListingAsync(long userId, CreateListingRequest req)
    {
        if (req.PriceSoft <= 0)
            return ServiceResult.Bad("Preço deve ser maior que zero");

        var creature = await db.CreatureInstances
            .FirstOrDefaultAsync(c => c.Id == req.CreatureInstanceId && c.OwnerId == userId);
        if (creature is null)
            return ServiceResult.NotFound("Criatura não encontrada");
        if (creature.IsDead)
            return ServiceResult.Bad("Essa criatura não sobreviveu à gestação");
        if (creature.SoldAt is not null)
            return ServiceResult.Bad("Essa criatura já foi vendida ao NPC");
        if (creature.PendingInboxClaim)
            return ServiceResult.Bad("Criatura pendente de resgate na Caixa de Entrada");
        // Bug real corrigido (12/08/2026, relatado pelo usuário): `HabitatId is null` também é
        // verdade pra uma criatura na MOCHILA (estado normal, não "em trânsito") — o check
        // bloqueava listar qualquer peixe guardado ali, sempre. O que realmente precisa ser
        // checado é se já existe uma listagem ATIVA, e se o peixe não está preso numa gestação.
        bool alreadyListed = await db.MarketListings.AnyAsync(m =>
            m.CreatureInstanceId == creature.Id && m.Status == ListingStatus.Active);
        if (alreadyListed)
            return ServiceResult.Bad("Criatura já está no mercado");
        bool breeding = await db.BreedingSlots.AnyAsync(s =>
            s.Status == BreedingStatus.InProgress && (s.ParentAId == creature.Id || s.ParentBId == creature.Id));
        if (breeding)
            return ServiceResult.Bad("Criatura está em gestação — não pode ser vendida agora");

        int activeListings = await db.MarketListings.CountAsync(m => m.SellerId == userId && m.Status == ListingStatus.Active);
        if (activeListings >= MarketDefaults.MaxActiveListingsPerSeller)
            return ServiceResult.Bad($"Limite de {MarketDefaults.MaxActiveListingsPerSeller} anúncios ativos atingido — cancele algum antes de listar outro.");

        creature.HabitatId = null; // sai do tanque enquanto listada
        var listing = new MarketListing
        {
            CreatureInstanceId = creature.Id,
            SellerId = userId,
            PriceSoft = req.PriceSoft,
            Status = ListingStatus.Active,
            CreatedAt = DateTime.UtcNow,
        };
        db.MarketListings.Add(listing);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Conflict("Esse peixe mudou de estado — atualize e tente de novo.");
        }
        return ServiceResult.Success(new { listing.Id });
    }

    public async Task<ServiceResult> CancelAsync(long userId, long id)
    {
        var listing = await db.MarketListings
            .Include(m => m.CreatureInstance)
            .FirstOrDefaultAsync(m => m.Id == id && m.SellerId == userId);
        if (listing is null)
            return ServiceResult.NotFound("Listagem não encontrada");
        if (listing.Status != ListingStatus.Active)
            return ServiceResult.Bad("Listagem não está ativa");

        listing.Status = ListingStatus.Cancelled;
        listing.ResolvedAt = DateTime.UtcNow;
        await ReturnToOwnerTankIfSpaceAsync(listing.CreatureInstance!, userId);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Conflict("A listagem mudou — atualize e tente de novo.");
        }
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> BuyAsync(long buyerId, long id)
    {
        var now = DateTime.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync();

        var listing = await db.MarketListings
            .Include(m => m.CreatureInstance)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (listing is null || listing.Status != ListingStatus.Active)
            return ServiceResult.NotFound("Listagem não encontrada ou não está ativa");
        if (listing.SellerId == buyerId)
            return ServiceResult.Bad("Você não pode comprar sua própria listagem");

        int softId = await db.CurrencyTypes.Where(c => c.Code == "SOFT").Select(c => c.Id).FirstAsync();
        var buyerWallet = await db.WalletBalances
            .FirstAsync(w => w.UserId == buyerId && w.CurrencyTypeId == softId);
        if (buyerWallet.Amount < listing.PriceSoft)
            return ServiceResult.Bad("Saldo insuficiente");
        var sellerWallet = await db.WalletBalances
            .FirstAsync(w => w.UserId == listing.SellerId && w.CurrencyTypeId == softId);

        buyerWallet.Amount -= listing.PriceSoft;
        sellerWallet.Amount += listing.PriceSoft;

        var creature = listing.CreatureInstance!;
        creature.OwnerId = buyerId;
        // Deixa de cair direto no tanque/mochila — vai pra Caixa de Entrada do comprador,
        // resgatado explicitamente (CLAUDE.md §8.23/§8.24). Espaço deixa de ser checado
        // aqui; falta de espaço agora é um erro no momento do resgate, não da compra.
        creature.HabitatId = null;
        creature.PendingInboxClaim = true;

        listing.Status = ListingStatus.Sold;
        listing.BuyerId = buyerId;
        listing.ResolvedAt = now;

        db.TransactionLogs.Add(new TransactionLog
        {
            Type = TransactionType.MarketSale,
            FromUserId = buyerId,
            ToUserId = listing.SellerId,
            CreatureInstanceId = creature.Id,
            CurrencyTypeId = softId,
            Amount = listing.PriceSoft,
            CreatedAt = now,
        });
        db.InboxEntries.Add(new InboxEntry
        {
            RecipientId = buyerId,
            Kind = InboxEntryKind.MarketPurchase,
            SenderUserId = listing.SellerId,
            CreatureInstanceId = creature.Id,
            CreatedAt = now,
        });

        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Concorrência: outro comprador levou a listagem (ou o saldo mudou).
            // A transação é descartada (rollback) — nada é cobrado nem transferido.
            return ServiceResult.Conflict("Essa listagem acabou de ser comprada ou alterada.");
        }
        return ServiceResult.Success(new { creatureId = creature.Id, paid = listing.PriceSoft });
    }

    /// <summary>Devolve a criatura ao tanque do dono se houver espaço; senão fica fora (HabitatId null).</summary>
    private async Task ReturnToOwnerTankIfSpaceAsync(CreatureInstance creature, long ownerId)
    {
        var habitat = await db.Habitats.FirstOrDefaultAsync(h => h.UserId == ownerId);
        if (habitat is null)
            return;
        int active = await db.CreatureInstances.CountAsync(c => c.HabitatId == habitat.Id);
        creature.HabitatId = active < habitat.Capacity ? habitat.Id : null;
    }
}
