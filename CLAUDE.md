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
- **Barbatana peitoral**

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

## 3. Tabela de raridade — Cauda, Nadadeira Dorsal, Barbatana Peitoral

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

### Padrão sobre a parte (estria/bolinha) — aplicado igualmente às 3 partes

| Tipo de padrão | Peso (%) |
|---|---|
| Sem padrão | 65% |
| Estria | 15% |
| Bolinha | 15% |
| Degradê | 4% |
| Manchado | 1% |

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

**Faixas de exibição ao jogador** — recalibradas via simulação de 100k seeds (28/07/2026, já com traits de movimento no score), produzindo a pirâmide 50% / 30% / 15% / 4.8% / 0.2%:
- Comum: score < 5.0
- Incomum: 5.0–6.7
- Raro: 6.7–8.4
- Épico: 8.4–11.2
- Lendário: 11.2+

> O score mínimo possível é ~2.6 (mesmo o peixe mais comum carrega a informação dos traits base), então as faixas antigas começando em 0 não funcionavam. Recalibrar essas faixas sempre que os pesos do `TraitWeightConfig` mudarem (rodar `dotnet run --project tools/Vivarium.Simulation`).

### 5.1 Decisões de implementação do score (v1)

- **Base do log:** log10 (`score = Σ -log10(P)` de cada trait sorteado).
- **Entram no score:** tier de shimmer do corpo; por parte (cauda/dorsal/peitoral): cor base (com probabilidade **já ajustada** pela correlação, quando ativa), tipo de padrão, cor do padrão (paleta renormalizada sem a cor base) e, apenas quando extremos, tamanho e opacidade do padrão; velocidade de cauda e de nadadeira **apenas nos extremos**, com peso reduzido (ver Movimento).
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

### 8.6 Fora do escopo do MVP

- **Alimentação**: cortada do MVP para não duplicar a função de "sink de manutenção" já coberta pela qualidade da água. Candidata a entrar na v2 como boost opcional (não como necessidade punitiva).
- **Breeding**: fica para v2, depois do motor de geração e mercado estarem validados.

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
- ParentAId (FK -> CreatureInstance, nullable) -- pronto pra breeding futuro
- ParentBId (FK -> CreatureInstance, nullable)
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
- Type (MarketSale | DirectTransfer | CurrencyPurchase | ItemPurchase | Sink)
- FromUserId (FK -> User, nullable)
- ToUserId (FK -> User, nullable)
- CreatureInstanceId (FK -> CreatureInstance, nullable)
- CurrencyTypeId (FK -> CurrencyType, nullable)
- Amount (decimal, nullable)
- CreatedAt
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

**Renderização do peixe:** composição via `<canvas>`, desenhando as camadas (corpo, cauda, dorsal, peitoral) como imagens sobrepostas, usando `globalCompositeOperation = 'overlay'` para o shimmer do corpo.

---

## 11. Próximos passos sugeridos

### Status atual (28/07/2026)

MVP jogável de ponta a ponta, rodando local contra o Neon. Feito:
- ✅ Motor de geração seed→traits (`Vivarium.Core/Generation`), simulação de pesos e faixas de raridade calibradas
- ✅ **Traits de movimento** (velocidade/amplitude de cauda e nadadeira; extremos no score com peso 0.5) — seção 5.1
- ✅ Backend completo (auth JWT, loop de jogo com tick lazy, mercado, loja de itens, transferência direta) — seção 12; **58 testes verdes** (Core + API)
- ✅ Banco no **Neon** (sa-east-1) com migrations aplicadas; connection string em user-secrets local
- ✅ Frontend React/Vite: auth, **aquário animado** (peixes nadando, seleção por clique), mercado, loja
- ✅ **Cauda com onda viajante** (undulação em S via sprite + blit por fatias) em `fishRenderer.js` e no protótipo — 100% renderização, tunável em `MOVEMENT_TUNING`
- ✅ Ferramentas de dev: `dev.cmd` (sobe API+front+navegador), botões dev de gerar/limpar peixes (só em Development)

