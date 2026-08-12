namespace Vivarium.Core.Gameplay;

/// <summary>
/// Parâmetros do breeding (CLAUDE.md 8.8). A gestação escala com a raridade
/// combinada dos pais — mesma linguagem exponencial da renda (IncomeCalculator):
/// casais comuns cruzam rápido, casais raros/lendários levam dias. Isso é o sink
/// de tempo que a economia estava sem (ver revisão de economia, gap #1).
/// </summary>
public static class BreedingDefaults
{
    /// <summary>
    /// Base e growth reduzidos/reequilibrados (24→6, 0.12→0.185, 06/08/2026): usuário achou
    /// 2 dias pra cruzar um casal de incomuns tempo demais, e observou que custo em soft e
    /// risco de morte já são fricção suficiente pra pares baratos (nenhum dos dois ameaça a
    /// economia) — o tempo só precisa continuar sendo o freio real pros pares RICOS, já que
    /// custo é intencionalmente barato pra eles e o risco é mitigável (descanso, estabilizador,
    /// seguro). Por isso o corte é assimétrico: Base baixo deixa o comum quase imediato (6h),
    /// mas o Growth mais alto compensa e mantém o topo perto do valor anterior (lendário
    /// ~208h→~167h, ainda ~7 dias; épico típico ~80h, ~3-4 dias). Ver CLAUDE.md 8.8.
    ///
    /// TEMPORÁRIO (11/08/2026): Min=Max=1h — gestação FLAT de 1 hora pra TODO casal, direto
    /// de propósito do pedido do usuário. Antes disso (10/08/2026) já era um ÷10 temporário
    /// (6→0,6h / 6→0,6h / 240→24h) só pra fase de testes; agora, como os aquários serão
    /// resetados no lançamento mesmo, faz sentido ir direto pra 1h fixa pra todo mundo — mais
    /// volume de cruzamento pra achar bugs no fluxo (herança, risco de morte, avós, etc.) sem
    /// esperar nem os 24h que o teto anterior permitia pra um par lendário. `GestationGrowth`/
    /// `GestationRefScore` continuam intocados (a curva não mudou, só ficou irrelevante
    /// enquanto Min==Max — a fórmula ainda roda, só que o `Math.Clamp` sempre devolve 1h,
    /// então basta restaurar Min/Max pra reativar a escala por raridade). Reverter pra
    /// 6/6/240 antes de qualquer lançamento "de verdade" — não é mudança de design, é
    /// conveniência de QA (mesmo espírito do `GenerationIntervalMinutes` 60→10 em `TickConfig.cs`).
    /// </summary>
    public const double BaseGestationHours = 1.0;
    public const double GestationGrowth = 0.185;
    public const double GestationRefScore = 10.0;
    /// <summary>Piso TEMPORÁRIO em 1h — ver BaseGestationHours (reverter pra 6h no lançamento).</summary>
    public const double MinGestationHours = 1.0;
    /// <summary>Teto TEMPORÁRIO em 1h — ver BaseGestationHours (reverter pra 240h no lançamento).</summary>
    public const double MaxGestationHours = 1.0;
    public const double MutationChance = 0.04;

    /// <summary>
    /// Viés de raridade na herança do tier de brilho (0 = 50/50 puro, 1 = pesa
    /// pelo inverso exato da probabilidade). Calibrado por simulação (`Vivarium.Simulation
    /// breed`) pra favorecer "raro cruza com raro dá raro" sem virar atalho de
    /// "lavagem" de lendário cruzando com um peixe comum qualquer.
    /// </summary>
    public const double RarityBiasStrength = 0.15;

