# Vivarium — Contexto do Projeto

> Este arquivo é a fonte única de verdade sobre o **estado vigente** das decisões de design e arquitetura do Vivarium — leia-o por completo antes de sugerir código ou mudanças estruturais. Ao tomar novas decisões, atualize este arquivo (e commite a mudança) pra manter o contexto sempre atual entre sessões. Ele contém só o que está valendo HOJE; a jornada de como se chegou a cada número/decisão (investigações, bugs corrigidos, calibrações superadas, mecanismos removidos) vive em `HISTORY.md`, que não é carregado por padrão — consulte-o só quando precisar entender o *porquê* de algo. Ideias de feature já discutidas mas ainda não implementadas vivem em `BACKLOG.md` (idem, não carregado por padrão).

## 0. Sobre o projeto

**Vivarium** é um jogo idle de navegador (aquário virtual), pensado para ser deixado aberto como "tela de fundo" enquanto o jogador trabalha — geração passiva de peixes, mercado interno entre jogadores, e sistema de raridade genética por composição visual (seed-based, sem blockchain).

**Desenvolvedor:** solo dev, part-time (dois empregos), já experiente em .NET/EF Core/Postgres/Azure, sem experiência prévia publicando jogos ou com arquitetura front/back separada. Um colaborador (design/3D) apoia a parte visual.

**Filosofia de escopo:** MVP enxuto, cortar o que não é essencial para validar o motor central (geração + raridade + mercado) antes de expandir (breeding, múltiplas espécies, pesca ativa de descoberta, terrário/orquídea como novos habitats reaproveitando o mesmo motor).

**Modelo de negócio:** free-to-play, moeda dupla (soft ganha jogando / premium comprada), mercado interno em moeda soft sem cash-out. Transferência de item entre contas é permitida e pode ser negociada por dinheiro real fora do jogo (Discord/WhatsApp), mas isso é responsabilidade dos jogadores — o jogo não processa, não intermedia, nem cobra taxa sobre essas negociações externas.

---

## 1. Visão geral

O peixe é composto por 4 camadas visuais independentes, renderizadas sobre um corpo base:
- **Corpo** (com possível brilho/shimmer sobreposto)
- **Cauda**
- **Nadadeira dorsal**
- **Nadadeira peitoral**

Cada peixe nasce com um **seed único e imutável**. Todo trait visual é derivado desse seed via hash determinístico no nascimento e depois **congelado** (ver §6) — o mesmo peixe sempre renderiza igual, e só o seed + species + o JSON de traits já resolvido precisam ser persistidos.

---

## 2. Tabela de raridade — Corpo (brilho/shimmer)

O corpo é sempre desenhado na mesma base cinza. O que varia é uma camada de brilho aplicada por cima via blend mode (`overlay`/`screen`), com cor e opacidade próprias.

| Tier | Nome | Peso (%) | Cor do brilho | Opacidade do brilho |
|---|---|---|---|---|
| 0 | Sem brilho | 78% | — | 0% |
| 1 | Brilho sutil | 15% | Dourado, Prateado, Azulado | 10–25% |
| 2 | Brilho vibrante | 5.5% | Verde-esmeralda, Roxo, Rosa | 30–50% |
| 3 | Brilho raro | 1.3% | Arco-íris (gradiente), Preto absoluto | 55–75% |
| 4 | Brilho lendário | **0.02%** (chance real de sortear) | Iridescente (shift de cor conforme ângulo/tempo) | 80–100% |

> Dentro de cada tier, a opacidade exata também é sorteada dentro da faixa (uniforme). O peso real do sorteio do tier Lendário é 0.02% (pirâmide "Íngreme", ver `HISTORY.md §5`).

---

## 3. Tabela de raridade — Cauda, Nadadeira Dorsal, Nadadeira Peitoral

Paleta curada e fechada (não hue contínuo), pra garantir combinação visual sempre coerente.

| Cor | Peso base (%) |
|---|---|
| Laranja | 22% |
| Azul | 20% |
| Vermelho | 18% |
| Amarelo | 16% |
| Verde | 14% |
| Roxo | 6% |
| Preto | 3% |
| Branco puro | 1% |

Cada uma das 3 partes (cauda, dorsal, peitoral) sorteia **independentemente** dessa tabela — ver §4 pra regra de correlação com o brilho do corpo.

### Padrão sobre a parte — aplicado igualmente às 3 partes

| Tipo de padrão | Peso (%) |
|---|---|
| Sem padrão | 76.2% |
| Estria | 8% |
| Bolinha | 8% |
| Escamas | 3% |
| Raios | 1.6% |
| Ziguezague | 1.2% |
| Rede | 0.9% |
| Degradê | 0.4% |
| Manchado | 0.35% |
| Ocelo | 0.2% |
| Mármore | 0.05% |

Se houver padrão (qualquer tipo ≠ "sem padrão"):
- **Tamanho**: contínuo 0–100, normal(50,20) — extremos <10 ou >90 contam como "raro" no score.
- **Cor**: mesma paleta curada acima, nunca igual à cor de base da mesma parte.
- **Opacidade**: 20–90%, uniforme — abaixo de 30% ou acima de 80% conta como raro no score.

**Degradê — mix de duas cores:** subtrait `GradientMix` sorteia como a cor de base e a cor do padrão se misturam — `BaseDominant`/`Even`/`PatternDominant`, pesos 45%/10%/45% (gradiente vertical, ponto de corte desloca conforme o mix; `patternSize` continua controlando só a suavidade da transição, trait ortogonal com salt próprio `{parte}_pattern_mix`). Regra de score assimétrica: em `Even`, as duas cores contam pro score (padrão default de qualquer parte com padrão); em `BaseDominant`/`PatternDominant`, só a cor dominante conta. `Even` (10% dentro do Degradê, ~0.04% de todas as partes) é mais raro que Mármore.

---

## 4. Regra de correlação (brilho do corpo → cor das partes)

Se o corpo saiu em Tier 2, 3 ou 4, a tabela de peso de cor das partes é ajustada: a cor mais próxima do tom do brilho recebe **+15 pontos percentuais** de peso (renormalizando o resto proporcionalmente). Mapa: Esmeralda→Verde, Roxo→Roxo, Rosa→Vermelho, Arco-íris→Branco puro, Preto absoluto→Preto, Iridescente→Branco puro.

**Cor absoluta (visual):** quando cauda, dorsal e peitoral saem todas na MESMA cor (mesma condição do bônus `sameColor3`, §5.1), o corpo — normalmente cinza — é tingido com essa cor também (`fishRenderer.getBodySprite(tintColor)`, overlay sobre o gradiente cinza, mantém a textura por baixo). Puramente visual, o score não muda — o bônus de conjunto já existe desde antes. Chance do zero (sem correlação de brilho ativa) é baixa, em torno de 3% somando todas as cores. Cruzar dois pais com as 3 partes já na mesma cor no Ninho aumenta bastante a chance, mas mutação sempre pode resortear.

---

## 5. Cálculo de Rarity Score

```
RarityScore = -log10(P_corpo × P_cauda × P_dorsal × P_peitoral × P_tamanho_extremo × P_opacidade_extremo)
```

Onde cada `P_x` é a probabilidade (peso/100) do valor sorteado naquele trait. Calculado uma vez na criação do peixe e **congelado** no banco (§6) — nunca mais recalculado do zero.

**Faixas de exibição ao jogador (vigentes hoje):**
- Comum: score < 5.45
- Incomum: 5.45–12.04
- Raro: 12.04–13.78
- Épico: 13.78–16.60
- Lendário: 16.60+

> Recalibradas várias vezes em ago/2026 conforme o sistema de raridade evoluiu (corpo pesando mais, bônus de conjunto, 11 padrões, pirâmide "Íngreme") — trajetória completa em `HISTORY.md §5`. Espelhado em `fishRenderer.js BANDS`, `format.js RARITY_RANGES`, `MarketService.BandNameOf`, `Vivarium.AdminReset -- band-distribution`. **Sem migration/backfill necessário pra mudar os cortes** — são só uma classificação stateless do score já gravado.

### 5.1 Decisões de implementação do score

