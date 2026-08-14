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

Tabela **v3 (12/08/2026)**: "Sem padrão" domina mais (76.2%) e há 11 tipos, os novos com pesos baixos de propósito (raro = valioso). Ocelo e Mármore são a caça de topo. Cada parte sorteia 1 padrão desta mesma tabela. **Degradê caiu de 0.6% pra 0.4%** (delta somado em "Sem padrão") como parte da mudança abaixo — pedido do usuário pra torná-lo mais raro e visualmente mais marcante.

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
- **Tamanho do padrão**: sorteio contínuo 0–100 (pequeno a grande), com peso maior no meio (distribuição normal), tiers extremos (muito pequeno <10 ou muito grande >90) contam como "raro" e entram no cálculo de rarity score.
- **Cor do padrão**: mesma paleta curada acima, mas nunca igual à cor de base da mesma parte (evita padrão invisível).
- **Opacidade do padrão**: 20–90%, sorteio uniforme; abaixo de 30% ou acima de 80% conta como raro no score.

**Degradê — mix de duas cores (12/08/2026):** só pra esse padrão, um subtrait novo (`GradientMix`) sorteia como a cor de base e a cor do padrão se misturam visualmente na parte — `BaseDominant` (base ocupa a maior parte), `Even` (mistura equilibrada, 50/50) ou `PatternDominant` (padrão ocupa a maior parte), pesos **45% / 10% / 45%**. Visualmente é um gradiente vertical cujo ponto de corte se desloca conforme o mix (`fishRenderer.js`, `drawPattern`); `patternSize` continua controlando só a suavidade da transição, independente do mix — são traits ortogonais, cada um com seu próprio salt (`{parte}_pattern_mix`), seguindo o princípio já documentado de "trait novo = salt novo, sem tocar nos existentes". Pedido explícito do usuário: o Degradê "merece destaque" — feio virar mais um padrão qualquer entre 11.
- **Regra de score assimétrica** (só o Degradê tem isso): em `Even`, as duas cores contam pro score, exatamente como já acontecia por padrão pra qualquer parte com padrão (cor de base + cor do padrão sempre são pontuadas independentemente). Em `BaseDominant`/`PatternDominant`, **só a cor dominante conta** — a minoritária é subtraída do score depois de já ter sido somada (`TraitGenerator.GeneratePart`/`BreedPart`, delta calculado à parte antes de somar em `score`, importa pra bater bit a bit entre geração fresca e mutação no breeding). Resultado: `Even` (10% de chance dentro do Degradê, ~0.04% de todas as partes — mais raro que Mármore) rende mais score que os assimétricos, recompensando genuinamente o resultado mais raro.
- Calibração (`Vivarium.Simulation`, 1M seeds): frequência real bate exatamente com o peso configurado; cortes de raridade (`RARITY_RANGES`/`BANDS`) praticamente não se moveram (5.34/7.45/9.78/13.89 vs. os já documentados 5.4/7.5/9.8/14.0, dentro da variação normal entre rodadas) — **não precisou recalibrar** o frontend.
- Guia in-game (`RarityGuide.jsx`) ganhou uma seção própria explicando o mecanismo, já que ele foge do padrão "1 trait, 1 tabela" do resto do sistema.

---

## 4. Regra de correlação (brilho do corpo → cor das partes)

Se o corpo saiu em Tier 2, 3 ou 4 (brilho vibrante, raro ou lendário), a tabela de peso de cor das partes é ajustada: a cor mais próxima do tom do brilho recebe **+15 pontos percentuais** de peso (renormalizando o resto proporcionalmente).

Exemplo: corpo saiu "Tier 3 — Preto absoluto" → peso de "Preto" nas partes sobe de 3% para ~18%, o resto da tabela é reduzido proporcionalmente. Isso cria a sensação de "conjunto raro combinando" sem eliminar a chance das outras cores aparecerem.

**Cor absoluta (visual, 08/08/2026, ideia do usuário):** quando cauda, dorsal e peitoral saem todas na MESMA cor (a mesma condição do bônus `sameColor3`, §8.6), o corpo — normalmente sempre cinza — passa a ser tingido com essa cor também (`fishRenderer.getBodySprite(tintColor)`, overlay sobre o gradiente cinza, mantém a textura de escama por baixo). Puramente visual — o score **não muda**, o bônus de conjunto já existe desde antes; isso só dá uma recompensa visual condizente com a raridade extra que o conjunto já rende. Sem correlação de brilho (Tier ≥2) ativa, a chance de sair um peixe "de cor absoluta" do zero é baixa (Σ p_cor³ sobre a paleta) — em torno de **3%** somando todas as cores, dominado pelas cores mais comuns (Laranja sozinha ≈1.06%, Branco puro sozinho ≈0.0001%). Cruzar dois pais com as 3 partes já na mesma cor no Ninho aumenta bastante essa chance (cada parte herda ~50/50 de um dos pais, então se os dois pais oferecem a mesma cor pra aquela parte, ela quase sempre "vence"), mas não garante — mutação (§8.8) sempre pode resortear do zero.

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

> ⚠️ **Gap real confirmado (10/08/2026): `TraitConfigVersion` nunca foi de fato incrementado.** `TraitConfigV1.Version` é uma constante fixa (`= 1`) e `TraitGenerator.Generate`/`BreedTraits` **lançam exceção** se receberem qualquer versão diferente de 1 — ou seja, não existe hoje um jeito de manter uma tabela de pesos "antiga" viva pros peixes que já nasceram com ela. Toda mudança de peso desde o lançamento (Raridade v2 29-30/07, novos padrões, etc.) já aconteceu sem bump de versão, violando a garantia documentada acima. Consequência real: os **traits visuais** de um peixe antigo (sempre recalculados ao vivo do `Seed`, nunca armazenados) mudam silenciosamente toda vez que os pesos mudam, mas o **`RarityScore`** (calculado uma vez na coleta e congelado no banco) fica desatualizado — o peixe passa a ser exibido/renderizado com um conjunto de traits que não bate mais com a raridade gravada. Achado ao investigar um relato do usuário: dois peixes "iguais" no tanque (um pai raro/9.7, um filhote lendário/14.9) — o filhote tinha herdado quase todos os traits do pai, e o pai **já renderizava visualmente como lendário**, só o `RarityScore` dele continuava congelado no valor de quando nasceu (antes da Raridade v2).
>
> **Correção aplicada (10/08/2026):** `tools/Vivarium.AdminReset` ganhou `diff-scores [email]` (só leitura, recalcula com o motor atual e compara com o gravado) e `fix-scores` (recalcula e sobrescreve `RarityScore` de toda criatura viva divergente — não mexe em `Seed`/ancestralidade, só sincroniza o número com o que já está sendo renderizado). Rodado uma vez em produção: 19 criaturas corrigidas (todas de uma única conta antiga, criadas antes da Raridade v2), delta líquido -62,6 (a maioria estava com score MAIOR que o real: `TraitConfigVersion`, não travando os pesos, deixa qualquer combinação de mudanças futuras derivar pra qualquer direção).
>
> **Isso VAI acontecer de novo na próxima mudança de peso/algoritmo** — a causa raiz (versionamento vestigial) não foi corrigida, só o sintoma. Até alguém implementar de verdade o versionamento (ex: `TraitWeightConfig` já existe no schema §9.2 mas nunca é lido pelo motor — `TraitConfigV1` é uma classe estática hardcoded, não uma linha de banco), a política operacional é: **depois de qualquer mudança que altere pesos/algoritmo de um trait já existente, rodar `dotnet run --project tools/Vivarium.AdminReset -- fix-scores` em produção** antes de considerar a mudança concluída — senão o campo `RarityScore` de peixes já existentes fica invisivelmente errado até alguém notar (como aconteceu aqui, dias depois).
>
> **Primeiro bump real de `Version` (1→2, 12/08/2026, mecanismo de mix do Degradê acima) — e um risco de outage descoberto no processo, não só na ferramenta admin.** Ao dar bump em `TraitConfigV1.Version` e rodar a suíte de testes da API, 17 testes quebraram com 500 — não era só `fix-scores`/`diff-scores`/`dump-traits` (AdminReset) que passavam `c.TraitConfigVersion` (o valor GRAVADO na linha, sempre 1) pro motor; **os próprios serviços da API em produção fazem o mesmo** (tanque, breeding, ranking, resolução de cor de cauda pra sinergia — qualquer lugar que deriva traits de uma criatura já existente). Como `Generate`/`BreedTraits` lançavam exceção pra qualquer `configVersion` diferente da atual, o primeiro bump de verdade quebraria **toda criatura já existente em produção** com erro 500 assim que o deploy saísse — até alguém rodar `fix-scores`, uma janela real de instabilidade pros jogadores, não uma falha só de ferramenta interna.
>
> **Correção mais profunda que só `fix-scores`:** o guard `if (configVersion != TraitConfigV1.Version) throw` nunca teve sentido prático — só existe UMA config hardcoded (`TraitConfigV1`), nunca houve suporte real a múltiplas versões (o comentário do parágrafo acima já apontava isso). Travar em qualquer mismatch virava uma mina que detonaria em TODO bump futuro, não só este. `TraitGenerator.Generate`/`BreedTraits` agora só lançam exceção pra `configVersion > TraitConfigV1.Version` (dado corrompido/impossível — não pode vir "do futuro"); qualquer versão igual ou anterior usa a config atual silenciosamente, sem erro. Isso fecha o risco de outage de vez, não só pra este bump — testes existentes (`VersaoDeConfigDesconhecida_Lanca`, que usa 999) continuam passando sem mudança, já que 999 > 2 sempre lança.
>
> `AdminReset -- fix-scores` também ganhou o fix que já estava previsto: passava a versão GRAVADA na linha (`c.TraitConfigVersion`) pro motor em vez da versão ATUAL (`TraitConfigV1.Version`) — com o guard relaxado isso não quebra mais, mas continuava semanticamente errado (recalculava com a config errada) e nunca atualizava `c.TraitConfigVersion` de volta, deixando o campo desatualizado pra sempre e expondo o mesmo tipo de bug de novo a cada rebalanceamento. Corrigido: `fix-scores`/`diff-scores`/`dump-traits` sempre usam `TraitConfigV1.Version` (a versão atual do motor) e `fix-scores` agora também grava `c.TraitConfigVersion` junto com o `RarityScore` corrigido. Ainda vale rodar `diff-scores` (auditoria) + `fix-scores` depois de qualquer deploy que mude pesos/algoritmo — só que agora é higiene, não uma corrida contra um outage.

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

