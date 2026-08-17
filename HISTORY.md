# Vivarium — Histórico / Arqueologia de decisões

> Este arquivo **não** é carregado por padrão em toda sessão — é o `CLAUDE.md` que cumpre esse papel (fonte de verdade do estado vigente). Aqui fica a jornada: investigações, bugs já corrigidos, números de calibração superados, mecanismos removidos. Consulte quando precisar entender *por que* um valor ou regra do `CLAUDE.md` é o que é hoje. Organizado pelas mesmas seções do `CLAUDE.md`.

---

## §5 — Evolução dos cortes de Rarity Score

**v1 → v2 (29/07/2026):** faixas recalibradas via simulação de 100k seeds (raridade v2: corpo pesa 2.5× no score, bônus de conjunto coeso, 11 padrões), pirâmide 50%/30%/15%/4.8%/0.2%: Comum <5.4, Incomum 5.4–7.5, Raro 7.5–9.8, Épico 9.8–14.0, Lendário 14.0+. Distribuição (100k): min ~2.73, p50 5.36, p99.8 14.01, max ~18.9 (v1 ia de ~2.6 a ~11.2).

**Pirâmide "Íngreme" (14–15/08/2026) — Lendário 1 em 5.000 de verdade.** `TraitConfigV1.ShimmerTiers.Legendary` reduzido 0,2%→0,02% (chance REAL de sortear o brilho). `WeightedTable.BiasedInheritProbability` ganhou amortecimento log-ratio pra compensar — reduzir o peso do Legendary tinha ampliado a "lavagem" de Legendário pro filhote via viés de herança, o oposto do pretendido. Cortes (1M seeds), Comum mantido em 50% (crescer pra 61,8% quase dobrava peixe Comum com atributo isolado raríssimo, 9,43% vs 4,99%): Comum <5.45, Incomum 5.45–12.24, Raro 12.24–13.27, Épico 13.27–16.60, Lendário 16.60+.

**15/08/2026, dois ajustes no mesmo dia.** 1ª tentativa: corte inicial deu Raro 1,00%/Épico 0,10% (10:1) — usuário achou Raro comum demais e Épico raro demais (confirmado com `Vivarium.AdminReset -- band-distribution`). Só Raro/Épico moveu (14,85→13,27): Raro caiu pra 0,62%, Épico subiu pra 0,48%. **2ª tentativa (final, vigente):** usuário achou os dois parecidos e pediu Raro perto de 1,00% com Épico ~0,30% — Incomum doou 0,20pp (12,24%→12,04%) pra viabilizar sem desfazer o ajuste anterior: **Comum <5.45, Incomum 5.45–12.04, Raro 12.04–13.78, Épico 13.78–16.60, Lendário 16.60+** (valores vigentes hoje, já no CLAUDE.md §5).

**Achado na investigação (não é bug):** peixes FRESCOS já batiam perto do alvo; o desequilíbrio percebido vinha de FILHOTES — o viés de herança de raridade (§8.8) empurra a raridade de peixes "legado" (nascidos com pesos generosos de antes de 14/08) pra dentro da população nova. Se voltar a incomodar depois que o legado envelhecer, a alavanca certa é `RarityBiasStrength`/`BiasedInheritProbability`, não outro corte de banda.

**Padrão sobre a parte, tabela v3 (12/08/2026):** antes de virar 11 tipos com "Sem padrão" 76.2%, veio de uma tabela anterior mais simples. Degradê caiu de 0.6%→0.4% (delta pra "Sem padrão") no mesmo dia — pedido do usuário pra torná-lo mais raro e mais marcante. Calibração 1M seeds: cortes de banda praticamente não se moveram (5.34/7.45/9.78/13.89 vs. 5.4/7.5/9.8/14.0 documentados) — não precisou recalibrar frontend.

---

## §7 — `TraitConfigVersion`: gap de versionamento (10–13/08/2026, resolvido por reescrita arquitetural)