- **Base do log:** log10.
- **Entram no score:** tier de shimmer do corpo (× `ShimmerScoreWeight = 2.5`); por parte (cauda/dorsal/peitoral): cor base (já ajustada pela correlação, quando ativa), tipo de padrão, cor do padrão, e — só nos extremos — tamanho e opacidade do padrão; velocidade de cauda/nadadeira só nos extremos, peso reduzido; bônus de conjunto coeso.
- **Bônus de conjunto coeso:** mesmo padrão (≠ Sem padrão) em 2 partes: +1.0; nas 3: +2.5. Mesma cor de base em 2 partes: +0.8; nas 3 (monocromático): +2.0 (`SamePattern2/3Bonus`, `SameColor2/3Bonus`, `TraitGenerator.SetBonus`, espelhado em `generator.js`).
- **Não entram no score:** cor do shimmer dentro do tier, opacidade do shimmer, amplitudes de movimento (todos uniformes/estéticos).
- **Tamanho do padrão:** normal(50,20) clampada 0–100; extremos <10/>90 ≈2.3% cada.
- **Opacidade do padrão:** uniforme 20–90; extremos <30/>80, P=1/7 cada.
- **Movimento:** velocidade de cauda/nadadeira normal(50,20) clampada 0–100 (extremos <10/>90 ≈4.55% cada). Só extremos entram no score, peso 0.5 (`MovementScoreWeight`). Amplitudes (cauda 0.20–0.75 rad, nadadeira 0.15–0.75 rad) uniformes, fora do score. `swimSpeedOf`: 0.75·cauda + 0.25·nadadeira — cauda rápida = peixe rápido, coerência visual.

**Lição de balanceamento:** ao calibrar variância dentro de um tier, balancear pelo TETO/variância, não só pela média — sorte rara não deve gerar vantagem desproporcional entre jogadores (motivou o taper do Lendário e a redução do growth do Épico, ver `HISTORY.md §8.6`).

---

## 6. Algoritmo determinístico: Seed → Traits

```csharp
public FishTraits GenerateTraits(long seed, int speciesId, int configVersion)
{
    var config = LoadTraitWeightConfig(speciesId, configVersion);
    var traits = new FishTraits();

    // Cada trait usa um "sub-seed" derivado do seed principal + um salt fixo
    // pra evitar correlação indesejada entre traits que deveriam ser independentes
    traits.BodyShimmerTier = WeightedPick(config.BodyShimmer, Hash(seed, "body_shimmer"));
    traits.BodyShimmerColor = WeightedPick(config.ShimmerColors, Hash(seed, "body_shimmer_color"));
    traits.BodyShimmerOpacity = RangePick(tierRange, Hash(seed, "body_shimmer_opacity"));

    var correlationBoost = traits.BodyShimmerTier >= 2 ? traits.BodyShimmerColor : null;

    traits.TailColor = WeightedPick(ApplyCorrelation(config.PartColors, correlationBoost), Hash(seed, "tail_color"));
    traits.DorsalColor = WeightedPick(ApplyCorrelation(config.PartColors, correlationBoost), Hash(seed, "dorsal_color"));
    traits.PectoralColor = WeightedPick(ApplyCorrelation(config.PartColors, correlationBoost), Hash(seed, "pectoral_color"));

    // ... padrão, tamanho, opacidade seguem a mesma lógica por parte

    return traits;
}

long Hash(long seed, string salt)
{
    // hash determinístico simples (ex: combinar seed + salt via SHA256 truncado pra long)
}
```

**Mecanismo oficial de extensão:** adicionar um trait novo = adicionar um `salt` novo. Como cada trait deriva de `Hash(seed, salt)` isolado, incluir um trait não muda nenhum trait existente. Se o trait novo não entrar no rarity score (ou entrar só nos extremos, com peso), os scores de peixes existentes também não mudam. Só bumpar `TraitConfigVersion` quando **mudar pesos/algoritmo de um trait já existente**, não ao adicionar trait novo.

**Estado atual do versionamento:** desde que os traits passaram a ser congelados no nascimento (§6), o motor não recalcula mais traits de peixes existentes a partir do `Seed` — `TraitConfigVersion` é hoje só um campo de auditoria (o gap histórico de versionamento nunca corrigido de verdade, que causava `RarityScore` desatualizado a cada rebalanceamento, está documentado em `HISTORY.md §7`). **Lição operacional que continua valendo:** depois de qualquer mudança que altere pesos/algoritmo de um trait já existente, rodar `dotnet run --project tools/Vivarium.AdminReset -- backfill-traits` em produção antes de considerar a mudança concluída, e rodar de novo **depois** do deploy do backend terminar (a janela entre migration e o backend novo subir pode criar peixes com `TraitsJson` nulo — achado real em `HISTORY.md §8.22.1`).

---

## 7. Loop de gameplay — MVP

### 7.1 Geração e coleta

- A cada `GenerationIntervalMinutes` (ver §7.11), surge um item novo na fila, até `QueueCap` (default 5).
- Free: coleta manual (clique). VIP: coleta automática (§7.3), com opt-out (§7.18).
- Seed do peixe é sorteado no momento da **coleta**, não da entrada na fila.
- Conta nova ganha 1 peixe já pronto pra coletar no registro — evita o "primeiro clique vazio".

### 7.2 Qualidade da água (sink de manutenção)

Variável 0–100, degrada por tick de tempo real. Abaixo de 40: geração mais lenta. Abaixo de 15: chance pequena do peixe da fila "adoecer" (reduz rarity score potencial, não mata). Filtro (item, soft) restaura pra 100; filtro automático reduz a taxa de degradação (ver níveis em §7.15).

### 7.3 Diferenciação online/offline

Diferencial do VIP (coleta automática) só vale enquanto a aba do jogo está aberta — trocar de aba ainda conta como online; fechar o navegador conta como offline.

**Heartbeat:** cliente manda a cada 60s, independente de foco. Servidor: heartbeat < ~3min → online; senão offline a partir do último heartbeat.

| Estado | Taxa de geração | Coleta |
|---|---|---|
| Online (free) | Cheia | Manual |
| Online (VIP) | Cheia | Automática |
| Offline (free ou VIP) | ~40-50% da online | Manual ao retornar |

Campos no `Habitat`: `LastTickAt`, `LastHeartbeatAt`, `OnlineGenerationRate`, `OfflineGenerationRate`.

> **Melhoria futura:** trocar heartbeat por WebSocket/SSE — evita depender de throttling de timer em segundo plano e detecta desconexão em tempo real.

### 7.4 Tanque — capacidade e progressão

Ver §7.15 (faixas de capacidade) — o upgrade infinito original foi substituído por 3 faixas nomeadas com curvas próprias.

### 7.5 Decisões de implementação do loop — `Vivarium.Core/Gameplay`

Lógica pura em `HabitatTicker.ProcessTick` (recebe estado + "agora", devolve o que mudou; sem banco/relógio). Parâmetros em `TickConfig`, defaults de tanque novo em `HabitatDefaults`.

- Janela do tick dividida por heartbeat: online do `LastTickAt` até `LastHeartbeatAt`.
- Progresso persistido em `GenerationProgressMinutes`; a cada intervalo completo, 1 item entra na fila (limitado ao espaço livre).
- Fila cheia não acumula estoque — progresso clampado em 1 intervalo.
- Qualidade < 40: geração ×0.5. Filtro automático: degradação ×0.5 (taperado, ver §7.15).
- Doença (qualidade < 15): 10% de chance por item novo; na coleta, sorteia 2 seeds e fica o de menor rarity score.
- Seed de coleta sorteado via RNG criptográfico, nunca na entrada da fila.

### 7.6 Economia — farm de moedas por raridade

Renda passiva contínua por peixe no tanque, escalada pela raridade. Lógica pura em `IncomeCalculator.cs`; parâmetros em `TickConfig`; acumulada no tick preguiçoso, creditada automática (idle puro).

- **Fórmula vigente:** `coinsPorHora(score) = IncomeBasePerHour(1.5) · exp(IncomeGrowth(0.42) · (score − IncomeRefScore(4)))`, com taper acima do score 14 (`IncomeLegendaryTaperScore=14.0`, `IncomeLegendaryTaperGrowth=0.10`). Comum (~5) ~2.3/h, raro (7.5) ~6.5/h, épico (9.8–14.0) ~17–100/h, lendário (14+) ~100–200/h no máximo observado (1M seeds). Trajetória de calibração completa em `HISTORY.md §8.6`.
- **Sinergia por cor, por parte (v3, vigente):** cada parte (cauda/dorsal/peitoral) soma bônus independente: `bonus(N) = N<2 ? 0 : min(SynergyMaxBonus(0.10), SynergyBaseBonus(0.03) + SynergyPerExtraMatch(0.01)·(N−2))`. Pior caso (3 partes saturadas) = +30%. N=9 peixes pra saturar uma parte.
- **Fator água com patamar:** `WaterFactor(maint)=1` pra `maint ≥ 80` (`IncomeWaterPlateau`); abaixo: `(maint/80)^0.7`. Usa a MÉDIA início/fim da janela do tick (`0.5·(WaterFactor(início)+WaterFactor(fim))`).
- **Degradação ponderada por raridade:** `base·(1 + DegradationPerFishFactor(0.30)·pesoTotal)`, `pesoTotal = Σ rarityScore/DegradationRarityRefScore(5)` — tanque rico suja mais que tanque grande-mas-comum.
- **Online/offline:** reusa `OnlineGenerationRate`/`OfflineGenerationRate` (1.0/0.45). Offline com teto de 8h (`IncomeOfflineCapMinutes=480`).
- **Acúmulo:** `Habitat.CoinAccrual` guarda a fração; renda passiva não vai pro `TransactionLog` (inundaria a auditoria).
- **Anti-cheat:** tudo server-side, limitado por tempo real decorrido (`LastTickAt`), água/raridade lidas do banco, `DateTime.UtcNow` do servidor.
- **Simulação de trajetória completa:** `Vivarium.Simulation simulate` roda um "jogador sintético" por 120 dias (3 perfis: casual/ativo/dedicado) comprando filtro+upgrade+breeding ao mesmo tempo — usado pra validar qualquer mudança de economia antes de aplicar. **Lição de simulador:** nunca assumir que um upgrade automático substitui 100% a ação manual equivalente sem checar o efeito real no código (achado real documentado em `HISTORY.md §8.6`).

