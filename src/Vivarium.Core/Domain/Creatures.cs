namespace Vivarium.Core.Domain;

// Traits (13/08/2026): congelados no nascimento em TraitsJson (ver abaixo) — antes disso, nenhum
// trait era salvo, tudo era recalculado do Seed a cada exibição, o que exigia reconstruir a
// ancestralidade de um pai que também era filhote e causou uma sequência de bugs reais (CLAUDE.md
// §8.19.1, §10). Seed/TraitConfigVersion continuam gravados como histórico/proveniência.
public class CreatureInstance
{
    public long Id { get; set; }
    public int SpeciesId { get; set; }
    public Species? Species { get; set; }
    public long OwnerId { get; set; }
    public User? Owner { get; set; }
    /// <summary>Null quando está listado no mercado ou em trânsito.</summary>
    public long? HabitatId { get; set; }
    public Habitat? Habitat { get; set; }
    public long Seed { get; set; }
    public int TraitConfigVersion { get; set; }
    public decimal RarityScore { get; set; }
    /// <summary>
    /// Traits resolvidos (`CreatureTraits` serializado, `System.Text.Json`) congelados no
    /// nascimento (13/08/2026) — mesmo padrão de coluna `text` já usado em
    /// `TraitWeightConfig.ConfigJson`/`ItemDefinition.EffectJson`, sem `jsonb` nativo (não há
    /// precedente disso no schema). Cruzar um filhote passa a LER `TraitsJson` dos pais em vez de
    /// reconstruir a partir do seed — elimina o bug de profundidade de ancestralidade por completo
    /// (sem limite de gerações, já que nunca mais recalcula, só lê o que já foi congelado).
    /// </summary>
    /// <summary>
    /// Nullable só durante a transição (migration + backfill) — depois do `backfill-traits`
    /// rodar em produção, toda criatura tem isso preenchido; código de aplicação assume não-nulo.
    /// </summary>
    public string? TraitsJson { get; set; }
    /// <summary>
    /// Trace de origem por slot (`TraitGenerator.TraitSourceEntry[]` serializado) — só preenchido
    /// em filhotes cruzados DEPOIS desta mudança (13/08/2026). Alimenta o selo "Herdado do Pai
    /// A/B" (CLAUDE.md §8.19) sem precisar recalcular nada. Null pra peixes frescos e pra filhotes
    /// antigos (o detalhe de origem só existe no momento do nascimento — não dá pra reconstruir
    /// retroativamente pra quem já passou desse momento).
    /// </summary>
    public string? BreedingSourceJson { get; set; }
    public long? ParentAId { get; set; }
    public CreatureInstance? ParentA { get; set; }
    public long? ParentBId { get; set; }
    public CreatureInstance? ParentB { get; set; }
    /// <summary>
    /// Seed dos pais denormalizado no filho (imutável, nunca muda) — histórico/curiosidade
    /// (exibido na tela). Não é mais usado pra reconstruir traits (ver `TraitsJson` acima).
    /// </summary>
    public long? ParentASeed { get; set; }
    public long? ParentBSeed { get; set; }
    /// <summary>
    /// Seeds dos AVÓS (pais de ParentA/ParentB) — histórico/curiosidade, mesma razão de
    /// ParentASeed/BSeed acima. O mecanismo que os usava funcionalmente (puxar um traço de um avô,
    /// `GrandparentReachChance`) foi REMOVIDO em 13/08/2026 junto da migração pra `TraitsJson` — já
    /// estava em 0.1% de chance (a pedido do usuário) e exigiria carregar os avós como entidades
    /// extras, complexidade desproporcional ao efeito. Null se o respectivo pai (A ou B) não era
    /// ele mesmo um filhote.
    /// </summary>
    public long? ParentAGrandparentASeed { get; set; }
    public long? ParentAGrandparentBSeed { get; set; }
    public long? ParentBGrandparentASeed { get; set; }
    public long? ParentBGrandparentBSeed { get; set; }
    /// <summary>Nº de gestações que este peixe já completou como pai/mãe (risco de morte cresce com o uso).</summary>
    public int BreedCount { get; set; }
    /// <summary>
    /// Quando este peixe terminou sua última gestação (null se nunca cruzou). Descansar
    /// (ficar fora do ninho por um tempo antes de cruzar de novo) decai o risco acumulado
    /// de BreedCount — ver `BreedingCalculator.EffectiveBreedCount`.
    /// </summary>
    public DateTime? LastBredAt { get; set; }
    public bool IsDead { get; set; }
    public DateTime? DiedAt { get; set; }
    /// <summary>
    /// Vendido ao NPC (vendor, §8.12) — preço baixo, mas não apaga a linha (mesma razão do
    /// IsDead: preservar a FK Restrict de linhagem ParentAId/BId e o histórico do TransactionLog).
    /// Null enquanto o peixe está no jogo (tanque, mochila ou listado).
    /// </summary>
    public DateTime? SoldAt { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>
    /// Peixe apareceu na Mochila/tanque via coleta AUTOMÁTICA de VIP (§8.3), sem o jogador ter
    /// clicado em nada — pula o momento de revelação (CollectCelebration) que a coleta manual
    /// sempre mostra. Fica escondido/silhueta na Mochila até o jogador marcar como visto
    /// (`POST /api/game/creatures/{id}/mark-seen`). Default false: só a coleta automática
    /// (`GameService.CollectAllReadyAsync`) grava true; coleta manual e breeding já têm seu
    /// próprio momento de revelação, não precisam disso.
    /// </summary>
    public bool IsNew { get; set; }
    /// <summary>
    /// "Primeiro dono" (14/08/2026) — setado uma única vez na criação (coleta da fila ou
    /// nascimento via breeding) e NUNCA alterado depois, mesmo que o peixe troque de mãos via
    /// mercado/transferência (isso é `OwnerId`, que muda). Guarda o Id, não o username — pensando
    /// no suporte futuro a troca de nome de usuário: exibir o username exigiria resolver via join
    /// no momento da leitura (mesmo padrão já usado em `MarketListing`/`SellerName`), nunca
    /// denormalizar a string. `required`: força todo `new CreatureInstance` a setar o campo — sem
    /// isso, um insert esquecido gravaria silenciosamente uma FK inválida (0).
    /// </summary>
    public required long OriginalOwnerId { get; set; }
    public User? OriginalOwner { get; set; }
    /// <summary>
    /// Peixe comprado no mercado ou recebido por transferência direta, ainda não resgatado na
    /// Caixa de Entrada (§8.23/8.24 do CLAUDE.md) — já pertence ao novo dono (`OwnerId` já
    /// trocou), mas fica fora do tanque/mochila (sempre `HabitatId == null` enquanto pendente) até
    /// o jogador confirmar o resgate, que então roda a mesma lógica de tanque-ou-mochila de
    /// sempre (`GameService.TryPlaceAsync`). Default false — todo o resto da criação de peixe
    /// (coleta da fila, breeding) não passa por aqui, nasce direto no tanque/mochila normal.
    /// </summary>
    public bool PendingInboxClaim { get; set; }
}

public class MarketListing
{
    public long Id { get; set; }
    public long CreatureInstanceId { get; set; }
    public CreatureInstance? CreatureInstance { get; set; }
    public long SellerId { get; set; }
    public User? Seller { get; set; }
    public long? BuyerId { get; set; }
    public User? Buyer { get; set; }
    public decimal PriceSoft { get; set; }
    public ListingStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