    /// <summary>
    /// Chance (por "slot": brilho, cauda, dorsal, peitoral — 31/07/2026) de um filhote herdar
    /// um traço de um AVÔ em vez do pai direto, quando esse pai é ele mesmo um filhote — um
    /// traço "pulando uma geração", como recessivo. Deliberadamente MENOR que a chance de herdar
    /// do pai direto (que domina o resto do tempo).
    /// Trajetória de ajustes no mesmo dia (12/08/2026), a pedido do usuário: 0.15→0.03→0.01→0.001.
    /// Essa chance é sorteada de forma INDEPENDENTE 8 vezes por cruzamento (4 slots × 2 lados dos
    /// pais) — com 15%, a chance de PELO MENOS UM traço vir de um avô em qualquer cruzamento era
    /// `1-(0.85)^8 ≈ 72.8%`, quase 3 em cada 4 cruzamentos, deixando a influência dos avós parecer
    /// dominante mesmo a chance individual sendo baixa (usuário relatou resultados "estranhos"
    /// analisando o histórico de cruzamentos de uma conta real com múltiplas gerações). Depois de
    /// confirmar caso a caso (via consulta direta ao banco) que cada ocorrência era o mecanismo
    /// funcionando corretamente — não bug — o usuário decidiu por conta própria deixar as chances
    /// "como se fossem peixes gerados do zero", ou seja, essencialmente desligar o mecanismo, mas
    /// preservando um resíduo mínimo em vez de zerar de vez (pra não remover a mecânica por
    /// completo). Com 0.001, a chance de pelo menos 1 traço vir de avô cai pra
    /// `1-(0.999)^8 ≈ 0.8%` — praticamente nunca acontece, mas ainda existe. Validado com
    /// `Vivarium.Simulation breed`.
    /// </summary>
    public const double GrandparentReachChance = 0.001;

    // --- Custo dinâmico (soft) ---
    // O tempo de gestação já é o sink principal pra pares raros (um lendário
    // parado ~88h custa ~65k de renda perdida); o custo em moeda só precisa ser
    // um toque adicional, não o sink pesado. Mesma forma exponencial da gestação.
    public const decimal BaseCostSoft = 150m;
    public const double CostGrowth = 0.10;
    public const double CostRefScore = 10.0;
    public const decimal MinCostSoft = 100m;
    public const decimal MaxCostSoft = 5000m;

    // --- Risco de morte crescente por cruzamento ---
    // Cada gestação completada aumenta o risco do PRÓXIMO cruzamento — sem limite
    // fixo, mas nunca garantido (nunca chega em 100%). n = nº de gestações já
    // completadas por esse peixe (BreedCount antes desta coleta).
    public const double BaseDeathChance = 0.05;
    public const double MaxDeathChance = 0.85;
    public const double DeathRiskGrowth = 0.25;

    /// <summary>
    /// Descanso (31/07/2026): o risco acumulado (BreedCount) decai com o tempo que o peixe
    /// passa FORA do ninho antes do próximo cruzamento — meia-vida em dias. Ex.: um peixe com
    /// BreedCount=4 que descansou 5 dias entra no próximo cruzamento "contando" como ~2 gestações
    /// pro cálculo de risco; 10 dias de descanso, ~1. Nunca deleta o histórico (BreedCount
    /// continua subindo pra sempre — é só o RISCO que se recupera com paciência), e nunca é
    /// obrigatório: quem quer certeza paga o seguro; quem tem pressa paga o estabilizador; quem
    /// tem tempo, descansa de graça. Ver `EffectiveBreedCount`.
    /// </summary>
    public const double RestHalfLifeDays = 5.0;

    // --- Estabilizador genético (soft, redução parcial) e Seguro de cruzamento (premium,
    // garantia total) — opt-in no Start, cobrados ali mesmo (sem item/inventário: mais simples
    // e o preço já reflete o risco do momento). Mutuamente exclusivos; seguro tem prioridade se
    // os dois forem marcados. Ambos existem pra dar ao jogador uma alavanca ativa contra o risco,
    // além do descanso passivo — sem eliminar de vez a tensão do risco pra quem não paga nada.
    public const decimal StabilizerCostSoft = 150m;
    public const double StabilizerReductionFactor = 0.5;