**Efeitos da água no cliente (só visual):** peixes mais lentos com água ruim (`speedFactor = 0.5 + 0.5·maint/100`); água esverdeia/suja progressivamente abaixo de `MURK_CLEAN_ABOVE=80`.

### 7.7 Mochila — storage de criaturas

Três estados por criatura: **no tanque** (`HabitatId` do habitat, não listada), **na mochila** (`HabitatId=null`, sem listing ativa), **à venda** (`HabitatId=null`, listing ativa). Mochila guarda peixes sem gastar vaga do tanque. Cap `BackpackCapacity = 100`. Coletar/comprar/receber com tanque cheio vai pra mochila (nunca limbo); se os dois estiverem cheios, ação bloqueada com mensagem clara. Endpoints: `GET /api/game/backpack`, `POST /api/game/creatures/{id}/store`, `POST /api/game/creatures/{id}/deploy`.

### 7.8 Breeding

Sink de soft (custo dinâmico) + sink de tempo (pais fora do tanque durante a gestação) + risco crescente do pai não sobreviver a cada uso.

- **Habitat dedicado:** `HabitatType.Code="Breeding"`, capacity=2 por usuário, criado no registro. Nunca passa por `HabitatTicker.ProcessTick`.
- **Gestação:** `GestationHours(scoreA, scoreB) = BaseGestationHours · exp(GestationGrowth(0.185) · (scoreA+scoreB − GestationRefScore))`, clamp `[MinGestationHours, MaxGestationHours]`. **Valores de produção reais: Base=6h, Min=6h, Max=240h** (2 comuns 6h, comum+raro ~10,5h, 2 raros ~18h, 2 épicos ~55h, 2 lendários até 240h).
  - ⚠️ **Override TEMPORÁRIO de QA ainda ativo:** `Base=Min=Max=1.0h` — TODO casal gesta em exatamente 1h, independente da raridade. Motivo: maximizar volume de cruzamentos pra achar bugs antes do lançamento (aquários serão resetados de qualquer forma). **Reverter pra 6/6/240 antes de qualquer lançamento "de verdade"** (`src/Vivarium.Core/Gameplay/BreedingConfig.cs`).
- **Custo dinâmico:** `CostSoft(scoreA, scoreB)`, base=150, growth=0.10, ref=10, clamp `[100, 5000]`. 2 comuns 150 soft; 2 lendários ~1108 soft (o sink real pros ricos é o tempo de lockup, não o custo em soft).
- **Risco de morte crescente:** `BreedCount` (nº de gestações completadas como pai/mãe) — `DeathChance(n) = BaseDeathChance + (MaxDeathChance−BaseDeathChance)·(1−exp(−DeathRiskGrowth·n))`, teto 85%, nunca garantido. Se morre: `IsDead=true`+`DiedAt` (linha preservada pra manter FK de linhagem).
- **Mitigação do risco (3 alavancas, todas opt-in):**
  - **Descanso (grátis, passivo):** `EffectiveBreedCount` decai o `BreedCount` por meia-vida exponencial (`RestHalfLifeDays`, real=**5.0**). ⚠️ **Override temporário de QA ainda ativo: `RestHalfLifeDays=0.2`** (~4h48min em vez de 5 dias) — reverter pra 5.0 antes do lançamento.
  - **Estabilizador (soft):** `useStabilizer` — cobra `StabilizerCostSoft(150)`, reduz risco pela metade (`StabilizerReductionFactor=0.5`).
  - **Seguro (premium):** `useInsurance` — garante 0% de morte, custo escala com o risco removido (`InsuranceCostPremium`, clamp `[20,400]`).
  - Risco/seguro travados no `Start`, não recalculados no `Collect`.
- **Herança trait-a-trait** (`TraitGenerator.BreedTraits`): por trait (shimmer/cauda/dorsal/peitoral), rolagem decide mutação (re-sorteia do zero) vs. herança (escolhe pai A/B via `BiasedInheritProbability(probA, probB, RarityBiasStrength=0.15)` — favorece o valor mais raro). Aplica-se a shimmer, cor e padrão de cada parte. Movimento continua 50/50 puro.
- **Anti-duplicação:** `DuplicationStreak` conta slots consecutivos vindos do mesmo pai sem mutar; `penalty = min(AntiDuplicationMaxPenalty(0.75), 1 − AntiDuplicationDecay(0.55)^streak)` empurra o threshold de herança pra longe do lado que já vem ganhando — mas **nunca ultrapassa 0.5** (nunca inverte o lado favorecido pela raridade; bug de inversão corrigido em 13/08, ver `HISTORY.md §8.8`). Mutação reseta a sequência. Movimento fica de fora.
- **Piso de mutação:** quando um trait sofre mutação, o resultado nunca pode ficar mais comum que o pai mais fraco dos dois — `floorWeight = max(peso do valor do pai A, peso do valor do pai B)` restringe a tabela antes de sortear (`WeightedTable.Restrict` + `PickBiasedTowardRare`, `MutationRarityBiasStrength=0.15`). Auto-limitado: quando o pai mais fraco já é o valor mais comum da tabela, a regra não muda nada.
  - **Não garante score TOTAL do filhote ≥ pai mais fraco** — a garantia é só POR TRAIT; herança independente por slot pode perder o bônus de conjunto coeso que os pais tinham (ver `HISTORY.md §8.8`, "filhote de elite").
- **Mutação:** `MutationChance = 0.04`.
- **Sem mecanismo de avô** — removido por completo em 13/08/2026 quando os traits passaram a ser congelados no nascimento (§6); `BreedTraits` lê o `TraitsJson` real do pai direto, sem limite de profundidade de reconstrução (ver `HISTORY.md §8.8` pra a história completa do mecanismo removido).
- **Prévia sem compromisso:** `GET /api/breeding/quote` — custo, gestação, `ChildTierDistribution`, `BreedCount`/risco de cada pai, sem gastar nada.
- **Revelação clique-a-clique (Raro+):** `CollectCelebration` monta o peixe camada por camada a cada clique (corpo→brilho→cauda→dorsal→peitoral), raridade escondida até a última parte (`FishCanvas` prop `revealStep`). Aplica na coleta do tanque e no Ninho.
- **Despedida do pai perdido:** se um pai não sobrevive, `CollectCelebration` mostra retrato em cinza + animação de entrada, separado do resto por linha divisória.
- **Resgate de pai preso no Ninho:** `CollectAsync` exige espaço pro pior caso (filhote + 2 pais, 3 vagas) ANTES de mexer em qualquer coisa; `GameService.GetTankAsync` roda `RescueStrandedBreedingParentsAsync` a cada carregamento do tanque (defesa em profundidade, cobre também peixes já presos de antes da correção).
- **Registro de cruzamentos:** `GET /api/breeding/history` — últimas 50 gestações coletadas, mais recente primeiro, com pais/filhote completos.
- **Chance e origem no reveal:** cada fator de raridade exibido mostra `probPct` (probabilidade conjunta do grupo) e `source` (`"parentA"`/`"parentB"`/`"mutation"`) — lido direto de `CreatureInstance.BreedingSourceJson`, congelado no nascimento junto do `TraitsJson`.
- **Endpoints:** `GET /api/breeding`, `GET /api/breeding/quote`, `GET /api/breeding/history`, `POST /api/breeding/start` (aceita `useStabilizer`/`useInsurance`), `POST /api/breeding/collect`, `POST /api/breeding/rush`.

### 7.9 Fora do escopo do MVP