- **Fórmula:** `coinsPorHora(score) = IncomeBasePerHour · exp(IncomeGrowth · (score − IncomeRefScore))` — **base 1.5** (era 1.7, reduzida de novo em 31/07 pra compensar o buff que o patamar de água deu à renda — ver abaixo), **growth 0.42** (era 0.49, reduzido em 06/08/2026 — ver justificativa abaixo), ref 4, **acima do score 14 (piso do Lendário) troca pra `IncomeLegendaryTaperGrowth` — ver taper abaixo, 12/08/2026**. Comum (score ~5) ~2.3/h, raro (início 7.5) ~6.5/h, épico (9.8–14.0) ~17/h no piso da faixa até ~100/h no topo, lendário (14+) de ~100/h (piso) a ~200/h no score máximo observado numa amostra de 1M seeds (~21) — antes do taper, essa mesma cauda extrema chegava a quase 2000/h. Topo íngreme dentro de uma faixa comprimida = lendário continua cobiçado, sem outlier de sorte desproporcional.
- **Growth reduzido de 0.49→0.42 (06/08/2026):** a faixa Épico (9.8–14.0, 4.2 pontos de score) é bem mais larga que Incomum (2.1) ou Raro (2.3) — com o crescimento exponencial, isso fazia a renda variar ~7.8x *dentro do próprio tier* (26/h no piso do épico a 201/h no topo, antes do ajuste), deixando runs de sorte curtos (poucos peixes coletados, 1-2 saindo "épico alto") desproporcionalmente fortes — usuário relatou 15 coletas com 3 épicos, 2 rendendo 140/h, e sentiu que ficou fácil demais chegar numa renda alta. Medido: essa combinação específica (≥3 épicos em 15 + ≥2 com score ≥13.26) tem chance conjunta ~0.1-0.2%, ou seja, sorte real, mas o teto do tier (201/h) já era alto o bastante pra um outlier raro desequilibrar a sensação de progresso. Reduzir o growth pra 0.42 corta o teto do épico pela metade (~100/h) quase sem afetar comum/incomum (score perto do `IncomeRefScore=4`, muda só ~7% no extremo da tabela) e mantendo o "jackpot" do lendário (ainda ~7.8x um épico no topo). Revalidado com `Vivarium.Simulation economy`/`simulate`: carteira dos 3 perfis (casual/ativo/dedicado) continua oscilando mas crescendo em 120 dias.
- **Taper do Lendário — comprime a cauda extrema, sem tocar no resto (12/08/2026):** o mesmo padrão de reclamação do ajuste de 06/08 apareceu de novo, agora um nível acima — usuário relatou um amigo pegando um lendário nos testes com renda "absurda" e temeu desbalanceamento. Investigado com uma amostra real de **1M seeds** (maior que os 100k já documentados em §5) via `Vivarium.Simulation dump`: o score máximo observado subiu pra ~21.1 (vs ~18.9 nos 100k), e com o growth uniforme (0.42) isso rendia quase **2000/h** no extremo — bem acima do "mais de 700/h" já documentado, e uma variação de **19.6x** só dentro do tier Lendário (piso 100/h). Pesquisado o padrão usado em jogos idle pra esse problema (soft cap contínuo — sigmoide/logístico ou piecewise com crescimento reduzido acima de um ponto, nunca um teto rígido nem manter exponencial puro) e testadas 6 curvas candidatas contra a amostra real antes de decidir. Escolhida a curva **piecewise**, mesma filosofia já usada em `IncomeWaterPlateau`/`FilterTaperExponent` (contínua, sem "penhasco", só muda acima de um ponto): `IncomeLegendaryTaperScore = 14.0` (exatamente o piso do Lendário — abaixo disso, ZERO mudança em Comum/Incomum/Raro/Épico) e `IncomeLegendaryTaperGrowth = 0.10` (usuário pediu a opção **conservadora**, entre 0.15 e 0.10 testadas, pra evitar disparo de farm de soft). Resultado: piso do Lendário intocado (100/h), teto no score máximo observado cai pra **~200/h** (era ~2000/h), variação interna do tier cai de 19.6x pra **~2x**. `IncomeCalculator.CoinsPerHour` (`src/Vivarium.Core/Gameplay/IncomeCalculator.cs`) ganhou o branch condicional; espelhado em `generator.js` (`CONFIG.income.taperScore/taperGrowth`). **Não precisa de backfill/legado:** `RarityScore` (cached por peixe) não mudou — só a fórmula que converte score→renda, e `coinsPerHour` nunca é persistido por peixe (sempre recalculado ao vivo do `RarityScore` a cada tick, `IncomeCalculator.CoinsPerHour`/`Accrue`) — todo peixe já existente (inclusive lendários já coletados) passa a render pela curva nova automaticamente no próximo tick, sem rodar nenhum script administrativo nem bump de `TraitConfigVersion` (esse só se aplica quando o motor de TRAITS/score muda, não quando só a fórmula de renda muda — ver o aviso de `TraitConfigVersion` mais abaixo). Validado com `Vivarium.Simulation economy`/`simulate`: economia continua saudável nos 3 perfis em 120 dias.
- **Sinergia por cor de cauda (29/07):** N peixes com a mesma cor de cauda no tanque → cada um multiplica a renda por `1 + SynergyPerMatch·(N−1)` com teto `SynergyMaxBonus` (0.15 / +80%). Ex.: 5 de cauda azul → +60% cada. Cria demanda por peixes específicos no mercado ("montar tanque temático") = uso inteligente das moedas. `GameService` deriva a cor via `TraitGenerator`; `IncomeCalculator.FishIncome`/`SynergyMultiplier`. O cliente exibe a sinergia no tanque (`generator.js: synergyMultiplier`).
- **Geração (29/07):** `GenerationIntervalMinutes = 25` (era 15) → mais lento + lendário ~1/mês pro jogador ativo (~2 sem. pro dedicado; sim: `Vivarium.Simulation economy`).
- **Fator água com patamar (31/07/2026):** `WaterFactor(maint) = 1` pra `maint ≥ IncomeWaterPlateau` (80%) — água "quase perfeita" não é mais punida, só abaixo do patamar é que dói. Abaixo de 80%: `(maint/80)^0.7` (mesmo expoente de antes, só reescalado — contínuo em 80%, sem "penhasco"). Ex.: 90% → 100% de renda (antes: 93%), 50% → 72% (antes: 62%), 15% → 30% (antes: 26%), 0% → 0%. Pedido do usuário: não faz sentido punir manutenção "quase perfeita" — só abaixo de 80 a água deveria começar a doer. Como isso é um buff em quase toda a faixa 0-100%, `IncomeBasePerHour` caiu de 1.7→1.5 (~-12%) pra manter o ritmo geral parecido com antes (mesmo princípio do ajuste 2.0→1.7 em 29/07 — ver `TickConfig` pros detalhes do cálculo). Validado com `Vivarium.Simulation simulate`: carteira ainda oscila mas cresce nos 3 perfis depois do ajuste.
- **Aviso ao comprar filtro com água já alta (31/07/2026):** `FILTER_WARN_THRESHOLD = 95` (`frontend/lib/tankMath.js`) — comprar filtro com água ≥95% não muda a renda (já está no patamar de 80+). `TankView`/`StoreView` interceptam a compra do item `filter_basic` nesse caso e mostram um `ConfirmModal` perguntando se o jogador quer mesmo gastar — evita desperdício sem bloquear a ação (o jogador pode confirmar mesmo assim, ex.: quer deixar guardado o filtro automático de qualquer forma).
- **Visibilidade da perda por água suja (31/07/2026):** `TankView` mostra um indicador `-X/h` (`.water-loss`, coral) colado no medidor de água sempre que o potencial a água cheia (`tankPotential`, já existia) supera a renda atual em mais de 0.05/h — antes só aparecia num tooltip discreto ("de X" pequeno no chip de renda, que continua existindo). O novo indicador fica ao lado do botão "Filtro · 20", pra comparar visualmente o custo do filtro com a perda acumulada sem precisar calcular nada.
- **Degradação escala com peixes (29/07, curva mais agressiva em 07/08, ponderada por raridade em 08/08):** `base·(1 + DegradationPerFishFactor·pesoTotal)` — k **0.10→0.30** (07/08/2026, a pedido do usuário: fazer o auto-filtro de 500 soft "se pagar" num prazo razoável). Payback do auto-filtro (supondo o jogador filtrar ao bater o patamar de 80%, `IncomeWaterPlateau`) caiu de ~9-12 dias pra **~3,5-7,3 dias** dependendo do tamanho do tanque (3/5/10 peixes). `pesoTotal` deixou de ser a contagem simples de peixes em 08/08/2026 — agora é a soma de `rarityScore/DegradationRarityRefScore` de cada um (ver §11, "Degradação ponderada por raridade"), então tanque rico/raro suja mais que tanque grande-mas-comum. Revalidado com `Vivarium.Simulation simulate`: carteira dos 3 perfis continua oscilando mas crescendo em 120 dias. *Nota de sustentabilidade original ainda vale:* isto é sabor/consequência de descuido, **não** o balanceador principal — mesmo ponderado, o upkeep de um tanque rico continua pequeno relativo ao bruto (ex: 25 raros ~7,4/h de upkeep vs ~216-294/h de renda bruta). A sustentabilidade de longo prazo depende dos **ralos de progressão** (aquários em tiers — Fase B; breeding — 8.8), não da manutenção.
- **Bug corrigido: cor de cauda de filhote divergia entre cliente e servidor (08/08/2026).** `GameService.TailColorOf` (usado só pra `SynergyMultiplier`, o bônus de sinergia por cor de cauda) cacheava por `seed` e sempre chamava `TraitGenerator.Generate(seed)` puro — correto pra peixe gerado normal, mas **errado pra filhote de breeding**, cujo trait real vem de `BreedTraits` (herança dos pais, não do seed sozinho). O cliente (`frontend/lib/generator.js: traitsOf`) já usava o motor certo (`breedTraits`) pra exibir a cor na tela, então tanque com filhotes tinha a cor exibida divergindo da cor usada pelo servidor pra calcular `coinsPerHour` — os dois caíam em grupos de sinergia diferentes mesmo com água a 100%. Sintoma relatado pelo usuário: "prejuízo mesmo com água 100%" (parecia bug de água, era bug de sinergia). Fix: cache trocou a chave de `seed` pra `CreatureInstance.Id`, e `TailColorOf` agora recebe a entidade inteira — se tem `ParentASeed`/`ParentBSeed`, resolve via `TraitGenerator.BreedTraits` com a ancestralidade completa (avós inclusive, mesmos parâmetros de `BreedingDefaults`); senão, `Generate(seed)` normal. `FishIncomeListAsync` passou a projetar a entidade completa (ids de pais/avós) em vez de só `{Seed, RarityScore}`, sem query extra. Commit `a1e0ca7`.
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
- **Revelação clique-a-clique pra Raro+ (08/08/2026, iterada 3x no mesmo dia a pedido do usuário):** `CollectCelebration` (score ≥ 7.5, mesmo corte de `BANDS`/§5 que já abre a celebração) monta o peixe **de verdade** na tela a cada toque, em vez de só revelar texto — corpo (sempre visível, é a "base") → brilho → cauda → dorsal → peitoral, com a raridade escondida atrás de "???" até a última parte. `fishRenderer.drawFish` ganhou um parâmetro `layers` opcional (`{shimmer,tail,dorsal,pectoral}`, default todas `true`) que desenha só um subconjunto das partes; `FishCanvas` ganhou `revealStep` (0–4) que resolve as camadas via `REVEAL_ORDER`. Callers existentes (`AquariumCanvas`, todo outro uso de `FishCanvas`) não passam `revealStep` e continuam desenhando o peixe completo — sem mudança de comportamento fora da celebração. Aplica tanto na coleta do tanque quanto no Ninho (`variant="breeding"`) — no Ninho só o FILHOTE passa pela revelação; os retratos dos pais e a seção de despedida continuam visíveis desde o início (não são o "mistério" do momento). Não se aplica a Comum/Incomum (frequentes demais, o suspense viraria fricção).
  - **Histórico da iteração (por que não é mais como foi descrito antes):** 1ª versão só Épico+ (9.8) com timer automático (900ms/passo) e texto revelando — usuário pediu estender a Raro+; 2ª pediu pra também valer no Ninho; 3ª — "não foi bem essa animação que pensei, quero que cada clique libere uma parte, e vá montando o peixe visualmente parte a parte" — trocou o timer automático por clique manual E trocou a revelação de texto por desenho real no canvas (daí o `layers` novo em `drawFish`). Testado ao vivo via `dev.cmd` a cada rodada (inclusive um teste completo de Ninho: iniciar cruzamento real, `POST /api/dev/breeding/finish` pra pular a gestação, coletar e clicar 4x).
- **Endpoints:** `GET /api/breeding`, `GET /api/breeding/quote`, `POST /api/breeding/start`, `POST /api/breeding/collect`.
- **Frontend:** aba "Ninho" (`BreedingView.jsx`) — picker de 2 peixes; ao selecionar os 2, uma **barra fixa no rodapé** (`.sticky-bar`) chama um **modal de prévia** (custo/gestação/chances/risco, via `/quote`) antes de confirmar; `AquariumCanvas` com `theme="breeding"` (tingimento rosado + corações) durante a gestação ativa; `CollectCelebration` ganhou `variant="breeding"` (mostrada **sempre** ao coletar um filhote, não só se raro — é um evento demorado que merece o momento). Fix de CSS: `.card-row` de ações (mochila) ganhou `flex-wrap` + botões mais compactos — estourava a borda do card com 3 botões. **(12/08/2026)** picker de peixes e `ParentPreviewCard` (prévia de confirmação) ganharam o selo `🐣 Filhote` (`.bred-tag`, mesmo padrão já usado em Mochila/Tanque/Mercado) quando `creature.isBred` — antes só aparecia o `FishCanvas` renderizado corretamente, sem indicação textual de que era um filhote.
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
  - **Ideia refinada (08/08/2026, não implementada):** comida vira um item ativo (comprado na loja, custo em soft) que o jogador literalmente "joga" no aquário — em vez de todo peixe ganhar o boost igualmente, os peixes **disputam** a comida, e só quem pega ganha um multiplicador temporário de produção (X% por Y minutos). O ganhador poderia ser decidido pela velocidade de nado (`swimSpeedOf(traits)`, já existe, derivada dos traits de movimento — cauda rápida = mais chance de chegar primeiro), com um componente de sorte ponderada pra não ficar 100% determinístico. O ponto central da mecânica: **o valor esperado da comida escala com a raridade de quem está no tanque** — um tanque só de comuns não compensa gastar comida (o boost sobre uma renda baixa não paga o custo do item), mas um tanque com um peixe raro/lendário torna a aposta valiosa (boost sobre uma renda já alta). Cria: (1) um sink ativo opcional (diferente da água, que é passiva), (2) tensão/expectativa a cada uso (não se sabe quem vai vencer), (3) mais um motivo pra montar tanques de peixes valiosos (reforça o mesmo princípio da sinergia de cor, §8.6). Reaproveitaria peças já existentes: `swimSpeedOf` (chance de vencer), `IncomeCalculator`/`coinsPerHourOf` (base do cálculo do boost, aplicado como multiplicador temporário — mesmo princípio da sinergia). Nada disso está implementado; fica registrado aqui pra quando a v2 for retomada.
  - **Refinamento 2 (mesma data):** ração **por tier de raridade** (Comum/Incomum/Raro/Épico/Lendário, mesmas faixas de `BANDS`/§5) em vez de uma ração genérica pra todo o tanque — só peixe do tier correspondente pode pegar aquela ração (ração Épica só compete entre os Épicos do tanque; se não houver nenhum peixe daquele tier, a ração não pode ser usada). Cada tier tem seu próprio preço, crescente com o tier (mesma lógica de "preço acompanha o valor esperado" já usada em `VendorCalculator`/filtros em nível). Isso resolve o problema de balanceamento do refinamento 1: em vez de o jogador **apostar** às cegas em qual peixe do tanque vai vencer (podendo desperdiçar em um comum), ele **escolhe** deliberadamente qual tier alimentar — vira uma decisão calculável (custo da ração Lendária vs. valor esperado do boost sobre a renda de um Lendário) em vez de um jogo de sorte com fio arriscado de "posso estar jogando dinheiro fora". A disputa por velocidade de nado continua existindo, só que **dentro do tier** (só importa quando há 2+ peixes do mesmo tier competindo pela mesma ração).

- **Sujeira visual do aquário (cocô dos peixes) — ideia discutida (08/08/2026), não implementada:** proposta original do usuário era cocô "de verdade" — cada peixe soltando cocô após X tempo, acumulando no substrato, dissolvendo depois de mais um tempo, e **esse acúmulo sendo a causa real** da degradação da água (o cascudo futuro limparia esse cocô como manutenção). Decisão tomada: separar a causa mecânica da representação visual, em vez de implementar como simulação real.
  - **Por quê:** a degradação da água já é server-side, calibrada por simulação (`Vivarium.Simulation economy`) e ponderada por raridade (§8.6, "Degradação ponderada por raridade") — trocar isso por uma simulação de cocô por peixe (spawn/acúmulo/dissolução com estado próprio) significaria reconstruir e recalibrar do zero um sistema econômico que já funciona, por um ganho essencialmente estético.
  - **Direção escolhida:** cocô **puramente decorativo**, sincronizado com o `murk` que já existe (mesmo padrão de `drawAlgaePatches`/`fishRenderer.js` — quanto pior a água, mais cocô visível no substrato), sem nenhum estado novo no servidor. A física (o que realmente degrada a água) continua exatamente como está hoje. Quando o cascudo existir, ele "come" os sprites de cocô visualmente enquanto por baixo dos panos continua só somando a `FilterCapacity` (mesmo hook já documentado acima) — o jogador vê uma causa e um efeito coerentes (peixe suja, cascudo limpa) sem a engine reinventar como a água realmente degrada.


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
>
> ⚠️ **Temporário (10/08/2026), mesmo espírito:** tempo de gestação do Ninho também dividido por **10** só pra fase de testes — `BreedingDefaults.BaseGestationHours` 6→**0,6h**, `MinGestationHours` 6→**0,6h**, `MaxGestationHours` 240→**24h** (`GestationGrowth`/`GestationRefScore` intocados — só a escala geral do tempo, não a curva). Comum quase instantâneo (~36min), lendário no teto continua sendo o mais lento proporcionalmente (~24h em vez de ~10 dias). **Reverter pra 6/6/240 antes de qualquer lançamento "de verdade"** (`src/Vivarium.Core/Gameplay/BreedingConfig.cs`) — não é mudança de design, é conveniência de QA, igual ao ajuste da geração de peixe acima.
>
> ⚠️ **Temporário (11/08/2026), foi além — gestação FLAT de 1h pra todo mundo:** a pedido do usuário, `MinGestationHours`/`MaxGestationHours`/`BaseGestationHours` = **1.0** pros três — a fórmula exponencial continua no código, mas o `Math.Clamp(Min, Max)` sempre devolve 1h já que Min==Max, então TODO casal (comum ou lendário) gesta em exatamente 1h agora. Motivo explícito do usuário: os aquários vão ser resetados no lançamento de qualquer forma, então vale a pena maximizar o volume de cruzamentos nesta fase pra achar bugs no fluxo (herança, risco de morte, avós, seguro/estabilizador) o mais rápido possível. **Reverter pra 6/6/240 antes de qualquer lançamento "de verdade"** (mesmo arquivo) — a curva de `GestationGrowth`/`GestationRefScore` não mudou, só restaurar Min/Max/Base já reativa a escala por raridade.