**Gap real confirmado (10/08/2026):** `TraitConfigV1.Version` era uma constante fixa (`=1`) e o motor lançava exceção pra qualquer versão diferente — não existia forma de manter uma tabela de pesos "antiga" viva pros peixes já nascidos. Toda mudança de peso desde o lançamento (Raridade v2, novos padrões) aconteceu sem bump de versão. Consequência: traits visuais (sempre recalculados do `Seed`) mudavam silenciosamente a cada rebalanceamento, mas `RarityScore` (congelado na coleta) ficava desatualizado — peixe renderizava como lendário com score de raro antigo. Achado investigando um relato real (pai raro/9.7 renderizando como lendário, filhote lendário/14.9 herdado dele).

**Correção aplicada (10/08/2026):** `Vivarium.AdminReset` ganhou `diff-scores`/`fix-scores`. Rodado em produção: 19 criaturas corrigidas, delta líquido -62,6.

**Primeiro bump real de Version (1→2, 12/08/2026) — risco de outage descoberto:** ao dar bump em `TraitConfigV1.Version`, 17 testes quebraram com 500 — não só as ferramentas admin, os próprios serviços da API (tanque, breeding, ranking, sinergia) passavam a versão GRAVADA na linha pro motor, que lançava exceção pra qualquer mismatch. Bump real teria quebrado toda criatura existente em produção até rodar `fix-scores` — janela real de instabilidade. Corrigido: guard mudou pra só lançar em `configVersion > Version atual` (dado impossível); qualquer versão igual/anterior usa a config atual silenciosamente. `fix-scores` também passou a gravar `TraitConfigVersion` de volta (antes deixava o campo desatualizado pra sempre).

**Resolução definitiva (13/08/2026, ver §8.19.2 no CLAUDE.md):** o problema de fundo (recalcular traits do Seed a cada exibição, sujeito a qualquer mudança de peso) foi eliminado trocando a arquitetura — traits passaram a ser calculados uma vez no nascimento e congelados em `TraitsJson`. `TraitConfigVersion` virou vestigial (só auditoria).

**Lição permanente (já em memória `feedback-traitconfigversion-nunca-bumpado.md`):** depois de qualquer deploy que mude o motor de traits, rodar `backfill-traits` ANTES e DEPOIS do deploy do backend (a janela entre migration e o backend novo terminar de subir permite peixes nascerem com `TraitsJson` nulo — ver §8.22.1 abaixo).

---

## §8.6 — Economia: histórico de calibração