    public const decimal InsuranceBasePremium = 20m;
    /// <summary>Premium por ponto percentual de risco combinado (dos dois pais) sendo removido.</summary>
    public const decimal InsurancePerRiskPercent = 3.0m;
    public const decimal InsuranceMinPremium = 20m;
    public const decimal InsuranceMaxPremium = 400m;
}

public static class BreedingCalculator
{
    /// <summary>Horas de gestação: exponencial na soma dos rarity scores dos pais, com teto.</summary>
    public static double GestationHours(decimal parentAScore, decimal parentBScore)
    {
        double combined = (double)(parentAScore + parentBScore);
        double hours = BreedingDefaults.BaseGestationHours
            * Math.Exp(BreedingDefaults.GestationGrowth * (combined - BreedingDefaults.GestationRefScore));
        return Math.Clamp(hours, BreedingDefaults.MinGestationHours, BreedingDefaults.MaxGestationHours);
    }

    /// <summary>Custo em soft pra iniciar a gestação: exponencial na soma dos rarity scores dos pais, com teto.</summary>
    public static decimal CostSoft(decimal parentAScore, decimal parentBScore)
    {
        double combined = (double)(parentAScore + parentBScore);
        double cost = (double)BreedingDefaults.BaseCostSoft
            * Math.Exp(BreedingDefaults.CostGrowth * (combined - BreedingDefaults.CostRefScore));
        return Math.Clamp((decimal)cost, BreedingDefaults.MinCostSoft, BreedingDefaults.MaxCostSoft);
    }

    /// <summary>
    /// Chance de um pai não sobreviver à gestação, dado quantas ele já completou
    /// antes desta (n=0 na primeira vez). Cresce em direção a MaxDeathChance sem
    /// nunca alcançar 100% — sempre sobra uma margem de sorte. Aceita um valor
    /// fracionário (n) pra funcionar com o descanso (`EffectiveBreedCount`), que
    /// decai o BreedCount inteiro pra um "efetivo" contínuo.
    /// </summary>
    public static double DeathChance(double effectiveBreedCount)
    {
        double n = Math.Max(0, effectiveBreedCount);
        return BreedingDefaults.BaseDeathChance
            + (BreedingDefaults.MaxDeathChance - BreedingDefaults.BaseDeathChance)
            * (1 - Math.Exp(-BreedingDefaults.DeathRiskGrowth * n));
    }

    /// <summary>
    /// BreedCount "efetivo" pro cálculo de risco, descontando o descanso desde a última
    /// gestação completada por esse peixe (`LastBredAt`) até `asOf` (o momento do novo Start —
    /// não recomputado durante a gestação em si, que não conta como descanso). Decaimento
    /// exponencial de meia-vida `RestHalfLifeDays`: sem histórico ou peixe nunca cruzado,
    /// devolve o BreedCount cru (nada a decair).
    /// </summary>
    public static double EffectiveBreedCount(int breedCount, DateTime? lastBredAt, DateTime asOf)
    {
        if (breedCount <= 0 || lastBredAt is null)
            return breedCount;

        double restDays = Math.Max(0, (asOf - lastBredAt.Value).TotalDays);
        double decay = Math.Pow(0.5, restDays / BreedingDefaults.RestHalfLifeDays);
        return breedCount * decay;
    }

    /// <summary>
    /// Custo em premium do seguro de cruzamento: garante 0% de morte pros dois pais nesta
    /// gestação. Escala com o risco combinado sendo removido (mais caro quanto mais arriscado
    /// o casal estava antes do seguro) — mesma lógica de "pague mais pra remover mais" do rush.
    /// </summary>
    public static decimal InsuranceCostPremium(double parentADeathChance, double parentBDeathChance)
    {
        double combinedPercent = (parentADeathChance + parentBDeathChance) * 100.0;
        decimal cost = BreedingDefaults.InsuranceBasePremium + BreedingDefaults.InsurancePerRiskPercent * (decimal)combinedPercent;
        return Math.Clamp(cost, BreedingDefaults.InsuranceMinPremium, BreedingDefaults.InsuranceMaxPremium);
    }
}