Falta pra ir ao ar (depende de contas do usuário):
- ⏳ **Deploy backend** no Oracle Cloud (Dockerfile pronto; env vars `ConnectionStrings__Vivarium`, `Jwt__Key` forte, `Cors__AllowedOrigins__0`)
- ⏳ **Deploy frontend** no Cloudflare Pages (build `npm run build`, output `frontend/dist`, env `VITE_API_URL`)
- ⏳ **Assets do designer** (item 2 abaixo) — trocar as formas procedurais do `fishRenderer.js`/protótipo pelos sprites reais
- ⏳ Trocar heartbeat por WebSocket/SSE (melhoria pós-MVP, seção 8.3); v2: alimentação e breeding (seção 8.6)

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
| POST | `/api/game/creatures/{id}/transfer` | ✓ | transferência direta por username (audita `DirectTransfer`; bloqueada se listada) |
| GET | `/api/market/listings?skip&take` | ✓ | listagens ativas |
| POST | `/api/market/listings` | ✓ | lista criatura própria por preço em SOFT |
| POST | `/api/market/listings/{id}/cancel` | ✓ | cancela e devolve ao tanque |
| POST | `/api/market/listings/{id}/buy` | ✓ | compra (transacional + TransactionLog) |
| GET | `/api/items/` | ✓ | catálogo (preço de upgrade calculado pelo nível atual) |
| POST | `/api/items/{key}/buy` | ✓ | compra e aplica efeito; registra `ItemPurchase` (sink) |

**Itens do MVP** (seed via migration `SeedItemDefinitions`): `filter_basic` (20 soft, restaura água pra 100 — tick roda antes, pra degradação pendente ser aplicada primeiro), `auto_filter` (500 soft, permanente via UserInventory, tick lê e degrada na metade), `tank_upgrade` (base 50 soft, +1 capacidade, preço = base × 1.5^(capacidade − 3) — seção 8.4). Filtro e upgrade aplicam na hora (sem inventário); só o auto_filter fica em UserInventory.

**Testes de integração** (`tests/Vivarium.Api.Tests`): API completa via `WebApplicationFactory` contra SQLite in-memory (Postgres só em staging/produção) — fluxos de auth, coleta, VIP auto-coleta e mercado ponta a ponta.

## 13. Estrutura da solution

- `Vivarium.slnx` — solution (.NET 10)
- `src/Vivarium.Core` — domínio e motor de geração (sem dependência de web/banco; testável isolado); entidades do schema da seção 9 em `Domain/`
- `src/Vivarium.Api` — ASP.NET Core minimal API + EF Core/Npgsql; `Data/VivariumDbContext` (índices, enums como string, seed de CurrencyType/HabitatType/Species) e migrations em `Data/Migrations`. `Endpoints/` (auth, game, market — ver seção 12), `Services/` (TokenService, PasswordHasher, GameService). Connection string `Vivarium` (dev aponta pra localhost; produção via env var `ConnectionStrings__Vivarium` no Neon)
- `tests/Vivarium.Api.Tests` — testes de integração da API (WebApplicationFactory + SQLite in-memory)
- `frontend/` — React + Vite (stack da seção 10). Telas: auth (login/registro), tanque (fila com coleta, qualidade da água, peixes em Canvas via `fishRenderer.js`) e mercado (comprar/vender/cancelar). Heartbeat a cada 60s + refresh do tanque a cada 30s. `generator.js` é o mesmo port JS verificado do protótipo (traits derivados client-side do seed; rarity score vem da API). Dev: `npm run dev` (proxy pra API local). Deploy Cloudflare Pages: build `npm run build`, output `frontend/dist`, env `VITE_API_URL` apontando pro backend
- `tests/Vivarium.Core.Tests` — xUnit
- `tools/Vivarium.Simulation` — console de validação estatística dos pesos (`dotnet run --project tools/Vivarium.Simulation [N]`); modo `dump [N]` imprime traits canônicos por seed para verificar ports do motor
- `prototype/fish-composer.html` — protótipo visual standalone (Canvas); digite um seed ou busque por tier de brilho