- **Geração mais lenta:** `HabitatDefaults.GenerationIntervalMinutes` 25→**60** (quase dobra). Cadência de lendário recalculada por simulação (`Vivarium.Simulation economy`): casual ~1 a cada 282 dias, ativo ~1/71 dias, dedicado ~1/35 dias (~1 por mês) — antes disso o "dedicado" já estava em ~2 semanas, rápido demais pro objetivo.
- **Gestação mais lenta (30-31/07/2026), depois corte assimétrico (06/08/2026):** `BreedingDefaults.BaseGestationHours` foi 8→24 (3x, anti-rush) e depois **24→6** (usuário achou 2 dias pra incomuns demais — ver §8.8 pro raciocínio completo do corte assimétrico, que subiu `GestationGrowth` 0.12→0.185 pra compensar e manter o topo do lendário quase intocado). `MinGestationHours` seguiu o mesmo padrão do Base em cada mudança (4→12→6).
- **Acelerar (rush) com premium:** `RushCalculator` (`src/Vivarium.Core/Gameplay/RushCalculator.cs`) — custo proporcional ao tempo restante, sem termo de raridade explícito (a gestação de peixes raros já é mais longa, então o custo de pular já escala com a raridade indiretamente via mais horas restantes). Fila: `0.15 premium/min` restante (60 min = 9 premium). Gestação: `2.0 premium/hora` restante (24h = 48 premium; teto de 240h = 480 premium). Só rush **total** no MVP (pula tudo de uma vez, não parcial).
- **Endpoints:** `POST /api/game/queue/{id}/rush` (fila) e `POST /api/breeding/rush` (gestação ativa) — debitam premium, zeram `ReadyAt`/`readyAt` pra agora, auditam `TransactionLog.TimeSkip` (novo valor no enum `TransactionType`). O custo de cada item/gestação já vem calculado nas respostas normais (`QueueItemDto.RushCostPremium`, `BreedingSlotDto.RushCostPremium`) — sem round-trip extra pra saber o preço antes de clicar.
- **Frontend:** botão "⚡ {custo}" (`.rush-btn`, roxo/epic — `--r-epico`) ao lado de itens da fila não prontos (`TankView.jsx`) e da gestação ativa (`BreedingView.jsx`). Chip de saldo premium "💎" no topbar (`GameView.jsx`) — a moeda premium nunca tinha aparecido na UI antes, sempre foi 0.
- **Gap real, não escondido:** não existe processador de pagamento integrado (Stripe ou similar) — então hoje **não há forma real de um jogador comprar premium**. O mecanismo de jogo está pronto e testado; falta só a ponte com dinheiro real, que é uma integração maior (conta de comerciante, webhook de confirmação, etc.) fora do escopo desta mudança. Pra testar localmente, `/api/dev/coins?currency=PREMIUM` (só em Development) credita premium — nunca existe em produção (mesma regra de todos os endpoints `/api/dev/*`).
- **Ninho: confirmação antes de gastar premium (10/08/2026).** Usuário reportou que o botão de acelerar (só o raio "⚡ {custo}") não deixava claro que gastava moeda premium — risco de clique acidental. Fix só no `BreedingView.jsx` (o de `TankView.jsx` continua como antes, não foi pedido): o botão agora mostra "⚡ Acelerar com 💎 {custo}" (custo destacado em dourado, `.rush-btn-premium`) e abre um `ConfirmModal` (`danger`, mesmo padrão de ações irreversíveis) antes de chamar `api.rushBreeding()` — mostra o custo e quanto tempo seria pulado (`timeLeft`).
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
- **Preço do upgrade dentro da faixa:** `CapacityBands.SmoothPrice(currentCapacity)` resolve `CapacityBands.BandFor` e usa a curva daquela faixa — cada faixa é independente, não uma extensão da anterior. Teto absoluto checado em `BuyAsync` antes do débito da carteira (`habitat.Capacity >= CapacityBands.MaxCapacity` → 400).
- **Custo de transição entre faixas (08/08/2026, a pedido do usuário — a progressão "estava linear demais"):** `CapacityBand.TransitionCost` — um custo bem acima da curva suave, cobrado ao cruzar o teto de uma faixa (ex: capacidade 5→6 entra no Aquário Grande). Valores calibrados via `Vivarium.Simulation economy`/`simulate`: Aquário→Grande **4000 soft** (~10x o último degrau suave do Aquário), Grande→Master **12000 soft** (~19x o último degrau suave do Grande) — ordem de grandeza acima de propósito, pra ser um "gate" de verdade, não mais um passo incremental.
  - **Achado real durante a calibração:** com esses valores, a política ingênua do "jogador sintético" de `Vivarium.Simulation simulate` (que sempre inicia uma nova gestação de breeding assim que pode pagar) nunca acumulava o suficiente pra trocar de faixa em 120 dias — nem o perfil "dedicado" (16h/dia). Causa: breeding consumia o caixa todo dia antes de sobrar o suficiente pro `TransitionCost`. Corrigido no simulador (não no jogo real) com uma heurística de "poupar pro upgrade": quando há transbordo de mochila persistente e a carteira ainda não alcança o preço da transição, o jogador sintético pausa novas gestações até juntar o valor — depois disso, os 3 perfis alcançam a capacidade máxima (15) dentro de 120 dias, com menos gestações no total (trade-off real entre os dois sinks, esperado). **Não é um comportamento imposto ao jogo real** — é só o simulador aprendendo a priorizar, do mesmo jeito que um jogador real racional faria.
- **Upgrade e troca de aquário viraram produtos SEPARADOS na loja (08/08/2026, a pedido do usuário — trocar de aquário mudava o preço do MESMO botão "Comprar", sem aviso claro do salto):** o catálogo (`VivariumDbContext`, seed de `ItemDefinition`) ganhou 2 itens novos — `aquario_grande` (Id 6, 4000 soft) e `aquario_master` (Id 7, 12000 soft) — ao lado do `tank_upgrade` (Id 3) já existente, que passou a servir SÓ a curva suave dentro da faixa atual (nunca mais cobra `TransitionCost`).
  - `ItemService.TankItemState(key, habitat)` centraliza o estado (preço/dono/bloqueado) dos 3 produtos: `tank_upgrade` fica `Locked` (`LockedReason` explicando o motivo) ao atingir o teto da faixa atual ou o teto absoluto; `aquario_grande`/`aquario_master` ficam `Locked` até a capacidade atingir o `MinCapacity` da faixa de destino, viram compráveis exatamente nesse teto, e `Owned` depois de comprados (não recompra). `ItemDto` ganhou `Locked`/`LockedReason` (ambos com default, não quebra clientes antigos).
  - `CapacityBands` ganhou `SmoothPrice(int)` (curva pura, usada pelo `tank_upgrade`) separado de `PriceForUpgrade(int)` (curva-ou-transição, usado só pelo `Vivarium.Simulation`, que continua raciocinando em termos de "próxima compra" sem se importar com produtos de loja).
  - Migration de dados `AddSeparateTankProducts` (só `InsertData`, sem mudança de schema).
  - **Frontend:** `StoreView.jsx` perdeu o `ConfirmModal` de aviso (não fazia mais sentido — o produto já é nomeado e mostra o preço fixo direto no card, sem susto de "mesmo botão, preço 80x maior"); itens com `locked=true` aparecem com opacidade reduzida, ícone 🔒 e o motivo do bloqueio, sem botão de compra.
- **`TankResponse`** ganhou `CapacityBandName` (nome da faixa atual, pra UI) e `CapacityBandDegradationFactor` (pro cliente exibir o impacto de água por peixe corretamente sem duplicar `CapacityBands` em JS — `generator.js: waterDegradationPerFishPerHour(score, bandFactor)` recebe o fator já resolvido do backend).
- **Frontend:** `TankView.jsx` mostra o nome da faixa no chip de capacidade; `StoreView.jsx` descreve os 3 níveis de filtro e os 3 produtos de tanque (upgrade suave + 2 trocas de faixa); `FishDetail.jsx` passa `tank.capacityBandDegradationFactor` pro cálculo de impacto de água exibido.
- **Testes:** `GameplayTests.cs` (`CapacityBandsTests` — resolução de faixa; `FiltroComCoberturaParcial_BeneficioTaperaSuavemente`, `FaixaDeCapacidadeMaior_DegradaMaisRapido`) + `ItemTests.cs` (catálogo com 5 itens, filtro nível 2 não bloqueado por já ter o nível 1, teto de capacidade retorna 400).
- **Hook do cascudo (não implementado):** "cascudo" é um **peixe novo** (uma espécie/criatura futura, nadaria no tanque como qualquer outro peixe) — **não confundir com o filtro automático** (`auto_filter`/`auto_filter_2`/`auto_filter_3`), que é equipamento comprado na loja. Comentário em `GameService.ApplyTickAsync` documenta que o bônus de limpeza passiva do cascudo entraria somado a `FilterCapacity` (ou como multiplicador extra no `filterFactor`) — mesma fórmula, mais um termo, sem estrutura nova; só a origem do bônus é diferente (peixe vivo, não item). **Lado visual (08/08/2026, branch `fish-visual-polish`):** o aquário ganhou manchas de alga na decoração que crescem com a água suja (`drawAlgaePatches`, `fishRenderer.js`, reaproveita o mesmo `murk` que já esverdeia a água) — quando o cascudo existir, o efeito dele seria reduzir/limpar essas manchas por onde passa, reaproveitando esse mesmo helper em vez de criar sistema visual novo (TODO já deixado no código).

### 8.16 Ranking global + visita a aquário de outro jogador (09/08/2026)

Gancho social simples: dois rankings globais (raridade total do tanque e renda/hora), sem opt-out (decisão explícita do usuário — MVP simples, mesmo princípio de exposição de username já usado no Mercado/transferência), e a possibilidade de "visitar" o aquário de outro jogador — o mesmo `AquariumCanvas` animado, só leitura.

- **`LeaderboardService`** (`src/Vivarium.Api/Services/LeaderboardService.cs`): `AllAquariumSnapshotsAsync` carrega, em duas queries batched (habitats "Aquarium" de todos os usuários, depois todas as criaturas desses habitats), um snapshot em memória por usuário: soma de `RarityScore` (raridade total) e `IncomeCalculator.TankRatePerHour` (mesma fórmula que já alimenta `coinsPerHour` no `/api/game/tank`). **Sem cache/job** — recalcula a cada request; pro tamanho atual do jogo (beta) é rápido o bastante; se crescer, cachear por alguns minutos é a melhoria natural (não implementada agora, otimização prematura pro estágio atual).
- **`TailColorResolver`** (`src/Vivarium.Api/Services/TailColorResolver.cs`): a lógica de `TailColorOf` que já existia dentro de `GameService` (resolve a cor de cauda certa mesmo pra filhotes de breeding, via ancestralidade — fix do bug de renda `a1e0ca7`) foi extraída pra uma classe estática compartilhada, reaproveitada tanto por `GameService` quanto por `LeaderboardService` — sem duplicar a correção.
- **Top 100 + posição própria:** `GetLeaderboardAsync(userId, metric)` ordena o snapshot pela métrica pedida (`rarity`/`income`, 400 se outro valor) e devolve os 100 primeiros; se o usuário que pediu não estiver entre eles, calcula a posição real dele no ranking completo (`SelfOutsideTop`, separado da lista principal).
- **Visita só-leitura:** `GetSpectatorTankAsync(username)` busca o aquário do usuário pelo `Username` e devolve `Username`/`MaintenanceLevel`/`CapacityBandName`/`Creatures` (via `CreatureDto.From`, mesmo mapeamento do tanque próprio — filhotes renderizam certo, `ParentASeed`/avós já vêm inclusos). **Não roda `ApplyTickAsync`** — visitar não deveria mutar o estado de outro jogador; a água exibida pode ficar levemente desatualizada até o dono logar de novo, aceitável numa visão só-leitura.
- **Endpoints:** `GET /api/leaderboard/{metric}`, `GET /api/leaderboard/visit/{username}` (ambos autenticados — só é preciso estar logado, não ter consentimento de quem está sendo visitado).
- **Frontend:** nova aba "🏆 Ranking" (`RankingView.jsx`) com 2 sub-abas (Raridade/Renda), lista das 100 entradas com a própria linha destacada (`.is-self`) e a posição própria separada abaixo quando fora do top 100; botão "Visitar" troca a view pro modo espectador (`AquariumCanvas` com `interactive={false}`, sem nenhuma ação de jogo disponível) com um botão "← Voltar ao ranking".
- **Testes:** `LeaderboardTests.cs` — ordenação por cada métrica, métrica inválida 400, usuário fora do top 100 aparece em `SelfOutsideTop` com o rank certo (testado inserindo 100 usuários "fantasma" direto no banco via navegação EF, sem passar pelo registro HTTP — mais rápido), visitar username inexistente 404, visitar retorna as criaturas certas incluindo um filhote de breeding (`ParentASeed`/`ParentBSeed` presentes no DTO).

### 8.17 Limpeza Automática (VIP) + Sensor de Qualidade da Água (12/08/2026)

Resolve uma lacuna real do VIP: mesmo com coleta automática (8.3), o jogador ainda precisava entrar no jogo periodicamente só pra clicar em "Filtro" — nenhum mecanismo restaurava a água sozinho. Isso contradizia a premissa central do jogo ("deixado aberto como tela de fundo", §0) e esvaziava parte do valor do VIP. Cogitamos fazer o próprio Filtro Automático (8.15) manter a água sempre em 100%, mas isso zeraria o peso da raridade na degradação (`fishFactor`, calibrado em 8.6/8.15 pra "tanque rico suja mais") sempre que a cobertura fosse total — decisão explícita de **não** ir por aí; a automação entrou como comportamento de tick, não como mudança na fórmula de degradação.

- **Limpeza Automática (grátis, qualquer VIP ativo):** no mesmo tick que já roda a coleta automática (`GameService.ApplyTickAsync`, hoisting de `vipOnline` reaproveitado pros dois), o servidor compra sozinho um Filtro (`filter_basic`, preço lido do `ItemDefinition`, nunca hardcoded) assim que `MaintenanceLevel` cruza um gatilho — **0% por padrão**, sem precisar de nenhuma compra. Roda **depois** de `AccrueIncomeAsync`: a renda do intervalo que passou já foi calculada em cima da água real daquele período, a limpeza só afeta o tick seguinte — mesmo raciocínio causal já usado pra compra manual ("tick roda antes, pra degradação pendente ser aplicada primeiro").
- **Sensor de Qualidade da Água:** item permanente, **por aquário** (`Habitat.HasWaterSensor`, não UserInventory — não faz sentido acumular quantidade, mesmo motivo de `tank_upgrade`/`aquario_grande`), compra única que libera um **slider livre** (passos de 1%) pra escolher o gatilho de 0 até `TickConfig.WaterSensorMaxTriggerPercent` (80%, == `IncomeWaterPlateau` — acima disso a renda média não sobe mais, só cresce a frequência/custo das compras automáticas). Preço cresce com a faixa do aquário no momento da compra (`CapacityBand.WaterSensorPrice`, mesmo padrão de `TransitionCost`) — placeholder a calibrar via `Vivarium.Simulation`: Aquário 800 soft, Aquário Grande 2000 soft, Aquário Master 4500 soft. Só tem efeito com VIP ativo — comprável sem VIP, mas fica dormente até assinar (mesmo espírito do Filtro Automático).
- **Análise matemática que guiou o design:** o gatilho ótimo **não é sempre o teto** — simulando o líquido (renda média − custo em soft de filtros automáticos) por gatilho, um tanque modesto de raros teve pico em 60% (não 80%, onde comprar filtro com tanta frequência já custa mais do que a renda extra vale), enquanto um tanque rico de lendários continuou subindo até 80%. Por isso o controle é um slider livre, não uma escada de tiers empurrando sempre pro topo — é uma escolha estratégica real, calculável, que depende da composição do tanque de cada jogador.
- **Novo `ItemCategory.WaterSensor`:** especial-casado em `ItemService.ListAsync`/`BuyAsync` (`WaterSensorState`, mesmo padrão de `TankItemState` pros itens de aquário) — preço nunca vem do cliente, sempre resolvido a partir de `CapacityBands.BandFor(habitat.Capacity)`. `ItemDefinition` seed `Id=8, Key="water_sensor"` (migration `AddWaterSensor`, junto com as 2 colunas novas em `Habitat`).
- **Endpoint de configuração** (não é compra): `POST /api/game/water-sensor/trigger` — 400 se `HasWaterSensor=false` ou `percent` fora de `[0, WaterSensorMaxTriggerPercent]`; sempre resolve o habitat a partir do usuário autenticado (nunca aceita um id vindo do cliente).
- **Segurança revisada antes de fechar** (pedido explícito do usuário): preço sempre server-side; ownership sempre via JWT, nunca por id do cliente; VIP-gating sempre consultado no banco (`HasActiveVipAsync`), nunca confiado do cliente; saldo insuficiente nunca compra e nunca fica negativo (`if (wallet is null || wallet.Amount < price) return;`); spam de requisição não multiplica compras automáticas (a checagem `MaintenanceLevel > trigger` bloqueia re-compra até o tempo real degradar de novo — mesma propriedade que já protege o resto do tick preguiçoso, §8.6); toda compra automática audita `TransactionLog` igual a uma manual; a limpeza só roda no habitat tipo Aquário (nunca no Ninho, que já não passa por `ApplyTickAsync`, §8.8) — garantido por construção, já que `FindHabitatAsync` só resolve `HabitatType.Code == "Aquarium"`. Corrida de dupla compra (double-spend) reaproveita a mesma concorrência otimista (`xmin`) já usada em `Habitat`/`WalletBalance` — não testável em SQLite (mesma limitação de sempre, §12.1) mas coberta em produção (Postgres) pelo mesmo mecanismo de todo o resto da loja.
- **Frontend:** `StoreView.jsx` — card do Sensor no catálogo normal; depois de comprado, o botão "Comprar" vira um slider (`WaterSensorSlider`, salva com debounce de 400ms via `POST /api/game/water-sensor/trigger`, nota "Só tem efeito com VIP ativo" quando aplicável). Card do VIP atualizado explicando a Limpeza Automática grátis. `TankView.jsx` ganhou um chip `🤖 X%` perto do medidor de água, visível só com VIP ativo. `HowItWorksGuide.jsx` (seção VIP) explica os dois mecanismos.
- **Testes:** `WaterSensorTests.cs` (18 casos — compra por faixa, dupla compra, validação de gatilho, isolamento entre usuários, limpeza automática respeitando o gatilho configurado, sem saldo não compra e não fica negativo, sem VIP nunca limpa mesmo com sensor configurado, offline não limpa) + `frontend/cypress/e2e/water-sensor.cy.js` (5 casos E2E — não executado neste ambiente por falta de acesso de rede ao binário do Cypress, mas segue 1:1 o padrão já validado de `store.cy.js`/`vip.cy.js`).