- **Alimentação:** cortada, candidata a v2. Ideia refinada (não implementada): ração por tier de raridade, item ativo que peixes do mesmo tier disputam por velocidade de nado (`swimSpeedOf`) — valor esperado escala com a raridade de quem está no tanque.
- **Sujeira visual (cocô):** decidido ser puramente decorativo, sincronizado com o `murk` existente — a física real de degradação da água continua exatamente como está, sem estado novo no servidor.
- **Cascudo:** peixe futuro (não item de loja) que ajudaria na limpeza passiva — hook comentado em `GameService.ApplyTickAsync`, sem implementação. O filtro automático (item comprado) já cobre a função equivalente hoje.
- **Fusão de peixes:** cogitada e adiada — risco de canibalizar o sink de breeding se virar caminho determinístico/sem risco pra subir de raridade.
- **Comentários no aquário visitado:** ver §7.16 — precisa de moderação básica antes de ir pra produção.
- **Backlog de ideias novas ainda não implementadas** (link de indicação, rate limiting de login/forgot-password): ver `BACKLOG.md`.

### 7.10 Recompensa diária

Resgatável 1x/dia calendário UTC. Elegibilidade calculada on-demand (`User.LastDailyRewardAt`), sem job. Endpoints: `GET /api/game/daily-reward`, `POST /api/game/daily-reward/claim` (audita `TransactionLog.DailyReward`).

**Redesenho 17/08/2026** — era valor fixo (25 soft, sem streak, sem variância); virou uma "roleta" com 4 componentes, lógica pura em `DailyRewardCalculator` (`Vivarium.Core/Gameplay`), parâmetros em `TickConfig`:
- **Valor base escalado pela renda:** `max(DailyRewardMinSoft=25, coinsPerHora × DailyRewardIncomeHours=3)` — quem tem um tanque melhor ganha mais, sempre com piso de 25 pra quem tá começando.
- **Roleta:** sorteia dentro de `base × [1−0.4, 1+0.4]` (`DailyRewardRouletteRange`) — variância visível, não um valor determinístico.
- **Streak (dias consecutivos):** `+5%/dia` (`DailyRewardStreakBonusPerDay`) até o teto de `+50%` (`DailyRewardStreakBonusCap`); `DailyRewardCalculator.NextStreak` soma 1 se o último resgate foi ONTEM, senão **reseta pra 1** (não só "recomeça sem perder nada" — perde o bônus acumulado de verdade). Decisão explícita do usuário: reset total é o que estimula presença diária real; nunca deixa o jogador abaixo do valor BASE (só o bônus é perdido, nunca o piso).
- **Chance de ovo:** `DailyRewardEggChance = 3%` de vir, além da moeda, um **Ovo Raro** (`DailyRewardEggItemKey = "egg_rare"`, ver §7.9-egg) de brinde — entregue via Caixa de Entrada (`InboxEntryKind.DailyRewardEgg`), montado inline em `GameService.ClaimDailyRewardAsync` (não via `InboxService.QueueSystemMessage` — `InboxService` depende de `GameService`, injetar o inverso criaria dependência circular).
- **`User.DailyRewardStreak`** (int, migration `AddDailyRewardStreak`) persiste o streak atual.
- **Frontend:** `DailyRewardModal.jsx` — abre ao clicar no botão do topbar (mostra a faixa min/max, sequência atual e bônus ANTES de resgatar); resgatar dispara uma animação de "roleta" (números girando dentro da faixa) que assenta no valor real devolvido pela API — sequência e bônus concedido ficam sempre visíveis durante o giro (pedido explícito do usuário).
- **Testes:** `DailyRewardCalculatorTests.cs` (Core, todas as funções puras) + `DailyRewardTests.cs` (Api, inclui teste estatístico — 200 resgates simulados — confirmando que a chance de ovo realmente dispara) + `daily-reward.cy.js` (E2E, mocka a API).

### 7.11 Ritmo lento anti-rush + acelerar com premium

Geração e cruzamento são deliberadamente lentos — a única forma de comprimir esse tempo é premium (comprada com dinheiro real, quando o processador de pagamento existir — gap real, ver §10).

- **Geração:** `GenerationIntervalMinutes` real de produção = **60**. ⚠️ **Override TEMPORÁRIO de QA ainda ativo: 10** — mais volume de peixe pra testar sem esperar horas. **Reverter pra 60 antes de qualquer lançamento "de verdade"** (`TickConfig.cs`, `HabitatDefaults.GenerationIntervalMinutes`).
- **Rush (pular tempo com premium):** `RushCalculator` — fila: `0.15 premium/min` restante; gestação: `2.0 premium/hora` restante (teto 480 premium a 240h). Só rush total (não parcial). Endpoints: `POST /api/game/queue/{id}/rush`, `POST /api/breeding/rush` — debitam premium, zeram `ReadyAt`, auditam `TransactionLog.TimeSkip`.
- **Gap real, não escondido:** não existe processador de pagamento integrado — hoje não há forma real de um jogador comprar premium. Mecanismo de jogo pronto e testado; falta a ponte com dinheiro real (fora do escopo até aqui). Dev local: `/api/dev/coins?currency=PREMIUM` (só em Development).

### 7.12 Venda ao NPC / vendor

Sink pra duplicatas/comuns acumulados. `VendorCalculator.Price(rarityScore)` reusa a curva de `IncomeCalculator`: `preço = max(VendorMinPrice(1), coinsPorHora(score) × VendorHoursEquivalent(2.0))`. Um comum vende por ~9 soft. Não apaga a linha — marca `SoldAt=now`+`HabitatId=null` (preserva linhagem/auditoria). Endpoint: `POST /api/game/creatures/{id}/sell-vendor`.

### 7.13 Peixe inicial no registro

Conta nova ganha 1 peixe já pronto pra coletar (`ReadyAt=now`) no mesmo `SaveChangesAsync` do habitat, no registro.

### 7.14 Painel de admin

`User.IsAdmin` (bool, checado sempre fresco do banco, nunca embarcado no JWT — token sem revogação, 7 dias). Endpoint: `POST /api/admin/give-starter-fish-all` (403 se não-admin). Sem UI pra promover admin — só via update direto no banco.

### 7.15 Tanque em faixas de capacidade + filtros em níveis

- **3 faixas nomeadas** (`CapacityBands`): "Aquário" (3-5), "Aquário Grande" (5-10), "Aquário Master" (10-15, teto absoluto). Cada faixa tem `PriceBase`/`PriceGrowth`/`DegradationBandFactor` próprios.
- **Custo de transição entre faixas:** cobrado ao cruzar o teto de uma faixa — Aquário→Grande 4000 soft, Grande→Master 12000 soft (bem acima da curva suave, gate deliberado).
- **3 produtos separados na loja:** `tank_upgrade` (curva suave dentro da faixa atual), `aquario_grande`, `aquario_master` (cada um trava/destrava conforme a capacidade atual, nunca recompra).
- **Filtro em nível, sem penhasco:** `FilterCapacity` (decimal) substitui o binário antigo. Cobertura total reduz degradação pela metade; acima disso, `filterFactor` decai suavemente de volta a 1.0 (`FilterTaperExponent=1.0`), nunca corta abrupto. 3 níveis: `auto_filter` (cobre 5, 500 soft), `auto_filter_2` (cobre 10, 1200 soft), `auto_filter_3` (cobre 18, 2500 soft) — níveis não empilham, o melhor prevalece.
- **Hook do cascudo:** ver §7.9 — comentário em `GameService.ApplyTickAsync`, sem implementação.

### 7.16 Ranking global + visita a aquário de outro jogador

Dois rankings (`rarity`/`income`), sem opt-out, top 100 + posição própria. `LeaderboardService.AllAquariumSnapshotsAsync` recalcula a cada request (sem cache — ok pro tamanho atual). Visita é só-leitura (`GetSpectatorTankAsync`, não roda tick — não muta estado de outro jogador). Endpoints: `GET /api/leaderboard/{metric}`, `GET /api/leaderboard/visit/{username}`.

### 7.17 Limpeza Automática (VIP) + Sensor de Qualidade da Água

- **Limpeza Automática (grátis, VIP ativo):** no mesmo tick da coleta automática, o servidor compra sozinho um Filtro assim que `MaintenanceLevel` cruza o gatilho configurado (default 0%). Roda depois de `AccrueIncomeAsync` (a renda do intervalo já foi calculada em cima da água real).
- **Sensor de Qualidade da Água:** item permanente por aquário (`Habitat.HasWaterSensor`), libera um slider (0 até `WaterSensorMaxTriggerPercent=80`) pra escolher o gatilho. Preço cresce com a faixa do aquário (`CapacityBand.WaterSensorPrice`). Só tem efeito com VIP ativo. Endpoint de config (não compra): `POST /api/game/water-sensor/trigger`.
- **Gatilho ótimo não é sempre o teto** — depende da composição do tanque (por isso é slider livre, não escada de tiers).

### 7.18 Opt-out de coleta/limpeza automática de VIP + "peixe novo" na Mochila

`Habitat.AutoCollectEnabled`/`AutoCleanEnabled` (default `true` — preserva comportamento pra quem nunca mexeu). Configurável mesmo sem VIP; só tem efeito com VIP. `CreatureInstance.IsNew` (default `false`) — só `true` na coleta AUTOMÁTICA; peixe some da lista até `mark-seen`, exibido com selo "🆕 Novo" + silhueta. Endpoints: `POST /api/game/toggles`, `POST /api/game/creatures/{id}/mark-seen`.

