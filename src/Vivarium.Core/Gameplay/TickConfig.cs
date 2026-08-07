namespace Vivarium.Core.Gameplay;

/// <summary>
/// Parâmetros do loop de gameplay (CLAUDE.md seção 8). Valores default do MVP;
/// ajustáveis via simulação sem tocar na lógica.
/// </summary>
public sealed record TickConfig
{
    /// <summary>Sem heartbeat há mais que isso = tanque offline.</summary>
    public TimeSpan HeartbeatTimeout { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>Degradação base (~-1 de qualidade a cada 20 min com 0 peixes).</summary>
    public decimal DegradationPerMinute { get; init; } = 1m / 20m;
    /// <summary>
    /// 0.30 (era 0.10, 07/08/2026): curva mais agressiva pra o auto-filtro (500 soft) se
    /// pagar num prazo razoável — payback caiu de ~9-12 dias pra ~3,5-7,3 dias dependendo
    /// do tamanho do tanque (calibrado supondo o jogador filtrar ao bater o patamar de
    /// 80%, IncomeWaterPlateau). Só o fator por peixe mudou — tanque vazio/começo de jogo
    /// não fica mais punitivo, o efeito cresce só com o tanque estabelecido.
    /// </summary>
    public decimal DegradationPerFishFactor { get; init; } = 0.30m;
    /// <summary>
    /// Peso por raridade (08/08/2026): cada peixe conta como `score/DegradationRarityRefScore`
    /// em vez de 1 fixo — quem rende mais suja mais. Ref=5 mantém um comum (score~5) igual
    /// a antes (peso 1, contribuição ~0,9/h); um épico/lendário (score~15) pesa ~3x mais
    /// (~2,7/h). Calibrado em CLAUDE.md 8.6/11 antes de implementar.
    /// </summary>
    public decimal DegradationRarityRefScore { get; init; } = 5m;

    /// <summary>Abaixo disso a velocidade de geração cai.</summary>
    public decimal LowMaintenanceThreshold { get; init; } = 40m;
    public decimal LowMaintenanceRateFactor { get; init; } = 0.5m;

    /// <summary>Abaixo disso, itens novos da fila podem nascer doentes.</summary>
    public decimal SickMaintenanceThreshold { get; init; } = 15m;
    public double SickChance { get; init; } = 0.10;

    // --- Farm de moedas (renda passiva por raridade) ---
    // coinsPorHora(score) = IncomeBasePerHour * exp(IncomeGrowth * (score - IncomeRefScore))
    // Base reduzida (ritmo lento) mantendo o topo íngreme (lendário valioso).
    // 1.7 (era 2.0): compensa o +19% de renda mediana que a raridade v2 trouxe
    // (score mediano subiu 5.0→5.36) — ver CLAUDE.md 8.6.
    /// <summary>
    /// 1.5 (era 1.7, 31/07/2026): compensa o buff que o patamar de água (IncomeWaterPlateau)
    /// deu pra renda em quase toda a faixa 0-100% (deixar de perder renda de 80-100 e cair
    /// mais devagar logo abaixo empurra a média pra cima em ~10-17% dependendo da água típica
    /// do jogador — ver IncomeWaterPlateau). Reduzir a base mantém o ritmo geral parecido com
    /// antes, só que sem punir manutenção "quase perfeita".
    /// </summary>
    public double IncomeBasePerHour { get; init; } = 1.5;
    /// <summary>
    /// 0.42 (era 0.49, 06/08/2026): a faixa Épico (score 9.8-14.0) é bem mais larga que
    /// Incomum/Raro, então o mesmo crescimento exponencial acumulava demais dentro do
    /// próprio tier — um épico no topo da faixa rendia ~201/h contra ~26/h no piso (7.8x),
    /// deixando runs de sorte (poucos peixes, 1-2 épicos "altos") desproporcionalmente
    /// fortes. Reduzir o growth corta o teto do épico pela metade (~100/h) sem achatar
    /// o comum/incomum (pertinho do IncomeRefScore, quase não muda) nem eliminar o
    /// "jackpot" do lendário (ainda ~7.8x um épico no topo). Ver CLAUDE.md 8.6.
    /// </summary>
    public double IncomeGrowth { get; init; } = 0.42;
    public double IncomeRefScore { get; init; } = 4.0;
    /// <summary>
    /// Água 80-100%: sem perda de renda (o jogador não é punido por "quase perfeito" —
    /// só abaixo do patamar a água começa a doer). Abaixo disso, mesma curva de antes
    /// (potência IncomeWaterExp), só reescalada pra o patamar em vez de 100 — cai suave,
    /// sem penhasco logo abaixo do corte (31/07/2026).
    /// </summary>
    public double IncomeWaterPlateau { get; init; } = 0.80;
    /// <summary>Fator água abaixo do patamar: (maint/plateau)^IncomeWaterExp. Água 0 rende ~0.</summary>
    public double IncomeWaterExp { get; init; } = 0.7;
    /// <summary>Teto de minutos offline que rendem (8h) — evita acúmulo infinito.</summary>
    public decimal IncomeOfflineCapMinutes { get; init; } = 480m;

    // --- Sinergia por cor de cauda (forte): N peixes de mesma cor → cada um
    // multiplica a renda por 1 + SynergyPerMatch·(N-1), com teto SynergyMaxBonus.
    public double SynergyPerMatch { get; init; } = 0.15;
    public double SynergyMaxBonus { get; init; } = 0.80;

    // --- Venda ao NPC (vendor, §8.12): preço deliberadamente baixo — poucas horas do
    // que o peixe já renderia sozinho — pra dar vazão a duplicatas/comuns acumulados
    // sem competir com o mercado entre jogadores (esse continua sendo o preço "de verdade").
    public double VendorHoursEquivalent { get; init; } = 2.0;
    public decimal VendorMinPrice { get; init; } = 1m;

    /// <summary>
    /// Expoente do taper suave do filtro acima da capacidade coberta (08/08/2026) — mesmo
    /// papel do <see cref="IncomeWaterExp"/>: acima de <c>FilterCapacity</c> o benefício do
    /// filtro não corta de uma vez pra "sem filtro", ele decai suavemente de volta a 1.0
    /// conforme o excedente de peixes cresce. Ver <see cref="HabitatTicker"/>.
    /// </summary>
    public double FilterTaperExponent { get; init; } = 1.0;

    public static readonly TickConfig Default = new();
}

/// <summary>
/// Faixa nomeada de capacidade do tanque (08/08/2026) — o tanque evolui dentro do mesmo
/// <c>Habitat</c> por faixas com nome, preço e degradação próprios, em vez de um único
/// upgrade infinito. Ver CLAUDE.md §8.13.
/// </summary>
public sealed record CapacityBand(
    int MinCapacity,
    int MaxCapacity,
    string Name,
    decimal PriceBase,
    double PriceGrowth,
    decimal DegradationBandFactor);

/// <summary>
/// As 3 faixas de capacidade do MVP (08/08/2026): "Aquário" (3-5), "Aquário Grande" (5-10),
/// "Aquário Master" (10-15). Preço e degradação por faixa são placeholders — calibrar via
/// `Vivarium.Simulation economy`/`simulate` antes do lançamento (mesmo fluxo de toda
/// constante econômica deste projeto).
/// </summary>
public static class CapacityBands
{
    public static readonly CapacityBand Aquario = new(3, 5, "Aquário", PriceBase: 50m, PriceGrowth: 1.5, DegradationBandFactor: 1.0m);
    public static readonly CapacityBand AquarioGrande = new(5, 10, "Aquário Grande", PriceBase: 140m, PriceGrowth: 1.45, DegradationBandFactor: 1.25m);
    public static readonly CapacityBand AquarioMaster = new(10, 15, "Aquário Master", PriceBase: 400m, PriceGrowth: 1.4, DegradationBandFactor: 1.55m);