### 8.18 Pai preso no Ninho por falta de espaço (12/08/2026)

Achado via relato real de usuário: um pai sobrevivente à gestação sem vaga no tanque/mochila ficava parado dentro do próprio habitat de reprodução pra sempre — invisível pro jogador, sem nenhum mecanismo de recuperação mesmo depois de abrir espaço (o Ninho nunca passa pelo tick normal do jogo, §8.8). Duas camadas de correção, a pedido do usuário ("solução mais simples: só permitir a coleta quando houver espaço"):

- **Prevenção:** `BreedingService.CollectAsync` agora exige espaço pro pior caso (filhote + os 2 pais sobrevivendo, 3 vagas no total entre tanque+mochila) ANTES de mexer em qualquer coisa — mais conservador que o estritamente necessário (às vezes um pai morre e sobraria vaga), mas garante que ninguém fica preso.
- **Resgate (defesa em profundidade):** `GameService.GetTankAsync` ganhou `RescueStrandedBreedingParentsAsync` — roda a cada carregamento do tanque, procura qualquer criatura do usuário estacionada num habitat tipo Breeding sem estar referenciada por uma gestação em andamento, e tenta mover pro tanque/mochila assim que houver espaço. Cobre tanto casos futuros (defesa extra, mesmo com a prevenção acima) quanto peixes JÁ presos de antes dessa correção.
- **Testes:** `Collect_SemEspacoPraFilhoteEOsDoisPais_Bloqueia` (regressão da prevenção) + `Tanque_ResgataPeixePresoNoNinhoSemGestacaoAtiva` (regressão do resgate, simula o estado preso direto no banco).

### 8.19 Registro de cruzamentos + chance/origem de cada atributo (12/08/2026)

Pedido do usuário: consultar o histórico de cruzamentos já feitos, e ver — no momento da revelação do filhote — a chance que cada atributo tinha de sair e se veio de herança (de qual pai) ou de mutação, "pra esclarecer eventuais dúvidas" sobre sorte/azar. Surgiu de uma investigação real (usuário achou a herança "aleatória demais"; várias consultas diretas ao banco confirmaram que a engine estava correta — casos "impossíveis" eram sempre explicáveis via mutação genuína ou o mecanismo de avô, nunca bug).

- **Histórico (`GET /api/breeding/history`):** reaproveita os dados que `BreedingSlot` já guarda — nada novo além de `ParentADied`/`ParentBDied` (bool?, gravados na coleta; antes só existiam na resposta HTTP momentânea de `CollectAsync`, nunca persistidos). `BreedingService.GetHistoryAsync` devolve as últimas 50 gestações coletadas do usuário, mais recente primeiro, com pais/filhote completos (`CreatureDto`, inclui seeds de avós). Frontend: botão "📜" no Ninho (`BreedingHistory.jsx`, modal com `flex-wrap` — testado em 375px sem overflow horizontal) — lista cada gestação com os 3 peixes (pais + filhote), selo 🕊️ pro pai que não sobreviveu, data e custo.
- **Chance e origem no reveal (`CollectCelebration.jsx`):** os fatores de `rarityBreakdownOf`/`bredRarityBreakdown` já carregavam `probPct` (raw, sem peso) — só não era exibido. Cada `attrLine` ganhou `probPct` (probabilidade CONJUNTA do grupo — cor × padrão quando patternado — calculada multiplicando as frações, não somando; pontos já são `-log10(prob)` então somar pontos ↔ multiplicar probabilidades são equivalentes, mas o peso do `shimmerScoreWeight` quebraria essa equivalência pro corpo, por isso a implementação multiplica `probPct` direto em vez de derivar da soma de pontos) e `source` (`"parentA"` / `"parentB"` / `"grandparentA1/A2/B1/B2"` / `"mutation"`).
- **De onde vem `source`:** `effectiveParentTraits` (`generator.js`) passou a devolver `{ traits, source }` em vez do objeto de traits solto — `source` é `"own"` (pai direto) ou `"grandparent1"/"grandparent2"` (mecanismo de avô, §8.8, residual desde o mesmo dia). `bredRarityBreakdown` combina isso com `inheritOrMutate`'s `{mutated, fromA}` pra rotular cada fator: mutação vence tudo (`"mutation"`); senão, o lado vencedor (`fromA`) determina se foi o pai direto ou um avô daquele lado. Exibido como selo (`"🧬 Herdado do Pai A"`, `"🎲 Mutação (não veio de nenhum pai)"`) só quando `variant="breeding"` — peixe comum do tanque não tem "pai" pra rotular.
- **`GrandparentReachChance` — trajetória completa do dia:** 0.15 → 0.03 → 0.01 → **0.001** (residual mínimo, decisão final do usuário: "deixar as chances como se fossem peixes gerados do zero", mas sem remover o mecanismo por completo). Com 8 sorteios independentes por cruzamento (4 slots × 2 lados), a chance de pelo menos 1 traço vir de avô caiu de ~72.8% (no valor original) pra ~0.8% (`1-(0.999)^8`) — de "domina a maioria dos cruzamentos" pra "praticamente nunca, mas ainda existe". `MutationChance` também caiu no mesmo dia (0.08→0.04) pelo mesmo motivo (herança mais confiável em cruzamentos multi-geração, importante pro custo/risco de cruzar Lendários no lançamento).
- **Testes:** unitários (`generator.test.js`) ajustados pra nova forma `{traits, source}` de `effectiveParentTraits`; E2E novo (`breeding-flow.cy.js`) confirma que a chance e o selo de origem aparecem no reveal de um filhote de verdade (com `parentASeed`/`BSeed`).

### 8.19.1 Investigação da precisão de `ResolveOwnTraits` — mantido como está, decisão documentada (12/08/2026)

Continuando a investigação do §8.19: um caso concreto (relatado com print pelo usuário, conta `EoNeng`) mostrou a cauda do filhote rotulada "Herdado do Pai B" com uma cor que o Pai B, na tela dele, **não tem**. Investigação (reprodução direta com os seeds reais) confirmou a causa: `TraitGenerator.ResolveOwnTraits` (usado quando um pai é ele mesmo um filhote) reconstrói os avós DESSE pai sempre como frescos (`Generate(seed)` puro) — mesmo quando esses avós são eles próprios filhotes com traits reais diferentes. `traitsOf(pai)` (a tela do próprio peixe) já usa os 6 campos completos do PAI (`Parent[A|B]Seed` + os 4 `Parent[A|B]GrandparentSeed`); `ResolveOwnTraits`, chamado de dentro do cruzamento do FILHO, só recebia 3 desses 6 campos.

**A correção "óbvia" (usar os 6 campos do pai, threading extra pela struct `ParentAncestry`) foi implementada, testada (21 testes verdes em `Vivarium.Core.Tests`) e depois **revertida antes do deploy** — motivo:

Corrigir a precisão de "valor do pai usado no cruzamento do filho" exige que o servidor use, no nascimento do filho, dados que **não cabem no filho** (o schema já limita a exatamente 2 gerações por criatura — CLAUDE.md §7/§9.3, decisão deliberada, documentada desde 31/07/2026). Se o `RarityScore`/traits do FILHO forem calculados com o pai resolvido em profundidade total (6 campos), mas o FILHO só consegue guardar 3 desses 6 campos pra uso futuro — então, quando ESSE filho crescer e virar pai de um neto, a reconstrução de "o valor do filho" (tanto na tela dele quanto dentro do cruzamento do neto) **não vai reproduzir** o valor que realmente foi usado pra calcular o `RarityScore` dele no nascimento. Isso reintroduziria, um nível mais fundo, exatamente a classe de bug já sofrida duas vezes neste projeto (`RarityScore` gravado divergindo do que é renderizado — ver §10 "Gap real confirmado 10/08" e o bug do `FishCanvas` 10/08) — mas agora silenciosa e sem ferramenta de correção (`fix-scores` já existe pro caso antigo, não pra este).

**Decisão:** manter `ResolveOwnTraits` como está (2 gerações reais — pai + avô — e bisavós tratados como frescos, exatamente como já documentado em §7 pra o mecanismo de avô). O rótulo "Herdado do Pai B" no reveal (§8.19) significa "herdado da LINHAGEM do Pai B, pela mesma matemática de seed usada em todo o motor" — não uma promessa de "idêntico ao que a tela do Pai B mostra agora", quando o Pai B tem 2+ gerações de ancestralidade própria (o caso raro que expôs isso). Extensão real do schema (armazenar profundidade 3, `bisavós` reais) resolveria de vez, mas o problema recursivo continua (o bisavô de amanhã vira o "pai com ancestralidade incompleta" de depois-de-amanhã) — não é uma correção pontual, é escolher até onde a "genética" é rastreada com precisão, trade-off já aceito no design original. Sem mudança de comportamento nesta entrada — só a decisão e o porquê documentados, pra não reabrir a mesma investigação do zero numa sessão futura.

### 8.19.2 Traits congelados no nascimento — fim definitivo do bug de profundidade de ancestralidade (13/08/2026)

A "decisão de manter como está" do §8.19.1 durou menos de um dia: no mesmo 13/08/2026, um novo cruzamento real (`marco`) reproduziu a mesma classe de bug — filhote com atributo "herdado" que o pai, na própria tela, não exibia — junto de um bug novo (o resultado REAL do filhote, não só o texto, saía com score bem abaixo dos dois pais; causa: `ApplyPenalty` do anti-duplicação podia inverter o lado favorecido pela raridade, corrigido separadamente no mesmo commit). O usuário perguntou diretamente: "se eu só aumentar a profundidade guardada, o problema não volta a acontecer assim que passar dessa profundidade nova?" — resposta correta é sim, e foi o gatilho pra abandonar a abordagem de "reconstruir sob demanda com profundidade limitada" (§8.19.1) de vez, em vez de esticá-la de novo.

**Mudança de arquitetura:** os traits de um peixe deixam de ser recalculados a cada exibição (do `Seed`, com até 2 gerações de ancestralidade denormalizada) e passam a ser **calculados uma única vez, no nascimento, e congelados** em `CreatureInstance.TraitsJson` (novo campo, `text`/JSON manual — mesmo padrão de `TraitWeightConfig.ConfigJson`). Cruzar um filhote passa a **ler** o `TraitsJson` já resolvido de cada pai (uma leitura direta) em vez de reconstruir a partir do seed dele — como nenhuma reconstrução acontece além de um hop (pai→filho), o limite de profundidade deixa de existir: não importa quantas gerações de breeding já aconteceram, cada peixe sempre lê o valor real e definitivo do pai direto.

- **Motor (`TraitGenerator.BreedTraits`):** nova assinatura recebe `CreatureTraits ownA, ownB` (já resolvidos) em vez de `ParentAncestry` (seeds + seeds de avô). `ParentAncestry`, `ResolveOwnTraits`, `EffectiveParentTraits` e o mecanismo de avô (`GrandparentReachChance`, já residual em 0.001 — §8.8) foram **removidos por completo**, não só desativados — decisão do usuário via pergunta direta (opção "remover de vez" vs. "adaptar"), já que o mecanismo existia só pra cobrir o caso que a arquitetura nova elimina estruturalmente.
  - Ganhou também um `IReadOnlyList<TraitSourceEntry>` de retorno — um registro por slot (`shimmerTier`, e `color`/`pattern` de cada parte) indicando `ParentA`/`ParentB`/`Mutation`, calculado uma vez no nascimento em vez de re-derivado toda vez que a UI precisa mostrar "de onde veio" (§8.19).