**Coleta automática cai pra mochila quando o tanque está cheio** (nunca trava a fila) — mesma regra da coleta manual.

**Lição (achada corrigindo um bug real, ver `HISTORY.md §8.22`):** qualquer objeto "peixe" fabricado fora da API (não só em testes, também código de produção como o aquário decorativo da tela de login) precisa ter `.traits` preenchido desde 13/08/2026 — não há mais fallback pra `generateTraits(seed)`.

### 7.19 Caixa de entrada

Peixe comprado no Mercado ou recebido via transferência **não aparece mais direto no tanque/mochila** — vira entrada pendente na Caixa de Entrada, entregue só quando o jogador clica "Resgatar" (tanque se houver espaço, senão mochila).

- Schema: `InboxMessage` (1 por envio administrativo OU notificação de sistema — `CreatedByAdminId` nullable, null quando gerada pelo sistema) + `InboxEntry` (1 por destinatário/evento, `Kind`: `AdminMessage`/`MarketPurchase`/`DirectTransfer`/`MarketSale`). `CreatureInstance.PendingInboxClaim`+`OriginalOwnerId` (FK imutável, "primeiro dono", preparado pra suporte futuro a troca de username — não exposto em nenhum DTO ainda).
- Compra/transferência não checam mais espaço no momento da ação — sempre funciona (dado saldo/posse ok); marca `PendingInboxClaim=true`. Checagem de espaço migrou pro momento do resgate.
- **6 bloqueios enquanto pendente:** some da Mochila, não pode ser retransferido, relistado, usado como pai de breeding, nem vendido ao NPC.
- **Notificação de venda no Mercado (16/08/2026):** quando uma listagem é vendida, o VENDEDOR recebe uma entrada `Kind.MarketSale` avisando o valor já creditado — só informativa (sem `CreatureInstanceId`, sem reward pra resgatar; o soft já foi creditado direto na hora da venda por `MarketService.BuyAsync`, na mesma transação). `InboxService.QueueSystemMessage` é o helper reusável pra qualquer notificação de sistema (sem admin) — mesmo padrão vale pra futuras notificações do backlog.
- Ações do jogador: resgate individual/em massa, "Ler tudo", "Apagar mensagens lidas" (nunca remove recompensa em aberto).
- Admin: `POST /api/admin/inbox/send` — broadcast ou lista de usernames (username inexistente não bloqueia o envio, loga os não encontrados). Recompensa opcional: moeda (soft/premium) **e/ou ovo** (17/08/2026) — os dois podem vir juntos na mesma mensagem.
- **Ovo(s) como recompensa de admin (17/08/2026, iterado 2x no mesmo dia):** `InboxEntry.RewardItemDefinitionId` (POR ENTRADA, não por mensagem — 1ª versão usava `InboxMessage.RewardItemDefinitionId`, mas o usuário pediu no mesmo dia pra mandar **vários ovos, de tiers diferentes, na mesma mensagem** — só cabia com o campo na entrada) referencia um `ItemDefinition` de categoria `Egg` (mesmo catálogo da Loja, §7.21, nenhum item novo). **O peixe só é gerado no momento do resgate**, nunca no envio — evita gerar N peixes de uma vez num broadcast "Todos os jogadores" que ninguém abriu ainda. `GameService.GenerateEggCreatureAsync` é o helper compartilhado (extraído de `ItemService.BuyAsync`) usado tanto pra comprar quanto pra resgatar um ovo de recompensa — mesma regra de espaço ("bloqueia sem custar nada se tanque+mochila cheios").
  - **1 entrada por ovo:** `AdminSendMessageAsync(rewardEggKeys)` aceita uma lista com repetição = quantidade (ex: `["egg_common","egg_common","egg_legendary"]` = 2 Comum + 1 Lendário) — cria 1 `InboxEntry` por unidade, todas apontando pra MESMA `InboxMessage` (título/corpo/moeda continuam compartilhados). Se não há ovo nenhum, cai no caminho de sempre (1 entrada genérica).
  - **Moeda não pode duplicar:** como agora um jogador pode ter várias entradas da mesma mensagem, a recompensa em moeda (que é por MENSAGEM) só é creditada na primeira entrada resgatada — `TryApplyRewardAsync` checa se algum "irmão" da mesma mensagem já foi resgatado antes de creditar de novo.
  - `InboxEntryDto.RewardEggKey` (por entrada) expõe qual ovo está associado a CADA entrada ANTES do resgate (pro jogador ver o tier/cor de cada uma antes de abrir); a resposta do `claim` ganhou um campo `creature` (só preenchido quando o resgate gerou um peixe), usado pelo frontend pra abrir a mesma `CollectCelebration variant="egg"` da compra na Loja.
  - **Frontend:** `AdminPanel.jsx` ganhou um "carrinho" (select + botão "+ Adicionar", chips removíveis "Ovo Comum ×2") em vez de um único `<select>` — `InboxView.jsx` não precisou de nenhuma mudança pra suportar isso, já que cada entrada continua carregando no máximo 1 ovo (a complexidade de "vários" fica inteira no backend, que decompõe em várias entradas).
- **Decisão de design (17/08/2026, ainda não implementada):** a "chance de peixe grátis" do redesenho da recompensa diária (`BACKLOG.md #4`) vai usar esse MESMO mecanismo — em vez de gerar um peixe direto, a recompensa diária entregaria um ovo pela Caixa de Entrada pro jogador abrir. Registrado aqui pra quando a feature #4 for implementada não reinventar a entrega.
- Endpoints: `GET /api/inbox/`, `POST /api/inbox/{id}/claim`, `POST /api/inbox/claim-all`, `POST /api/inbox/mark-all-read`, `POST /api/inbox/clear-claimed`, `POST /api/admin/inbox/send`.
- **Backlog relacionado (não implementado):** comentários no aquário visitado (Ranking → "Visitar") — precisa de moderação básica antes de ir pra produção.

### 7.20 Perfil do jogador + "esqueci minha senha"

Editar email/senha a partir do ícone de conta + fluxo completo de redefinição por email.

- **Provedor: Resend** (API HTTP, não SMTP — a VM Oracle só libera saída em 22/80/443). `IEmailSender` genérico; `ResendEmailSender` só é registrado se `Resend:ApiKey` estiver configurada, senão `NullEmailSender` (loga, nunca quebra o app).
- ⚠️ **Gap real ativo:** sem domínio próprio verificado no Resend, o remetente de sandbox só entrega email pro dono da conta Resend — em produção, só o dono da conta recebe de verdade até alguém verificar um domínio (SPF/DKIM). Resolve junto com o gap de "domínio próprio" já listado em §10.
- **Token de reset:** 32 bytes aleatórios, só o hash SHA256 fica no banco, expira em 1h, pedir de novo invalida o anterior. Resposta de "esqueci a senha" nunca revela se o email existe (anti-enumeração).
- Endpoints: `POST /api/auth/forgot-password`, `POST /api/auth/reset-password`, `PUT /api/account/email`, `PUT /api/account/password` (sempre exigem senha atual).
- Frontend sem router: link do email (`?resetToken=...`) checado direto em `App.jsx` via `URLSearchParams`.

### 7.21 Ovo de peixe (loot box em diamante)

Item consumível pago em premium (💎) que gera 1 peixe na hora, com viés de raridade — reaproveita `WeightedTable.PickBiasedTowardRare` (mesmo mecanismo já usado no piso de mutação do breeding, §7.8) em vez de inventar um sorteio novo.

