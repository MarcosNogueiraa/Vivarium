# Vivarium — Contexto do Projeto

> Este arquivo é a fonte única de verdade sobre as decisões de design e arquitetura do Vivarium. Leia-o por completo antes de sugerir código ou mudanças estruturais. Ao tomar novas decisões de produto/arquitetura ao longo do desenvolvimento, atualize este arquivo (e commite a mudança) para manter o contexto sempre atual entre sessões.

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

Cada peixe nasce com um **seed único e imutável**. Todo trait visual é derivado desse seed via hash determinístico — isso garante que o mesmo peixe sempre renderize igual, sem precisar armazenar cada atributo calculado individualmente (só o seed + species precisam ser persistidos; os traits podem ser recalculados a qualquer momento a partir deles).

---

## 2. Tabela de raridade — Corpo (brilho/shimmer)

O corpo é sempre desenhado na mesma base cinza. O que varia é uma camada de brilho aplicada por cima via blend mode (`overlay`/`screen`), com cor e opacidade próprias.

| Tier | Nome | Peso (%) | Cor do brilho | Opacidade do brilho |
|---|---|---|---|---|
| 0 | Sem brilho | 78% | — | 0% |
| 1 | Brilho sutil | 15% | Dourado, Prateado, Azulado | 10–25% |
| 2 | Brilho vibrante | 5.5% | Verde-esmeralda, Roxo, Rosa | 30–50% |
| 3 | Brilho raro | 1.3% | Arco-íris (gradiente), Preto absoluto | 55–75% |
| 4 | Brilho lendário | 0.2% | Iridescente (shift de cor conforme ângulo/tempo) | 80–100% |

> Dentro de cada tier, a opacidade exata também é sorteada dentro da faixa (uniforme), então dois peixes "Tier 1 dourado" ainda têm leve diferença entre si.

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

Cada uma das 3 partes (cauda, dorsal, peitoral) sorteia **independentemente** dessa tabela — mas veja a regra de correlação abaixo, que ajusta os pesos condicionalmente.

### Padrão sobre a parte — aplicado igualmente às 3 partes

Tabela **v2 (29/07/2026)**: "Sem padrão" domina mais (76%) e há 11 tipos, os novos com pesos baixos de propósito (raro = valioso). Ocelo e Mármore são a caça de topo. Cada parte sorteia 1 padrão desta mesma tabela.

| Tipo de padrão | Peso (%) |
|---|---|
| Sem padrão | 76% |
| Estria | 8% |
| Bolinha | 8% |
| Escamas | 3% |
| Raios | 1.6% |
| Ziguezague | 1.2% |
| Rede | 0.9% |
| Degradê | 0.6% |
| Manchado | 0.35% |
| Ocelo | 0.2% |
| Mármore | 0.05% |

Se houver padrão (qualquer tipo ≠ "sem padrão"):
- **Tamanho do padrão**: sorteio contínuo 0–100 (pequeno a grande), com peso maior no meio (distribuição normal), tiers extremos (muito pequeno <10 ou muito grande >90) contam como "raro" e entram no cálculo de rarity score.
- **Cor do padrão**: mesma paleta curada acima, mas nunca igual à cor de base da mesma parte (evita padrão invisível).
- **Opacidade do padrão**: 20–90%, sorteio uniforme; abaixo de 30% ou acima de 80% conta como raro no score.

---

## 4. Regra de correlação (brilho do corpo → cor das partes)

Se o corpo saiu em Tier 2, 3 ou 4 (brilho vibrante, raro ou lendário), a tabela de peso de cor das partes é ajustada: a cor mais próxima do tom do brilho recebe **+15 pontos percentuais** de peso (renormalizando o resto proporcionalmente).

Exemplo: corpo saiu "Tier 3 — Preto absoluto" → peso de "Preto" nas partes sobe de 3% para ~18%, o resto da tabela é reduzido proporcionalmente. Isso cria a sensação de "conjunto raro combinando" sem eliminar a chance das outras cores aparecerem.

---

## 5. Cálculo de Rarity Score (não arbitrário)

```
RarityScore = -log(P_corpo × P_cauda × P_dorsal × P_peitoral × P_tamanho_extremo × P_opacidade_extremo)
```

Onde cada `P_x` é a probabilidade (peso/100) do valor sorteado naquele trait. Usar `-log` da probabilidade combinada é o mesmo princípio usado em informação/entropia: quanto mais raro o conjunto, maior o score, e a escala fica gerenciável (em vez de multiplicar frações minúsculas que ficam ilegíveis).

Isso pode ser calculado uma vez na criação do peixe e cacheado no banco (não precisa recalcular a cada exibição).

**Faixas de exibição ao jogador** — recalibradas via simulação de 100k seeds (**29/07/2026**, raridade v2: corpo pesa 2.5× no score, bônus de conjunto coeso, 11 padrões), produzindo a pirâmide 50% / 30% / 15% / 4.8% / 0.2%:
- Comum: score < 5.4
- Incomum: 5.4–7.5
- Raro: 7.5–9.8
- Épico: 9.8–14.0
- Lendário: 14.0+

> Distribuição v2 (100k): min ~2.73, p50 5.36, p99.8 14.01, max ~18.9. O corpo pesar mais e os bônus de conjunto **subiram e alongaram a cauda** (antes máx ~14; a raridade v1 começava em ~2.6 e topava em 11.2). Recalibrar essas faixas sempre que os pesos do `TraitWeightConfig` mudarem (rodar `dotnet run --project tools/Vivarium.Simulation` e copiar os cortes p50/p80/p95/p99.8 pra `fishRenderer BANDS` + `App.jsx RARITY_RANGES`).

### 5.1 Decisões de implementação do score (v1)

