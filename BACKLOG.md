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

## ~~3. Ovo de peixe (loot box em diamante)~~ — IMPLEMENTADO (17/08/2026)

Ver `CLAUDE.md §7.9` (itens) e `§7.6` (schema/motor). Preços/vieses iniciais (Comum 8💎/bias 0.15, Raro 30💎/bias 0.35, Lendário 90💎/bias 0.55) são um ponto de partida — recalibrar com uso real como qualquer outro sistema econômico do jogo.

---

## ~~4. Redesenho da recompensa diária~~ — IMPLEMENTADO (17/08/2026)

Ver `CLAUDE.md §7.10`. As 4 direções entraram nessa ordem: base = `max(25, coinsPerHora×3)` → roleta ±40% em cima do base → streak (+5%/dia, teto +50%, reseta pra 1 ao faltar um dia) multiplica o resultado → 3% de chance de um Ovo Raro extra pela Caixa de Entrada, independente das outras 3. Lógica pura em `DailyRewardCalculator`.

---

## ~~5. Rate limiting de login + limite de uso do "esqueci minha senha"~~ — IMPLEMENTADO (18/08/2026)

Ver `CLAUDE.md §11.1`/`SecurityConfig.cs`. Lockout por conta (5 falhas → 15 min, zera no
sucesso) + freio de forgot-password (5 min entre pedidos por email + teto global de 80/dia)
— ambos silenciosos, sem alterar a resposta HTTP (anti-enumeração intacta).

---

## 6. Rever lógica de filtros de limpeza + valores de limpeza conforme o aquário cresce (18/08/2026)

**O quê:** usuário pediu pra revisitar (a) a lógica dos filtros de limpeza (manual + automático em níveis, §7.15) e (b) os valores/degradação de água conforme a faixa de capacidade do aquário cresce (Aquário → Aquário Grande → Aquário Master).

**Estado atual (referência, CLAUDE.md §7.15/§7.6):** `FilterCapacity` cobre X peixes comuns equivalentes, decai suavemente acima da cobertura (`FilterTaperExponent`); degradação da água já é ponderada por raridade (`DegradationPerFishFactor`) e por `CapacityBand.DegradationBandFactor` por faixa.

**Feito (18/08/2026):** preço do Filtro manual (`filter_basic`) escalando por faixa (20/50/120 soft, Aquário/Grande/Master) — era fixo em 20 soft sempre, ficando irrisório num tanque endgame. Ver `CLAUDE.md §7.15`.

**Ainda em aberto:** o resto — progressão de preço/cobertura dos filtros AUTOMÁTICOS vs. o crescimento em capacidade E raridade do tanque (achado real na sessão passada: um Aquário Master cheio de peixe raro pode pesar bem mais que os 18 cobertos pelo Filtro Automático III), e qualquer outra coisa que incomodar na lógica de degradação por faixa. Precisa de mais conversa específica antes de mexer.

**Bloqueios:** nenhum.

---

## ~~7. Níveis do jogador~~ — IMPLEMENTADO (18/08/2026)

Ver `CLAUDE.md §7.22`. XP por contagem de ações (coleta/breeding), só social/cosmético, nível sempre derivado ao vivo do XP. Avatar = peixe escolhido manualmente pelo jogador (não upload, não auto-atualização), aparece no Perfil e no Ranking. Ranking ganhou paginação real via SQL no mesmo pacote (motivado por essa feature precisar mostrar avatar/nível por linha).