- **Motor:** `TraitGenerator.GenerateBiased(seed, biasStrength, configVersion)` — mesma geração fresca de `Generate`, mas aplica o bias aos mesmos 7 slots que já têm viés de raridade no breeding (tier de brilho + cor/padrão de cauda/dorsal/peitoral). `biasStrength=0` é byte-idêntico a `Generate(seed)` (nenhuma mudança de comportamento pra quem já chama sem viés — confirmado por teste). `CreatureCollector.CollectBiased` é o wrapper de coleta equivalente (sem o mecanismo de "desvantagem" da água suja, que não faz sentido numa compra deliberada).
- **3 tiers** (`ItemCategory.Egg`, `ItemEffect.EggBiasStrength` lido do `EffectJson`): Ovo Comum (8💎, bias 0.15), Ovo Raro (30💎, bias 0.35), **Ovo Lendário (90💎, bias 0.75 — subiu de 0.55 no mesmo dia, ver "Recalibração" abaixo)**. Preços/vieses iniciais — a recalibrar com uso real, mesmo espírito de todo outro sistema econômico do jogo.
- **Multiplicador de TIER DE BRILHO vs. coleta normal, medido empiricamente** (`dotnet run --project tools/Vivarium.Simulation -- eggodds`, 3M seeds por tier — não confiar só na fórmula, o rescale de `PickBiasedTowardRare` não é intuitivo): chance de sair brilho **Lendário** sobe **3,2×** (Comum), **14,9×** (Raro), e — no bias original de 0.55 do Ovo Lendário — **63,4×** (de 1 em 5.000 pra ~1 em 80). Tabela completa (todos os tiers de brilho × todos os tiers de ovo) espelhada no Guia de Raridade (`RarityGuide.jsx`, `EGG_ODDS`) e nas descrições da Loja (`StoreView.jsx`) — **os números aparecem pro jogador**, não só a fórmula interna (1ª versão da feature só tinha adjetivo vago tipo "chance melhorada", sem número; corrigido no mesmo dia a pedido do usuário, que perguntou explicitamente pelo multiplicador real).
- **Recalibração do Ovo Lendário (17/08/2026, mesmo dia): tier de brilho ≠ banda de raridade exibida.** Usuário relatou "3 ovos lendários, vieram 5 peixes Incomum" — investigado com `Vivarium.Simulation -- eggodds` estendido pra reportar a **banda de score** (Comum/Incomum/Raro/Épico/Lendário, o que o jogador vê), não só o tier de brilho isolado. Achado real: com bias=0.55, **Incomum continuava sendo o resultado mais provável mesmo no Ovo Lendário (64% de chance)** — a banda Incomum é enorme por construção (score 5.45–12.04, cobre quase metade da população mesmo sem viés nenhum), então o viés do ovo tirava peso principalmente do "Comum" sem necessariamente atravessar até "Raro". Chance de Raro+ era só ~30% — não era bug nem azar do usuário (3 ovos sem nenhum Raro+ tem ~34% de chance de acontecer), mas parecia pouco compensador pra um item pago em diamante. Testados 10 valores de bias (0.55 a 1.00, `Vivarium.Simulation -- eggbias`, 3M seeds cada) — escolhido **0.75**: Raro+ 58%, Épico+ 38%, só-Lendário 14% (era 30%/15%/3,4%). Só o Ovo Lendário mudou; Comum (0.15) e Raro (0.35) mantidos. Migration `RaiseLegendaryEggBias` (só `UpdateData` no `EffectJson`, reversível).
- **Entrega direta** (não pela Caixa de Entrada) — mesma regra "direto, como a fila normal" da coleta manual: `ItemService.BuyAsync` gera o peixe e chama `game.TryPlaceAsync` ANTES de debitar qualquer coisa; se não houver espaço no tanque nem na mochila, a compra é bloqueada sem custar nada.
- **Moeda:** `ItemService.BuyAsync` agora resolve a moeda por categoria (`Egg`→PREMIUM, resto→SOFT) em vez de sempre assumir SOFT — generalização pequena que abre caminho pra qualquer item futuro pago em premium.
- **Frontend:** card na Loja com preço em 💎 (em vez de `<Coin/>`), botão desabilitado sem saldo premium suficiente. **Toda** compra abre `CollectCelebration` (`variant="egg"`, não só Raro+) — primeiro um cluster de ova de peixe (não emoji de galinha 🥚, feedback do usuário) tingido pela cor do tier comprado (`eggTier`, reusa `--r-comum`/`--r-raro`/`--r-lendario`, a mesma paleta de raridade do resto do jogo) que "racha" ao toque (`hatchEgg`, animação CSS); depois disso, segue o fluxo normal — Raro+ ainda ganha a revelação clique-a-clique (`suspense`), Comum/Incomum aparece revelado na hora. Mesmo componente e mesmo corte de `BANDS` da coleta do Tanque, só com o passo do ovo antes.
- Endpoint: reusa `POST /api/items/{key}/buy` (resposta ganhou um campo `creature`, presente só quando o item comprado gera um peixe).

---

## 8. Schema de dados completo

### 8.1 Princípios de desacoplamento adotados

1. **`Habitat` genérico, não `Tank` fixo** — mesmo motor de tick/geração serve pra aquário/terrário/futuros habitats.
2. **`Creature` genérico, não `Fish` fixo** — o motor seed→traits→rarity não tem nada de específico de peixe.
3. **`CurrencyType` como tabela**, não campos fixos no usuário — nova moeda não quebra schema.
4. **`TransactionLog` único e genérico** pra tudo que envolve valor/posse mudando de mãos — ferramenta central de auditoria/anti-cheat.

### 8.2 Tabelas

```
User
- Id (PK)
- Username
- Email
- PasswordHash
- CreatedAt
- LastDailyRewardAt (datetime, nullable)
- IsAdmin (bool, default false)

VipSubscription
- Id (PK)
- UserId (FK -> User)
- StartAt
- EndAt
- Status (Active | Expired | Cancelled)

PasswordResetToken
- Id (PK)
- UserId (FK -> User)
- TokenHash (string, único) -- SHA256 do token bruto; o valor bruto nunca é persistido
- ExpiresAt
- UsedAt (datetime, nullable)
- CreatedAt

CurrencyType
- Id (PK)
- Code (SOFT | PREMIUM)
- Name

WalletBalance
- Id (PK)
- UserId (FK -> User)
- CurrencyTypeId (FK -> CurrencyType)
- Amount

HabitatType
- Id (PK)
- Code (Aquarium | Terrarium | Breeding...)
- Name

Habitat
- Id (PK)
- UserId (FK -> User)
- HabitatTypeId (FK -> HabitatType)
- Capacity (int)
- MaintenanceLevel (decimal, 0-100)
- QueueCap (int)
- GenerationIntervalMinutes (int)
- OnlineGenerationRate (decimal)
- OfflineGenerationRate (decimal)
- GenerationProgressMinutes (decimal)
- LastTickAt (datetime)
- LastHeartbeatAt (datetime)
- CreatedAt
- HasWaterSensor (bool, default false)
- AutoCleanTriggerPercent (decimal, default 0)
- AutoCollectEnabled (bool, default true)
- AutoCleanEnabled (bool, default true)

Species
- Id (PK)
- HabitatTypeId (FK -> HabitatType)
- Name
- BaseSpriteKey

TraitWeightConfig
- Id (PK)
- SpeciesId (FK -> Species, nullable = regra global)
- PartType (Body | Tail | Dorsal | Pectoral | ...)
- TraitCategory (ShimmerTier | BaseColor | PatternType | PatternSize | PatternColor | PatternOpacity)
- ConfigJson
- Version (int)
- EffectiveFrom (datetime)

GenerationQueueItem
- Id (PK)
- HabitatId (FK -> Habitat)
- SpeciesId (FK -> Species)
- ReadyAt (datetime)
- Status (Pending | Collected)
- IsSick (bool)

CreatureInstance
- Id (PK)
- SpeciesId (FK -> Species)
- OwnerId (FK -> User)
- HabitatId (FK -> Habitat, nullable)
- Seed (bigint) -- histórico/auditoria; não é mais lido pra derivar traits
- TraitConfigVersion (int) -- vestigial, só auditoria (ver §6)
- RarityScore (decimal, cacheado)
- TraitsJson (text) -- traits CONGELADOS no nascimento; fonte de verdade pra exibir o peixe
- BreedingSourceJson (text, nullable) -- só filhotes: origem de cada slot (ParentA/ParentB/Mutation)
- ParentAId (FK -> CreatureInstance, nullable)
- ParentBId (FK -> CreatureInstance, nullable)
- ParentASeed (bigint, nullable) -- histórico/auditoria
- ParentBSeed (bigint, nullable)
- ParentAGrandparentASeed/BSeed, ParentBGrandparentASeed/BSeed (bigint, nullable) -- histórico/auditoria; o mecanismo que os usava funcionalmente (puxar traço de avô) foi removido em 13/08/2026 (motor não lê mais esses campos, só a auditoria)
- BreedCount (int, default 0)
- LastBredAt (datetime, nullable)
- IsDead (bool, default false)
- DiedAt (datetime, nullable)
- SoldAt (datetime, nullable)
- IsNew (bool, default false)
- PendingInboxClaim (bool, default false)
- OriginalOwnerId (FK -> User, required) -- "primeiro dono" imutável
- CreatedAt

ItemDefinition
- Id (PK)
- Key
- Name
- Category (Filter | AutoFilter | HabitatUpgrade | WaterSensor | ...)
- EffectJson
- PriceSoft
- PricePremium (nullable)

UserInventory
- Id (PK)
- UserId (FK -> User)
- ItemDefinitionId (FK -> ItemDefinition)
- Quantity

MarketListing
- Id (PK)
- CreatureInstanceId (FK -> CreatureInstance)
- SellerId (FK -> User)
- BuyerId (FK -> User, nullable)
- PriceSoft (decimal)
- Status (Active | Sold | Cancelled)
- CreatedAt
- ResolvedAt (nullable)

TransactionLog
- Id (PK)
- Type (MarketSale | DirectTransfer | CurrencyPurchase | ItemPurchase | Sink | Breeding | BreedingLoss | DailyReward | TimeSkip | VendorSale | BreedingInsurance | VipPurchase)
- FromUserId (FK -> User, nullable)
- ToUserId (FK -> User, nullable)
- CreatureInstanceId (FK -> CreatureInstance, nullable)
- CurrencyTypeId (FK -> CurrencyType, nullable)
- Amount (decimal, nullable)
- CreatedAt

BreedingSlot
- Id (PK)
- UserId (FK -> User)
- HabitatId (FK -> Habitat)
- ParentAId (FK -> CreatureInstance)
- ParentBId (FK -> CreatureInstance)
- StartedAt
- ReadyAt
- CostPaid (decimal)
- ParentADeathChance (decimal)
- ParentBDeathChance (decimal)
- InsuranceUsed (bool, default false)
- Status (InProgress | Collected)
- ChildCreatureId (FK -> CreatureInstance, nullable)

InboxMessage
- Id (PK)
- Title, Body
- RewardCurrencyTypeId (FK -> CurrencyType, nullable)
- RewardAmount (decimal, nullable)
- RewardItemDefinitionId (FK -> ItemDefinition, nullable) -- dormente, sem UI admin ainda
- RewardItemQuantity (int, nullable)
- CreatedAt

InboxEntry
- Id (PK)
- InboxMessageId (FK -> InboxMessage, nullable)
- RecipientUserId (FK -> User)
- Kind (AdminMessage | MarketPurchase | DirectTransfer)
- CreatureInstanceId (FK -> CreatureInstance, nullable)
- ReadAt (datetime, nullable)
- ClaimedAt (datetime, nullable)
- CreatedAt
```