- **Base do log:** log10 (`score = Σ -log10(P)` de cada trait sorteado).
- **Entram no score:** tier de shimmer do corpo (**× `ShimmerScoreWeight` = 2.5** — o corpo é a área de destaque e domina a raridade; ver Corpo pesa mais abaixo); por parte (cauda/dorsal/peitoral): cor base (com probabilidade **já ajustada** pela correlação, quando ativa), tipo de padrão, cor do padrão (paleta renormalizada sem a cor base) e, apenas quando extremos, tamanho e opacidade do padrão; velocidade de cauda e de nadadeira **apenas nos extremos**, com peso reduzido (ver Movimento); **bônus de conjunto coeso** (ver abaixo).
- **Corpo pesa mais (v2, 29/07/2026):** a contribuição do tier de shimmer é multiplicada por `ShimmerScoreWeight = 2.5` (`TraitConfigV1`). Conserta o caso do iridescente (tier lendário, P=0.2%) que sem o peso ficava abaixo de um mero degradê de nadadeira. Como o peso multiplica só a informação do tier já existente, **não é** mudança de algoritmo de trait — mas mudou o *score*, então recalibramos as faixas (seção 5).
- **Bônus de conjunto coeso (v2, 29/07/2026):** somado ao score quando as 3 partes "combinam" — recompensa peixes visualmente coerentes e cria demanda de mercado por partes específicas. Mesmo **padrão** (≠ Sem padrão) em 2 partes: +1.0; nas 3: +2.5. Mesma **cor de base** em 2 partes: +0.8; nas 3 (monocromático): +2.0 (`SamePattern2/3Bonus`, `SameColor2/3Bonus`). É um bônus fixo (não `-log10`), calibrado por simulação. Implementado em `TraitGenerator.SetBonus` (C#) e espelhado em `generator.js rarityBreakdown` e no port do protótipo.
- **Não entram:** cor do shimmer dentro do tier (uniforme — a raridade já está capturada pelo tier), opacidade do shimmer (uniforme na faixa do tier) e as amplitudes de movimento (uniformes, só estética).
- **Tamanho do padrão:** normal(média 50, desvio 20) clampada em 0–100; extremos <10 ou >90 têm P≈2.3% cada.
- **Opacidade do padrão:** uniforme 20–90; extremos <30 ou >80 têm P=1/7 cada.
- **Mapa de correlação shimmer→cor de parte:** Esmeralda→Verde, Roxo→Roxo, Rosa→Vermelho, Arco-íris→Branco puro, Preto absoluto→Preto, Iridescente→Branco puro (tiers 0–1 não ativam correlação).
- **Movimento (28/07/2026):** velocidade de cauda e de nadadeira são normal(50,20) clampada em 0–100 (extremos <10 ou >90 ≈4.55% cada). Só os extremos entram no score, com **peso 0.5** (`MovementScoreWeight`) sobre o `-log10(P)` da banda — um extremo vale ≈0.82, metade de um extremo de tamanho de padrão. As amplitudes (cauda 0.20–0.75 rad, nadadeira 0.15–0.75 rad) são uniformes e ficam fora do score. No aquário, a velocidade de nado do peixe é **calculada** dos traits (`swimSpeedOf`: 0.75·cauda + 0.25·nadadeira), então cauda rápida = peixe rápido — coerência visual, não sorteio novo.

---

## 6. Modelo de dados

```
Species
- Id
- Name
- BaseSpriteKey (referência ao asset base cinza)

FishInstance
- Id
- SpeciesId (FK)
- OwnerId (FK -> User)
- Seed (bigint, gerado uma vez, imutável)
- RarityScore (decimal, calculado na criação, cacheado)
- CreatedAt
- ParentAId (FK -> FishInstance, nullable, para linhagem/breeding futuro)
- ParentBId (FK -> FishInstance, nullable)

TraitWeightConfig
- Id
- PartType (Body | Tail | Dorsal | Pectoral)
- TraitCategory (ShimmerTier | BaseColor | PatternType | PatternSize | PatternColor | PatternOpacity)
- ConfigJson (lista de {value, weight})
- SpeciesId (nullable — null = regra global aplicada a todas as espécies)
```

> Nenhum trait individual é salvo — tudo é derivado do `Seed` on-demand pelo motor de geração. Isso facilita rebalancear pesos no futuro (mudar `TraitWeightConfig` não quebra peixes já existentes, porque eles guardam o seed, não o resultado — mas cuidado: **se você mudar os pesos depois, os peixes antigos vão recalcular diferente se o algoritmo mudar**. Recomendo versionar o `TraitWeightConfig` com uma data de vigência, e o `FishInstance` guardar qual versão de config foi usada no nascimento, para manter peixes antigos consistentes.)

```
FishInstance (campo adicional)
- TraitConfigVersion (int, snapshot da versão de config usada ao nascer)
```

---

## 7. Algoritmo determinístico: Seed → Traits

Pseudocódigo (C#):

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

O ponto-chave: **um hash por trait**, não um único random state sequencial. Isso evita que mudar a ordem de cálculo dos traits no futuro (ex: adicionar um trait novo no meio do processo) altere o resultado dos traits que já existiam antes dele — cada trait é independente e sempre reproduzível isoladamente.

> **Mecanismo oficial de extensão:** adicionar um trait novo = adicionar um `salt` novo. Como cada trait deriva de `Hash(seed, salt)` isolado, incluir um trait não muda nenhum trait existente (nem o seu hash, nem a ordem). Se o trait novo **não** entrar no rarity score (ou entrar só nos extremos, com peso), os scores dos peixes existentes também não mudam. Foi assim que os traits de movimento (velocidade/amplitude de cauda e nadadeira) entraram em 28/07/2026 sem tocar em nenhum outro trait. Só bumpar `TraitConfigVersion` quando **mudar pesos/algoritmo de um trait já existente**, não ao adicionar trait novo.

---

## 8. Loop de gameplay — MVP

### 8.1 Geração e coleta

O tanque gera peixe passivamente (fila de geração), o jogador coleta o que já está pronto — não é "tentar a sorte" a cada clique, é colher o que já foi gerado.

- A cada X minutos (ex: 15 min), surge um item novo na fila, até um cap (ex: máximo 5 acumulados)
- Free: coleta manual (clique em cada item da fila)
- VIP: coleta automática — **mas só enquanto o navegador está com a aba do jogo aberta** (ver seção 8.3)
- Seed do peixe é sorteado no momento da **coleta**, não da entrada na fila

### 8.2 Qualidade da água (sink de manutenção)

Variável 0–100, degrada por tick de tempo real (~-1 a cada 20 min, ajustável via simulação).

- Abaixo de 40: velocidade de geração cai
- Abaixo de 15: chance pequena do peixe da fila "adoecer" (reduz rarity score potencial, não mata)
- Recuperação: "filtro" (item, moeda soft) restaura pra 100; "filtro automático" (upgrade de tanque) reduz taxa de degradação pela metade

### 8.3 Diferenciação online/offline

O diferencial do VIP (coleta automática) só vale **enquanto o navegador está com a aba aberta** — trocar de aba ou minimizar ainda conta como online; fechar o navegador ou desligar o PC conta como offline.

**Mecanismo: heartbeat simples**
```
Client: envia heartbeat pro servidor a cada 60s, independente da aba estar em foco 
        (sem checagem de Page Visibility API — trocar de aba não conta como offline)
Server: se último heartbeat < ~3 min atrás → tanque "online"; senão → "offline" a partir 
        do momento do último heartbeat recebido
```

| Estado | Taxa de geração | Coleta |
|---|---|---|
| Online (free) | Taxa cheia | Manual |
| Online (VIP) | Taxa cheia | Automática |
| Offline (free ou VIP) | Taxa reduzida (~40-50% da online) | Manual ao retornar |

Diferença online/offline mantida **moderada** (não muito maior que 2x) para não criar incentivo forte de script simulando heartbeat falso em segundo plano.

Campos no `Tank`: `LastTickAt`, `LastHeartbeatAt`, `OnlineGenerationRate`, `OfflineGenerationRate`.

> **Melhoria futura (pós-MVP):** trocar heartbeat por polling (`setInterval`) por uma conexão persistente (WebSocket ou SSE). Isso evita depender do comportamento de throttling de timers em segundo plano, que varia entre navegadores (Chrome, Firefox, Safari), e detecta desconexão em tempo real (fechar aba, perder internet, PC dormir) em vez de esperar o timeout do heartbeat.

### 8.4 Tanque — capacidade e progressão

- Tanque inicial: 3 peixes ativos + fila de geração de 5
- Upgrade de capacidade: comprado com moeda soft, custo crescente (~1.5x por nível)

### 8.5 Decisões de implementação do loop (v1) — `Vivarium.Core/Gameplay`

Lógica pura em `HabitatTicker.ProcessTick` (recebe estado + "agora", devolve o que mudou; sem banco/relógio — a API aplica na entidade). Parâmetros em `TickConfig`, defaults de tanque novo em `HabitatDefaults`.

- **Janela do tick dividida por heartbeat**: online do `LastTickAt` até o `LastHeartbeatAt`; se o heartbeat está fresco (< 3 min), a janela inteira é online; sem heartbeat, inteira offline.
- **Progresso persistido**: campo `GenerationProgressMinutes` no Habitat guarda o resto de minutos efetivos entre ticks (minutos × taxa × fator de manutenção). A cada `GenerationIntervalMinutes` completos, 1 item entra na fila (limitado ao espaço livre).
- **Fila cheia não acumula estoque**: progresso é clampado em 1 intervalo — ao abrir espaço, sai no máximo 1 item quase pronto, não 5 de uma vez.
- **Qualidade < 40**: velocidade de geração × 0.5. **Filtro automático**: degradação × 0.5. Fator usa o nível do início da janela (simplificação; tick roda com frequência).
- **Doença (qualidade < 15)**: 10% de chance por item novo (`IsSick` no GenerationQueueItem). Efeito na coleta (`CreatureCollector`): "desvantagem" — sorteia 2 seeds e fica o de menor rarity score. Reduz potencial sem matar e sem quebrar a derivação seed→traits.
- **Seed de coleta**: sorteado na coleta via RNG criptográfico (`NewRandomSeed`), nunca na entrada da fila.

### 8.6 Economia — farm de moedas por raridade (28/07/2026)

Renda passiva: cada peixe no tanque farma soft continuamente, escalado pela raridade. Lógica pura em `Vivarium.Core/Gameplay/IncomeCalculator.cs`; parâmetros em `TickConfig`; acumulada no tick preguiçoso (`GameService.ApplyTickAsync`) e **creditada automática** na carteira (idle puro, sem clique).

- **Fórmula:** `coinsPorHora(score) = IncomeBasePerHour · exp(IncomeGrowth · (score − IncomeRefScore))` — **base 1.5** (era 1.7, reduzida de novo em 31/07 pra compensar o buff que o patamar de água deu à renda — ver abaixo), **growth 0.42** (era 0.49, reduzido em 06/08/2026 — ver justificativa abaixo), ref 4. Comum (score ~5) ~2.3/h, raro (início 7.5) ~6.5/h, épico (9.8–14.0) ~17/h no piso da faixa até ~100/h no topo, lendário (14+) de ~100/h a mais de 700/h no topo observado (score ~18.9). Topo íngreme = lendário cobiçado.
- **Growth reduzido de 0.49→0.42 (06/08/2026):** a faixa Épico (9.8–14.0, 4.2 pontos de score) é bem mais larga que Incomum (2.1) ou Raro (2.3) — com o crescimento exponencial, isso fazia a renda variar ~7.8x *dentro do próprio tier* (26/h no piso do épico a 201/h no topo, antes do ajuste), deixando runs de sorte curtos (poucos peixes coletados, 1-2 saindo "épico alto") desproporcionalmente fortes — usuário relatou 15 coletas com 3 épicos, 2 rendendo 140/h, e sentiu que ficou fácil demais chegar numa renda alta. Medido: essa combinação específica (≥3 épicos em 15 + ≥2 com score ≥13.26) tem chance conjunta ~0.1-0.2%, ou seja, sorte real, mas o teto do tier (201/h) já era alto o bastante pra um outlier raro desequilibrar a sensação de progresso. Reduzir o growth pra 0.42 corta o teto do épico pela metade (~100/h) quase sem afetar comum/incomum (score perto do `IncomeRefScore=4`, muda só ~7% no extremo da tabela) e mantendo o "jackpot" do lendário (ainda ~7.8x um épico no topo). Revalidado com `Vivarium.Simulation economy`/`simulate`: carteira dos 3 perfis (casual/ativo/dedicado) continua oscilando mas crescendo em 120 dias.
- **Sinergia por cor de cauda (29/07):** N peixes com a mesma cor de cauda no tanque → cada um multiplica a renda por `1 + SynergyPerMatch·(N−1)` com teto `SynergyMaxBonus` (0.15 / +80%). Ex.: 5 de cauda azul → +60% cada. Cria demanda por peixes específicos no mercado ("montar tanque temático") = uso inteligente das moedas. `GameService` deriva a cor via `TraitGenerator`; `IncomeCalculator.FishIncome`/`SynergyMultiplier`. O cliente exibe a sinergia no tanque (`generator.js: synergyMultiplier`).
- **Geração (29/07):** `GenerationIntervalMinutes = 25` (era 15) → mais lento + lendário ~1/mês pro jogador ativo (~2 sem. pro dedicado; sim: `Vivarium.Simulation economy`).
- **Fator água com patamar (31/07/2026):** `WaterFactor(maint) = 1` pra `maint ≥ IncomeWaterPlateau` (80%) — água "quase perfeita" não é mais punida, só abaixo do patamar é que dói. Abaixo de 80%: `(maint/80)^0.7` (mesmo expoente de antes, só reescalado — contínuo em 80%, sem "penhasco"). Ex.: 90% → 100% de renda (antes: 93%), 50% → 72% (antes: 62%), 15% → 30% (antes: 26%), 0% → 0%. Pedido do usuário: não faz sentido punir manutenção "quase perfeita" — só abaixo de 80 a água deveria começar a doer. Como isso é um buff em quase toda a faixa 0-100%, `IncomeBasePerHour` caiu de 1.7→1.5 (~-12%) pra manter o ritmo geral parecido com antes (mesmo princípio do ajuste 2.0→1.7 em 29/07 — ver `TickConfig` pros detalhes do cálculo). Validado com `Vivarium.Simulation simulate`: carteira ainda oscila mas cresce nos 3 perfis depois do ajuste.
- **Aviso ao comprar filtro com água já alta (31/07/2026):** `FILTER_WARN_THRESHOLD = 95` (`frontend/lib/tankMath.js`) — comprar filtro com água ≥95% não muda a renda (já está no patamar de 80+). `TankView`/`StoreView` interceptam a compra do item `filter_basic` nesse caso e mostram um `ConfirmModal` perguntando se o jogador quer mesmo gastar — evita desperdício sem bloquear a ação (o jogador pode confirmar mesmo assim, ex.: quer deixar guardado o filtro automático de qualquer forma).
- **Visibilidade da perda por água suja (31/07/2026):** `TankView` mostra um indicador `-X/h` (`.water-loss`, coral) colado no medidor de água sempre que o potencial a água cheia (`tankPotential`, já existia) supera a renda atual em mais de 0.05/h — antes só aparecia num tooltip discreto ("de X" pequeno no chip de renda, que continua existindo). O novo indicador fica ao lado do botão "Filtro · 20", pra comparar visualmente o custo do filtro com a perda acumulada sem precisar calcular nada.
- **Degradação escala com peixes (29/07, curva mais agressiva em 07/08, ponderada por raridade em 08/08):** `base·(1 + DegradationPerFishFactor·pesoTotal)` — k **0.10→0.30** (07/08/2026, a pedido do usuário: fazer o auto-filtro de 500 soft "se pagar" num prazo razoável). Payback do auto-filtro (supondo o jogador filtrar ao bater o patamar de 80%, `IncomeWaterPlateau`) caiu de ~9-12 dias pra **~3,5-7,3 dias** dependendo do tamanho do tanque (3/5/10 peixes). `pesoTotal` deixou de ser a contagem simples de peixes em 08/08/2026 — agora é a soma de `rarityScore/DegradationRarityRefScore` de cada um (ver §11, "Degradação ponderada por raridade"), então tanque rico/raro suja mais que tanque grande-mas-comum. Revalidado com `Vivarium.Simulation simulate`: carteira dos 3 perfis continua oscilando mas crescendo em 120 dias. *Nota de sustentabilidade original ainda vale:* isto é sabor/consequência de descuido, **não** o balanceador principal — mesmo ponderado, o upkeep de um tanque rico continua pequeno relativo ao bruto (ex: 25 raros ~7,4/h de upkeep vs ~216-294/h de renda bruta). A sustentabilidade de longo prazo depende dos **ralos de progressão** (aquários em tiers — Fase B; breeding — 8.8), não da manutenção.
- **Online/offline:** reusa `OnlineGenerationRate`/`OfflineGenerationRate` (1.0/0.45). Offline farma a 45% **com teto de 8h** (`IncomeOfflineCapMinutes=480`). Lembrar (8.3): trocar de aba continua online; só navegador fechado = offline.
- **Fator água na renda usa a MÉDIA início/fim da janela (29/07):** `IncomeCalculator.Accrue` recebe a água antes e depois do tick e usa `0.5·(WaterFactor(início)+WaterFactor(fim))`. Conserta a incoerência de creditar renda offline a "água cheia" enquanto a água decaía (numa ausência longa a água chega a 0). Online, com ticks frequentes, início≈fim → igual ao instantâneo.
- **Acúmulo:** campo `Habitat.CoinAccrual` guarda a fração; credita moedas inteiras à carteira. Renda passiva **não** vai pro `TransactionLog` (inundaria a auditoria). `/api/game/tank` devolve `coinsPerHour` (já com fator água) pra UI mostrar "+X/h".
- **Anti-cheat:** tudo server-side; renda limitada por tempo real decorrido (`LastTickAt` avança a cada tick → spam de refresh não multiplica); água/raridade lidas do banco; `DateTime.UtcNow` do servidor. Ver `[[anti-cheat-economia]]` na memória.
- **Simulação de trajetória completa (30/07/2026):** `Vivarium.Simulation simulate` — antes disso, `economy`/`breed` só testavam cada sink isolado (renda vs. água parada, ou custo/gestação de breeding sozinho); `simulate` roda um "jogador sintético" por 120 dias (tick de HORA em hora, reusando `HabitatTicker.ProcessTick`/`IncomeCalculator.Accrue` de verdade — não reimplementa a fórmula) que compra filtro, upgrade de tanque **e** breeding ao mesmo tempo, competindo pelo mesmo saldo, pra 3 perfis (casual/ativo/dedicado 2h/8h/16h online por dia). Resultado: carteira oscila mas **cresce** ao longo dos 120 dias nos 3 perfis (não trava, não infla sem fricção) — economia saudável mesmo com os 3 sinks disputando o caixa juntos.
  - **Bug real encontrado ao escrever a simulação:** a política inicial do "jogador" só comprava filtro manual quando `!hasAutoFilter` — mas o auto-filtro só **reduz a taxa de degradação pela metade** (`HasAutoFilter: 0.5×` em `HabitatTicker`), não substitui o filtro manual. Isso derrubou a água a 0 permanentemente por volta do dia 10-11 em todo perfil testado, e como `WaterFactor(0)=0`, a renda zerava e **travava pra sempre** (carteira idêntica bit-a-bit por 100+ dias seguidos — foi assim que ficou óbvio que era bug de simulação, não achado de balanceamento). Corrigido no simulador (filtro manual continua sendo comprado mesmo com auto-filtro). **Isso é um bug só do "jogador sintético" da simulação, não do jogo real** — um jogador humano vendo a água esverdear no aquário percebe e clica em "Filtro" mesmo tendo auto-filtro; o simulador precisava da mesma regra explícita. Vale de alerta pra qualquer política futura desse simulador: nunca assumir que um upgrade automático substitui 100% a ação manual equivalente sem checar o efeito real no código (`HabitatTicker`/`TickConfig`).

**Efeitos da água no cliente (só visual):** peixes ficam mais lentos (`speedFactor = 0.5 + 0.5·maint/100`) e a água **esverdeia e suja** conforme piora — `drawTankBackground`/`drawTankForeground` recebem `quality` e interpolam pro verde-turvo, reduzem a luz e adicionam algas/sujeira flutuando e película nas bordas. A sujeira só **começa abaixo de 80** de qualidade (`MURK_CLEAN_ABOVE` em `fishRenderer.js`): acima disso a água está visualmente limpa; de 80 pra baixo escala linearmente até podre em 0.

### 8.7 Mochila — storage de criaturas (28/07/2026)

Um peixe do jogador tem **três estados** (fonte de verdade: `HabitatId` + listagem ativa; sem campo novo):

| Estado | `HabitatId` | Listagem ativa? | Farma moeda / nada no tanque |
|---|---|---|---|
| No tanque | = do habitat | não | sim |
| Na mochila | `null` | não | não |
| À venda | `null` | sim | não |

- **Mochila = `OwnerId = user`, `HabitatId = null`, sem `MarketListing` ativa.** Guarda peixes sem gastar vaga do tanque (só o tanque farma — mantém a capacidade como recurso escasso). Cap `BackpackCapacity = 50`.
- **Conserta o furo #2 da auditoria (limbo):** coletar/comprar/receber com o tanque cheio agora vai pra **mochila** (se houver espaço), nunca pra um "limbo" invisível. Se tanque **e** mochila cheios, a ação é bloqueada com mensagem clara.
- **Transições:** mover tanque↔mochila (respeitando a capacidade do tanque); listar do mercado sai do tanque/mochila; coletar prioriza tanque, senão mochila.
- **Endpoints:** `GET /api/game/backpack`, `POST /api/game/creatures/{id}/store` (tanque→mochila), `POST /api/game/creatures/{id}/deploy` (mochila→tanque). Front: aba "Mochila".
- **É a fundação do breeding** (8.8): os pais podem vir da mochila.

### 8.8 Breeding — implementado (30/07/2026, 3ª iteração)

Resolve o **gap #1** da economia ([[revisao-economia]]): não havia sink recorrente que escalasse com a riqueza. Breeding é sink de soft (custo dinâmico) + sink de tempo (os pais ficam fora do tanque principal, sem render renda, durante a gestação) + risco crescente do pai não sobreviver a cada uso.

- **Habitat de reprodução dedicado:** novo `HabitatType` (`Code = "Breeding"`, id 2), um `Habitat` capacity=2 por usuário (criado no registro — `AuthEndpoints` — e sob demanda pra contas antigas — `BreedingService.FindOrCreateBreedingHabitatAsync`). Ao iniciar, os 2 pais têm `CreatureInstance.HabitatId` movido pra esse habitat — sem flag de lock nova, reaproveitando o princípio "`Habitat` genérico" (§9.1). O habitat de breeding **nunca** passa por `HabitatTicker.ProcessTick`, então não gera fila nem conta renda.
- **Gestação escala com a raridade combinada dos pais:** `BreedingCalculator.GestationHours(scoreA, scoreB) = BaseGestationHours · exp(GestationGrowth · (scoreA+scoreB − GestationRefScore))`, clamp `[6h, 240h]` (06/08/2026: base 24→6h, growth 0.12→0.185, piso 12→6h — ver justificativa completa abaixo). 2 comuns 6h, comum+raro ~10,5h, 2 raros ~18h, 2 épicos (score~11 cada) ~55h, 2 lendários no teto de 240h pros mais extremos — mas um par lendário "raso" (score~14 cada, o mínimo do tier) fica em ~167h (~7 dias), não no teto.
- **Corte assimétrico do tempo de gestação (06/08/2026):** usuário achou 2 dias pra cruzar um casal de incomuns tempo demais, e observou (corretamente) que custo em soft e risco de morte já são fricção suficiente pra pares baratos — nenhum dos dois ameaça a economia. Mas nenhum dos dois substitui o tempo como freio pros pares RICOS: o custo em soft é intencionalmente barato pra eles (ver "Custo dinâmico" abaixo) e o risco de morte é mitigável (descanso de graça, estabilizador, seguro) — um jogador rico pode comprar a certeza de volta. Por isso o corte não foi uniforme: `BaseGestationHours` caiu bastante (24→6h, deixa o comum quase imediato) mas `GestationGrowth` subiu (0.12→0.185) pra compensar e manter o topo do lendário perto do valor anterior — preservando o único freio que não é "comprável". `MinGestationHours` acompanhou o Base (12→6h), senão o piso anularia o corte do comum. Validado via `Vivarium.Simulation breed`: 2 comuns 6h, 2 raros ~18h, 2 épicos ~55h (score~11 cada; um épico mais raro no topo do tier chega a ~150h+, mesma variância interna ao tier já vista na renda — §8.6), 2 lendários "rasos" ~167h. Trajetória completa (`Vivarium.Simulation simulate`) revalidada: economia continua saudável nos 3 perfis em 120 dias, com bem mais gestações por ciclo (mais rápido → mais cruzamentos) e mais mortes em proporção (esperado, mais exposição ao risco).
- **Custo dinâmico (30/07, 3ª iteração):** `BreedingCalculator.CostSoft(scoreA, scoreB)` — mesma forma exponencial, base 150, growth 0.10, ref 10, clamp `[100, 5000]`. Cresce bem mais devagar que a renda (growth 0.10 vs. 0.49 da renda): 2 comuns 150 soft (~27h da renda do casal — um toque real pra quem começa), 2 lendários 1108 soft (~1.5h da renda do casal — parece "barato" isoladamente, mas o verdadeiro sink pra pares ricos é o **tempo de lockup**: 2 lendários parados 88h sem render ~370/h cada é ~65k de renda perdida em oportunidade, muito maior que o custo em dinheiro). O custo em soft não precisa ser o sink pesado pros ricos — só precisa doer pra quem está começando.
- **Risco de morte crescente por cruzamento (30/07, 3ª iteração):** cada `CreatureInstance` tem `BreedCount` (nº de gestações completadas como pai/mãe). Ao coletar, ANTES de devolver cada pai, rola `BreedingCalculator.DeathChance(n) = BaseDeathChance + (MaxDeathChance − BaseDeathChance)·(1 − exp(−DeathRiskGrowth·n))` — sem limite fixo, nunca garantido (teto 85%). Calibrado (`Vivarium.Simulation breed`): uso #1 risco 5%, #2 ≈23%, #3 ≈37%, #5 ≈56% — sobrevivência acumulada cai pra ~47% já no 4º uso. Se morre: `IsDead=true`+`DiedAt` (não apaga a linha — preservaria a FK `Restrict` de lindagem `ParentAId/BId`; `BackpackQuery`/`MarketService`/`TransferAsync` bloqueiam peixe morto), audita `TransactionLog.BreedingLoss`.
- **Herança trait-a-trait** (`TraitGenerator.BreedTraits`): por trait, um roll decide mutação (re-sorteia do zero, mesma tabela — legendário continua ~0.2%) vs. herança (escolhe pai A/B). Subtraits condicionais seguem a MESMA fonte do trait pai. RarityScore recalculado a partir da probabilidade real de cada valor final — nunca copiado dos pais.
- **Viés de raridade na herança** (`WeightedTable.BiasedInheritProbability(probA, probB, rarityBias) = pA^-bias / (pA^-bias + pB^-bias)`, pais com o mesmo valor caem em 0.5 automático): até 30/07 só o `ShimmerTier` usava esse viés — cor e padrão de cada parte (6 escolhas, que dominam o score já que shimmer é só 1 termo, mesmo pesado 2.5×) herdavam **50/50 puro**, sem favorecer o valor mais raro. Isso deixava o filhote livre pra "regredir" perto do piso da população mesmo vindo de dois pais decentes (usuário relatou cruzar um incomum com um raro e sair um filhote score 2.8/Comum — investigação confirmou que não era bug de cálculo, era esse buraco de design). **Corrigido em 31/07/2026:** `rarityBias` agora se aplica também a `BreedPart` (cor e padrão), reusando a mesma constante `RarityBiasStrength = 0.15` já calibrada (sem knob novo pra tunar). Subtraits de padrão (cor/tamanho/opacidade) já seguiam a mesma fonte do pick de padrão, então ganham o viés de graça. Movimento continua 50/50 puro (contribuição pequena no score, não era o driver da regressão). Números calibrados por simulação (`Vivarium.Simulation breed`):
  - Shimmer tier: 2 Lendário → 91.8% mantém; Lendário+Raro → 52.4%; Lendário+comum → 65.0% (teto anti-"lavagem" ~70%); população geral não inflaciona (~0.2%, igual ao baseline).
  - Cor de parte (par mais extremo do jogo: Branco puro 1% vs. Laranja 22%): 2 Branco → 92.0% mantém; Branco+Laranja → 56.5% (mesma margem anti-"lavagem" do shimmer); 2 Laranja → 0.1% (só via mutação).
  - Efeito agregado: "filhos que superam o score do pai mais raro" (pares aleatórios da população, não selecionados) subiu de **25.18% → 29.62%**.
  - Testado em `BreedTraitsTests.cs` (`ViesDeRaridade_TambemFavoreceCorDePartesMaisRaras`, `ViesDeCor_NaoPermiteLavagemComParceiroComum`, `RarityScore_NuncaFicaAbaixoDoPisoDaPopulacao`) e espelhado em `generator.test.js`.
- **Bug crítico corrigido (30/07, 3ª iteração):** o filhote coletado tinha o `RarityScore` calculado corretamente por `BreedTraits`, mas era **exibido** via `Generate(childSeed)` normal — um seed novo e aleatório, sem NENHUMA relação com os pais (78% de chance de sair cinza, igual à população base, mascarando completamente a herança calculada). Fix: `CreatureInstance` ganhou `ParentASeed`/`ParentBSeed` (denormalizado do pai, imutável — evita join), expostos em `CreatureDto`/`ListingDto`; `frontend/lib/generator.js` ganhou um port completo de `BreedTraits` (`breedTraits`, `bredRarityBreakdown`, `inheritOrMutate`, `biasedInheritProbability`, `probabilityOf`) verificado 1:1 contra o C# (`Vivarium.Simulation breeddump`, 5.000 seeds, 0 mismatches — traits E score). Helpers `traitsOf(creature)`/`rarityBreakdownOf(creature)` escolhem o motor certo (`isBred` ou não); todo componente que desenha um peixe (`FishCanvas`, `AquariumCanvas`, `FishDetail`, `ShimmerLabel`, `tankMath.js`) usa esses helpers agora, nunca `generateTraits(seed)` direto num peixe que pode ser filhote.
- **Prévia sem compromisso:** `GET /api/breeding/quote?parentAId=&parentBId=` — custo, gestação, `TraitGenerator.ChildTierDistribution` (distribuição de probabilidade do tier do filho, cálculo fechado sem RNG) e o `BreedCount`/risco de morte de cada pai, tudo sem gastar nada. `BreedingSlotDto` ganhou `CostPaid` (o que foi cobrado na gestação ativa).
- **Fluxo:** `BreedingService.StartAsync`/`GetStatusAsync`/`CollectAsync`/`GetQuoteAsync` — mesmo padrão `ServiceResult` da fase 4b. `CollectAsync` devolve `CollectBreedingResponse { Child, ParentADied, ParentBDied }`.
- **Endpoints:** `GET /api/breeding`, `GET /api/breeding/quote`, `POST /api/breeding/start`, `POST /api/breeding/collect`.
- **Frontend:** aba "Ninho" (`BreedingView.jsx`) — picker de 2 peixes; ao selecionar os 2, uma **barra fixa no rodapé** (`.sticky-bar`) chama um **modal de prévia** (custo/gestação/chances/risco, via `/quote`) antes de confirmar; `AquariumCanvas` com `theme="breeding"` (tingimento rosado + corações) durante a gestação ativa; `CollectCelebration` ganhou `variant="breeding"` (mostrada **sempre** ao coletar um filhote, não só se raro — é um evento demorado que merece o momento). Fix de CSS: `.card-row` de ações (mochila) ganhou `flex-wrap` + botões mais compactos — estourava a borda do card com 3 botões.
- **Despedida do pai perdido (31/07/2026):** quando um pai não sobrevive à gestação, `CollectCelebration` mostra uma seção de despedida — não só um aviso genérico de "X pai(s) não sobreviveram". `BreedingView.collect()` tira um snapshot de `status.slot.parentA`/`parentB` (o `CreatureDto` completo) **antes** de chamar `/api/breeding/collect` — depois de coletar a gestação vira inativa e esses dados somem da resposta de status. Cada pai perdido (`deadParents`, até 2) ganha um retrato próprio (`FishCanvas` em cinza — `filter: grayscale(0.85) brightness(0.8)`, `.farewell-portrait`) com uma animação suave de entrar e assentar (`farewell-fade`), separado por uma linha divisória do resto da celebração (tom contido — o filhote nasceu, o luto é à parte, não compete pelo mesmo destaque). Testado em `frontend/cypress/e2e/breeding-farewell.cy.js`.

**Mitigar o risco de morte (31/07/2026)** — a pedido do usuário: três alavancas complementares, nenhuma obrigatória, priorizando uma economia sustentável (o gasto real é opcional, não uma taxa disfarçada):

- **Descanso (passivo, de graça):** `CreatureInstance.LastBredAt` (nullable, setado a cada gestação completada como pai/mãe) registra quando o peixe terminou seu último cruzamento. `BreedingCalculator.EffectiveBreedCount(breedCount, lastBredAt, asOf)` decai o `BreedCount` acumulado por uma meia-vida exponencial (`RestHalfLifeDays = 5`): um peixe com `BreedCount=6` que descansou 10 dias (2 meias-vidas) entra no próximo cruzamento "contando" como `BreedCount` efetivo ≈1.5, não 6 — risco cai de ~67% pra ~30% só de esperar. Calculado como-de-agora (`DateTime.UtcNow`) na prévia (`/breeding/quote`) e travado no `now` do `/breeding/start` (a gestação em si NÃO conta como descanso — o pai está "ocupado"). `BreedCount` bruto nunca decai (histórico permanente); só o RISCO calculado se recupera com paciência. Calibração em `Vivarium.Simulation breed` (seção "DESCANSO").
- **Estabilizador genético (soft, redução parcial):** opt-in `useStabilizer` no `POST /api/breeding/start` — cobra `BreedingDefaults.StabilizerCostSoft` (150, fixo, somado ao custo normal da gestação) e reduz o risco travado dos dois pais pela metade (`StabilizerReductionFactor = 0.5`). Sem item/inventário: é cobrado e aplicado direto no Start (mais simples que UserInventory pra algo usado uma vez por gestação).
- **Seguro de cruzamento (premium, garantia total):** opt-in `useInsurance` no mesmo endpoint — garante 0% de morte pros dois pais nesta gestação. Custo escala com o risco combinado sendo removido: `BreedingCalculator.InsuranceCostPremium(chanceA, chanceB) = clamp(InsuranceBasePremium + InsurancePerRiskPercent·(chanceA+chanceB)·100, 20, 400)` — barato pra um par fresco (~50 premium), até o teto de 400 pra veteranos de alto risco. Mesma filosofia do rush (§8.11): premium compra conveniência opcional, nunca é exigido. Se os dois forem marcados, o seguro tem prioridade (o estabilizador não é cobrado nem faz diferença).
- **Travado no Start, não recalculado no Collect:** `BreedingSlot` ganhou `ParentADeathChance`/`ParentBDeathChance` (já com descanso + estabilizador/seguro aplicados) e `InsuranceUsed` — `CollectAsync` rola o risco usando esses valores gravados, não recalcula do zero (evita que o tempo gasto NA gestação conte como descanso, e mantém consistente o que foi mostrado/pago no Start). Expostos em `BreedingSlotDto` pra a UI mostrar "risco travado: X%" ou um selo "🛡️ Seguro ativo" durante a gestação.
- **`TransactionType.BreedingInsurance`** (novo valor no enum) audita o gasto em premium do seguro, separado do `Breeding` (soft) normal.
- **Frontend (`BreedingView.jsx`):** no modal de prévia, a seção de risco ganhou três opções em rádio (`.safety-options`) — sem proteção / estabilizador / seguro — com o custo e o risco resultante recalculados ao vivo (client-side, espelhando a mesma aritmética simples: `chance × StabilizerReductionFactor` ou `0` pro seguro). Durante a gestação ativa, mostra o risco travado ou o selo de seguro.
- **Testes:** `BreedingCalculatorTests.cs` (Core — `EffectiveBreedCount`, `InsuranceCostPremium`) + 4 casos novos em `BreedingTests.cs` (Api — seguro cobra e zera risco, saldo premium insuficiente falha, estabilizador cobra extra e reduz pela metade, descanso reduz o risco mostrado na prévia).

### 8.9 Fora do escopo do MVP

- **Alimentação**: cortada do MVP para não duplicar a função de "sink de manutenção" já coberta pela qualidade da água. Candidata a entrar na v2 como boost opcional (não como necessidade punitiva).

### 8.10 Recompensa diária (30/07/2026)

Gancho de retenção simples: resolve o gap "nada convida o jogador a voltar todo dia" apontado na revisão de 30/07 ([[review-melhorias-30-07]] na memória). Decisão de produto tomada sem streak — consistente com a filosofia já documentada de que ausência nunca pune duro (§8.3, diferença online/offline sempre moderada; §8.6, água nunca "mata" nada, só reduz renda).

- **Mecânica:** resgatável **1x por dia calendário UTC**, sem streak, sem penalidade por pular um dia — só não acumula (não dá pra resgatar 2 dias de uma vez). Valor fixo `EconomyDefaults.DailyRewardSoft = 25` soft (dá pra comprar 1 filtro e sobra um pouco; não distorce a economia).
- **Elegibilidade:** `User.LastDailyRewardAt` (nullable) — pode resgatar se nulo ou se `hoje.Date > LastDailyRewardAt.Date`. Sem job/cron: calculado on-demand no `GameService`, mesmo padrão "lazy" do resto do loop de jogo.
- **Endpoints:** `GET /api/game/daily-reward` (status: `canClaim`, `amount`, `nextAvailableAtUtc`) e `POST /api/game/daily-reward/claim` (credita e audita `TransactionLog.DailyReward` — novo valor no enum `TransactionType`, já que "renda passiva" comum não é logada mas isso É uma transação nomeada, similar a `ItemPurchase`).
- **Frontend:** botão "🎁 Recompensa diária" no topbar (`GameView.jsx`), só aparece quando `canClaim`, com leve animação de pulso (`.daily-reward-btn`, `daily-reward-pulse`) pra chamar atenção sem ser agressivo. Some depois de resgatar (poll de `useDailyReward`, 5 min — não é urgente, é baseado em dia calendário, só cobre virar o dia com a aba aberta).
- **Testes:** `tests/Vivarium.Api.Tests/DailyRewardTests.cs` (5 casos, incl. "dia seguinte pode resgatar de novo" simulando `LastDailyRewardAt -1 dia`) + `frontend/cypress/e2e/daily-reward.cy.js` (2 casos E2E, API mockada).

### 8.11 Ritmo lento anti-rush + acelerar com moeda premium (31/07/2026)

Decisão de produto explícita: o jogo precisa ser **impossível de rushar de graça** — geração de peixe e cruzamento passaram a ser deliberadamente lentos, e a **única** forma de comprimir esse tempo é pagando em moeda premium (comprada com dinheiro real, quando o processador de pagamento existir — ver gap abaixo). Isso dá à moeda PREMIUM (schema desde o início, nunca usada até aqui — CLAUDE.md §9, `CurrencyType`) sua primeira utilidade real.

> ⚠️ **Temporário (07/08/2026):** `GenerationIntervalMinutes` reduzido de 60→**10** só pra fase de testes com jogadores reais (mais volume de peixe pra testar sem esperar horas) — não é uma mudança de design, é conveniência de QA. **Reverter pra 60 antes de qualquer lançamento "de verdade"** (`TickConfig.cs`, `HabitatDefaults.GenerationIntervalMinutes`).

- **Geração mais lenta:** `HabitatDefaults.GenerationIntervalMinutes` 25→**60** (quase dobra). Cadência de lendário recalculada por simulação (`Vivarium.Simulation economy`): casual ~1 a cada 282 dias, ativo ~1/71 dias, dedicado ~1/35 dias (~1 por mês) — antes disso o "dedicado" já estava em ~2 semanas, rápido demais pro objetivo.
- **Gestação mais lenta (30-31/07/2026), depois corte assimétrico (06/08/2026):** `BreedingDefaults.BaseGestationHours` foi 8→24 (3x, anti-rush) e depois **24→6** (usuário achou 2 dias pra incomuns demais — ver §8.8 pro raciocínio completo do corte assimétrico, que subiu `GestationGrowth` 0.12→0.185 pra compensar e manter o topo do lendário quase intocado). `MinGestationHours` seguiu o mesmo padrão do Base em cada mudança (4→12→6).
- **Acelerar (rush) com premium:** `RushCalculator` (`src/Vivarium.Core/Gameplay/RushCalculator.cs`) — custo proporcional ao tempo restante, sem termo de raridade explícito (a gestação de peixes raros já é mais longa, então o custo de pular já escala com a raridade indiretamente via mais horas restantes). Fila: `0.15 premium/min` restante (60 min = 9 premium). Gestação: `2.0 premium/hora` restante (24h = 48 premium; teto de 240h = 480 premium). Só rush **total** no MVP (pula tudo de uma vez, não parcial).
- **Endpoints:** `POST /api/game/queue/{id}/rush` (fila) e `POST /api/breeding/rush` (gestação ativa) — debitam premium, zeram `ReadyAt`/`readyAt` pra agora, auditam `TransactionLog.TimeSkip` (novo valor no enum `TransactionType`). O custo de cada item/gestação já vem calculado nas respostas normais (`QueueItemDto.RushCostPremium`, `BreedingSlotDto.RushCostPremium`) — sem round-trip extra pra saber o preço antes de clicar.
- **Frontend:** botão "⚡ {custo}" (`.rush-btn`, roxo/epic — `--r-epico`) ao lado de itens da fila não prontos (`TankView.jsx`) e da gestação ativa (`BreedingView.jsx`). Chip de saldo premium "💎" no topbar (`GameView.jsx`) — a moeda premium nunca tinha aparecido na UI antes, sempre foi 0.
- **Gap real, não escondido:** não existe processador de pagamento integrado (Stripe ou similar) — então hoje **não há forma real de um jogador comprar premium**. O mecanismo de jogo está pronto e testado; falta só a ponte com dinheiro real, que é uma integração maior (conta de comerciante, webhook de confirmação, etc.) fora do escopo desta mudança. Pra testar localmente, `/api/dev/coins?currency=PREMIUM` (só em Development) credita premium — nunca existe em produção (mesma regra de todos os endpoints `/api/dev/*`).
- **Testes:** `RushCalculatorTests.cs` (Core) + `RushTests.cs` (Api, 5 casos: custo aparece e escala, sem saldo falha, com saldo libera coleta, item já pronto não deixa acelerar de novo, gestação segue o mesmo fluxo) + `frontend/cypress/e2e/rush.cy.js` (2 casos E2E). Simulação de trajetória (`Vivarium.Simulation simulate`) re-rodada após o rebalanceamento: economia continua saudável (carteira cresce nos 3 perfis em 120 dias), só que agora com menos da metade dos peixes coletados no mesmo período — o ritmo "de graça" ficou visivelmente mais lento, como pretendido.

### 8.12 Venda ao NPC / vendor (31/07/2026)

Sink pra duplicatas/comuns acumulados: surgiu de uma discussão sobre reduzir a oferta de peixe comum no mercado sem desacelerar o loop central de coleta (que já tinha sido desacelerado o suficiente na §8.11). Em vez de apertar `GenerationIntervalMinutes` de novo — instrumento cego que atrasaria retenção sem resolver especificamente o excesso de comuns — o sink certo é dar vazão às duplicatas: venda instantânea ao NPC por um preço deliberadamente baixo.

- **Preço:** `VendorCalculator.Price(rarityScore)` (`src/Vivarium.Core/Gameplay/VendorCalculator.cs`) reaproveita a mesma curva de `IncomeCalculator.CoinsPerHour` — não inventa fórmula nova. `preço = max(VendorMinPrice, coinsPorHora(score) × VendorHoursEquivalent)`, com `VendorHoursEquivalent = 2.0` e `VendorMinPrice = 1` (`TickConfig`). Um comum (score ~5) vende por ~9 soft (menos que 1 filtro básico, 20 soft); a curva escala com a raridade só pra não ser absurda em peixes bons, mas o preço nunca chega perto do valor real do mercado entre jogadores — quem tem um peixe que vale a pena deveria listar, não vender pro NPC.
- **Não apaga a linha:** mesma razão do `IsDead` de breeding (§8.8) — `CreatureInstance.ParentAId/BId` tem FK `Restrict`, e `TransactionLog`/`BreedingSlot.ChildCreatureId` também referenciam a linha. Vender ao NPC marca `SoldAt = now` e `HabitatId = null`; a criatura some das queries de tanque/mochila (`GameService.BackpackQuery` e a query de tanque por `HabitatId`) mas a linha continua existindo pra preservar linhagem e auditoria.
- **Endpoint:** `POST /api/game/creatures/{id}/sell-vendor` — bloqueia se listada no mercado, morta ou já vendida; credita o soft na hora, audita `TransactionLog.VendorSale` (novo valor no enum `TransactionType`).
- **Frontend:** botão "Vender ao NPC · {preço}" na Mochila e nos detalhes do peixe no Tanque, com um `ConfirmModal` novo (`components/ConfirmModal.jsx`, reaproveita a casca do `Modal.jsx`) — mensagem explícita de que o preço é baixo e a ação não pode ser desfeita, `.btn-danger` (coral) pra diferenciar visualmente do "Vender" (listar no mercado, que é reversível via cancelar). `generator.js` ganhou `vendorPriceOf(rarityScore)` espelhando a fórmula (mesmo princípio de todo cálculo client-side: só display, o servidor é a fonte).
- **Testes:** `VendorCalculatorTests.cs` (Core) + `VendorSaleTests.cs` (Api, 6 casos: credita e some do tanque/mochila, preço escala com raridade, bloqueia se listada/já vendida/de outro usuário) + teste unitário espelho em `generator.test.js`.
- **Ideias relacionadas discutidas e não implementadas:** fusão "10 peixes iguais → 1 melhor" foi cogitada como sink adicional, mas adiada — risco real de canibalizar o sink de breeding (custo dinâmico, gestação, risco de morte) se virar um caminho determinístico e sem risco pra subir de raridade. Se for retomada, deveria ser um reroll com odds melhoradas (reaproveitando `BiasedInheritProbability`), não um upgrade garantido, e definida por cor de cauda (não raridade) pra também servir de ferramenta de farm temático (sinergia de cor, §8.6).

### 8.13 Peixe inicial no registro (07/08/2026)

Conta nova ganha 1 peixe **já pronto pra coletar** na fila, sem esperar o primeiro ciclo de geração (60 min) — resolve o "primeiro clique vazio" (jogador loga, vê o tanque parado, não tem nada pra fazer até a primeira geração terminar). Implementado em `AuthEndpoints.cs` (register): cria o `GenerationQueueItem` com `ReadyAt = now` no mesmo `SaveChangesAsync` do habitat (usa a navegação `Habitat` do EF em vez de `HabitatId`, já que o Id só existe depois de salvar). Seed sorteado normalmente na coleta (mesma regra de sempre, §8.5) — não é um peixe "especial", só a fila começa com 1 item em vez de 0.

**Backfill único pros jogadores que já existiam (07/08/2026):** rodado direto contra o Neon (script em `scripts/backfill-starter-fish-2026-08-07.sql`, com dry-run antes de aplicar) — deu +1 peixe pronto a todo aquário com espaço na fila. 16 contas afetadas.

### 8.14 Painel de admin (07/08/2026)

`User.IsAdmin` (bool, default false) — não é papel de jogo, é acesso a ferramentas administrativas pontuais. Checado sempre fresco do banco (`AdminService.IsAdminAsync`), nunca embarcado no JWT (token não tem revogação, §12.1 — colocar `IsAdmin` lá deixaria um admin removido continuar admin até o token expirar em 7 dias).

- **Endpoint:** `POST /api/admin/give-starter-fish-all` (`AdminEndpoints.cs`/`AdminService.cs`) — mesma mecânica do peixe inicial (§8.13), aplicada a todos os aquários com espaço na fila de uma vez. Devolve `{ habitatsAffected }`. 403 (`ErrorKind.Forbidden`, novo no `ServiceResult`) se `IsAdmin=false`.
- **Frontend:** botão "🎣 Dar peixe a todos" no topbar, visível só quando `tank.isAdmin` (campo novo em `TankResponse`, calculado em `GameService.GetTankAsync`) — atrás de um `ConfirmModal` (ação afeta todo mundo, não é reversível).
- **Ativação:** não existe UI pra promover admin (só acontece via update direto no banco, deliberadamente — não é uma feature de jogo, é acesso operacional). Usuário `marco` (marcosogenio@hotmail.com) é admin.
- **Testes:** `AdminTests.cs` (não-admin recebe 403; admin dá peixe a todos os aquários elegíveis, respeitando QueueCap).

### 8.15 Tanque em faixas de capacidade + filtros em níveis (08/08/2026, branch `next-release`)

Progressão de médio/longo prazo pro upgrade de tanque: em vez de um único upgrade infinito (+1 por compra, preço `1.5^(cap-3)` pra sempre), a capacidade evolui dentro do mesmo `Habitat` por **3 faixas nomeadas**, cada uma com curva de preço e degradação de água próprias — "Aquário" (3-5), "Aquário Grande" (5-10), "Aquário Master" (10-15, teto absoluto do MVP). Em paralelo, o filtro automático deixa de ser binário (tinha/não tinha) e passa a ter **níveis**, cada um cobrindo uma capacidade de peso de peixes; acima da cobertura o benefício tapera suavemente, sem penhasco — mesmo princípio de `IncomeWaterPlateau`/`IncomeWaterExp` (§8.6). Fora de escopo agora: o futuro "cascudo" — **um peixe novo** (espécie/criatura que nadaria no tanque, não um item de loja; diferente do filtro automático, que é equipamento comprado) que ajudaria na limpeza passiva — só um hook comentado em `GameService.ApplyTickAsync`, sem implementação.

- **`CapacityBand`/`CapacityBands`** (`src/Vivarium.Core/Gameplay/TickConfig.cs`): record com `MinCapacity`, `MaxCapacity`, `Name`, `PriceBase`, `PriceGrowth`, `DegradationBandFactor`; `CapacityBands.BandFor(capacity)` resolve a faixa atual (capacidade no teto de uma faixa ainda conta como dessa faixa), `CapacityBands.MaxCapacity` é o teto absoluto (15). Valores calibrados via `Vivarium.Simulation economy`/`simulate`: Aquário (base 50, growth 1.5, fator 1.0 — preço preservado do sistema antigo), Aquário Grande (base 140, growth 1.45, fator 1.25), Aquário Master (base 400, growth 1.4, fator 1.55).
- **Filtro em nível, sem penhasco:** `HabitatTickState.FilterCapacity` (decimal, era `HasAutoFilter` bool) substitui o binário. Em `HabitatTicker.ProcessTick`: cobertura total (`ActiveFishWeight <= FilterCapacity`) reduz a degradação pela metade (mesmo benefício de sempre); acima disso, `filterFactor = 0.5 + 0.5·(1 − (FilterCapacity/ActiveFishWeight)^FilterTaperExponent)` — decai suavemente de volta a 1.0 conforme o excedente cresce, nunca corta abrupto pra "sem filtro". `FilterTaperExponent = 1.0` (`TickConfig`). O fator de degradação da faixa (`CapacityBandDegradationFactor`) multiplica a fórmula junto (tanque maior suja proporcionalmente mais).
- **`ItemDefinition.EffectJson` finalmente ativado (08/08/2026):** existia desde o início mas nunca era desserializado (decorativo) — agora `ItemEffect.Parse` (`ItemService.cs`) lê `filterCapacity` (níveis de filtro) e `capacityDelta` (upgrade, já era o comportamento hardcoded, sem mudança de efeito). `GameService.FilterCapacityAsync` busca todos os filtros automáticos possuídos do jogador e usa o **máximo** `filterCapacity` — níveis não empilham, o melhor prevalece.
- **3 níveis de filtro (migration de dados `AddFilterTiers`, só dados, sem mudança de schema):** `auto_filter` (Id 2, cobre 5, 500 soft, `EffectJson` migrado de `{"autoFilter":true}` pra `{"filterCapacity":5}`), `auto_filter_2` (Id 4, cobre 10, 1200 soft), `auto_filter_3` (Id 5, cobre 18, 2500 soft).
- **Bug corrigido:** `ItemService.OwnedAutoFilterAsync`/`ListAsync.Owned` checavam por `ItemCategory` (categoria inteira) — com múltiplos níveis na mesma categoria `AutoFilter`, isso bloquearia incorretamente comprar um nível 2 já tendo o nível 1. `UserInventory` já tinha índice único em `(UserId, ItemDefinitionId)`, não em categoria — corrigido pra checar por `ItemDefinitionId` específico (`OwnedItemDefinitionIdsAsync`).
- **Preço do upgrade por faixa:** `ItemService.CurrentPrice` resolve `CapacityBands.BandFor(habitat.Capacity)` e usa a curva daquela faixa — cada faixa é independente, não uma extensão da anterior. Teto absoluto checado em `BuyAsync` antes do débito da carteira (`habitat.Capacity >= CapacityBands.MaxCapacity` → 400).
- **`TankResponse`** ganhou `CapacityBandName` (nome da faixa atual, pra UI) e `CapacityBandDegradationFactor` (pro cliente exibir o impacto de água por peixe corretamente sem duplicar `CapacityBands` em JS — `generator.js: waterDegradationPerFishPerHour(score, bandFactor)` recebe o fator já resolvido do backend).
- **Frontend:** `TankView.jsx` mostra o nome da faixa no chip de capacidade; `StoreView.jsx` descreve os 3 níveis de filtro e o upgrade por faixa; `FishDetail.jsx` passa `tank.capacityBandDegradationFactor` pro cálculo de impacto de água exibido.
- **Testes:** `GameplayTests.cs` (`CapacityBandsTests` — resolução de faixa; `FiltroComCoberturaParcial_BeneficioTaperaSuavemente`, `FaixaDeCapacidadeMaior_DegradaMaisRapido`) + `ItemTests.cs` (catálogo com 5 itens, filtro nível 2 não bloqueado por já ter o nível 1, teto de capacidade retorna 400).
- **Hook do cascudo (não implementado):** "cascudo" é um **peixe novo** (uma espécie/criatura futura, nadaria no tanque como qualquer outro peixe) — **não confundir com o filtro automático** (`auto_filter`/`auto_filter_2`/`auto_filter_3`), que é equipamento comprado na loja. Comentário em `GameService.ApplyTickAsync` documenta que o bônus de limpeza passiva do cascudo entraria somado a `FilterCapacity` (ou como multiplicador extra no `filterFactor`) — mesma fórmula, mais um termo, sem estrutura nova; só a origem do bônus é diferente (peixe vivo, não item). **Lado visual (08/08/2026, branch `fish-visual-polish`):** o aquário ganhou manchas de alga na decoração que crescem com a água suja (`drawAlgaePatches`, `fishRenderer.js`, reaproveita o mesmo `murk` que já esverdeia a água) — quando o cascudo existir, o efeito dele seria reduzir/limpar essas manchas por onde passa, reaproveitando esse mesmo helper em vez de criar sistema visual novo (TODO já deixado no código).

---

## 9. Schema de dados completo (MVP, desacoplado para escalar)

### 9.1 Princípios de desacoplamento adotados

Antes das tabelas, os 4 princípios que guiaram as escolhas abaixo — cada um resolve um problema concreto de acoplamento que apareceria se você modelasse "pensando só em peixe":

1. **`Habitat` genérico, não `Tank` fixo.** Hoje é só aquário, mas você já cogitou terrário/orquídea no futuro. Se `Tank` for uma entidade fixa amarrada a "água", portar pra terrário exige reescrever o motor. Com `Habitat` genérico + `HabitatType` (enum: Aquarium, Terrarium...), o mesmo motor de tick/geração serve pros dois, só mudando os dados de configuração.
2. **`Creature` genérico, não `Fish` fixo.** Mesma lógica: o motor de seed→traits→rarity que desenhamos não tem nada de especificamente "peixe" — ele já foi pensado genérico. O nome da entidade deveria refletir isso desde já.
3. **`CurrencyType` como tabela, não campos fixos `SoftBalance`/`PremiumBalance` no usuário.** Se um dia você quiser um terceiro tipo de moeda (ex: "ticket de evento"), não quebra schema — só insere uma linha nova em `CurrencyType`.
4. **`TransactionLog` único e genérico pra tudo que envolve valor ou posse mudando de mãos** (venda no mercado, transferência direta, compra de moeda premium). Isso vira sua ferramenta central de auditoria/anti-cheat — um único lugar pra investigar exploit ou duplicação, em vez de logs espalhados por tabela.

### 9.2 Tabelas

```
User
- Id (PK)
- Username
- Email
- PasswordHash
- CreatedAt
- LastDailyRewardAt (datetime, nullable) -- último resgate da recompensa diária (8.10)
- IsAdmin (bool, default false) -- acesso a ferramentas administrativas, não é papel de jogo (8.14)

VipSubscription
- Id (PK)
- UserId (FK -> User)
- StartAt
- EndAt
- Status (Active | Expired | Cancelled)
-- Separado do User (não um bool "IsVip") para permitir histórico e, 
-- no futuro, diferentes tiers de assinatura sem alterar User.

CurrencyType
- Id (PK)
- Code (SOFT | PREMIUM)
- Name

WalletBalance
- Id (PK)
- UserId (FK -> User)
- CurrencyTypeId (FK -> CurrencyType)
- Amount
-- Um saldo por (User, CurrencyType) em vez de colunas fixas — nova moeda 
-- não exige migration estrutural.

HabitatType
- Id (PK)
- Code (Aquarium | Terrarium...)
- Name

Habitat
- Id (PK)
- UserId (FK -> User)
- HabitatTypeId (FK -> HabitatType)
- Capacity (int)
- MaintenanceLevel (decimal, 0-100) -- generalização de "qualidade da água"
- QueueCap (int)
- GenerationIntervalMinutes (int)
- OnlineGenerationRate (decimal)
- OfflineGenerationRate (decimal)
- GenerationProgressMinutes (decimal) -- resto de progresso entre ticks
- LastTickAt (datetime)
- LastHeartbeatAt (datetime)
- CreatedAt

Species
- Id (PK)
- HabitatTypeId (FK -> HabitatType) -- uma espécie pertence a um tipo de habitat
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
- IsSick (bool) -- nasceu com qualidade da água crítica; coleta com desvantagem

CreatureInstance
- Id (PK)
- SpeciesId (FK -> Species)
- OwnerId (FK -> User)
- HabitatId (FK -> Habitat, nullable -- null quando está listado no mercado ou em trânsito)
- Seed (bigint)
- TraitConfigVersion (int)
- RarityScore (decimal, cacheado)
- ParentAId (FK -> CreatureInstance, nullable) -- linhagem (breeding, 8.8)
- ParentBId (FK -> CreatureInstance, nullable)
- ParentASeed (bigint, nullable) -- denormalizado do pai p/ reconstruir traits (BreedTraits) sem join
- ParentBSeed (bigint, nullable)
- ParentAGrandparentASeed (bigint, nullable) -- avós (31/07/2026): denormalizados do PAI (ParentASeed/BSeed dele), habilita GrandparentReachChance e evita reconstruir um pai-que-é-filhote com Generate(seed) errado
- ParentAGrandparentBSeed (bigint, nullable)
- ParentBGrandparentASeed (bigint, nullable)
- ParentBGrandparentBSeed (bigint, nullable)
- BreedCount (int, default 0) -- nº de gestações já completadas como pai/mãe
- LastBredAt (datetime, nullable) -- quando terminou a última gestação; descanso decai o risco (8.8, BreedingCalculator.EffectiveBreedCount)
- IsDead (bool, default false) -- não sobreviveu a uma gestação (risco cresce com BreedCount)
- DiedAt (datetime, nullable)
- SoldAt (datetime, nullable) -- vendido ao NPC (vendor, 8.12); não apaga a linha (mesma razão do IsDead)
- CreatedAt

ItemDefinition
- Id (PK)
- Key (ex: "filter_basic", "tank_upgrade_1")
- Name
- Category (Filter | AutoFilter | HabitatUpgrade | ...)
- EffectJson -- efeito interpretado pelo motor, não hardcoded no código
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
- Type (MarketSale | DirectTransfer | CurrencyPurchase | ItemPurchase | Sink | Breeding | BreedingLoss | DailyReward | TimeSkip | VendorSale | BreedingInsurance)
- FromUserId (FK -> User, nullable)
- ToUserId (FK -> User, nullable)
- CreatureInstanceId (FK -> CreatureInstance, nullable)
- CurrencyTypeId (FK -> CurrencyType, nullable)
- Amount (decimal, nullable)
- CreatedAt

BreedingSlot -- par em gestação (8.8); habitat de reprodução dedicado (HabitatType "Breeding")
- Id (PK)
- UserId (FK -> User)
- HabitatId (FK -> Habitat) -- o habitat de reprodução do usuário
- ParentAId (FK -> CreatureInstance)
- ParentBId (FK -> CreatureInstance)
- StartedAt
- ReadyAt -- StartedAt + BreedingCalculator.GestationHours(scoreA, scoreB)
- CostPaid (decimal) -- BreedingCalculator.CostSoft(scoreA, scoreB) + estabilizador (se usado) no momento do Start
- ParentADeathChance (decimal) -- risco travado no Start (descanso + estabilizador/seguro já aplicados); usado tal qual no Collect
- ParentBDeathChance (decimal)
- InsuranceUsed (bool, default false) -- seguro de cruzamento comprado (premium); garantiu 0% de morte pros dois pais
- Status (InProgress | Collected)
- ChildCreatureId (FK -> CreatureInstance, nullable) -- preenchido na coleta
```

### 9.3 Por que isso escala bem sem over-engineering

Vale notar: esse nível de desacoplamento (`Habitat` genérico, `CurrencyType` como tabela) tem um custo pequeno de complexidade agora — mas é o tipo de decisão que compensa porque **mexe na fundação**, não numa feature de borda. Trocar `Tank` por `Habitat` depois que já tiver 10 mil linhas de código dependendo do nome específico é caro; decidir isso agora é gratuito. Já coisas como sistema de guilda, evento sazonal, ranking — essas eu **não** modelaria agora, porque são features de borda que não afetam a fundação: dá pra adicionar tabela nova depois sem tocar no que já existe.

---

## 10. Stack técnico (fechado)

| Camada | Escolha | Motivo |
|---|---|---|
| Backend | ASP.NET Core + EF Core | Já domina, alta produtividade, performance não é gargalo no MVP |
| Banco | PostgreSQL | Neon.tech (free tier) pra começar sem custo |
| Frontend | React + Canvas | Composição de camadas do peixe é simples o bastante pra não justificar engine de jogo (Phaser); React cobre UI padrão (mercado, inventário, wallet) |
| Deploy frontend | Cloudflare Pages | Build estático, CDN global, grátis |
| Deploy backend | Oracle Cloud Free Tier (VPS via Docker) | Mantém stack .NET intacta, grátis pra sempre (não é trial), sem "sleep" que atrapalharia o heartbeat online/offline |

**Nota sobre Cloudflare:** Cloudflare Workers **não roda .NET** (runtime baseado em V8, pensado pra JS/TS/WASM leve) — por isso o backend vai pro Oracle Cloud, não pro Cloudflare. Cloudflare fica só como host do frontend estático (e pode futuramente atuar como proxy/CDN na frente do backend, se quiser).

**Plano B de hospedagem do backend** (caso a instância free do Oracle fique indisponível por demanda na região): Render ou Fly.io free tier — mesma stack .NET, mas o serviço "dorme" após um tempo sem uso e demora alguns segundos pra acordar no primeiro request, o que pode desestabilizar o heartbeat em fase de poucos usuários. Aceitável como fallback temporário, não como escolha primária.

**TLS do backend (06/08/2026):** a API não tem HTTPS embutido (sem `UseHttpsRedirection`/Kestrel TLS no código — espera um proxy na frente, daí `ForwardedHeaders__Enabled`). Como o frontend roda em HTTPS no Cloudflare Pages, o navegador bloqueia chamadas a um backend em HTTP puro (mixed content), então o backend também precisa de HTTPS. Solução 100% grátis (sem domínio pago, sem assinatura): **DuckDNS** (subdomínio grátis, ex. `vivarium.duckdns.org`, apontado pro IP público reservado da VM) + **Caddy** (reverse proxy que emite e renova certificado Let's Encrypt automaticamente, sem passo manual de certbot) rodando na própria VM do Oracle, na frente do container da API — arquitetura Oracle+Neon inalterada, é só uma camada de rede adicional. Descartadas: domínio próprio pago (sem necessidade), Cloudflare Quick Tunnel (URL muda a cada restart, incompatível com `VITE_API_URL` fixo no build do frontend). Artefatos versionados em `deploy/` (`docker-compose.yml`, `Caddyfile`, `.env.example`, passo a passo em `deploy/README.md`).

**Renderização do peixe:** composição via `<canvas>`, desenhando as camadas (corpo, cauda, dorsal, peitoral) como imagens sobrepostas, usando `globalCompositeOperation = 'overlay'` para o shimmer do corpo.

---

## 11. Próximos passos sugeridos

### Status atual (28/07/2026)

MVP jogável de ponta a ponta, rodando local contra o Neon. Feito:
- ✅ Motor de geração seed→traits (`Vivarium.Core/Generation`), simulação de pesos e faixas de raridade calibradas
- ✅ **Traits de movimento** (velocidade/amplitude de cauda e nadadeira; extremos no score com peso 0.5) — seção 5.1
- ✅ Backend completo (auth JWT, loop de jogo com tick lazy, mercado, loja de itens, transferência direta, camada de serviço `ServiceResult` — fase 4b) — seção 12; **148 testes verdes** (78 API + 70 Core, 07/08/2026)
- ✅ **Breeding** (30/07/2026, seção 8.8) — habitat de reprodução dedicado, gestação escalada por raridade combinada, herança trait-a-trait; resolve o gap #1 de sink recorrente
- ✅ Banco no **Neon** (sa-east-1) com migrations aplicadas; connection string em user-secrets local
- ✅ Frontend React/Vite: auth, **aquário animado** (peixes nadando, seleção por clique), mercado, loja
- ✅ **Cauda com onda viajante** (undulação em S via sprite + blit por fatias) em `fishRenderer.js` e no protótipo — 100% renderização, tunável em `MOVEMENT_TUNING`
- ✅ Ferramentas de dev: `dev.cmd` (sobe API+front+navegador), botões dev de gerar/limpar peixes (só em Development)

- ✅ **(06/08/2026) Publicado em produção:** backend no Oracle Cloud (`147.15.36.29`, Docker Compose com `api`+`caddy`, TLS automático via DuckDNS+Let's Encrypt — `deploy/`) em `https://vivarium-online.duckdns.org`; frontend no Cloudflare Pages/Workers em `https://vivarium.marcospdnnogueira.workers.dev` (auto-deploy a cada push no `master`, confirmado funcionando). Testado ponta a ponta (registro, login, tanque). VM protegida (só chave SSH ed25519, senha desabilitada, fail2ban, firewall restrito a 22/80/443).
- ✅ **(07/08/2026) Resolvido — causa raiz era falta de swap, não fail2ban:** a VM tem só **954 MB de RAM e nenhum swap** por padrão (Always Free micro). `docker compose up -d --build` (que roda `dotnet publish` dentro do build stage) esgotou a memória e **travou a VM inteira** (SSH e HTTPS pararam de responder pra qualquer IP, não só pra quem estava operando — inicialmente suspeitamos do `fail2ban`, mas o próprio usuário confirmou de outro IP que também não conseguia acessar, o que descartou essa hipótese). Um reboot pelo console do Oracle destravou a VM (containers voltaram sozinhos, `restart: unless-stopped`, mas ainda com a imagem antiga). Fix aplicado: **swapfile de 2GB** (`/swapfile`, persistido em `/etc/fstab`) — com essa margem, o rebuild completo (`dotnet publish`, ~170s) terminou sem travar. Deploy da API atualizado com sucesso pra `4b5de9c`. **Lição:** builds de imagem (`--build`) nessa VM só devem rodar com o swapfile ativo (`free -h` pra confirmar `Swap: 2.0Gi` antes de qualquer `docker compose up --build`).

Falta pra ir ao ar de verdade (depende de contas/decisões do usuário):
- ⏳ **Assets do designer** (item 2 abaixo) — trocar as formas procedurais do `fishRenderer.js`/protótipo pelos sprites reais
- ⏳ Domínio próprio (opcional — hoje usa DuckDNS/workers.dev, funcional mas feio); processador de pagamento pra premium (§8.11)
- ⏳ Trocar heartbeat por WebSocket/SSE (melhoria pós-MVP, seção 8.3); v2: alimentação e breeding (seção 8.6)
- ⏳ **VIP incompleto (06/08/2026):** o modelo (`VipSubscription`) e a lógica de consumo (`GameService.HasActiveVipAsync`, auto-coleta no tick — §8.1) já existem, mas não há **nenhum** jeito de ativar uma assinatura hoje — sem endpoint, sem item de loja, nem dev-endpoint de teste. Falta: decidir o modelo de venda (assinatura recorrente? preço?), endpoint de compra/renovação e, futuramente, o mesmo processador de pagamento pendente do premium.
- ✅ **(08/08/2026) Degradação da água ponderada por raridade — implementado.** Cada peixe agora soma `rarityScore/DegradationRarityRefScore` (peso) à degradação, em vez de sempre 1 fixo — quem rende mais suja mais. `DegradationRarityRefScore=5` calibrado pra um comum (score~5) continuar exatamente como antes (peso 1, ~0,9/h); um raro (score~8,65) sobe pra peso ~1,73 (~1,6/h); um épico/lendário (score~15) chega a peso 3 (~2,7/h). `HabitatTickState.ActiveFishCount` (int) virou `ActiveFishWeight` (decimal) em `HabitatTicker.cs` — `GameService.ApplyTickAsync` computa o peso a partir dos `RarityScore` já carregados (`fish.Sum(f => f.RarityScore) / ref`, zero query extra). Espelhado em `generator.js` (`waterDegradationPerFishPerHour(rarityScore)`, agora recebe o score em vez de ser uma constante fixa) e no `FishDetail.jsx`. Testes: `PeixeMaisRaro_DegradaAguaMaisRapido` (API, compara dois aquários com 1 peixe cada, raridades diferentes) + teste unitário em `generator.test.js`. Revalidado com `Vivarium.Simulation economy`/`simulate`: upkeep de "10 raros" subiu de 2,4→3,3/h, "1 lendário" de 0,8→1,1/h; economia dos 3 perfis continua saudável em 120 dias.

### Passos originais

1. ✅ **(27/07/2026)** Validar a tabela de pesos via simulação — `tools/Vivarium.Simulation`, 100k seeds: distribuição real bate com os pesos (lendário 0.21% vs 0.2% esperado); faixas de exibição recalibradas na seção 5
2. Fechar com seu amigo de design: 1 corpo base cinza + 8 cores de parte (cauda/dorsal/peitoral) + 4 texturas de padrão (estria, bolinha, degradê, manchado) — esse é o pacote mínimo de assets pro MVP
3. ✅ **(27/07/2026)** Motor de geração (seed → traits) implementado em `src/Vivarium.Core/Generation` (config v1 hardcoded em `TraitConfigV1`, migra pro banco quando o backend existir), coberto por unit tests em `tests/Vivarium.Core.Tests`
4. ✅ **(27/07/2026)** Composição visual em Canvas prototipada em `prototype/fish-composer.html` (arquivo único, abre direto no navegador): 4 camadas (cauda → dorsal → corpo cinza + shimmer via `overlay` → peitoral), padrões (estria/bolinha/degradê/manchado) com tamanho/cor/opacidade dos traits, animação leve (bob + cauda) e iridescente com shift de cor no tempo. Contém um **port JS do motor** (verificado: 2.000 seeds idênticos ao C# via `Vivarium.Simulation dump` + `crosscheck`); o cliente final receberá traits prontos da API — o port é só para o protótipo funcionar sem backend. Formas das partes são placeholder procedural até os assets do designer chegarem.

## 12. API — endpoints e decisões (MVP)

**Auth:** JWT Bearer (7 dias, HS256; `Jwt:Key` via appsettings em dev / env var `Jwt__Key` em produção). Senha com PBKDF2-SHA256 (100k iterações, salt aleatório) — sem dependência do ASP.NET Identity. Registro cria automaticamente: carteiras (100 SOFT / 0 PREMIUM — `EconomyDefaults`) e o tanque inicial com `HabitatDefaults`.

**Tick lazy, sem job agendado:** o tick roda dentro de heartbeat/tank/collect (`GameService.ApplyTickAsync`). Auto-coleta VIP acontece no tick quando o tanque está online e há assinatura ativa. No heartbeat, o tick roda ANTES de atualizar `LastHeartbeatAt` (senão um retorno após dias contaria a ausência como tempo online).

**Mercado:** listar tira a criatura do tanque (`HabitatId = null`); cancelar/comprar devolve ao tanque do dono/comprador **se houver espaço** (senão fica fora, `HabitatId null`). Compra roda em transação de banco com revalidação de status e registra `MarketSale` no `TransactionLog` (From=comprador, To=vendedor). Sem taxa de mercado no MVP.

| Método | Rota | Auth | Descrição |
|---|---|---|---|
| GET | `/health` | — | status |
| GET | `/api/creatures/preview/{seed}` | — | traits de qualquer seed (sem banco) |
| POST | `/api/auth/register` | — | cria user + carteiras + tanque; retorna token |
| POST | `/api/auth/login` | — | username ou email + senha; retorna token |
| POST | `/api/game/heartbeat` | ✓ | marca online (janela de 3 min); roda tick |
| GET | `/api/game/tank` | ✓ | estado completo: fila, criaturas, carteira; roda tick |
| POST | `/api/game/collect/{queueItemId}` | ✓ | coleta manual (valida pronto/capacidade) |
| POST | `/api/game/queue/{queueItemId}/rush` | ✓ | pula a espera da fila pagando premium (8.11) |
| POST | `/api/game/creatures/{id}/transfer` | ✓ | transferência direta por username (audita `DirectTransfer`; bloqueada se listada) |
| POST | `/api/game/creatures/{id}/sell-vendor` | ✓ | venda instantânea ao NPC por preço baixo (8.12); audita `VendorSale` |
| GET | `/api/game/daily-reward` | ✓ | status do resgate diário (8.10): `canClaim`, `amount`, `nextAvailableAtUtc` |
| POST | `/api/game/daily-reward/claim` | ✓ | resgata a recompensa diária (1x/dia UTC; audita `DailyReward`) |
| GET | `/api/market/listings?skip&take` | ✓ | listagens ativas |
| POST | `/api/market/listings` | ✓ | lista criatura própria por preço em SOFT |
| POST | `/api/market/listings/{id}/cancel` | ✓ | cancela e devolve ao tanque |
| POST | `/api/market/listings/{id}/buy` | ✓ | compra (transacional + TransactionLog) |
| GET | `/api/items/` | ✓ | catálogo (preço de upgrade calculado pelo nível atual) |
| POST | `/api/items/{key}/buy` | ✓ | compra e aplica efeito; registra `ItemPurchase` (sink) |
| GET | `/api/breeding` | ✓ | gestação em andamento do usuário, se houver (8.8) |
| GET | `/api/breeding/quote` | ✓ | prévia sem custo: custo/gestação/chances do filho/risco de morte dos pais |
| POST | `/api/breeding/start` | ✓ | leva 2 peixes próprios pro habitat de reprodução; debita `CostSoft` dinâmico; registra `Breeding` (sink); aceita `useStabilizer`/`useInsurance` opcionais pra mitigar o risco de morte (8.8) |
| POST | `/api/breeding/collect` | ✓ | coleta o filhote quando pronto (herança trait-a-trait); devolve os pais sobreviventes ao tanque/mochila; rola risco de morte |
| POST | `/api/breeding/rush` | ✓ | pula o tempo restante de gestação pagando premium (8.11) |
| POST | `/api/admin/give-starter-fish-all` | ✓ (admin) | dá +1 peixe pronto a todo aquário com espaço na fila; 403 se `User.IsAdmin=false` (8.14) |

**Itens do MVP** (seed via migration `SeedItemDefinitions`): `filter_basic` (20 soft, restaura água pra 100 — tick roda antes, pra degradação pendente ser aplicada primeiro), `auto_filter` (500 soft, permanente via UserInventory, tick lê e degrada na metade), `tank_upgrade` (base 50 soft, +1 capacidade, preço = base × 1.5^(capacidade − 3) — seção 8.4). Filtro e upgrade aplicam na hora (sem inventário); só o auto_filter fica em UserInventory.

**Testes de integração** (`tests/Vivarium.Api.Tests`): API completa via `WebApplicationFactory` contra SQLite in-memory (Postgres só em staging/produção) — fluxos de auth, coleta, VIP auto-coleta, mercado, renda, mochila ponta a ponta.

### 12.1 Hardening / anti-cheat (auditoria 28–29/07/2026)

- **Concorrência otimista (`xmin`)** em `Habitat`, `WalletBalance`, `MarketListing`, `CreatureInstance` (condicional a `Database.IsNpgsql()` — SQLite dos testes não tem `xmin`; migration `AddConcurrencyTokens` é no-op de DDL). Fecha: compra dupla da mesma listagem (criação de dinheiro), double-list, corrida de preço de item, double-credit de renda. Endpoints tratam `DbUpdateConcurrencyException`: tick recarrega e segue (renda fica exatamente-uma-vez); ações do usuário retornam 409.
- **Limbo eliminado:** coletar/comprar/receber com tanque cheio vai pra **mochila** (8.7), nunca mais some. Compra bloqueia antes de cobrar se comprador sem espaço.
- **Rate limiting:** global 300/min por usuário/IP + `auth` 10/min por IP (config `RateLimiting:*`) — já cobre Market/Item/Breeding/Game (o `GlobalLimiter` é aplicado a toda a pipeline, não só a `auth`). Atrás de proxy exige `UseForwardedHeaders` no deploy pra ver o IP real: **feito** em 30/07 — `ForwardedHeaders__Enabled=true` liga `app.UseForwardedHeaders()` (desligado por padrão, dev/testes não têm proxy); ver `Program.cs`.
- **Validação:** email via `MailAddress.TryCreate`; username `[A-Za-z0-9_-]`, 3–32.
- **Deferidos (documentados, não feitos):** JWT sem revogação (validade 7d); taxa de mercado como sink; teto de listagens por usuário; multi-conta (soft sem cash-out limita o dano). *(`window.prompt`→modais: **feito** em 30/07 — `components/PromptModal.jsx`.)*
- **Chance de herdar traço de um avô (31/07/2026) — CORRIGE o bug acima e vira mecânica de jogo:** o bug era real (`BreedTraits` reconstruía um pai-que-é-filhote com `Generate(seed)`, traits fantasmas) — a pedido do usuário, em vez de só corrigir silenciosamente, virou uma mecânica: **chance MENOR (não a regra) de um traço vir de um AVÔ em vez do pai direto**, tipo um traço recessivo "pulando uma geração".
  - `CreatureInstance` ganhou 4 colunas (`ParentAGrandparentASeed`/`BSeed`, `ParentBGrandparentASeed`/`BSeed` — migration `AddGrandparentSeeds`), denormalizadas do PAI no momento da criação do filho (`parentA.ParentASeed`/`ParentBSeed` — **zero query nova**, já vinham carregados na entidade). Expostas em `CreatureDto`.
  - `TraitGenerator.BreedTraits` ganhou overload com `ParentAncestry(Seed, GrandparentASeed, GrandparentBSeed)` — resolve os traits REAIS de cada pai (`ResolveOwnTraits`: `Generate` se fresco, recomputa 1 nível se filhote) e, por "slot" (shimmer/cauda/dorsal/peitoral, 1x por lado), `EffectiveParentTraits` tem `GrandparentReachChance = 0.15` (mesma ordem do `RarityBiasStrength`) de chance de trocar o candidato desse lado por um dos avós (50/50 entre eles) em vez do pai. A sobrecarga antiga (3 seeds) virou um wrapper fino (`ParentAncestry(seed, null, null)`, reach=0) — **nenhum código existente mudou de comportamento**.
  - **Escopo deliberado:** resolve exatamente 2 gerações (pai real + avô real); bisavós usam `Generate(seed)` puro. A prévia (`ChildTierDistribution`/`traitDistribution`) não modela avós no cálculo fechado — só o resultado real (na coleta) incorpora o mecanismo; mesma simplificação já aceita pra correlação de cor na prévia.
  - Validado (`Vivarium.Simulation breed`): reach observado 14.9% vs. esperado 15.0% (medido direto no mecanismo interno — `EffectiveParentTraits`, não na saída ponta-a-ponta, que é ambígua já que o valor "próprio" de um pai bred já reflete os mesmos avós).
  - `generator.js` espelha 1:1 (mesmos salts) — crosscheck manual de 2.000 seeds com ancestralidade de avós, 0 mismatches (mesmo mecanismo de `Vivarium.Simulation breeddump`/`grandparentdump`).
  - Testes: `BreedTraitsTests.cs` (6 novos, incl. `EffectiveParentTraits`/`ResolveOwnTraits` diretos via `InternalsVisibleTo`) + `generator.test.js` (6 novos) + `BreedingTests.cs` (`FilhoteDeFilhote_DenormalizaOsSeedsDosAvosCorretamente`, cruza avós → filhote → neto, confirma a denormalização de 2 gerações ponta-a-ponta).
- **CI (30/07/2026):** `.github/workflows/ci.yml` — job `backend` (`dotnet build`+`dotnet test` via `Vivarium.slnx`, sem secrets — testes de API usam SQLite in-memory) e job `frontend` (`npm ci`+`npm run build`), em push/PR pra main/master. Zero CI existia antes disso.
- **Docker hardening (30/07/2026):** `src/Vivarium.Api/Dockerfile` — imagem final roda como usuário não-root (`appuser`), tem `curl` instalado só pra alimentar o `HEALTHCHECK` (bate em `/health` a cada 30s).
- **Testes novos (30/07/2026):** `PasswordHasherTests.cs` e `TokenServiceTests.cs` (antes só cobertos indiretamente via `AuthTests.cs`) — total foi de 91 pra 105 testes verdes.
- **Robustez do polling idle (30/07/2026, banner em destaque 31/07/2026):** `useGame.js`/`useBreeding.js` engoliam falha de heartbeat/refresh em silêncio — quem deixa a aba aberta como tela de fundo não percebia que parou de sincronizar (rede caiu, token expirou). Agora contam falhas consecutivas e expõem `syncError`; a partir de 2 falhas seguidas, `GameView` mostra uma **faixa de aviso em destaque** logo abaixo do topbar (`.sync-banner`, coral/`--coral`, ícone piscando) — não é mais um pill discreto que dava pra não notar. O texto sugere explicitamente **recarregar a página (F5)** e tem um botão "Recarregar agora" (`window.location.reload()`). Some no próximo sucesso. Teste E2E usa `cy.clock()`/`cy.tick()` pra simular as falhas consecutivas sem esperar os 60s reais (`cypress/e2e/sync-banner.cy.js`).

## 13. Estrutura da solution

- `Vivarium.slnx` — solution (.NET 10)
- `src/Vivarium.Core` — domínio e motor de geração (sem dependência de web/banco; testável isolado); entidades do schema da seção 9 em `Domain/`
- `src/Vivarium.Api` — ASP.NET Core minimal API + EF Core/Npgsql; `Data/VivariumDbContext` (índices, enums como string, seed de CurrencyType/HabitatType/Species) e migrations em `Data/Migrations`. `Endpoints/` (auth, game, market, item, breeding — ver seção 12), `Services/` (TokenService, PasswordHasher, GameService, MarketService, ItemService, BreedingService — todos devolvendo `Http/ServiceResult`, fase 4b). Connection string `Vivarium` (dev aponta pra localhost; produção via env var `ConnectionStrings__Vivarium` no Neon)
- `tests/Vivarium.Api.Tests` — testes de integração da API (WebApplicationFactory + SQLite in-memory)
- `frontend/` — React + Vite (stack da seção 10). **Arquitetura modular (29/07/2026)** — o antigo `App.jsx` monolítico (~960 linhas) foi quebrado em:
  - `src/lib/` — sem React: `api.js` (cliente HTTP), `generator.js` (port do motor de traits, verificado contra o C#), `fishRenderer.js` (desenho do peixe + ambiente do tanque), `format.js` (rótulos/formatação PT-BR), `tankMath.js` (cálculos derivados do tanque: sinergia, produção, ETA), `motion.js` (`reducedMotion`).
  - `src/hooks/` — `useGame` (tanque + heartbeat/refresh + userId), `useToast`, `useBreeding` (status da gestação, polling 30s — 8.8).
  - `src/components/` — primitivos reutilizáveis: `Coin`, `TraitRow`, `RarityBadge`, `ShimmerLabel`, `Toast`, `Modal` (casca de modal com Esc/backdrop), `FishCanvas` (1 peixe), `AquariumCanvas` (aquário animado + aura; prop `theme` — `"breeding"` tinge a água de rosa e troca as bolhas por corações sutis, `fishRenderer.js`).
  - `src/views/` — telas: `AuthView`, `GameView` (shell + tabs), `TankView`, `MarketView`, `StoreView`, `BackpackView`, `FishDetail`, `RarityGuide`, `BreedingView` (aba "Ninho" — 8.8).
  - `src/App.jsx` fica só com o gate de auth. Heartbeat a cada 60s + refresh do tanque a cada 30s (em `useGame`). `generator.js` é o mesmo port JS verificado do protótipo (traits derivados client-side do seed; rarity score vem da API). Dev: `npm run dev` (proxy pra API local). Deploy Cloudflare Pages: build `npm run build`, output `frontend/dist`, env `VITE_API_URL` apontando pro backend
  - **Identidade visual (v3, 30/07/2026 — de volta ao "aquário profundo" escuro):** o pivot claro/vibrante (v2, mesma data) foi revertido a pedido do usuário — voltou o tema escuro único original: fundo escuro (`--bg-top/mid/bottom` #04181f→#020d12), glass **escuro** translúcido (`--panel`/`--panel-strong`), sombras pretas profundas, acentos vibrantes sobre o escuro (aqua `--glow` #54e6d1, azul `--glow-2` #7ad3ff, coral, âmbar `--gold`). Tipografia editorial mantida: **Fraunces** (display) + **Hanken Grotesk** (UI). Design tokens no topo de `src/styles.css` (mesmos seletores da v2, só os tokens e os literais de superfície clara — cards, chips, fish-stage — voltaram pro escuro; um único tema, sem modo claro). Raridade: `--r-*` do CSS espelham as `BANDS` em `lib/fishRenderer.js` (comum #93a7b0, incomum #57b876, raro #4d8fe0, épico #a86ce4, lendário #f0b93b), claras o bastante pra ler no escuro. Arte do tanque em `fishRenderer.js`: `drawTankBackground`/`drawTankForeground` voltaram à água teal escura → verde-podre conforme a sujeira, raios de luz cáustica, vinheta preta nas bordas — aceitam um `theme` opcional (`"breeding"` tinge de rosa/roxo escuro + bolhas em coração pro Ninho, CLAUDE.md 8.8). Auth é um hero com aquário ambiente escuro atrás de um cartão de vidro escuro.
- **Testes do frontend (30/07/2026):** antes disso não existia nenhum — foco em não gastar tokens de agente validando UI manualmente (nem abrindo Chrome, ver seção de preferências do usuário) a cada mudança; agora basta rodar os comandos abaixo e ler o resultado.
  - **Unitários (Vitest):** `frontend/src/lib/*.test.js`, cobrem a lógica pura de `generator.js` (motor determinístico seed→traits, incl. teste de regressão via `toMatchSnapshot` — só atualizar o snapshot com `-u` quando a mudança no motor for intencional, e nesse caso o `TraitConfigVersion` do backend também precisa subir), `tankMath.js` (sinergia/produção/ETA) e `format.js` (rótulos PT-BR). `frontend/src/lib/vitest.setup.js` faz stub de `Path2D` (só existe no browser) pra permitir importar `fishRenderer.js` em ambiente Node. Rodar: `npm test` (uma vez) ou `npm run test:watch`.
  - **E2E (Cypress):** `frontend/cypress/e2e/*.cy.js` — sobe a build de produção (`vite preview`, porta 4173) e mocka toda a API via `cy.intercept` (fixtures em `cypress/fixtures/`), então roda sem precisar do backend/Postgres. Rodar: `npm run e2e` (builda se necessário, sobe o preview, roda os specs, mata o servidor — via `start-server-and-test`) ou `npm run cypress:open` pra depurar interativamente.
  - CI (`.github/workflows/ci.yml`) roda os dois: `npm test` e depois `cypress-io/github-action` (cuida das dependências do Linux e cache do binário) contra o preview.
- `tests/Vivarium.Core.Tests` — xUnit
- `tools/Vivarium.Simulation` — console de validação estatística dos pesos (`dotnet run --project tools/Vivarium.Simulation [N]`); modo `dump [N]` imprime traits canônicos por seed para verificar ports do motor; `economy`/`breed` testam renda e breeding isoladamente; `simulate` (30/07/2026) roda um jogador sintético por 120 dias gastando em filtro+upgrade+breeding ao mesmo tempo — ver seção 8.6
- `prototype/fish-composer.html` — protótipo visual standalone (Canvas); digite um seed ou busque por tier de brilho
