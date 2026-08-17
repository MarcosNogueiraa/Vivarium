# Vivarium — Backlog de ideias futuras (não implementadas)

> Este arquivo **não** é carregado por padrão em toda sessão — é o `CLAUDE.md` que cumpre esse papel. Aqui ficam ideias de feature já discutidas mas ainda não implementadas: o que são, por que, o que já foi decidido, o que ainda está em aberto, e que mecanismo/código existente dá pra reaproveitar quando forem puxadas pra implementação de verdade. Quando uma entrada for implementada, ela sai daqui e vira uma seção real em `CLAUDE.md` (mesmo caminho que a Caixa de Entrada já percorreu — foi backlog, depois §7.19).

---

## ~~1. Notificação de venda no Mercado~~ — IMPLEMENTADO (16/08/2026)

Ver `CLAUDE.md §7.19`. `InboxService.QueueSystemMessage` é o helper genérico resultante (sem exigir contexto de admin), usado por `MarketService.BuyAsync` — reusável pras próximas notificações de sistema deste backlog.

---

## 2. Link de indicação (referral) — 10% de comissão em compras de diamante (16/08/2026)

**O quê:** jogador indica outro via link; toda compra de diamante (premium) que o indicado fizer, o indicador ganha 10%.

**Decisão já tomada:** percentual é 10%, sobre o valor COMPRADO (não sobre o que o indicado eventualmente gasta depois).

**⚠️ Bloqueio real e total, confirmado no código:** não existe processador de pagamento integrado. `TransactionType.CurrencyPurchase` só é usado pelo endpoint `/api/dev/coins?currency=PREMIUM` (dev-only, nunca existe em produção). Não existe `ReferralCode`/`ReferredBy` (ou qualquer campo equivalente) em lugar nenhum do schema hoje. Essa feature **não tem como funcionar de verdade** até o gap de pagamento (já documentado em `CLAUDE.md §7.11`/§10) ser resolvido — não faz sentido implementar a comissão em si antes disso.

**O que dá pra decidir/preparar desde já, sem o pagamento existir (é estrutural, não depende de dinheiro real):**
- `User.ReferralCode` (gerado automaticamente no registro, único).
- `User.ReferredByUserId` (FK nullable, setado no registro se um `?ref=CODE` válido tiver sido usado).

A comissão em si (crédito de 10% + um `TransactionType.ReferralCommission` novo) só entra quando o `CurrencyPurchase` real existir.

**Decisões em aberto:**
- O que conta como "entrar pelo link"? Só criar a conta, ou precisa de alguma primeira ação (ex: coletar o primeiro peixe)?
- A atribuição de indicação expira depois de um tempo, ou é permanente (toda compra futura do indicado sempre gera comissão)?
- Proteção contra fraude: um jogador se indicando com uma segunda conta própria (multi-conta) pra ganhar 10% de si mesmo.

---

## 3. Ovo de peixe (loot box em diamante) (16/08/2026)

**O quê:** item comprado em diamante que entrega um peixe aleatório, com chance melhorada de atributos de alta raridade de acordo com o tier do item comprado.

**Decisão já tomada:** metáfora "ovo de peixe", não "baú" — mais coerente com o tema aquário/genética do jogo (peixe nasce de ovo, não de tesouro), e alinhado com o sistema de raridade genética já existente.

**Mecanismo reusável já existente (confirmado no código):** `WeightedTable.PickBiasedTowardRare<T>` (`Vivarium.Core/Generation/WeightedValue.cs`, linhas 130-156) já existe e já vieza um sorteio pro lado raro — hoje só é usado dentro da mutação do breeding (`TraitGenerator.BreedTraits`/`InheritOrMutate`). É exatamente o mecanismo pronto pra qualquer "loot" com odds melhoradas — cada tier de ovo passaria um `biasStrength` diferente.

**A fazer quando implementar:** `TraitGenerator.Generate(seed, configVersion)` hoje sempre usa `WeightedTable.Pick` puro (sem bias). Precisa de uma variante (ex: `GenerateBiased(seed, biasStrength)`, ou um parâmetro opcional em `Generate`) que aplica o bias pelo menos no tier de shimmer (e possivelmente também cor/padrão das partes).

**Decisões em aberto:**
- Quantos tiers de ovo (ex: Comum/Raro/Lendário, preço em diamante crescente por tier)?
- O peixe entregue vai pra Caixa de Entrada (consistente com Mercado/Transferência, `CLAUDE.md §7.19`) ou direto pro tanque/mochila como a coleta normal da fila?
- `ItemService.BuyAsync` hoje é feito pra itens permanentes/consumíveis de manutenção (filtro, upgrade de tanque, sensor) — "abrir e gerar um peixe na hora" é um tipo de compra diferente (gera uma entidade nova, não só aplica um efeito num habitat). Pode precisar de um `ItemCategory` novo (`Egg`) tratado à parte no switch de `BuyAsync`, ou um endpoint dedicado em vez de reusar `/api/items/{key}/buy`.