### 8.3 Por que isso escala bem sem over-engineering

Esse nível de desacoplamento (`Habitat` genérico, `CurrencyType` como tabela) mexe na fundação — caro de trocar depois, gratuito de decidir agora. Já sistema de guilda, evento sazonal — features de borda, não modelaria agora, dá pra adicionar tabela nova depois sem tocar no que existe.

---

## 9. Stack técnico (fechado)

| Camada | Escolha | Motivo |
|---|---|---|
| Backend | ASP.NET Core + EF Core | Já domina, alta produtividade |
| Banco | PostgreSQL | Neon.tech (free tier) |
| Frontend | React + Canvas | Composição de camadas simples o bastante pra não justificar engine de jogo |
| Deploy frontend | Cloudflare Pages | Build estático, CDN global, grátis, auto-deploy a cada push em master |
| Deploy backend | Oracle Cloud Free Tier (VPS via Docker) | Stack .NET intacta, grátis, sem "sleep" |

**Cloudflare Workers não roda .NET** — por isso o backend fica no Oracle Cloud; Cloudflare só hospeda o frontend estático.

**Plano B de hospedagem do backend:** Render ou Fly.io free tier — mesma stack .NET, mas dorme após inatividade (pode desestabilizar o heartbeat em fase de poucos usuários). Fallback temporário, não escolha primária.