    public static readonly IReadOnlyList<CapacityBand> All = [Aquario, AquarioGrande, AquarioMaster];

    /// <summary>Capacidade máxima absoluta do MVP — teto do upgrade de tanque.</summary>
    public static int MaxCapacity => All[^1].MaxCapacity;

    /// <summary>
    /// Faixa que contém a capacidade atual. Capacidade no teto de uma faixa ainda conta
    /// como dessa faixa (só sobe de faixa ao comprar acima do teto).
    /// </summary>
    public static CapacityBand BandFor(int capacity)
    {
        foreach (var band in All)
            if (capacity <= band.MaxCapacity)
                return band;
        return All[^1];
    }
}

/// <summary>Valores iniciais da economia de um jogador novo.</summary>
public static class EconomyDefaults
{
    public const decimal StartingSoftBalance = 100m;
    public const decimal StartingPremiumBalance = 0m;

    /// <summary>
    /// Recompensa diária: gancho de retenção simples, sem streak — resgatável 1x por dia
    /// (calendário UTC), sem quebra/penalidade por ausência (mesma filosofia de "ausência
    /// nunca pune duro" do online/offline — CLAUDE.md 8.3/8.6). Valor fixo modesto: dá pra
    /// comprar 1 filtro (20 soft) e sobra um pouco, sem distorcer a economia (30/07/2026).
    /// </summary>
    public const decimal DailyRewardSoft = 25m;
}

/// <summary>Valores iniciais de um habitat novo (tanque inicial do MVP).</summary>
public static class HabitatDefaults
{
    public const int Capacity = 3;
    /// <summary>Storage de criaturas fora do tanque (não farmam). Base pro breeding.</summary>
    public const int BackpackCapacity = 50;
    public const int QueueCap = 5;
    /// <summary>
    /// Geração deliberadamente lenta — o ritmo "de graça" existe pra não dar pra rushar o
    /// jogo (30/07/2026: 25→60, quase dobra o tempo por peixe). A única forma de acelerar
    /// é pagar em moeda premium pra pular a espera (ver `RushCalculator`/8.11) — o tempo
    /// lento é a fricção intencional; dinheiro é o único jeito de comprimi-lo. Era 15→25→60.
    /// TEMPORÁRIO (07/08/2026): 60→10 só pra fase de testes com jogadores reais, pra dar
    /// mais volume de peixe rápido sem esperar horas — reverter pra 60 antes de qualquer
    /// lançamento "de verdade" (não é uma mudança de design, é conveniência de QA).
    /// </summary>
    public const int GenerationIntervalMinutes = 10;
    public const decimal OnlineGenerationRate = 1.0m;
    public const decimal OfflineGenerationRate = 0.45m;
    public const decimal MaintenanceLevel = 100m;
}