**Bloqueios:** nenhum técnico — diamante (premium) já existe como moeda e já pode ser ganho por outras vias (VIP, admin). O apelo comercial pleno da feature (jogador comprando diamante de verdade pra abrir ovos) depende do mesmo gap de processador de pagamento da feature 2, mas a mecânica em si já pode ser implementada e teria uso real (premium ganho de outras formas).

---

## 4. Redesenho da recompensa diária (16/08/2026)

**O quê:** hoje (`GameService.CanClaimDailyReward`/`ClaimDailyRewardAsync`, linhas 488-532) é flat — `EconomyDefaults.DailyRewardSoft = 25`, 1x por dia calendário UTC, sem streak, sem variância. O usuário quer repensar isso combinando 4 direções:

1. **Bônus por dias consecutivos.** Cuidado de design pra quando for implementar: streak QUEBRAR ao faltar um dia contradiz a filosofia já documentada no projeto ("ausência nunca pune duro" — mesmo princípio de `CLAUDE.md §7.3`/§7.6, diferença online/offline sempre moderada, água nunca "mata" nada). Recomendação: streak só ADICIONA upside (cresce o bônus), nunca reduz o valor abaixo do piso base — faltar um dia no máximo reseta o bônus acumulado, nunca deixa o jogador pior do que estava no dia 1.
2. **Valor escalado pela renda do aquário.** Fração do `coinsPerHour` atual do jogador em vez de um valor fixo — faz sentido tanto pra tanque grande quanto pequeno. Precisa de um piso mínimo pra quem começou hoje (tanque quase vazio, renda baixa).
3. **Roleta de valores variáveis.** Sorteio dentro de uma faixa em vez de um valor determinístico — mais expectativa/dopamina a cada resgate.
4. **Chance de peixe grátis.** Probabilidade pequena de vir um peixe já pronto em vez de/além do soft, com odds de raridade melhores que a coleta automática padrão — reaproveitaria o mesmo mecanismo de bias da feature 3 (`PickBiasedTowardRare`).

**Decisões em aberto:**
- Como as 4 direções se combinam num único fluxo? (ex: valor base = f(renda do aquário) → roleta aplica variância em cima desse valor base → streak multiplica o resultado final → peixe grátis é um bônus à parte, com chance independente das outras 3 mecânicas.)
- Schema novo provável: `User.DailyRewardStreak` (contador) + possivelmente algum campo de quando o streak quebrou, se streak resetar visualmente precisar ser mostrado ao jogador.

**Bloqueios:** nenhum.

---

## 5. Rate limiting de login + limite de uso do "esqueci minha senha" (16/08/2026)

**O quê:**
(a) Limitar tentativas de login pra reduzir exposição a força bruta.
(b) Limitar o uso de "esqueci minha senha" porque o serviço de email (Resend, plano grátis) tem cota diária de envios.

**Estado atual confirmado no código:**
- `Program.cs` (linhas 90-101): rate limit é só por IP (`RateLimiting:AuthPerMinute`, default 10/min, fixed-window, chave = IP da conexão), registrado como policy `"auth"`.
- `AuthEndpoints.cs` (linha 17): TODO o grupo `/api/auth` (`/register`, `/login`, `/forgot-password`, `/reset-password`) compartilha essa mesma policy — `/forgot-password` não tem nenhum limite adicional além do genérico de 10/min/IP.
- Não existe lockout POR CONTA após N tentativas de login falhas — nenhum campo tipo `FailedLoginCount`/`LockedUntil` existe no `User` hoje (confirmado via grep, zero resultados).
- `PasswordResetService.RequestAsync` (linha 25) já tem anti-enumeração (sempre responde igual, exista ou não a conta) e já invalida tokens anteriores não usados ao pedir de novo — mas não tem nenhum teto de VOLUME de envio.

**Decisões em aberto:**
- **Login:** decidir N (quantas tentativas falhas até travar), duração do lockout, e se o contador reseta no login bem-sucedido ou só depois do lockout expirar. Campo novo no `User`.
- **Forgot-password:** precisa de um limite mais apertado que o "auth" geral (10/min/IP não impede um usuário de esgotar a cota diária de email só sendo paciente) — duas camadas possíveis, não necessariamente excludentes:
  - Por conta/email: mínimo de tempo entre pedidos (ex: 1 por 5-10min por email).
  - Teto diário GLOBAL de emails enviados pelo sistema inteiro (contador simples em banco/cache, já que não há visibilidade direta da cota real do Resend) — precisa bloquear silenciosamente sem quebrar a resposta anti-enumeração já existente (o teto de cota não pode vazar a informação de "atingiu o limite" de um jeito que ajude a enumerar contas).

**Bloqueios:** nenhum — pode ser puxada a qualquer momento. É a mais orientada a segurança das 5 (sem incidente reportado até agora, mas antecipatória).