- **Renda por raridade (28/07/2026):** fórmula introduzida, `IncomeBasePerHour` começou em 2.0.
- **29/07/2026:** base 2.0→1.7 pra compensar o corpo pesar mais no score (rarity v2).
- **31/07/2026:** patamar de água (`IncomeWaterPlateau=80`) introduzido — água "quase perfeita" deixou de ser punida; como isso é buff em quase toda a faixa 0-100%, base caiu 1.7→1.5 pra manter o ritmo.
- **06/08/2026:** `IncomeGrowth` 0.49→0.42 — o tier Épico (faixa larga de score) fazia a renda variar ~7.8x dentro do próprio tier (26/h a 201/h), deixando runs de sorte curtos desproporcionalmente fortes. Usuário relatou 15 coletas com 3 épicos, 2 rendendo 140/h — chance conjunta medida ~0.1-0.2% (sorte real), mas o teto do tier (201/h) já desequilibrava a sensação de progresso. Growth 0.42 cortou o teto do épico pela metade (~100/h).
- **Taper do Lendário (12/08/2026):** amostra de 1M seeds mostrou score máximo ~21.1 (vs ~18.9 nos 100k antigos) — com growth uniforme isso rendia quase 2000/h no extremo, variação de 19.6x só dentro do tier Lendário. Testadas 6 curvas candidatas (soft cap contínuo, padrão de jogos idle); escolhida piecewise com `IncomeLegendaryTaperScore=14.0` (piso do Lendário, zero mudança abaixo disso) e `IncomeLegendaryTaperGrowth=0.10` (usuário pediu a opção conservadora entre 0.15/0.10). Resultado: piso intocado (100/h), teto no score máximo cai pra ~200/h, variação interna do tier cai pra ~2x. Sem backfill — `coinsPerHour` nunca é persistido, sempre recalculado ao vivo do `RarityScore` já gravado.
- **Sinergia de cor — 3 versões:** v1 (29/07) só cauda, teto +80% fácil de bater com poucos peixes. v2 (14/08) por parte separada (3 partes somam bônus independente), pior caso caiu de +80%→+45% — mas medido em dados reais (`tank-income`, 15/08) um tanque de 15 peixes batia o teto nas 3 partes com só 10 peixes, rendendo +40,4% — a suposição de "raro" não se confirmou. v3 (15/08, vigente): teto por parte 15%→10% (pior caso 45%→30%) E N pra saturar 5→9 (`SynergyBaseBonus` 0.075→0.03, `SynergyPerExtraMatch` 0.025→0.01). Revalidado: uplift total +40,4%→+19,4%, ninguém mais bate o teto sozinho.
- **Degradação escala com peixes:** k=0.10 (29/07) → k=0.30 (07/08, payback do auto-filtro de ~9-12 dias caiu pra ~3,5-7,3 dias) → ponderada por raridade (08/08): peso por peixe = `rarityScore/DegradationRarityRefScore(=5)` em vez de contagem simples — tanque rico suja mais que tanque grande-mas-comum. Comum (score~5) continua peso 1; raro (score~8,65) peso ~1,73; épico/lendário (score~15) peso 3.
- **Geração:** 15min (lançamento) → 25min (29/07, lendário ~1/mês pro jogador ativo) → 60min (31/07, ritmo anti-rush, ver §8.11) → **10min hoje, mas é override TEMPORÁRIO de QA (07/08/2026), ainda ativo — ver CLAUDE.md §8.11 pro aviso vigente.**
- **Bug corrigido (08/08/2026): cor de cauda de filhote divergia entre cliente e servidor.** `GameService.TailColorOf` cacheava por `seed` e chamava `Generate(seed)` puro — errado pra filhote de breeding (trait real vem de `BreedTraits`, herança dos pais). Cliente já usava o motor certo, então tanque com filhotes tinha cor exibida divergindo da cor usada pro cálculo de sinergia — sintoma relatado como "prejuízo mesmo com água 100%". Fix: cache trocou de `seed` pra `CreatureInstance.Id`, resolve via `BreedTraits` com ancestralidade completa quando aplicável.
- **Bug de simulação (30/07/2026):** o "jogador sintético" de `Vivarium.Simulation simulate` só comprava filtro manual se `!hasAutoFilter` — mas auto-filtro só reduz degradação pela metade, não substitui o filtro manual. Água caía a 0 permanentemente por volta do dia 10-11 em todo perfil, travando a renda pra sempre (bug só do simulador, não do jogo real — corrigido no simulador).

---

## §8.8 — Breeding: histórico de calibração e mecanismos removidos