- **Schema:** `CreatureInstance` ganha `TraitsJson` (sempre preenchido daqui pra frente) e `BreedingSourceJson` (só filhotes NOVOS — peixes já existentes não têm a origem por trait, só os valores finais congelados). `Seed`/`ParentASeed`/`ParentBSeed`/os 4 campos de avô **continuam gravados**, mas viram puramente históricos/auditoria — não são mais lidos pra derivar nada.
- **Auditabilidade preservada (resposta a uma preocupação do usuário: "isso abre brecha pra manipulação de peixes?"):** como `Seed` + `ParentAId`/`ParentBId` (FKs reais) continuam existindo, dá pra verificar a cadeia INTEIRA, sem limite de profundidade — `Vivarium.AdminReset -- audit-ancestry` percorre todas as criaturas em ordem de criação e confirma que `TraitsJson` bate com `Generate(seed)` (peixe fresco) ou `BreedTraits(seed, traits-do-pai-A-já-verificados, traits-do-pai-B-já-verificados)` (filhote) — mais forte que a auditoria antiga, que era limitada a 2 gerações.
- **Backfill (`Vivarium.AdminReset -- backfill-traits`):** preenche `TraitsJson` de toda criatura já existente, processando em ORDEM DE CRIAÇÃO e usando o motor NOVO em cascata (peixe fresco → `Generate`; filhote → `BreedTraits` com os traits do pai JÁ CALCULADOS nesta mesma passada, via dicionário em memória por Id). Isso é mais preciso que simplesmente "congelar o que já estava sendo exibido" — corrige de vez qualquer divergência de profundidade acumulada, já que agora cada filhote deriva do valor real do pai sem limite de gerações. Também resincroniza `RarityScore`/`TraitConfigVersion` no mesmo passe (mesmo espírito do antigo `fix-scores`, que foi removido — perdeu o sentido, já que não há mais "recalcular pra comparar", só "ler o que já está congelado").
- **Frontend (`generator.js`), simplificação grande:** `traitsOf(creature)` vira `return creature.traits` — zero cálculo. `rarityBreakdownOf(creature)` só recalcula probabilidade/pontos em cima dos valores JÁ RESOLVIDOS (sem RNG, sem seed), com `source` lido de `creature.breedingSource`. Removidos por completo: `breedTraits`, `resolveOwnTraits`, `effectiveParentTraits`, `newDuplicationStreak`, `inheritOrMutate`, `weightedPickBiasedTowardRare`, `restrictTable`, `weightOf` — toda a réplica JS do motor de herança deixou de ser necessária pra EXIBIR um peixe (o motor JS que sobra, `generateTraits(seed)`, serve só pra prévia de um seed avulso; a prévia do Ninho antes de confirmar, `traitDistribution`/`breedingPreview`, usa os traits reais já resolvidos dos pais, cálculo fechado sem RNG). **Maior ganho colateral:** não existe mais a obrigação de manter dois motores (C# e JS) bit-a-bit sincronizados pra sempre — o cliente nunca mais deriva um resultado de breeding, só exibe o que a API manda.
  - Todo componente que desenhava um peixe reconstruindo um objeto PARCIAL (`{seed, isBred, parentASeed, ...}`) — o mesmo padrão que já tinha escondido bugs reais 2x antes (`FishCanvas` 10/08 e 12/08/2026) — foi corrigido pra sempre passar a criatura COMPLETA: `FishCanvas` passou a receber `creature` inteiro (não mais seed/parentSeed/avós soltos).
- **`CreatureDto`** ganhou `Traits`/`BreedingSource` (desserializados de `TraitsJson`/`BreedingSourceJson`); `Seed`/ancestralidade continuam expostos (histórico/curiosidade na tela).
- **Ordem de deploy obrigatória** (schema + dado antes do código que passa a exigir o dado): migration → `backfill-traits` em produção → `audit-ancestry` (confirma zero divergência) → deploy backend → deploy frontend. Rodar backend novo antes do backfill quebraria toda leitura de criatura (`TraitsJson` nulo). **Gap real encontrado depois (§8.22.1, mesmo dia): o backend ANTIGO continua rodando durante a janela até o deploy novo terminar (minutos, não instantâneo) — qualquer peixe criado nesse intervalo nasce com `TraitsJson` nulo mesmo com o backfill já feito ANTES. Rodar `backfill-traits` de novo DEPOIS do deploy backend concluído fecha essa janela (idempotente pros já corretos).**

### 8.20 Coleta automática VIP travava com o tanque cheio + mochila 50→100 (12/08/2026)

Bug real corrigido, relatado pelo usuário (VIP ativo, aba aberta, fila cheia mesmo assim): `GameService.CollectAllReadyAsync` (a coleta automática só-VIP, chamada no tick — §8.3) só colocava peixes no TANQUE e **parava** (`break`) assim que ele enchia — diferente da coleta MANUAL (`CollectInternalAsync`), que sempre cai pra mochila quando o tanque está cheio. Resultado: um VIP com o tanque cheio ficava com a fila **permanentemente travada** (nunca mais coletava sozinho, mesmo com espaço de sobra na mochila) até abrir espaço manualmente — o oposto do que a coleta automática deveria garantir. Corrigido: agora, com o tanque cheio, os itens prontos vão pra mochila (mesma regra da coleta manual); só para de verdade quando tanque **e** mochila estão cheios.

De brinde, a pedido do usuário: `HabitatDefaults.BackpackCapacity` 50→100 — a mochila estava enchendo mais rápido do que dava pra esvaziar via mercado/vendor, e ficou ainda mais relevante depois do fix acima (mais peixes vão parar lá quando o tanque está cheio). Só a constante (`TickConfig.cs`) — o resto do sistema (loja, mercado, `/api/game/backpack`) já lê a capacidade de lá, sem hardcode em outro lugar.

Teste de regressão (`GameTests.Vip_Online_TanqueCheio_ColetaAutomaticaCaiParaMochilaEmVezDeTravar`): VIP online, tanque cheio (capacidade 3) com 2 itens extras prontos na fila — confirma fila vazia e os 2 excedentes na mochila, não travados.

### 8.21 Anti-duplicação de pai + piso de mutação no breeding (13/08/2026)

Duas mecânicas novas no motor de herança, pedidas pelo usuário depois de observar que o cruzamento atual permitia um filhote "clonar" quase todos os atributos do mesmo pai, e que mutação podia sair pior que os dois pais (sem coerência com o risco assumido no Ninho).

**Anti-duplicação:** os 7 "slots" com viés de raridade (tier de brilho, cor e padrão de cauda/dorsal/peitoral) eram sorteados de forma independente — nada impedia o filhote de puxar quase todos do MESMO pai. Agora, `TraitGenerator.DuplicationStreak` (uma instância nova por cruzamento, threaded pela mesma ordem em que os slots já eram computados) conta quantos slots CONSECUTIVOS já vieram do mesmo pai sem mutar — quanto maior a sequência, menor a chance do PRÓXIMO também vir dele: `penalty = min(AntiDuplicationMaxPenalty, 1 - AntiDuplicationDecay^streak)`, empurrando o threshold de herança pra longe do lado que já vem ganhando. Mutação reseta a sequência (já quebra a "clonagem" sozinha). Constantes calibradas por simulação: `AntiDuplicationDecay = 0.55`, `AntiDuplicationMaxPenalty = 0.75` (nunca vira 100% determinístico). Movimento (velocidade/amplitude) fica de fora — já é 50/50 puro sem viés de raridade, decisão de escopo já documentada.

**Piso de mutação:** hoje mutação (4% de chance por trait) era um sorteio 100% livre pela tabela de pesos — podia sair MAIS COMUM (mais fraco) que os dois pais. Regra nova (definida com precisão só depois de 2 rodadas de correção do usuário): quando um trait sofre mutação, o resultado nunca pode ficar mais comum que o pai MAIS FRACO (mais comum) dos dois — só pode empatar com ele ou ficar mais raro. **Não** depende dos pais serem iguais nem é um "salto pro extremo" — é só uma cláusula de piso, calculada como `floorWeight = max(peso do valor do pai A, peso do valor do pai B)`, restringindo a tabela (`WeightedTable.Restrict`) antes de sortear, com leve viés extra a favor do raro dentro do que sobra (`WeightedTable.PickBiasedTowardRare`, força `MutationRarityBiasStrength = 0.15`, mesma calibração já usada na herança). Exemplos validados em teste (`PisoDeMutacao_NuncaSaiMaisComumQueOPaiMaisFraco`): Preto+Preto (ambos 3%) → só Preto ou Branco; Azul+Vermelho (20%/18%) → só exclui Laranja (22%, mais comum que o piso Azul); Azul+Preto (20%/3%) → piso AINDA é Azul (o mais fraco), não Preto (o mais raro) — só exclui Laranja de novo.

**Auto-limitado por design — vale pros 7 slots, inclusive tier:** o piso só exclui valores MAIS COMUNS que o pai mais fraco. Quando o pai mais fraco já É o valor mais comum da tabela (ex: tier "Sem brilho", 78% de peso — o caso mais frequente do jogo), não existe nada "mais comum" pra excluir, então a regra não muda nada nesse cruzamento — mutação continua 100% livre, igual antes. Isso permitiu aplicar a MESMA regra ao tier de brilho sem caso especial (sem risco elevado de inflação de Lendário via o par mais comum do jogo).

**Efeito sobre o pior caso:** herança pura já garante o filhote com o valor exato de um dos pais (nunca pior que o mais fraco); o piso só impede que MUTAÇÃO (a única fonte capaz de ir abaixo disso) faça pior. Não é "sempre melhora" — é "nunca piora abaixo do que herança pura já garantiria".

**Impacto econômico medido (pedido explícito do usuário) — `Vivarium.Simulation mutationfloor` (novo modo, 13/08/2026):** simula 2.000 indivíduos por 20 gerações de cruzamento aleatório (filhote de hoje vira pai amanhã), comparando trajetória COM e SEM o piso (mesma semente de aleatoriedade nos dois lados). Resultado real: RarityScore médio da população estabiliza em torno de **+2,2% acima do baseline sem piso** na 20ª geração (crescimento desacelera de 0,133 pontos na 1ª metade das gerações pra 0,008 pontos na 2ª — converge, não dispara); % Lendário na população oscila em 0–0,8% nos dois cenários, dentro do ruído estatístico normal, sem sinal de inflação. Dado esse resultado, fechado em **100% de garantia** (sem dial residual) — o piso já é auto-limitado o bastante, e o efeito medido é pequeno e estável.

`FabricaDeLendarios_NaoInflacionaAcimaDoBaseline` (teste de regressão já existente, ≤1%) continua passando com os parâmetros de produção reais (agora incluindo piso+anti-duplicação na chamada). `RetencaoDeLendario_ComAvosNaoLendarios_AindaAltaMasMenorQuePaisFrescos` precisou de recalibração: quando os DOIS pais já compartilham o valor MAIS RARO da tabela (Lendário), uma mutação nesse slot não tem pra onde ir (nada é mais raro) — só pode sair Lendário de novo, então a retenção sobe pra perto de 100% (99,9%+) nesse cenário específico, efeito esperado e correto do piso, não regressão.

**Compatibilidade:** `TraitGenerator.BreedTraits`/`InheritOrMutate`/`ChildTierDistribution`/`ResolveOwnTraits` ganharam os 3 novos parâmetros (`mutationRarityBiasStrength`, `antiDuplicationDecay`, `antiDuplicationMaxPenalty`) — passados explicitamente pelos chamadores de produção (`BreedingService`, `TailColorResolver`, `Vivarium.AdminReset`), nunca lidos direto de `BreedingDefaults` dentro do motor (mantém `Vivarium.Core.Generation` desacoplado de `Vivarium.Core.Gameplay`, mesmo padrão já usado por `mutationChance`/`rarityBias`). A sobrecarga simples de 6 argumentos (usada só por testes legados) passa `mutationRarityBiasStrength: -1` — um sentinela negativo que desliga o piso por completo, preservando o sorteio livre de sempre pra quem não conhece o mecanismo novo. `generator.js` espelha tudo 1:1 (`restrictTable`, `weightedPickBiasedTowardRare`, `newDuplicationStreak`), com o mesmo sentinela negativo.

⚠️ **Ajuste temporário de QA no mesmo dia:** `BreedingDefaults.RestHalfLifeDays` 5.0→**0.2** (25x mais rápido, ~4h48min de meia-vida em vez de 5 dias) — a pedido do usuário, só pra acelerar os testes desta rodada. Mesmo espírito do `GenerationIntervalMinutes`/gestação já temporariamente ajustados — **reverter pra 5.0 antes de qualquer lançamento "de verdade"**.

### 8.21.1 Bug crítico corrigido no mesmo dia: anti-duplicação podia INVERTER o pai favorecido pela raridade (13/08/2026)

Poucas horas depois do deploy de §8.21, o usuário relatou (conta `EoNeng`, print do "Registro de cruzamentos") vários filhotes nascendo com `RarityScore` bem ABAIXO dos dois pais (ex: 8.1+6.3→3.3; 9.1+6.3→4.1; 6.3+7.7→3.3) — o oposto do que herança deveria produzir na maioria das vezes.

**Causa raiz:** `DuplicationStreak.ApplyPenalty` multiplicava o `threshold` de herança INTEIRO pela penalidade (`threshold * (1 - penalty)` quando o lado A vem ganhando a sequência) — sem nenhum limite. Como `RarityBiasStrength` é deliberadamente sutil por design (o `threshold` raramente passa de ~0.55-0.6, mesmo em pares bem díspares — só no caso mais extremo do jogo, Lendário×Sem-brilho, chega a ~0.71), bastava **1 ou 2 heranças seguidas do MESMO pai** — natural e esperado quando esse pai já tem os traços mais raros, exatamente o cenário que a raridade deveria recompensar — pra a penalidade (que sobe rápido: `1 - 0.55^1 = 0.45` já no primeiro "ganho" repetido) derrubar o threshold abaixo de 0.5, INVERTENDO o lado favorecido. Ou seja: a anti-duplicação, pensada só pra reduzir "clonagem" de identidade de pai, brigava ativamente com o mecanismo de raridade sempre que o pai mais raro também "ganhava" vários slots seguidos (o caso comum) — empurrando sistematicamente o filhote pro pai MAIS FRACO. Confirmado matematicamente (não só por suspeita): com o código antigo, `ApplyPenalty(0.71)` já caía pra `0.391` com uma sequência de tamanho 1.

**Correção:** `ApplyPenalty` agora só pode encolher o `threshold` até um "cara ou coroa" neutro em 0.5 — nunca ultrapassa pro lado oposto. Quando o lado que vem "ganhando" a sequência JÁ é o menos favorecido pela raridade (aconteceu por sorte no sorteio, não por viés), a penalidade continua livre pra empurrar além de 0.5 sem restrição (não há sinal de raridade forte sendo contrariado nesse caso). `generator.js` espelhado 1:1 no mesmo commit.

**Testes de regressão** (`BreedTraitsTests.cs`/`generator.test.js`): testam `DuplicationStreak.ApplyPenalty`/`applyPenalty` diretamente (classe interna exposta como `internal` só pra teste, mesmo padrão já usado em `ResolveOwnTraits`/`EffectiveParentTraits`) — confirmado que revertendo o fix, o teste falha exatamente como o bug real se manifestava (`threshold` caindo de 0.71 pra 0.391/0.178 com sequência de 1-10). `Vivarium.Simulation mutationfloor` re-rodado após o fix: impacto do piso de mutação continua estável (+2.1% vs +2.2% antes, dentro da margem de simulação), e a população em geral pontua mais alto nos dois cenários (com/sem piso) — confirma que o bug estava derrubando scores de forma ampla, não só em casos extremos.

**Consequência descoberta na hora seguinte (mesmo dia): `RarityScore` desatualizado pros filhotes nascidos ANTES do fix — mesma classe de bug já documentada em §10 (`TraitConfigVersion` nunca bumpado).** Usuário relatou (conta `marco`) um peixe filhote "mudando de cor" (nadadeira ficou amarela) e a renda do tanque parecendo errada. Causa: traits de filhote nunca são salvos — sempre recalculados ao vivo do seed+ancestralidade a cada exibição — então corrigir o algoritmo de herança muda a APARÊNCIA renderizada de peixes JÁ NASCIDOS, mas o `RarityScore` (calculado uma única vez, na coleta, e congelado no banco) não se atualiza sozinho. Como a renda (`coinsPerHour`) é derivada do `RarityScore` gravado, isso também deixava a renda do tanque incoerente com a raridade real exibida. `diff-scores` (sem filtro de usuário) confirmou: **54 criaturas divergentes em 4 contas** (`marcospdn` 31, `pintinhopiu` 5, `EoNeng` 11, `marco` 7) — `fix-scores` rodado em produção pra sincronizar (delta total 84,4 pontos). **Política operacional já documentada em §10 reforçada por este incidente:** depois de qualquer mudança que altere o algoritmo de herança/mutação (não só pesos), rodar `fix-scores` em produção é parte OBRIGATÓRIA do deploy, não um passo opcional posterior — o corte de `ApplyPenalty` desta seção deveria ter incluído esse passo no mesmo commit/deploy, não só depois que o usuário notou.

### 8.21.2 "Filhote de elite com score abaixo do pai mais fraco" — investigado, não é bug (13/08/2026)

Usuário fazendo teste de estresse cruzando peixes de altíssima raridade (marcospdn, pares Lendário×Lendário score ~19-25) reportou um filhote com score menor que o pai mais fraco. Investigação com os `TraitsJson`/`BreedingSourceJson` reais (não simulação — os 15 últimos cruzamentos coletados da conta): **7 de 15 saíram abaixo do pai mais fraco**, mas com quedas pequenas (-0.14 a -1.79 num score ~20-25) — nada parecido com o colapso catastrófico do bug de inversão da anti-duplicação (§8.21.1, já corrigido, que derrubava score pra perto do baseline populacional).

**Causa raiz:** esses pais de elite são quase monocromáticos/mono-padrão (as 3 partes na mesma cor e/ou padrão) — exatamente o que rende o **bônus de conjunto coeso** (§5.1: mesma cor nas 3 partes +2.0, mesmo padrão nas 3 +2.5). Herança por slot é independente (cor da cauda vem de um pai, padrão da dorsal pode vir do outro, etc.) — nada agrupa "todos os slots do mesmo pai" de propósito (a anti-duplicação, §8.8, só reduz a chance de clonagem total, não impede mistura parcial). Um filhote pode perfeitamente puxar cor de A e padrão de B, produzindo uma combinação que não é tão coesa quanto NENHUM dos dois pais sozinho — perdendo o bônus de conjunto que ambos os pais tinham (total ou parcialmente).

**Não é bug:** a única garantia já documentada (§8.8) é POR TRAIT — "herança pura garante o filhote com o valor exato de um dos pais; o piso de mutação impede que mutação fique pior que o mais fraco". Nunca foi prometido "score TOTAL do filhote ≥ score do pai mais fraco" — isso exigiria ou copiar um pai inteiro (elimina a variação que torna breeding interessante) ou um piso pós-cálculo arbitrário. O efeito só fica visível com frequência em cruzamentos de elite porque é justamente ali que o bônus de conjunto pesa mais no score total.

**Decisão do usuário:** manter como está — documentar o mecanismo (esta entrada) em vez de mudar o motor. Sem mudança de código.

### 8.22 Opt-out de coleta/limpeza automática de VIP + "peixe novo" na Mochila (13/08/2026)

Resolve uma lacuna real: coleta automática (§8.3) e Limpeza Automática (§8.18) de VIP eram sempre ligadas, sem opção de desligar — quem prefere o momento de revelação manual (`CollectCelebration`, §8.19) a cada peixe, ou quer economizar soft evitando compra automática de filtro num momento específico, não tinha escolha. Junto, resolvido o "sumiço silencioso": peixe que aparece sozinho na Mochila via coleta automática nunca passava pelo momento de revelação — o jogador só descobria via contagem, sem saber qual peixe era novo.

- **`Habitat.AutoCollectEnabled`/`AutoCleanEnabled`** (bool, default `true` — preserva o comportamento de sempre pra quem nunca mexeu no toggle). Migration `AddAutoToggleAndIsNew`: `defaultValue: true` explícito no `AddColumn` (o valor gerado automaticamente pelo EF seria `false`, que desligaria os dois recursos silenciosamente pra TODO habitat já existente — corrigido manualmente antes de aplicar). `GameService.ApplyTickAsync` passou a checar cada toggle antes de chamar `CollectAllReadyAsync`/`ApplyAutoCleanAsync` (além do check de VIP+online já existente). Configurável mesmo sem VIP ativo — só tem EFEITO com VIP (mesmo espírito do Sensor de Qualidade da Água, §8.18).
- **`CreatureInstance.IsNew`** (bool, default `false`) — só `true` quando a criatura nasce pela coleta AUTOMÁTICA (`CollectAllReadyAsync`); coleta manual e breeding sempre gravam `false`, já que o clique do jogador (ou a celebração do Ninho) já É o momento de revelação. `CollectOne` ganhou o parâmetro `isNew` (default `false`) só pra explicitar isso no call site da coleta automática.
- **`POST /api/game/toggles`** (`{autoCollectEnabled, autoCleanEnabled}`) e **`POST /api/game/creatures/{id}/mark-seen`** (zera `IsNew`, ownership checado por `OwnerId`, nunca confia no cliente) — endpoints novos em `GameEndpoints.cs`. `TankResponse`/`CreatureDto` expõem os campos novos.
- **Frontend:** `StoreView.jsx` ganhou `AutoToggles` (2 checkboxes simples, sem debounce — clique único, diferente do slider do sensor) dentro do card VIP, salvando na hora via `api.setToggles`. `BackpackView.jsx`: peixe com `isNew` mostra selo "🆕 Novo" e a silhueta (`.fish-silhouette`, `filter: brightness(0) saturate(0)`) — clicar chama `revealNew` (atualização otimista local + `api.markSeen`, com fallback pra `refresh()` completo se der erro) em vez de abrir o detalhe; segundo clique (já revelado) abre normalmente.
- **Bug real encontrado e corrigido no processo (pré-existente, não desta feature):** `AuthView.jsx` renderiza um aquário decorativo (`demoFish`, 6 peixes fake) no fundo da tela de login — desde a refatoração de traits congelados (§8.19.2, mesmo dia), `traitsOf(creature)` parou de ter fallback pra `generateTraits(seed)` quando `.traits` está ausente, e o `demoFish` nunca tinha esse campo. Resultado: a tela de login quebrava (`Cannot read properties of undefined (reading 'movement')`) de forma intermitente (a race entre o crash assíncrono do `requestAnimationFrame` do canvas e as asserções do teste explica por que só ~2 de 8 testes de um mesmo spec falhavam, não todos). Corrigido preenchendo `demoFish` com `generateTraits(BigInt(seed))` de verdade. Achado ao rodar a suíte e2e completa depois desta feature — lição: qualquer objeto "peixe" fabricado fora da API (não só em specs de teste, também em código de produção como esse) precisa de `.traits` desde a mudança de 13/08/2026.
- **Testes:** `TogglesTests.cs` (7 casos — default ligado, desligar cada toggle impede a ação correspondente mesmo com VIP+online, coleta manual não marca `IsNew`, `mark-seen` funciona e é ownership-checked) + `toggles.cy.js`/extensão em `backpack.cy.js` (revelar peixe novo).

### 8.22.1 Gap real no deploy de 13/08/2026: 1 peixe criado com `TraitsJson` nulo na janela migration→backend (13/08/2026, mesmo dia)

Usuário relatou (conta `marco`) um peixe comum (score ~5.1) "sumido" — não aparecia nem no tanque nem na mochila. Investigação (`CreatureInstance` com score próximo, checando `HabitatId`/`SoldAt`/`TraitsJson`): achada a criatura `#1405`, criada `2026-08-13 14:47:04`, sem `ParentAId`/`ParentBId` (peixe fresco, não filhote), **`TraitsJson` nulo** — único caso em 1040 criaturas (`count-null-traits`, varredura completa).

**Causa raiz:** a mesma classe de risco já documentada em §8.19.2 ("ordem de deploy obrigatória: migration → backfill → audit → deploy backend → deploy frontend"), mas um ângulo que a ordem documentada não cobria — o intervalo de tempo ENTRE aplicar a migration (coluna `TraitsJson` já existe no banco) e o deploy do backend NOVO terminar (código que efetivamente preenche a coluna na coleta) não é instantâneo (nesse deploy, ~4min de rebuild Docker + restart). Durante essa janela, o backend ANTIGO continuava rodando e recebendo requisições reais — `CollectOne` da versão antiga não sabia que `TraitsJson` existia, então qualquer peixe coletado nesse intervalo foi inserido com o valor padrão da coluna nullable: `NULL`. `CreatureDto.From` já tratava esse caso sem quebrar (`c.TraitsJson is not null ? ... : null`), mas no cliente `traitsOf(creature)` (`return creature.traits`) devolvia `null`, e qualquer leitura de `.tail`/`.shimmerTier` nesse peixe quebraria a renderização — daí ele "sumir" de qualquer lista que tenta desenhá-lo.

**Correção:** rodado `backfill-traits` de novo em produção — idempotente por design (mesma cascata determinística já documentada em §8.19.2; as 1039 criaturas já corretas recalculam pro MESMO valor, só a `#1405` realmente muda de `null` pra um valor real). `audit-ancestry` confirmou 0 divergências nas 1040 depois. Nenhuma migration nova, nenhum deploy — só rodar a ferramenta que já existe.

**Lição pro processo (ainda não automatizada):** peixes podem nascer DURANTE a janela migration→backend em qualquer deploy que adicione uma coluna preenchida na criação (não só `TraitsJson` — qualquer campo futuro no mesmo padrão). A mitigação real seria rodar `backfill-traits` (ou o equivalente da vez) uma SEGUNDA vez, DEPOIS do backend novo estar no ar, não só antes — cobre tanto os dados pré-existentes (backfill de sempre) quanto qualquer criação que escapou pela janela. Vale adicionar esse passo extra na sequência de deploy documentada em §8.19.2 na próxima vez que uma migration desse tipo (coluna preenchida na criação, não só schema) for feita.

### 8.23 Caixa de entrada — IMPLEMENTADO (14/08/2026)

Resolve as duas dores documentadas na sessão de planejamento original: (1) admin agora pode avisar/recompensar jogadores em massa ou por lista de usernames; (2) peixe comprado no Mercado ou recebido via "Transferir" **não aparece mais direto no tanque/mochila** — vira uma entrada pendente na Caixa de Entrada, só entregue de verdade (tanque se houver espaço, senão mochila) quando o jogador clica em "Resgatar".

- **Schema:** `InboxMessage` (1 por envio administrativo — título/corpo/público/recompensa opcional em moeda; campos de recompensa em item, `RewardItemDefinitionId`/`RewardItemQuantity`, existem no schema mas ficam **dormentes** — sem UI admin nesta leva, preparados pra quando a loja de itens premium existir) + `InboxEntry` (1 por (destinatário, evento) — `Kind`: `AdminMessage`/`MarketPurchase`/`DirectTransfer`; `ReadAt`/`ClaimedAt` nullable, `ClaimedAt == null` = pendente). `CreatureInstance` ganhou `PendingInboxClaim` (bool) e `OriginalOwnerId` (FK `required`, "primeiro dono" imutável, preparado pro futuro suporte a troca de username — guarda o Id, nunca o nome, mesmo princípio já usado no `SellerName` do Mercado). Migration `AddInboxAndOriginalOwner` fez backfill retroativo de `OriginalOwnerId = OwnerId` pros ~40 peixes já existentes em produção (decisão confirmada: dono atual vira "primeiro dono" retroativo).
- **Mercado/Transferência:** `MarketService.BuyAsync`/`GameService.TransferAsync` não checam mais espaço do destinatário no momento da ação — a compra/transferência sempre funciona (dado saldo/posse ok); em vez de colocar o peixe direto no tanque/mochila, marcam `PendingInboxClaim = true` e criam um `InboxEntry`. A checagem de espaço migrou inteira pro momento do **resgate** (simplificação de UX intencional, não regressão).
- **6 bloqueios pra criatura pendente:** enquanto `PendingInboxClaim = true`, o peixe some da Mochila (`BackpackQuery`), não pode ser retransferido, relistado no Mercado, usado como pai de breeding (`GetQuoteAsync`/`StartAsync`), nem vendido ao NPC vendor.
- **Ações do jogador:** resgate individual (`ClaimAsync`) ou em massa (`ClaimAllAsync`, sequencial no backend — nunca em paralelo, mesma razão de sempre com `xmin`), "Ler tudo" (`MarkAllReadAsync`), "Apagar mensagens lidas" (`ClearClaimedAsync` — só remove entradas com `ClaimedAt` preenchido, nunca uma recompensa em aberto mesmo que já lida). Itens resgatados **continuam visíveis**, marcados, até serem explicitamente apagados.
- **Admin:** `POST /api/admin/inbox/send` — manda pra todos os usuários ou uma lista de usernames; username inexistente na lista **não bloqueia o envio** (manda pros que existem, loga os não encontrados via `ILogger` e devolve `notFoundUsernames` na resposta pro admin ver na hora). Nova seção no `AdminPanel.jsx` existente.
- **Endpoints:** `GET /api/inbox/`, `POST /api/inbox/{id}/claim`, `POST /api/inbox/claim-all`, `POST /api/inbox/mark-all-read`, `POST /api/inbox/clear-claimed`, `POST /api/admin/inbox/send`.
- **Frontend:** nova aba "📬 Caixa" no topbar (badge com contagem de pendentes, mesmo padrão visual do selo "🆕" da Mochila), `InboxView.jsx` (cards por tipo de entrada — mensagem admin vs. entrega de peixe), `useInbox.js` (poll 30s, dado compartilhado entre o badge e a view pra não duplicar polling).
- **Testes:** 17 casos novos em `InboxTests.cs` (fluxo completo compra→inbox→claim, transferência→inbox→claim, os 6 bloqueios, admin broadcast/lista/username inválido, recompensa credita carteira, `OriginalOwnerId` imutável através de 2 saltos de posse, claim-all misto, mark-all-read, clear-claimed preserva pendentes) + `TransferTests.cs`/`MarketTests.cs`/`BackpackTests.cs` reescritos pro novo fluxo — 279 testes de backend verdes (174 API + 105 Core). `frontend/cypress/e2e/inbox.cy.js` (7 casos) — suíte e2e completa (25 specs, 117 testes) verde.
- **Ainda não exposto na UI:** `OriginalOwnerId` é capturado e preenchido em toda criação de peixe, mas não aparece em nenhum DTO/tela ainda (decisão de escopo — expor exigiria `.Include` em dezenas de call sites de `CreatureDto.From`, frágil demais pra um campo que hoje é só preparação arquitetural; fica pra quando "criado por" for de fato pedido).
- **Backlog relacionado (não implementado, sem desenho ainda):** comentários no aquário visitado (Ranking → "Visitar", §8.16) — precisa de moderação básica antes de ir pra produção.

### 8.24 Perfil do jogador + "esqueci minha senha" (14/08/2026) — IMPLEMENTADO

Editar email/senha a partir do ícone de conta (👤) + fluxo completo de redefinição de senha por email. Primeiro recurso do jogo que precisa mandar email de verdade — não existia nenhuma infra de envio até aqui.

- **Provedor escolhido: Resend** (API HTTP, não SMTP) — decisão deliberada porque a VM Oracle só libera saída em 22/80/443 hoje (`deploy/README.md`); uma chamada HTTPS não esbarra em bloqueio de porta SMTP (Oracle Free Tier historicamente restringe saída 25/587/465). `IEmailSender` (`src/Vivarium.Api/Services/IEmailSender.cs`) é genérico (não sabe nada de "reset de senha"), pra qualquer feature futura reusar. `ResendEmailSender` só é registrado se `Resend:ApiKey` estiver configurada (Program.cs); sem a chave, `NullEmailSender` entra no lugar e só loga o conteúdo — o app nunca quebra por falta de email configurado, mesmo espírito do gap já documentado pro processador de pagamento (§8.11).
- **⚠️ Gap real, não escondido — sandbox do Resend sem domínio verificado:** sem um domínio próprio verificado na conta Resend, o remetente de teste (`onboarding@resend.dev`) só entrega email pro endereço da PRÓPRIA conta Resend, não pra qualquer jogador. Ou seja: a mecânica está 100% funcional e testada (157 testes de API cobrem o fluxo inteiro com um `FakeEmailSender`), mas em produção **só o dono da conta Resend recebe o email de verdade** até alguém verificar um domínio (registros TXT/CNAME de SPF/DKIM). DuckDNS (domínio atual do backend, §10) tipicamente não expõe gerenciamento de DNS arbitrário — precisa de um domínio próprio de verdade (já um gap conhecido, §11 "domínio próprio (opcional)"), que resolveria os dois pendências de uma vez. Configurável via `Resend:FromAddress` assim que houver domínio verificado — zero mudança de código.
- **Token de reset** (`PasswordResetToken`, migration `AddPasswordResetToken`): 32 bytes aleatórios (`RandomNumberGenerator`), só o hash SHA256 (não PBKDF2 — já é alta entropia, não senha escolhida por humano, hash lento seria desperdício e atrapalharia o lookup O(1)) fica no banco. Expira em 1h; pedir de novo invalida qualquer link anterior ainda válido (só 1 ativo por vez). `PasswordResetService.RequestAsync` nunca revela se o email existe (sempre mesma resposta) — anti-enumeração de contas, mesmo princípio já usado noutros lugares do jogo pra não vazar informação de quem tem conta.
- **Endpoints:** `POST /api/auth/forgot-password` (público, rate-limited pelo grupo "auth" já existente), `POST /api/auth/reset-password` (público, token na URL do email nunca precisa de login), `PUT /api/account/email` e `PUT /api/account/password` (autenticados, `AccountEndpoints.cs`/`AccountService.cs` — sempre exigem a senha atual, mesmo padrão de qualquer ação sensível já usado no jogo).
- **Frontend sem router:** o link do email (`?resetToken=...`) é checado direto em `App.jsx` via `URLSearchParams` antes de decidir entre `AuthView`/`GameView` — funciona mesmo se o usuário já estiver logado noutra aba/sessão. `ResetPasswordView.jsx` (nova senha + confirmação) e o link "Esqueceu sua senha?" dentro de `AuthView.jsx` (modo `"forgot"`, mesma tela, sem navegação). `ProfileModal.jsx` (aberto via "✏️ Editar perfil" no dropdown de `AccountMenu.jsx`) tem as duas seções (trocar email / trocar senha) no mesmo modal.
- **Segredo tratado como tal:** a API key do Resend nunca foi escrita em código — só via `dotnet user-secrets` localmente (`Resend:ApiKey`) e, quando for pro ar, env var `Resend__ApiKey` na VM (mesmo padrão de `Jwt__Key`/`ConnectionStrings__Vivarium`, `deploy/.env`).
- **Testes:** `AccountTests.cs` (8 casos — trocar email/senha, senha atual errada, email duplicado, formato inválido, sem token) + `PasswordResetTests.cs` (7 casos — pedido com/sem conta existente, reset válido, token usado 2x, token inválido, token expirado, pedido novo invalida o anterior) + `frontend/cypress/e2e/profile.cy.js` (5 e2e) + `frontend/cypress/e2e/forgot-password.cy.js` (6 e2e). `VivariumApiFactory` ganhou `FakeEmailSender` (captura em memória, substitui `IEmailSender` só nos testes) — os testes extraem o token bruto de dentro do HTML capturado via regex, já que o token nunca é gravado em claro no banco.

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

PasswordResetToken -- "esqueci minha senha" (8.24)
- Id (PK)
- UserId (FK -> User)
- TokenHash (string, único) -- SHA256 do token bruto; o valor bruto nunca é persistido
- ExpiresAt
- UsedAt (datetime, nullable) -- null = ainda não usado
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
- HasWaterSensor (bool, default false) -- Sensor de Qualidade da Água comprado (§8.17)
- AutoCleanTriggerPercent (decimal, default 0) -- gatilho da Limpeza Automática de VIP; só tem efeito com HasWaterSensor=true
- AutoCollectEnabled (bool, default true) -- opt-out da coleta automática de VIP (§8.22)
- AutoCleanEnabled (bool, default true) -- opt-out da Limpeza Automática de VIP (§8.22)

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
- Seed (bigint) -- histórico/auditoria (audit-ancestry); não é mais lido pra derivar traits (13/08/2026, ver 8.19.2)
- TraitConfigVersion (int)
- RarityScore (decimal, cacheado)
- TraitsJson (text, nullable até o backfill) -- traits CONGELADOS no nascimento (13/08/2026, ver 8.19.2); fonte de verdade pra exibir o peixe, nunca mais recalculado do Seed
- BreedingSourceJson (text, nullable) -- só filhotes NOVOS: de onde veio cada slot (ParentA/ParentB/Mutation), congelado junto do TraitsJson
- ParentAId (FK -> CreatureInstance, nullable) -- linhagem (breeding, 8.8); base da auditoria de profundidade ilimitada (audit-ancestry)
- ParentBId (FK -> CreatureInstance, nullable)
- ParentASeed (bigint, nullable) -- histórico/auditoria, não funcionalmente necessário desde 13/08/2026 (BreedTraits lê TraitsJson do pai, não o seed)
- ParentBSeed (bigint, nullable)
- ParentAGrandparentASeed (bigint, nullable) -- histórico (mecanismo de avô removido em 13/08/2026, ver 8.19.2)
- ParentAGrandparentBSeed (bigint, nullable)
- ParentBGrandparentASeed (bigint, nullable)
- ParentBGrandparentBSeed (bigint, nullable)
- BreedCount (int, default 0) -- nº de gestações já completadas como pai/mãe
- LastBredAt (datetime, nullable) -- quando terminou a última gestação; descanso decai o risco (8.8, BreedingCalculator.EffectiveBreedCount)
- IsDead (bool, default false) -- não sobreviveu a uma gestação (risco cresce com BreedCount)
- DiedAt (datetime, nullable)
- SoldAt (datetime, nullable) -- vendido ao NPC (vendor, 8.12); não apaga a linha (mesma razão do IsDead)
- IsNew (bool, default false) -- só true na coleta AUTOMÁTICA de VIP (§8.22); zera via mark-seen
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
- ⏳ **Tornar o repositório GitHub privado (10/08/2026)** — hoje é público; ajuste pendente antes do lançamento oficial (pedido explícito do usuário, ainda não executado — mudar a visibilidade é uma ação manual no GitHub, fora do meu controle direto).
- ⏳ **Assets do designer** (item 2 abaixo) — trocar as formas procedurais do `fishRenderer.js`/protótipo pelos sprites reais
- ⏳ Domínio próprio (opcional — hoje usa DuckDNS/workers.dev, funcional mas feio); processador de pagamento pra premium (§8.11)
- ⏳ Trocar heartbeat por WebSocket/SSE (melhoria pós-MVP, seção 8.3); v2: alimentação e breeding (seção 8.6)
- ⏳ **Caixa de entrada (planejado, 14/08/2026, ver §8.23)** — mensagens de admin (broadcast/segmentada) com recompensa genérica (moeda/item/peixe) + peixe comprado no mercado/recebido por transferência passa a ficar pendente de resgate em vez de cair direto no tanque/mochila. Inclui o campo `OriginalOwnerId` ("primeiro dono") no `CreatureInstance`, pensado pro suporte futuro a troca de username. Nenhum código escrito ainda — só o desenho de schema/fluxo. Backlog anexo: comentários no aquário visitado (§8.16).
- ✅ **(11/08/2026) VIP — ativação implementada.** Modelo decidido com o usuário: pacotes de dias fixos pagos em moeda **premium** (não dinheiro real direto — reaproveita 100% a infra de premium já existente), **sem renovação automática** (expira sozinho, jogador recompra se quiser — mesmo espírito simples do estabilizador/seguro do Ninho, sem complexidade de cobrança recorrente/cancelamento). Preços definidos pelo usuário: 7 dias = 7 premium, 15 dias = 10 premium, 30 dias = 15 premium (`VipConfig.PackagePricePremium`, `src/Vivarium.Core/Gameplay/VipConfig.cs`) — desconto por dia cresce com o pacote, mesmo princípio do desconto por volume do PIX (§8.17, branch separada). `VipService.SubscribeAsync` **estende** `EndAt` se já houver assinatura ativa (comprar mais dias nunca desperdiça o que já foi pago) em vez de sobrescrever. Endpoints `GET /api/vip` (status + tabela de preços) e `POST /api/vip/subscribe` (`{days}` — só 7/15/30 aceitos). `TankResponse` ganhou `IsVip`/`VipEndAt` pro topbar mostrar um selo "👑 VIP" quando ativo. Frontend: card dedicado na Loja (`StoreView.jsx`, `.vip-card`, estilo `store-card--premium` já existente) com os 3 botões de pacote, desabilitados sem saldo. Nenhuma migration nova — `VipSubscription`/`SubscriptionStatus` já existiam no schema desde o início, só nunca tinham sido usados. Novo valor `VipPurchase` no enum `TransactionType` (sem migration, é coluna string). Testes: `VipCalculatorTests.cs` (Core) + `VipTests.cs` (Api, 5 casos) + `frontend/cypress/e2e/vip.cy.js` (4 casos E2E).
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

- **Bug real corrigido (12/08/2026, relatado pelo usuário): listar peixe da Mochila sempre retornava erro.** `MarketService.CreateListingAsync` checava `if (creature.HabitatId is null) return Bad("Criatura já está no mercado ou em trânsito")` — mas `HabitatId == null` também é o estado NORMAL de um peixe guardado na Mochila (§8.7), não só "já listado". Resultado: listar qualquer peixe direto da Mochila (fluxo comum, botão "Vender" na Mochila) sempre falhava com 400 — só listar do Tanque funcionava, por acaso (lá `HabitatId` não é null antes de listar). Nenhum teste existente cobria esse caminho (todos os testes de listagem usavam `CriarCriaturaNoTanque`). Corrigido: o check certo é se já existe uma `MarketListing` ATIVA pra essa criatura (`db.MarketListings.AnyAsync(...)`, mesmo padrão já usado em `SellToVendorAsync`/`BreedingService.StartAsync`) — não o `HabitatId`. Aproveitado pra fechar 2 gaps relacionados encontrados na mesma revisão: bloquear listar uma criatura já vendida ao NPC (`SoldAt is not null`) e uma criatura presa numa gestação em andamento (`BreedingSlots` ativo) — nenhum dos dois tinha proteção nenhuma antes. Testes: `MarketTests.Listar_CriaturaDaMochila_Funciona` (regressão — falha com 400 revertendo o fix, confirmado manualmente) + `Listar_MesmaCriaturaDuasVezes_Retorna400NaSegunda`.

| Método | Rota | Auth | Descrição |
|---|---|---|---|
| GET | `/health` | — | status |
| GET | `/api/creatures/preview/{seed}` | — | traits de qualquer seed (sem banco) |
| POST | `/api/auth/register` | — | cria user + carteiras + tanque; retorna token |
| POST | `/api/auth/login` | — | username ou email + senha; retorna token |
| POST | `/api/auth/forgot-password` | — | sempre responde igual, exista ou não a conta (anti-enumeração); manda email via Resend (8.24) |
| POST | `/api/auth/reset-password` | — | token do email (1h de validade, uso único) + nova senha |
| PUT | `/api/account/email` | ✓ | troca o email; exige a senha atual (8.24) |
| PUT | `/api/account/password` | ✓ | troca a senha; exige a senha atual (8.24) |
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
| GET | `/api/leaderboard/{metric}` | ✓ | ranking global (`rarity`\|`income`), top 100 + posição própria se fora do top (8.16) |
| GET | `/api/leaderboard/visit/{username}` | ✓ | tanque de outro jogador, só leitura (sem tick, sem fila/carteira) — pra visitar o aquário (8.16) |
| GET | `/api/vip` | ✓ | status da assinatura VIP (ativo/até quando) + tabela de preços dos pacotes |
| POST | `/api/vip/subscribe` | ✓ | compra um pacote (`{days}`: 7\|15\|30) pagando premium; estende se já houver assinatura ativa |
| POST | `/api/game/water-sensor/trigger` | ✓ | configura o gatilho (0–80%) da Limpeza Automática de VIP; exige o Sensor de Qualidade da Água já comprado (8.17) |
| POST | `/api/game/toggles` | ✓ | liga/desliga coleta automática e Limpeza Automática de VIP (opt-out, default ligados — 8.22) |
| POST | `/api/game/creatures/{id}/mark-seen` | ✓ | marca um peixe coletado automaticamente (IsNew) como visto — some o selo/silhueta da Mochila (8.22) |

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
  - **Bug real corrigido (10/08/2026): `FishCanvas` nunca recebia os 4 campos de avô.** Reportado pelo usuário: cruzou dois lendários, a celebração de coleta mostrou um filhote com nadadeira peitoral roxa, mas o mesmo peixe na Mochila apareceu idêntico aos pais. Causa: `FishCanvas.jsx` só aceitava `seed`/`isBred`/`parentASeed`/`parentBSeed` como props — reconstruía um objeto PARCIAL pra passar pro `traitsOf`, sem os 4 campos de avô (`parentAGrandparentASeed`/`BSeed`, `parentBGrandparentASeed`/`BSeed`). Sem eles, `resolveOwnTraits`/`effectiveParentTraits` (`generator.js`) caem no fallback silencioso de tratar aquele lado como se não tivesse ancestralidade própria (`generateTraits(seed)` puro em vez de `breedTraits`) — então TODO desenho de peixe filhote (`FishCanvas`, usado em Mochila/Tanque/Mercado/Ninho/detalhe/celebração) renderizava errado sempre que o mecanismo de avô (`GrandparentReachChance`) tinha sido exercido de verdade, enquanto o texto "por que é raro" (que chama `traitsOf(creature)` direto com a criatura completa, sem passar por `FishCanvas`) mostrava os traits certos — daí a discrepância entre telas. Corrigido em 2 lugares: `FishCanvas.jsx` ganhou os 4 props e repassa pro `traitsOf`; **todo chamador** (`CollectCelebration.jsx` ×3, `BackpackView.jsx`, `BreedingView.jsx` ×2, `FishDetail.jsx`, `MarketView.jsx`, `TankView.jsx`) passou a fornecê-los a partir da criatura. Achado de bônus no mesmo bug: `ListingDto`/`MarketService.ListingsAsync` (mercado entre jogadores) **nunca expôs os 4 campos de avô** desde que a feature existe — corrigido junto (novo `ParentAGrandparentASeed`/etc. no DTO). **Lição:** sempre que um objeto "criatura completa" ganha um campo novo necessário pra derivar traits corretamente (visto antes com `ParentASeed`/`BSeed` na correção do bug de renda `a1e0ca7`), grep por TODO lugar que desenha/deriva traits de um objeto PARCIAL (não a entidade inteira) é obrigatório — um componente de exibição reconstruindo um objeto reduzido é o padrão exato que esconde esse tipo de regressão.
  - **Bug real corrigido (12/08/2026, mesma classe do bug acima): `ChildTierDistribution` (prévia "Chance do brilho do filhote") usava `Generate(seed)` puro nos pais, ignorando ancestralidade.** Reportado pelo usuário: cruzou dois Épicos com brilho Lendário Iridescente de verdade e a prévia mostrou 98,2% de chance de o filhote sair **sem brilho** — quase o oposto do resultado real (pais lendários deveriam manter o tier na maioria das vezes). Causa idêntica à do `FishCanvas` (10/08): quando um pai É ele mesmo um filhote, `Generate(parentSeed)` devolve um tier aleatório sem relação com o tier REAL desse pai (78% de chance de "Sem brilho" do zero) — a prévia calculava a herança em cima desse tier fantasma. Corrigido: `TraitGenerator.ChildTierDistribution` passou a receber `ParentAncestry` (seed + seeds dos avós) em vez de `long` cru, e usa `ResolveOwnTraits` (mesma função já usada em `BreedTraits`) pra resolver o tier real de um pai filhote antes de calcular a distribuição; `BreedingService.GetQuoteAsync` monta a ancestralidade a partir de `ParentASeed`/`ParentBSeed` da entidade. **Importante:** o resultado REAL do cruzamento (`CollectAsync`) já usava `ParentAncestry` corretamente desde 31/07 — só a PRÉVIA (texto antes de confirmar) estava errada, não o peixe que de fato nascia. Teste de regressão (`BreedTraitsTests.ChildTierDistribution_PaisFilhotesComTierRealLendario_NaoIgnoraAAncestralidade`) construiu deliberadamente 2 pais filhotes com tier real Lendário (mas seed cru NÃO lendário) e confirmou que revertendo o fix o teste falha com 0% de retenção — bate com o sintoma relatado. **Lição reforçada:** todo lugar que deriva traits de um `long seed` cru (não de uma `ParentAncestry`/entidade completa) é suspeito quando o pai pode ser um filhote — vale grep por `Generate(` recebendo `.Seed` direto sempre que uma função nova de breeding for adicionada.
  - **Investigado a seguir, no mesmo dia: usuário reportou que o FILHOTE REAL (não só a prévia acima) também nasceu sem o brilho lendário.** Simulação estatística (`RetencaoDeLendario_ComAvosNaoLendarios_AindaAltaMasMenorQuePaisFrescos`, 50k amostras): retenção real de Lendário cruzando 2 pais filhotes com tier real Lendário e avós comuns é **~83%** (vs ~92% já calibrado pra pais "frescos") — o `GrandparentReachChance` (15% por lado) reduz um pouco a certeza, mas o viés de raridade ainda favorece fortemente o lado que continua Lendário quando o outro "escapa" pro avô. **Não é um bug adicional** — um filhote perder o tier tem ~17% de chance nesse cenário, dentro da variância normal; o caso relatado pelo usuário provavelmente foi só azar (ainda mais crível já que a prévia, bug acima, fazia parecer BEM mais improvável do que os 83% reais).
  - **`GrandparentReachChance` reduzido de 0.15 pra 0.03 (12/08/2026, dois ajustes no mesmo dia: usuário pediu primeiro 0.05, depois 0.03).** Investigando um relato de "resultados estranhos" no histórico de cruzamentos de uma conta real (`marcospdn`, múltiplas gerações de filhotes cruzando entre si), a causa raiz não era bug — era a matemática composta: `EffectiveParentTraits` é chamado 4x por lado (brilho/cauda/dorsal/peitoral) × 2 lados = **8 sorteios independentes de `GrandparentReachChance` por cruzamento**. Com 15% cada, a chance de PELO MENOS UM traço vir de um avô em qualquer cruzamento era `1-(0.85)^8 ≈ 72.8%` — quase 3 em cada 4 cruzamentos, fazendo a influência dos avós parecer dominante mesmo com uma chance individual baixa. Com 0.03, cai pra `1-(0.97)^8 ≈ 21.6%` — a mecânica continua existindo (não foi removida), só deixa de ser o caso comum. Efeito colateral esperado: a retenção de Lendário com avós comuns (teste acima) sobe de ~83% pra ~90% (menos chance de "escapar" pro avô não-lendário) — ajustada a asserção do teste pra refletir isso, não é regressão. `generator.js` espelhado (`CONFIG.breeding.grandparentReachChance`).
  - **Segundo ajuste no mesmo dia (12/08/2026): `GrandparentReachChance` 0.03→0.01 e `MutationChance` 0.08→0.04.** Investigando o relato acima com mais profundidade — reconstruí a árvore genealógica real de vários cruzamentos da conta `marcospdn` com o motor atual e cruzei com `Vivarium.Simulation breed` (5.000 amostras): os números batiam EXATAMENTE com a calibração documentada (branco+branco→92,0%, branco+laranja→56,5%, laranja+laranja→0,1% só mutação) — **nenhum bug encontrado na engine**. Um caso específico apontado como "impossível" (peixe #934: os 2 pais tinham cauda Laranja, filho saiu Vermelho) foi rastreado até os 4 avós — nenhum deles tinha cauda Vermelha em lugar nenhum da ancestralidade, confirmando **mutação genuína** (funcionando como projetado), não bug. Ainda assim, o usuário achou a herança "muito difícil de passar traços raros pros filhotes de filhotes" e pediu pra deixar mais confiável, especialmente pensando no custo/risco de cruzar peixes Lendários no lançamento — escolheu (via pergunta direta) reduzir `GrandparentReachChance` ainda mais E `MutationChance`. Validado com `Vivarium.Simulation breed`: branco+branco sobe de 92,0%→**96,1%**; branco+laranja de 56,5%→**59,0%**; chance de pelo menos 1 traço vir de avô por cruzamento cai de ~21,6%→**~7,7%** (`1-(0.99)^8`). Teste `RetencaoDeLendario_ComAvosNaoLendarios_AindaAltaMasMenorQuePaisFrescos` — retenção de Lendário com avós comuns sobe pra ~95% (era ~90% no ajuste anterior); espaço de busca de seed do teste (`ResolvedLegendaryParentSeed`) ampliado de 5k pra 40k, porque achar um "Lendário raro só via mutação" no mesmo conjunto fixo de seeds ficou mais raro com `MutationChance` mais baixo — não é regressão, é o teste precisando de mais tentativas pra achar o mesmo tipo de caso. `generator.js` espelhado (`CONFIG.breeding.mutationChance`/`grandparentReachChance`).
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
- **Responsividade mobile (10/08/2026):** relatos de jogadores de "tamanhos errados" (peixe cortado/espremido no modal de detalhe) — causa raiz não era falta de media query, era `FishCanvas` (`<canvas width={N}>`, HTML attribute fixo em px) dentro de containers flex com `align-items:center` e sem largura própria (`.detail-fish`, `.celebrate-fish`, `.parent-chip`, `.farewell-portrait`) — sem uma largura resolvível no pai, o `.fish-canvas{width:100%}` do CSS não tem o que preencher e o canvas cai pro tamanho fixo do atributo HTML, que não encolhe em tela estreita. Único container que já funcionava (`.parent-preview-card`, usado no Ninho) tinha o padrão certo: `width: min(Npx, 100%)` (ou `flex: 1 1 Npx`) — replicado nos containers quebrados. `.nav-pills` (6 abas) também ganhou `flex-wrap` — sem isso a navegação principal vazava da tela em telas estreitas, sem opção de rolar até as últimas abas. **Não foi possível confirmar visualmente em viewport mobile real** — `resize_window` e o atalho de device toolbar do Chrome DevTools não tiveram efeito neste ambiente de automação (o screenshot só captura o conteúdo da página, não emula viewport estreito); a correção foi validada por leitura de código (mesmo padrão já comprovado em produção) e pela suíte de testes de frontend (`npm test`, 57/57), não por captura de tela mobile — vale conferir num celular de verdade.
- **Revisão de UX mobile (11/08/2026):** dono do site mandou 2 screenshots reais de celular mostrando a UX mobile "quebrada/feia" — confirmado, dois problemas concretos, corrigidos com `cy.viewport` (única forma confiável de emular tela estreita neste projeto, `resize_window`/device toolbar do Chrome DevTools não funcionam neste ambiente de automação, ver bullet 10/08 acima):
  - **Cabeçalho mobile redesenhado (`GameView.jsx`, `styles.css`):** antes, o `.topbar` em telas estreitas empilhava marca / carteira+premium / admin+conta / nav em DUAS linhas — ~5 "linhas de chrome" antes de qualquer conteúdo do aquário aparecer. Reorganizado em 3 linhas fixas via CSS `order` (sem mudar a ordem no DOM, então o desktop nem percebe): 1) marca ---- ícone de conta; 2) as 6 abas viram uma tira de rolagem horizontal de UMA linha só (`overflow-x:auto`, `flex-wrap:nowrap`, scrollbar escondida) + botão "?"; 3) carteira/premium/recompensa-diária/dev (só o que existir). Dois wrappers novos e puramente estruturais (`.topbar-tabs`, `.topbar-stats`) usam `display:contents` em telas largas — ficam "invisíveis" pro layout, então o desktop continua pixel-idêntico; só assumem `display:flex` de verdade dentro do `@media (max-width:720px)`. Trocar de aba rola o botão ativo pra dentro da faixa visível (`scrollIntoView` num `useEffect` chaveado em `tab`).
  - **Modal "gigante e incompleto" — dois bugs reais, não um:** (1) `.modal-close` era um filho absolute do PRÓPRIO container que rolava (`.modal` tinha `overflow-y:auto`) — em conteúdo alto (ex: "Por que é raro" com vários atributos), rolar pra ver o fim empurrava o × pra fora da tela junto, o modal virava uma "página infinita" sem borda perceptível. Fix: `Modal.jsx` ganhou `.modal-body` (só ele rola; `.modal` agora só cresce até `max-height:92vh` e não rola mais), então `.modal-close` (fora do `.modal-body`) fica sempre alcançável, com um leve degradê no rodapé do body sinalizando "tem mais conteúdo abaixo". (2) Achado ESCONDIDO atrás do primeiro, só apareceu ao escrever o teste E2E: `.tank-layout` (aba Tanque) tem `isolation:isolate` (existente desde o efeito "cinema" de 08/08, pra conter o brilho `::before` de z-index negativo sem vazar pro resto da página) — isso prende QUALQUER modal renderizado como filho de `.tank-layout` (FishDetail, ConfirmModal, etc.) no stacking context local dele; por fora, `.tank-layout` inteiro (sem z-index próprio) perde a disputa de pintura pro `.topbar` (z-index 20, agora mais alto ainda com o cabeçalho de 3 linhas) sempre que o modal fica perto do topo da tela — o × ficava ali mas INCLICÁVEL, coberto pelo topbar. Corrigido de raiz: `Modal.jsx` agora renderiza via `createPortal(..., document.body)` — tira o modal inteiro da árvore de `.tank-layout`, escapando de qualquer stacking context/overflow de ancestral, presente ou futuro, não só desse caso específico.
  - **Passe mais amplo:** `.sticky-bar` (barra fixa de ações em massa — Mochila/Ninho) ganhou `max-width`+`flex-wrap` no mobile — sem isso, texto+2 botões conseguiam ficar mais largos que a tela e empurravam scroll horizontal da PÁGINA inteira (bug real achado nesta revisão, não só teórico). `.leaderboard-row` (Ranking) ganhou `flex-wrap` pra não espremer usuário/valor/botão "Visitar" numa linha só em telas estreitas.
  - **Testes:** `frontend/cypress/e2e/mobile-header.cy.js` (10 casos — marca+conta na mesma linha, as 6 abas numa linha rolável só, trocar pra aba fora da área visível funciona e rola até a vista, cabeçalho compacto <200px de altura, sem overflow horizontal em nenhuma das 6 abas) + `mobile-modal.cy.js` (2 casos — × continua visível/clicável depois de rolar o conteúdo até o fim, × fecha sem regressão no caso feliz), ambos com `cy.viewport(390, ...)`. Suíte completa revalidada: `npm test` 57/57, `npm run e2e` 48/48 (14 specs).
  - **Seções "Atributos"/"Por que é raro" recolhíveis (12/08/2026, pedido do usuário):** o modal de detalhe do peixe (`FishDetail.jsx`) continuava grande demais mesmo depois do fix acima — as duas seções de baixo (atributos + breakdown de raridade) sempre vinham abertas, empurrando bastante conteúdo pra rolar. Novo componente local `CollapsibleSection` (nasce fechado, `useState(false)`, sem persistência — cada abertura do modal é uma instância nova) envolve as duas; reaproveita o padrão de collapse que já existia em DOIS lugares (`TankView.jsx`/`.collapse-btn` e `HowItWorksGuide.jsx`/`Section`) em vez de inventar um terceiro — cabeçalho vira um `<button class="detail-section-head">` com o mesmo `.eyebrow` de sempre + um `.chevron` (mesma animação de rotação já usada em `.collapse-btn`). `mobile-modal.cy.js` precisou clicar nos dois cabeçalhos antes de checar o scroll (as seções fechadas por padrão deixavam o modal curto demais pra exercitar overflow de verdade nos 620px do teste) — sem isso o teste continuaria passando, mas perderia o sentido original.
  - **Toast dispensável por clique (12/08/2026, pedido do usuário):** a notificação (`.toast`, `useToast.js`) só sumia sozinha depois de 4s — se ela caísse em cima de um botão que o jogador queria clicar, tinha que esperar. `useToast` ganhou `dismiss()` (limpa o timer e zera a mensagem na hora) e passou a guardar o `setTimeout` num `ref` — sem isso, dois `notify()` próximos no tempo corriam risco do timer do PRIMEIRO apagar o SEGUNDO toast cedo demais (bug latente que já existia, só nunca tinha sido notado). `Toast.jsx` ficou clicável (`onClick={onDismiss}`, `cursor:pointer`, título "Clique pra fechar"). Testes: `frontend/cypress/e2e/toast.cy.js` (2 casos, com `cy.clock()`/`cy.tick()` — clique fecha na hora; sem clique, some sozinho em 4s como antes).
  - **Modal de detalhe não centralizado em celular real + passe de polish premium (12/08/2026):** dono do site reportou (celular de verdade) que o modal de detalhe do peixe não ficava centralizado. Investigado com instrumentação em Cypress (`cy.viewport`, medindo `getBoundingClientRect()`/`scrollWidth` direto — a única forma confiável de medir layout mobile neste projeto) em vez de só ler o CSS e assumir: achou DUAS causas raiz reais, nenhuma específica de Safari mobile — reproduzíveis de forma determinística em headless Chrome puro.
    1. **Bug principal — `.modal-backdrop{display:grid; place-items:center}` não centraliza a TRILHA, só o item dentro dela.** Sem `grid-template-columns` explícito, a trilha implícita é dimensionada pelo CONTEÚDO do `.modal` (`width:min(560px,96vw)`), não pelo espaço disponível do backdrop (`100vw − 40px` de padding). Em qualquer viewport abaixo de ~1000px, `96vw` já excede esse espaço — a trilha estoura o container, e sem `place-content` o excesso vaza só pra um lado (`justify-content` no default `normal`≈`start`), deslocando o modal em vez de centralizar. Desvio medido: ~12-14px, CONSTANTE em 320/375/390px de largura, e presente mesmo depois de eliminar todo overflow horizontal de página (achado #2) — prova de que não tinha nada a ver com quirk de viewport do Safari. Fix: `place-content: center` no backdrop, que centraliza a trilha mesmo maior que o container, distribuindo o excesso simetricamente. Confirmado via Cypress: desvio caiu de ~12px pra <0.01px nos 3 viewports testados.
    2. **Achado colateral, também real: `.tank-layout::before` (vinheta do modo cinema, `inset:-20px` nos 4 lados) causava overflow horizontal genuíno da página** sempre que `.content{padding}` (16px no mobile) é menor que os 20px de bleed — sobrava `20−16=4px` de área rolável fora da viewport (`document.documentElement.scrollWidth > clientWidth`, confirmado desligando o pseudo-elemento e comparando antes/depois). Os testes de overflow já existentes (`mobile-header.cy.js`) não pegavam isso porque comparavam contra `innerWidth` (já inclui a folga da barra de rolagem vertical) em vez de `clientWidth` — 4px reais ficavam mascarados pela tolerância errada. Fix: bleed só vertical (`inset:-20px 0`, o gradiente é centralizado então o respiro horizontal não fazia diferença visual perceptível) + `overflow-x:hidden` em `html,body` como rede de segurança geral (não a correção em si — só evita que um futuro elemento decorativo cause a mesma classe de bug de novo, já vista 2x neste projeto: `.sticky-bar` em 11/08, este em 12/08).
    - **Passe de polish mobile no mesmo commit:** alvos de toque pequenos (`.modal-close`/`.account-btn`/`.tool-btn`/`.guide-btn`, 34-38px; `.collapse-btn`, 26-30px) ganharam `min-width`/`min-height` (40px/38px) só no `@media (max-width:640px)` — CSS `min-width`/`min-height` vence sobre `width`/`height` fixos quando conflitam, então isso aumenta a área de toque sem duplicar cada regra de tamanho nem afetar a densidade visual no desktop. Achado à parte na mesma varredura: `.tank-tools` (botões de tela cheia/pop-up/alternar cliques no tanque) tinha opacidade 0.6 permanente fora de `:hover`/`:focus-within` — em qualquer dispositivo de toque (não só celular estreito, por isso `@media (hover:none)` em vez de largura de viewport) esses 3 botões ficavam quase invisíveis o tempo todo, já que um dedo nunca dispara `:hover`. Corrigido pra opacidade 1 em `hover:none`.
    - **Testes:** `mobile-modal.cy.js` ganhou 2 blocos novos (6 casos: modal centralizado em 320/375/390px de largura com tolerância de 2px, sem overflow horizontal de página no Tanque com peixe presente nos mesmos 3 tamanhos — o teste de overflow existente usava `tank-empty.json`, que nunca exercitava esse caminho). Suíte completa revalidada: `npm test` 57/57, `npm run e2e` 61/61 (16 specs).
- **Testes do frontend (30/07/2026):** antes disso não existia nenhum — foco em não gastar tokens de agente validando UI manualmente (nem abrindo Chrome, ver seção de preferências do usuário) a cada mudança; agora basta rodar os comandos abaixo e ler o resultado.
  - **Unitários (Vitest):** `frontend/src/lib/*.test.js`, cobrem a lógica pura de `generator.js` (motor determinístico seed→traits, incl. teste de regressão via `toMatchSnapshot` — só atualizar o snapshot com `-u` quando a mudança no motor for intencional, e nesse caso o `TraitConfigVersion` do backend também precisa subir), `tankMath.js` (sinergia/produção/ETA) e `format.js` (rótulos PT-BR). `frontend/src/lib/vitest.setup.js` faz stub de `Path2D` (só existe no browser) pra permitir importar `fishRenderer.js` em ambiente Node. Rodar: `npm test` (uma vez) ou `npm run test:watch`.
  - **E2E (Cypress):** `frontend/cypress/e2e/*.cy.js` — sobe a build de produção (`vite preview`, porta 4173) e mocka toda a API via `cy.intercept` (fixtures em `cypress/fixtures/`), então roda sem precisar do backend/Postgres. Rodar: `npm run e2e` (builda se necessário, sobe o preview, roda os specs, mata o servidor — via `start-server-and-test`) ou `npm run cypress:open` pra depurar interativamente.
  - CI (`.github/workflows/ci.yml`) roda os dois: `npm test` e depois `cypress-io/github-action` (cuida das dependências do Linux e cache do binário) contra o preview.
  - **Cobertura ampliada (10/08/2026):** até então só Tanque (rush/venda/despedida), auth, recompensa diária e o banner de sincronização tinham E2E — Mercado, Mochila, Loja e Ninho (fluxo principal, não só a despedida) e Ranking não tinham nenhum. `market.cy.js`, `backpack.cy.js`, `store.cy.js`, `breeding-flow.cy.js`, `ranking.cy.js` novos (11 specs). Achado na varredura: `RankingView.jsx` sempre renderizava `<Coin/>` junto do valor, mesmo na métrica "Raridade total" (raridade não é moeda) — corrigido pra usar 🏆 nessa métrica, mesmo ícone que o modo espectador já usava; travado por `ranking.cy.js`. Pegadinha de teste encontrada ao escrever esses specs, não bug de produto: `cy.contains(seletor, texto)` é case-sensitive mas faz *substring* match — "Filtro" batia tanto no card do item quanto no heading "Filtro automático" do card de status (usar `cy.contains("strong", /^Filtro$/)` ou escopar por `.closest`/`.within` quando o texto do botão se repete dentro e fora de um modal, como "Transferir" no card e no submit do `PromptModal`).
- `tests/Vivarium.Core.Tests` — xUnit
- `tools/Vivarium.Simulation` — console de validação estatística dos pesos (`dotnet run --project tools/Vivarium.Simulation [N]`); modo `dump [N]` imprime traits canônicos por seed para verificar ports do motor; `economy`/`breed` testam renda e breeding isoladamente; `simulate` (30/07/2026) roda um jogador sintético por 120 dias gastando em filtro+upgrade+breeding ao mesmo tempo — ver seção 8.6
- `prototype/fish-composer.html` — protótipo visual standalone (Canvas); digite um seed ou busque por tier de brilho