**TLS do backend:** API sem HTTPS embutido (espera proxy na frente). Solução: **DuckDNS** (subdomínio grátis) + **Caddy** (reverse proxy, emite/renova Let's Encrypt automaticamente) rodando na VM, na frente do container da API. Artefatos em `deploy/` (`docker-compose.yml`, `Caddyfile`, `.env.example`, `deploy/README.md`).

**VM Oracle:** só 954MB RAM sem swap por padrão — builds com `--build` exigem swapfile de 2GB ativo (`free -h` pra confirmar antes), senão a VM pode travar por completo (história em `HISTORY.md §10`).

**Renderização do peixe:** composição via `<canvas>`, camadas sobrepostas, `globalCompositeOperation='overlay'` pro shimmer do corpo.

---

## 10. Status e próximos passos

**Publicado e funcionando:** backend no Oracle Cloud (`https://vivarium-online.duckdns.org`, Docker Compose api+caddy, TLS via DuckDNS+Let's Encrypt); frontend no Cloudflare Pages/Workers (`https://vivarium.marcospdnnogueira.workers.dev`, auto-deploy a cada push em master). VM protegida (só chave SSH ed25519, senha desabilitada, fail2ban, firewall 22/80/443). **Deploy do backend NÃO é automático** — exige SSH+pull+rebuild manual; sempre conferir o commit rodando antes de assumir que um fix já está no ar.

**Implementado (motor central + loop completo):** geração seed→traits com traits congelados no nascimento (§6), backend completo (auth JWT, tick lazy, mercado, loja, transferência, ServiceResult), breeding com todos os mecanismos vigentes (§7.8), mochila, recompensa diária, anti-rush + premium, venda ao vendor, painel de admin, faixas de capacidade + filtros em nível, ranking + visita, limpeza automática VIP + sensor de água, opt-out de automação VIP, Caixa de Entrada, VIP (pacotes de dias em premium, sem renovação automática), perfil + esqueci senha (Resend).

**Pendente / gaps reais, não escondidos:**
- ⏳ Tornar o repositório GitHub privado antes do lançamento oficial (hoje é público).
- ⏳ **Assets do designer** — trocar as formas procedurais do `fishRenderer.js`/protótipo pelos sprites reais. Arquitetura já separa traits (dados) de renderização (Canvas), então trocar formas procedurais por `drawImage` de sprites é mudança de camada de renderização, não de motor — não deve pesar o jogo (PNGs pequenos, `drawImage` é leve). Recomendação pro designer: entregar partes em branco/cinza neutro (silhueta limpa) pra continuar tingindo programaticamente por cima (mesma técnica já usada no shimmer), em vez de um sprite pronto por combinação de cor (evita explosão de assets: 8 cores × 11 padrões × 4 partes).
- ⏳ Domínio próprio (hoje usa DuckDNS/workers.dev — funcional, mas também resolveria o gap do Resend em §7.20).
- ⏳ Processador de pagamento pra premium (§7.11).
- ⏳ Trocar heartbeat por WebSocket/SSE (§7.3).

---

## 11. API — endpoints e decisões (MVP)

**Auth:** JWT Bearer (7 dias, HS256). Senha com PBKDF2-SHA256 (100k iterações, salt aleatório). Registro cria carteiras (100 SOFT / 0 PREMIUM) + tanque inicial.

**Tick lazy, sem job agendado:** roda dentro de heartbeat/tank/collect (`GameService.ApplyTickAsync`). No heartbeat, o tick roda ANTES de atualizar `LastHeartbeatAt`.

**Mercado:** listar tira a criatura do tanque (`HabitatId=null` — mesmo estado da Mochila, diferenciado por ter `MarketListing` ativa). Cancelar/comprar entrega via Caixa de Entrada (§7.19), não mais direto no tanque. Compra roda em transação com revalidação de status, registra `MarketSale`. Sem taxa de mercado no MVP.

| Método | Rota | Auth | Descrição |
|---|---|---|---|
| GET | `/health` | — | status |
| GET | `/api/creatures/preview/{seed}` | — | traits de qualquer seed (sem banco) |
| POST | `/api/auth/register` | — | cria user + carteiras + tanque; retorna token |
| POST | `/api/auth/login` | — | username ou email + senha |
| POST | `/api/auth/forgot-password` | — | anti-enumeração; email via Resend |
| POST | `/api/auth/reset-password` | — | token do email (1h, uso único) + nova senha |
| PUT | `/api/account/email` | ✓ | troca email; exige senha atual |
| PUT | `/api/account/password` | ✓ | troca senha; exige senha atual |
| POST | `/api/game/heartbeat` | ✓ | marca online; roda tick |
| GET | `/api/game/tank` | ✓ | estado completo: fila, criaturas, carteira; roda tick |
| POST | `/api/game/collect/{queueItemId}` | ✓ | coleta manual |
| POST | `/api/game/queue/{queueItemId}/rush` | ✓ | pula espera pagando premium |
| POST | `/api/game/creatures/{id}/transfer` | ✓ | transferência direta por username → Caixa de Entrada |
| POST | `/api/game/creatures/{id}/sell-vendor` | ✓ | venda instantânea ao NPC |
| GET | `/api/game/daily-reward` | ✓ | status do resgate diário |
| POST | `/api/game/daily-reward/claim` | ✓ | resgata |
| GET | `/api/market/listings?skip&take` | ✓ | listagens ativas |
| POST | `/api/market/listings` | ✓ | lista criatura própria |
| POST | `/api/market/listings/{id}/cancel` | ✓ | cancela → Caixa de Entrada |
| POST | `/api/market/listings/{id}/buy` | ✓ | compra → Caixa de Entrada |
| GET | `/api/items/` | ✓ | catálogo |
| POST | `/api/items/{key}/buy` | ✓ | compra e aplica efeito |
| GET | `/api/breeding` | ✓ | gestação em andamento |
| GET | `/api/breeding/quote` | ✓ | prévia sem custo |
| GET | `/api/breeding/history` | ✓ | últimas 50 gestações coletadas |
| POST | `/api/breeding/start` | ✓ | inicia (aceita `useStabilizer`/`useInsurance`) |
| POST | `/api/breeding/collect` | ✓ | coleta filhote |
| POST | `/api/breeding/rush` | ✓ | pula tempo restante pagando premium |
| POST | `/api/admin/give-starter-fish-all` | ✓ (admin) | dá +1 peixe a todo aquário com espaço |
| POST | `/api/admin/inbox/send` | ✓ (admin) | broadcast/lista de usernames |
| GET | `/api/leaderboard/{metric}` | ✓ | ranking (`rarity`\|`income`), top 100 + posição própria |
| GET | `/api/leaderboard/visit/{username}` | ✓ | tanque de outro jogador, só leitura |
| GET | `/api/vip` | ✓ | status VIP + tabela de preços |
| POST | `/api/vip/subscribe` | ✓ | compra pacote (`{days}`: 7\|15\|30) em premium, estende se já ativo |
| POST | `/api/game/water-sensor/trigger` | ✓ | configura gatilho (0–80%) da Limpeza Automática |
| POST | `/api/game/toggles` | ✓ | liga/desliga coleta/limpeza automática |
| POST | `/api/game/creatures/{id}/mark-seen` | ✓ | marca peixe `IsNew` como visto |
| GET | `/api/inbox/` | ✓ | entradas da Caixa de Entrada |
| POST | `/api/inbox/{id}/claim` | ✓ | resgata uma entrada |
| POST | `/api/inbox/claim-all` | ✓ | resgata todas as pendentes |
| POST | `/api/inbox/mark-all-read` | ✓ | marca tudo como lido |
| POST | `/api/inbox/clear-claimed` | ✓ | apaga entradas já resgatadas |

**Itens do MVP:** `filter_basic` (20 soft), `auto_filter`/`auto_filter_2`/`auto_filter_3` (níveis, §7.15), `tank_upgrade`/`aquario_grande`/`aquario_master` (§7.15), `water_sensor` (§7.17), `egg_common`/`egg_rare`/`egg_legendary` (premium, §7.21).

**Testes de integração** (`tests/Vivarium.Api.Tests`): API completa via `WebApplicationFactory` contra SQLite in-memory.

### 11.1 Hardening / anti-cheat vigente

- **Concorrência otimista (`xmin`)** em `Habitat`, `WalletBalance`, `MarketListing`, `CreatureInstance` (condicional a `Database.IsNpgsql()`). Fecha compra dupla, double-list, corrida de preço, double-credit de renda. Endpoints tratam `DbUpdateConcurrencyException` (tick recarrega e segue; ações do usuário retornam 409).
- **Rate limiting:** global 300/min por usuário/IP + grupo `auth` 10/min por IP. Atrás de proxy: `ForwardedHeaders__Enabled=true` liga `app.UseForwardedHeaders()`.
- **Validação:** email via `MailAddress.TryCreate`; username `[A-Za-z0-9_-]`, 3–32.
- **Deferidos, documentados e não feitos:** JWT sem revogação (validade 7d); taxa de mercado como sink; teto de listagens por usuário; multi-conta (soft sem cash-out limita o dano).
- **CI:** `.github/workflows/ci.yml` — jobs `backend` (dotnet build+test) e `frontend` (npm ci+build), em push/PR pra master.
- **Docker:** imagem final roda como usuário não-root, `HEALTHCHECK` bate em `/health` a cada 30s.

---

## 12. Estrutura da solution

- `Vivarium.slnx` — solution (.NET 10)
- `src/Vivarium.Core` — domínio e motor de geração (sem dependência de web/banco); entidades do schema (§8) em `Domain/`
- `src/Vivarium.Api` — ASP.NET Core minimal API + EF Core/Npgsql. `Data/VivariumDbContext` + migrations. `Endpoints/` (auth, account, game, market, item, breeding, admin, leaderboard, vip, inbox), `Services/` (TokenService, PasswordHasher, GameService, MarketService, ItemService, BreedingService, LeaderboardService, VipService, InboxService, TailColorResolver, EmailSender — todos via `Http/ServiceResult`). Connection string `Vivarium` (produção via env var `ConnectionStrings__Vivarium`).
- `tests/Vivarium.Api.Tests` — testes de integração (WebApplicationFactory + SQLite in-memory)
- `frontend/` — React + Vite:
  - `src/lib/` — sem React: `api.js`, `generator.js` (só exibição — traits vêm prontos da API, ver §6), `fishRenderer.js`, `format.js`, `tankMath.js`, `motion.js`.
  - `src/hooks/` — `useGame`, `useToast`, `useBreeding`, `useInbox`.
  - `src/components/` — `Coin`, `TraitRow`, `RarityBadge`, `ShimmerLabel`, `Toast`, `Modal` (renderiza via `createPortal(document.body)` — evita ficar preso em stacking context de ancestral), `FishCanvas` (recebe a criatura completa, nunca um objeto parcial reconstruído — ver lição em §7.18), `AquariumCanvas`.
  - `src/views/` — `AuthView`, `GameView`, `TankView`, `MarketView`, `StoreView`, `BackpackView`, `FishDetail`, `RarityGuide`, `BreedingView`, `RankingView`, `InboxView`, `ProfileModal`, `ResetPasswordView`.
  - `src/App.jsx` — gate de auth. Heartbeat 60s + refresh do tanque 30s (`useGame`).
  - Deploy: Cloudflare Pages, build `npm run build`, output `frontend/dist`, env `VITE_API_URL`.
- **Identidade visual:** tema escuro único ("aquário profundo") — fundo escuro, glass translúcido escuro, acentos vibrantes (aqua/azul/coral/âmbar), tipografia Fraunces (display) + Hanken Grotesk (UI). Tokens no topo de `src/styles.css`. Histórico de iterações em `HISTORY.md §13`.
- **Responsividade mobile:** cabeçalho em 3 linhas fixas via CSS `order` em telas estreitas (marca+conta / abas em tira rolável / stats), modais via portal (não presos em stacking context), seções "Atributos"/"Por que é raro" recolhíveis, toast dispensável por clique. **Lição operacional:** `resize_window`/device toolbar do Chrome DevTools não funcionam neste ambiente de automação — `cy.viewport(...)` é a única forma confiável de testar layout mobile aqui. Histórico completo das correções em `HISTORY.md §13`.
- **Cards da Loja com altura uniforme (17/08/2026):** descrições de tamanho muito desigual estufavam alguns cards bem mais que outros (`Sensor de Qualidade da Água` era o pior caso — feedback do usuário, com print). `.store-card-desc` trunca em 3 linhas sempre (`-webkit-line-clamp` + altura fixa em `em`, reserva o mesmo espaço mesmo pra texto curto); "Ler mais" só aparece quando o texto realmente estoura — medido via DOM (`scrollHeight > clientHeight`, não contagem de caracteres, que quebraria em telas/fontes diferentes) — e abre o texto completo num `Modal` (mesmo componente reusado em toda a UI), sem derrubar a uniformidade do grid.
- **Testes do frontend:**
  - **Unitários (Vitest):** `frontend/src/lib/*.test.js` — `generator.js`, `tankMath.js`, `format.js`. Rodar: `npm test` / `npm run test:watch`.
  - **E2E (Cypress):** `frontend/cypress/e2e/*.cy.js` — builda produção (`vite preview`), mocka toda a API via `cy.intercept` (fixtures em `cypress/fixtures/`). Rodar: `npm run e2e` / `npm run cypress:open`.
  - CI roda os dois. **Lição de preferência do usuário:** rodar `npm test`/`npm run e2e` em vez de validar UI manualmente ou abrir Chrome — mas sempre perguntar antes de rodar a suíte completa (usuário pode já ter validado manualmente).
- `tests/Vivarium.Core.Tests` — xUnit
- `tools/Vivarium.Simulation` — console de validação estatística (`dotnet run --project tools/Vivarium.Simulation [N]`); modos `dump`, `economy`, `breed`, `simulate`, `mutationfloor`, `eggodds`/`eggbias` (§7.21 — `eggodds` reporta tier de brilho E banda de raridade por bias; `eggbias` varre vários valores de bias de uma vez, usado na recalibração do Ovo Lendário)
- `tools/Vivarium.AdminReset` — ferramentas administrativas de produção: `backfill-traits`, `audit-ancestry`, `band-distribution`, `tank-income <email>`
- `prototype/fish-composer.html` — protótipo visual standalone (Canvas)