- **Gestação — corte assimétrico (06/08/2026):** `BaseGestationHours` 24→6 (não 8→24→6 como versão antiga sugeria — a sequência real foi 8→24 em 30-31/07 por anti-rush, depois 24→6 em 06/08 porque 2 dias pra cruzar incomuns era tempo demais e nem custo em soft nem risco de morte freavam pares ricos o suficiente). `GestationGrowth` 0.12→0.185 pra compensar e manter o topo do Lendário quase intocado. `MinGestationHours` seguiu (12→6).
- **Ajustes temporários de QA na gestação, histórico de valores (todos revertidos exceto o de 11/08, ainda ativo — ver CLAUDE.md §8.11):** 07/08 dividido por 10 (Base/Min 6→0,6h, Max 240→24h) só pra testes; 11/08 foi além, `Min=Max=Base=1.0` pra TODO casal gestar em exatamente 1h — motivo explícito: aquários serão resetados no lançamento, maximizar volume de cruzamentos agora pra achar bugs de herança rápido. **Ainda ativo hoje.**
- **Viés de raridade na herança estendido (31/07/2026):** até 30/07 só `ShimmerTier` usava `BiasedInheritProbability`; cor/padrão de parte herdavam 50/50 puro, deixando filhote "regredir" perto do piso da população mesmo vindo de pais decentes (usuário relatou incomum×raro→comum score 2.8). Estendido reusando `RarityBiasStrength=0.15`. Efeito agregado: "filhos que superam o pai mais raro" subiu de 25.18%→29.62%.
- **Bug crítico (30/07/2026, 3ª iteração do breeding):** filhote tinha `RarityScore` certo mas era EXIBIDO via `Generate(childSeed)` — seed novo aleatório sem relação com os pais (78% de chance de sair cinza). Fix: `ParentASeed`/`ParentBSeed` denormalizados + port completo de `BreedTraits` no frontend (verificado 1:1 contra C#, 5.000 seeds).
- **Mecanismo de avô (31/07/2026 → REMOVIDO 13/08/2026):** nasceu de um bug real (`BreedTraits` reconstruía um pai-que-é-filhote com `Generate(seed)`, traits fantasmas) que virou mecânica: chance pequena (`GrandparentReachChance`) de um traço vir de um AVÔ em vez do pai direto, tipo traço recessivo. Trajetória do valor: 0.15 (31/07) → 0.03 (12/08, pois com 8 sorteios independentes por cruzamento a chance de pelo menos 1 traço vir de avô era ~72.8%, dominando a percepção) → 0.01 (12/08, mesmo dia, segundo ajuste) → 0.001 (12/08, terceiro ajuste, residual mínimo) → **removido por completo em 13/08/2026** quando os traits passaram a ser congelados no nascimento (ver §8.19.2 no CLAUDE.md) — a reconstrução de ancestralidade profunda deixou de ser necessária, então `ParentAncestry`, `ResolveOwnTraits`, `EffectiveParentTraits` e o mecanismo inteiro saíram do código.
  - **Bugs reais causados pela reconstrução de ancestralidade limitada (todos resolvidos pela remoção do mecanismo):**
    - `FishCanvas` nunca recebia os 4 campos de avô (10/08/2026) — todo desenho de peixe filhote renderizava errado quando o mecanismo de avô tinha sido exercido de verdade, enquanto o texto "por que é raro" (que usa a criatura completa) mostrava certo. Corrigido then (props + todos os callers), mas a causa raiz só fechou de vez com a remoção do mecanismo.
    - `ChildTierDistribution` (prévia "chance do brilho do filhote") usava `Generate(seed)` puro nos pais, ignorando ancestralidade (12/08/2026) — prévia mostrava 98,2% de chance de sair sem brilho cruzando dois Lendários. O resultado REAL do cruzamento já usava `ParentAncestry` corretamente desde 31/07; só a prévia estava errada.
    - Investigação de "resultados estranhos" (12/08/2026, conta `marcospdn`) confirmou que a engine estava correta em todos os casos — inclusive um caso "impossível" (2 pais cauda Laranja, filho Vermelho) era mutação genuína, não bug.
  - **Lição que sobrevive à remoção:** ao reconstruir estado derivado com profundidade limitada (aqui, 2 gerações de ancestralidade), qualquer objeto "parcial" que esqueça um campo produz resultado silenciosamente errado — isso motivou a decisão arquitetural de congelar em vez de sempre reconstruir (§8.19.2).
- **Anti-duplicação — bug de inversão (13/08/2026, corrigido no mesmo dia):** `DuplicationStreak.ApplyPenalty` multiplicava o `threshold` de herança pela penalidade sem limite — como `RarityBiasStrength` é sutil (threshold raramente >0.6), 1-2 heranças seguidas do MESMO pai (natural quando esse pai já tem os traços mais raros) derrubava o threshold abaixo de 0.5, INVERTENDO o lado favorecido pela raridade. Sintoma real: filhotes nascendo com score bem abaixo dos dois pais (ex: 8.1+6.3→3.3). Fix: `ApplyPenalty` agora só encolhe o threshold até 0.5 (neutro), nunca ultrapassa pro lado oposto quando o lado "ganhando" a sequência já é o mais favorecido pela raridade.
  - **Consequência descoberta na hora seguinte:** `RarityScore` desatualizado pros filhotes nascidos ANTES do fix (mesma classe do gap de §7) — 54 criaturas divergentes em 4 contas, corrigidas com `fix-scores`.
- **"Filhote de elite com score abaixo do pai mais fraco" (13/08/2026) — investigado, não é bug.** Pais de elite quase monocromáticos rendem o bônus de conjunto coeso (§5.1); herança por slot é independente, então um filhote pode puxar cor de A e padrão de B, perdendo o bônus que ambos os pais tinham parcial ou totalmente. A única garantia documentada é POR TRAIT, nunca foi prometido score TOTAL do filhote ≥ pai mais fraco. Decisão do usuário: manter como está, sem mudança de código.
- **Piso de mutação (13/08/2026):** medido impacto econômico via `Vivarium.Simulation mutationfloor` (2.000 indivíduos, 20 gerações) — RarityScore médio da população estabiliza em +2,2% acima do baseline sem piso (converge, não dispara); % Lendário oscila 0–0,8% nos dois cenários, sem sinal de inflação. Fechado em 100% de garantia (sem dial residual).
- **Chance de morte / mitigação (31/07/2026):** três alavancas implementadas na mesma sessão — descanso passivo (`LastBredAt`, meia-vida 5 dias, real; **override temporário de QA 0.2 ainda ativo hoje, ver CLAUDE.md §8.21**), estabilizador (soft, reduz risco pela metade), seguro (premium, zera risco).

---

## §8.19–8.19.2 — Traits congelados no nascimento: a jornada completa

**8.19 (12/08/2026):** feature original — histórico de cruzamentos (`GET /api/breeding/history`) + chance/origem de cada atributo no reveal (`probPct`+`source` em `rarityBreakdownOf`). Motivado por investigação real (usuário achou a herança "aleatória demais"; consultas diretas ao banco confirmaram engine correta em todos os casos).

**8.19.1 (12/08/2026, decisão revertida no dia seguinte):** um caso real (conta `EoNeng`) mostrou cauda do filhote rotulada "Herdado do Pai B" com cor que o Pai B não tinha na própria tela — `ResolveOwnTraits` reconstruía avós SEMPRE frescos, mesmo quando eram eles próprios filhotes com traits reais diferentes. Correção "óbvia" (usar os 6 campos completos do pai) foi implementada, testada (21 testes verdes) e **revertida antes do deploy** — o problema é recursivo: corrigir a precisão do PAI exige dados que não cabem no FILHO (schema limitado a 2 gerações), então o mesmo bug reapareceria um nível mais fundo quando o filho virasse pai de um neto. Decisão do dia: manter como estava, documentar o trade-off.

**8.19.2 (13/08/2026, decisão final que substituiu a de 8.19.1):** a "decisão de manter como está" durou menos de um dia — novo cruzamento real (`marco`) reproduziu a mesma classe de bug, e o usuário perguntou diretamente "se eu só aumentar a profundidade guardada, o problema não volta a acontecer assim que passar dessa profundidade nova?" — resposta é sim, gatilho pra abandonar "reconstruir sob demanda com profundidade limitada" de vez.

**Mudança de arquitetura (vigente hoje, ver CLAUDE.md §7/§9):** traits calculados uma única vez no nascimento e congelados em `CreatureInstance.TraitsJson`. Cruzar um filhote passa a LER o `TraitsJson` já resolvido de cada pai, sem limite de profundidade de reconstrução. `ParentAncestry`, `ResolveOwnTraits`, `EffectiveParentTraits` e o mecanismo de avô foram removidos por completo (decisão do usuário: remover, não adaptar).

- Schema: `TraitsJson`+`BreedingSourceJson` novos; `Seed`/seeds de ancestralidade viram puramente históricos/auditoria.
- Auditabilidade preservada: `Vivarium.AdminReset -- audit-ancestry` percorre toda a cadeia sem limite de profundidade (mais forte que a auditoria antiga, limitada a 2 gerações).
- Backfill: `Vivarium.AdminReset -- backfill-traits`, processa em ordem de criação, cascata determinística.
- Frontend: `generator.js` teve toda a réplica JS do motor de herança removida (`breedTraits`, `resolveOwnTraits`, `effectiveParentTraits`, etc.) — `traitsOf(creature)` virou `return creature.traits`. Maior ganho colateral: não existe mais obrigação de manter dois motores (C#/JS) bit-a-bit sincronizados pra EXIBIR um peixe.
- Ordem de deploy obrigatória: migration → `backfill-traits` → `audit-ancestry` → deploy backend → deploy frontend.

---

## §8.21.2 — Ver §8.8 acima ("filhote de elite" já coberto)

---

## §8.22 — Bug real no fundo da tela de login (13/08/2026)

`AuthView.jsx` renderiza um aquário decorativo (`demoFish`, 6 peixes fake) no fundo da tela de login. Desde a mudança de traits congelados (mesmo dia), `traitsOf(creature)` parou de ter fallback pra `generateTraits(seed)` quando `.traits` está ausente, e `demoFish` nunca tinha esse campo — tela de login quebrava intermitentemente (`Cannot read properties of undefined`). Corrigido preenchendo `demoFish` com `generateTraits(BigInt(seed))` de verdade. **Lição (já no CLAUDE.md como bullet):** qualquer objeto "peixe" fabricado fora da API precisa de `.traits` desde 13/08/2026.

---

## §8.22.1 — Gap real no deploy de 13/08/2026: peixe criado na janela migration→backend

Usuário relatou (conta `marco`) um peixe comum "sumido" — não aparecia em nenhuma lista. Investigação achou a criatura `#1405`, criada `2026-08-13 14:47:04`, `TraitsJson` nulo — único caso em 1040 criaturas. Causa: entre aplicar a migration (coluna já existe) e o deploy do backend NOVO terminar (~4min de rebuild+restart), o backend ANTIGO continuou rodando e recebendo requisições reais — qualquer peixe coletado nessa janela nasceu com `TraitsJson=NULL`. `traitsOf` devolvia `null`, quebrando a renderização em qualquer lista que tentasse desenhar o peixe (daí "sumir"). Correção: rodar `backfill-traits` de novo (idempotente) — as 1039 já corretas recalculam pro mesmo valor, só a `#1405` mudou de fato. **Lição já capturada no CLAUDE.md:** rodar o backfill uma SEGUNDA vez, depois do backend novo estar no ar, sempre que uma migration adicionar uma coluna preenchida na criação.

---

## §10 — VM Oracle travando por falta de swap (07/08/2026)

A VM tem só 954 MB de RAM e nenhum swap por padrão (Always Free micro). `docker compose up -d --build` (que roda `dotnet publish` dentro do build stage) esgotou a memória e travou a VM inteira (SSH e HTTPS pararam de responder pra qualquer IP — inicialmente suspeitou-se de `fail2ban`, descartado ao confirmar de outro IP). Reboot pelo console do Oracle destravou (containers voltaram sozinhos via `restart: unless-stopped`, mas com imagem antiga). Fix: swapfile de 2GB (`/swapfile`, persistido em `/etc/fstab`) — rebuild completo (~170s) terminou sem travar depois disso. **Lição (já no CLAUDE.md):** builds de imagem (`--build`) nessa VM só devem rodar com o swapfile ativo (`free -h` pra confirmar `Swap: 2.0Gi` antes).

---

## §12.1 — Bugs de hardening/mercado corrigidos

- **Listar peixe da Mochila sempre retornava erro (12/08/2026).** `MarketService.CreateListingAsync` checava `HabitatId is null` como "já está no mercado" — mas isso também é o estado normal de peixe guardado na Mochila. Listar do Tanque funcionava por acaso; listar da Mochila (fluxo comum) sempre falhava 400. Corrigido pro check certo: existe `MarketListing` ATIVA pra essa criatura. Aproveitado pra fechar 2 gaps relacionados: bloquear listar criatura já vendida ao NPC ou presa numa gestação em andamento (nenhum tinha proteção antes).
- **Robustez do polling idle (30-31/07/2026):** `useGame.js`/`useBreeding.js` engoliam falha de heartbeat/refresh em silêncio — quem deixa a aba aberta como tela de fundo não percebia dessincronia. Contador de falhas consecutivas + faixa de aviso em destaque (`.sync-banner`) a partir de 2 falhas, sugerindo F5.

---

## §13 — Identidade visual: histórico v1→v3

**v2 (30/07/2026):** pivot pra tema claro/vibrante — revertido no mesmo dia a pedido do usuário.

**v3 (30/07/2026, vigente):** de volta ao "aquário profundo" escuro original — fundo escuro (`--bg-top/mid/bottom` #04181f→#020d12), glass escuro translúcido, acentos vibrantes (aqua `--glow` #54e6d1, azul `--glow-2` #7ad3ff, coral, âmbar `--gold`). Tipografia: Fraunces (display) + Hanken Grotesk (UI). Raridade `--r-*` espelha `BANDS` em `fishRenderer.js`.

## §13 — Responsividade mobile: histórico de correções (10–12/08/2026)

- **10/08/2026:** relatos de "tamanhos errados" (peixe cortado no modal) — causa era `FishCanvas` (`<canvas width={N}>` fixo em px) dentro de containers flex sem largura própria resolvível; `.fish-canvas{width:100%}` não tinha o que preencher. Corrigido replicando o padrão `width:min(Npx,100%)` já usado em `.parent-preview-card`. `.nav-pills` ganhou `flex-wrap`. **Não foi possível confirmar visualmente** — `resize_window`/device toolbar do Chrome DevTools não funcionam neste ambiente de automação; validado só por leitura de código + suíte de testes.
- **11/08/2026 (2 screenshots reais de celular):** cabeçalho mobile redesenhado — de 2 linhas empilhadas (~5 linhas de chrome) pra 3 linhas fixas via CSS `order` (marca+conta / abas em tira rolável / carteira+stats), sem mudar ordem no DOM (desktop pixel-idêntico via `display:contents`). Modal "gigante e incompleto" — 2 bugs reais: (1) `.modal-close` era filho absolute do container que rolava, empurrado pra fora em conteúdo alto; fix: `.modal-body` isolado que rola, `.modal-close` fora dele. (2) achado escondido atrás do primeiro: `.tank-layout{isolation:isolate}` prendia modais no stacking context local, perdendo a disputa de pintura pro `.topbar` (z-index 20) — × ficava incliclável. Fix definitivo: `Modal.jsx` virou `createPortal(..., document.body)`. `.sticky-bar`/`.leaderboard-row` ganharam `flex-wrap` (overflow horizontal de página real, achado nesta revisão).
  - Testes: `cy.viewport(390,...)` — única forma confiável de emular tela estreita neste projeto.
- **12/08/2026, três ajustes no mesmo dia:**
  1. Seções "Atributos"/"Por que é raro" viraram recolhíveis (`CollapsibleSection`, nasce fechado) — reusa o padrão já existente em `TankView.jsx`/`HowItWorksGuide.jsx`.
  2. Toast ganhou dismiss por clique (`useToast.dismiss()`) — antes só sumia sozinho em 4s, bloqueando cliques em botões embaixo dele. Bug latente corrigido junto: dois `notify()` próximos no tempo tinham risco do timer do primeiro apagar o segundo cedo demais.
  3. Modal de detalhe não centralizado em celular real, reportado pelo dono do site — investigado com `getBoundingClientRect()`/`scrollWidth` medidos via Cypress (não só leitura de CSS). **Duas causas reais:** (a) `.modal-backdrop{display:grid;place-items:center}` não centraliza a trilha, só o item dentro dela — sem `grid-template-columns` a trilha é dimensionada pelo conteúdo (`width:min(560px,96vw)`), que excede o espaço disponível abaixo de ~1000px, vazando o excesso pra um lado só (~12-14px de desvio constante). Fix: `place-content:center`. (b) `.tank-layout::before` (vinheta do modo cinema, `inset:-20px`) causava overflow horizontal genuíno quando `.content{padding}` (16px mobile) é menor que os 20px de bleed. Fix: bleed só vertical + `overflow-x:hidden` em `html,body` como rede de segurança. Passe de polish: alvos de toque pequenos ganharam `min-width/height` 40px/38px no mobile; `.tank-tools` (opacidade 0.6 fora de hover) corrigido pra opacidade 1 em `@media(hover:none)` (invisível em qualquer dispositivo de toque, não só mobile estreito).

---

## §13 — Testes de frontend: cobertura ampliada (10/08/2026)

Até então só Tanque, auth, recompensa diária e banner de sincronização tinham E2E. Adicionados: Mercado, Mochila, Loja, Ninho (fluxo principal), Ranking (11 specs novos). Achado na varredura: `RankingView.jsx` sempre renderizava `<Coin/>` mesmo na métrica "Raridade total" (não é moeda) — corrigido pra usar 🏆. Pegadinha de teste (não bug de produto): `cy.contains(seletor, texto)` é case-sensitive mas faz *substring* match — "Filtro" batia tanto no card do item quanto no heading "Filtro automático"; usar regex ancorada ou escopar por `.closest`/`.within` quando o texto se repete dentro e fora de um modal.
