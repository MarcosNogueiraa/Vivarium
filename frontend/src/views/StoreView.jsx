import { useCallback, useEffect, useRef, useState } from "react";
import { api } from "../lib/api.js";
import { Coin } from "../components/Coin.jsx";
import { ConfirmModal } from "../components/ConfirmModal.jsx";
import { Modal } from "../components/Modal.jsx";
import { CollectCelebration } from "../components/CollectCelebration.jsx";
import { EggIcon } from "../components/EggIcon.jsx";
import { EGG_TIER } from "../lib/eggs.js";
import { FILTER_WARN_THRESHOLD, tankFishWeight } from "../lib/tankMath.js";
import { useAutoToggles } from "../hooks/useAutoToggles.js";

const DESCRIPTIONS = {
  filter_basic: "Uso único: limpa a água na hora, restaurando para 100%.",
  auto_filter: "Permanente: limpa a água sozinho, sem precisar comprar filtro toda hora. Cobre um tanque de até 5 peixes comuns — peixes raros contam mais nessa conta. Acima da cobertura o efeito não desliga de uma vez, só vai enfraquecendo aos poucos.",
  auto_filter_2: "Permanente: versão mais forte do Filtro Automático, cobre até 10 peixes comuns. Substitui o nível anterior — os dois não somam.",
  auto_filter_3: "Permanente: o nível mais forte de Filtro Automático, cobre até 18 peixes comuns. Ideal pro Aquário Master lotado.",
  tank_upgrade: "Abre mais 1 vaga no tanque (o preço sobe a cada compra). Só funciona dentro do aquário atual — pra crescer além do limite dele, é preciso trocar de aquário.",
  aquario_grande: "Troca para um aquário maior, com espaço para 5 a 10 peixes. Preço fixo e alto — é uma conquista de médio prazo, não um upgrade do dia a dia.",
  aquario_master: "Troca para o maior aquário do jogo, com espaço para 10 a 15 peixes. O investimento mais caro disponível.",
  water_sensor: "Permanente, por aquário: dá controle sobre a Limpeza Automática de VIP (veja o card VIP acima) — sem ele, VIP só limpa a água quando ela zera; com ele, você escolhe a partir de qual % isso acontece. Preço sobe se você trocar pra um aquário maior antes de comprar.",
  egg_common: "Gera 1 peixe na hora, com ~3× mais chance de sair Lendário que a coleta normal. Vai direto pro tanque (ou mochila, se o tanque estiver cheio).",
  egg_rare: "Gera 1 peixe na hora, com ~15× mais chance de sair Lendário que a coleta normal.",
  egg_legendary: "Gera 1 peixe na hora, com ~63× mais chance de sair Lendário que a coleta normal (de 1 em 5.000 pra cerca de 1 em 80). Ainda é sorte, não garantia — veja a tabela completa no Guia de Raridade.",
};

// Ícone + "peso" visual por item — sem isso todo card da loja tinha o mesmo
// destaque, de um filtro de 20 soft a um Aquário Master de 12000 (feedback
// do usuário). Master usa coroa: mesma linguagem "premium" da moldura
// dourada do tank-stage (TankView.jsx tier-master).
const ICONS = {
  filter_basic: "💧",
  auto_filter: "⚙️", auto_filter_2: "⚙️", auto_filter_3: "⚙️",
  tank_upgrade: "📐",
  aquario_grande: "🐳",
  aquario_master: "👑",
  water_sensor: "🧪",
};
const TIERS = { aquario_grande: "rare", aquario_master: "premium", egg_rare: "rare", egg_legendary: "premium" };

const FILTER_KEYS = ["auto_filter", "auto_filter_2", "auto_filter_3"];

/// Slider do gatilho da Limpeza Automática (§8.18) — só aparece depois de comprado o Sensor.
/// Salva com debounce (400ms) pra não disparar 1 request por pixel arrastado.
function WaterSensorSlider({ tank, notify, onSaved }) {
  const [value, setValue] = useState(Number(tank?.autoCleanTriggerPercent ?? 0));
  const timer = useRef(null);

  useEffect(() => { setValue(Number(tank?.autoCleanTriggerPercent ?? 0)); }, [tank?.autoCleanTriggerPercent]);

  function onChange(e) {
    const next = Number(e.target.value);
    setValue(next);
    clearTimeout(timer.current);
    timer.current = setTimeout(async () => {
      try { await api.setAutoCleanTrigger(next); await onSaved(); }
      catch (err) { notify(err.message); }
    }, 400);
  }

  const max = Number(tank?.waterSensorMaxTriggerPercent ?? 80);
  return (
    <div className="water-sensor-control">
      <div className="card-row" style={{ justifyContent: "space-between" }}>
        <span className="muted">Limpar automaticamente quando a água chegar a</span>
        <b>{value}%</b>
      </div>
      <input
        type="range" min={0} max={max} step={1} value={value} onChange={onChange}
        aria-label="Gatilho da limpeza automática"
      />
      {!tank?.isVip && <p className="faint">Só tem efeito com VIP ativo — a configuração fica guardada até você assinar.</p>}
    </div>
  );
}

/// Opt-out da coleta automática/Limpeza Automática de VIP — checkboxes simples (sem debounce,
/// diferente do slider do sensor, já que aqui é um só clique por vez, não arrasto contínuo).
/// Salva na hora via useAutoToggles (compartilhado com os ícones rápidos do Tanque).
function AutoToggles({ tank, notify, onSaved }) {
  const { busy, setCollect, setClean } = useAutoToggles(tank, notify, onSaved);
  return (
    <div className="card-row" style={{ flexDirection: "column", alignItems: "flex-start", gap: 6 }}>
      <label className="filter-toggle">
        <input
          type="checkbox" checked={tank?.autoCollectEnabled ?? true} disabled={busy}
          onChange={(e) => setCollect(e.target.checked)}
        />
        Coleta automática
      </label>
      <label className="filter-toggle">
        <input
          type="checkbox" checked={tank?.autoCleanEnabled ?? true} disabled={busy}
          onChange={(e) => setClean(e.target.checked)}
        />
        Limpeza automática
      </label>
      {!tank?.isVip && <p className="faint">Só tem efeito com VIP ativo — a configuração fica guardada até você assinar.</p>}
    </div>
  );
}

export function StoreView({ tank, refreshTank, notify }) {
  const [items, setItems] = useState(null);
  const [warnFilter, setWarnFilter] = useState(false);
  const [vip, setVip] = useState(null);
  const [vipBusy, setVipBusy] = useState(false);
  const [celebrate, setCelebrate] = useState(null); // { creature, eggTier } — peixe recém-nascido de um ovo
  const [readMore, setReadMore] = useState(null); // { name, text } — descrição truncada aberta por inteiro
  const [clamped, setClamped] = useState({}); // { [itemKey]: boolean } — só true quando o texto realmente estoura 3 linhas

  // Cards da loja tinham altura MUITO desigual (feedback do usuário, com print) — descrição do
  // Sensor de Qualidade da Água (bem mais longa que as outras) esticava aquele card sozinho.
  // Em vez de truncar por contagem de caracteres (frágil — muda com largura de tela/fonte), mede
  // o DOM de verdade: cada <p> tem `-webkit-line-clamp` (CSS) cortando visualmente em 3 linhas;
  // aqui só decide se o botão "Ler mais" aparece, comparando o texto cheio (scrollHeight) contra
  // o espaço truncado (clientHeight) — funciona pra qualquer texto, em qualquer largura.
  function measureClamp(key, el) {
    if (!el) return;
    const isClamped = el.scrollHeight > el.clientHeight + 1;
    setClamped((prev) => (prev[key] === isClamped ? prev : { ...prev, [key]: isClamped }));
  }

  const refresh = useCallback(async () => { setItems(await api.items()); }, []);
  useEffect(() => { refresh().catch((err) => notify(err.message)); }, [refresh, notify]);

  const refreshVip = useCallback(async () => { setVip(await api.vipStatus()); }, []);
  useEffect(() => { refreshVip().catch((err) => notify(err.message)); }, [refreshVip, notify]);

  async function buyVip(days) {
    setVipBusy(true);
    try {
      await api.subscribeVip(days);
      notify(`VIP +${days} dia(s)!`);
      await Promise.all([refreshVip(), refreshTank()]);
    } catch (err) { notify(err.message); }
    finally { setVipBusy(false); }
  }

  async function doBuy(item) {
    const result = await api.buyItem(item.key);
    setWarnFilter(false);
    if (result?.creature) {
      // Todo ovo abre a celebração (com a animação de chocar o ovo primeiro) — não só
      // Raro+. É uma compra deliberada, o jogador quer VER o peixe que saiu, mesmo comum.
      // Raro+ continua ganhando a revelação clique-a-clique por baixo (CollectCelebration
      // decide isso sozinho a partir do rarityScore).
      setCelebrate({ creature: result.creature, eggTier: EGG_TIER[item.key] ?? "common" });
    } else {
      notify(`${item.name} comprado!`);
    }
    await Promise.all([refresh(), refreshTank()]);
  }

  async function buy(item) {
    if (item.key === "filter_basic" && Number(tank?.maintenanceLevel ?? 0) >= FILTER_WARN_THRESHOLD) {
      setWarnFilter(true);
      return;
    }
    try { await doBuy(item); }
    catch (err) { notify(err.message); }
  }

  if (items === null) return <p className="hint">Carregando loja…</p>;
  const filterItem = items.find((i) => i.key === "filter_basic");

  // Nível ativo de fato = o de MAIOR índice possuído (níveis não empilham — o
  // melhor prevalece, GameService.FilterCapacityAsync). Os demais possuídos viraram
  // obsoletos — mostrar isso evita achar que "Adquirido ✓" em todos significa acumulado.
  const ownedFilterKeys = FILTER_KEYS.filter((k) => items.find((i) => i.key === k)?.owned);
  const activeFilterKey = ownedFilterKeys.at(-1) ?? null;
  const filterCapacity = Number(tank?.filterCapacity ?? 0);
  const fishWeight = tank ? tankFishWeight(tank.creatures) : 0;

  const premiumBalance = Number(tank?.wallet?.PREMIUM ?? 0);

  return (
    <div className="grid store-grid">
      <div className="card store-card store-card--premium vip-card">
        <span className="store-card-icon">👑</span>
        <strong>VIP</strong>
        <p className="muted">
          Coleta automática dos peixes prontos e Limpeza Automática da água (compra um Filtro sozinho
          quando a água zera — grátis, sem precisar de item nenhum), mas só enquanto o tanque está
          online (trocar de aba continua contando como online; fechar o navegador não). Pago em moeda
          premium, sem renovação automática — expira sozinho, sem cobrança recorrente.
        </p>
        <p className="muted">
          Quer que a limpeza aconteça antes da água zerar? O item <b>Sensor de Qualidade da Água</b>{" "}
          (mais abaixo na loja) libera esse controle.
        </p>
        <p className="muted">
          {tank?.isVip
            ? <>Ativo até <b>{new Date(tank.vipEndAt).toLocaleDateString("pt-BR")}</b></>
            : "Sem VIP ativo agora."}
        </p>
        <AutoToggles tank={tank} notify={notify} onSaved={refreshTank} />
        {vip && (
          <div className="card-row" style={{ flexWrap: "wrap", gap: 8 }}>
            {Object.entries(vip.packages).map(([days, price]) => (
              <button
                key={days} className="btn-primary" disabled={vipBusy || premiumBalance < price}
                title={premiumBalance < price ? "Saldo de moeda premium insuficiente" : undefined}
                onClick={() => buyVip(Number(days))}
              >
                {days}d · 💎{Number(price).toFixed(0)}
              </button>
            ))}
          </div>
        )}
      </div>
      <div className="card store-card filter-status-card">
        <strong>Filtro automático</strong>
        {activeFilterKey
          ? (
            <p className="muted">
              Nível ativo: <b>{items.find((i) => i.key === activeFilterKey)?.name}</b> — cobre até{" "}
              <b>{filterCapacity}</b> peixes comuns. Seu tanque hoje equivale a{" "}
              <b>{fishWeight.toFixed(1)}</b> peixes comuns (peixes raros contam mais que peixes comuns)
              {fishWeight > filterCapacity ? " — acima da cobertura, o efeito do filtro vai enfraquecendo aos poucos." : "."}
            </p>
          )
          : <p className="muted">Você ainda não tem filtro automático — a água suja na velocidade máxima. O filtro manual continua funcionando normalmente; considere um Filtro Automático pra não precisar comprar toda hora.</p>}
      </div>
      {items.map((item) => {
        const tier = TIERS[item.key];
        return (
        <div key={item.key} className={`card store-card${tier ? ` store-card--${tier}` : ""}${item.locked ? " store-card-locked" : ""}`}>
          <span className="store-card-icon">
            {EGG_TIER[item.key] ? <EggIcon tier={EGG_TIER[item.key]} /> : ICONS[item.key]}
          </span>
          <strong>{item.name}</strong>
          <p className="muted store-card-desc" ref={(el) => measureClamp(item.key, el)}>{DESCRIPTIONS[item.key] ?? ""}</p>
          {clamped[item.key] && (
            <button
              className="link-btn store-read-more"
              onClick={() => setReadMore({ name: item.name, text: DESCRIPTIONS[item.key] })}
            >
              Ler mais
            </button>
          )}
          {item.locked && <p className="muted store-locked-reason">🔒 {item.lockedReason}</p>}
          {item.key === "water_sensor" && item.owned
            ? <WaterSensorSlider tank={tank} notify={notify} onSaved={refreshTank} />
            : (
              <div className="card-row">
                <span className="price">
                  {item.currency === "PREMIUM" ? "💎" : <Coin />}{Number(item.price).toFixed(0)}
                </span>
                {item.locked
                  ? <span className="owned">Bloqueado</span>
                  : item.owned
                    ? (item.key === activeFilterKey
                      ? <span className="owned owned-active">Ativo ✓</span>
                      : <span className="owned">{FILTER_KEYS.includes(item.key) ? "Possuído (nível anterior)" : "Adquirido ✓"}</span>)
                    : (
                      <button
                        className="btn-primary" onClick={() => buy(item)}
                        disabled={item.currency === "PREMIUM" && premiumBalance < item.price}
                        title={item.currency === "PREMIUM" && premiumBalance < item.price ? "Saldo de moeda premium insuficiente" : undefined}
                      >
                        Comprar
                      </button>
                    )}
              </div>
            )}
        </div>
        );
      })}
      {warnFilter && filterItem && (
        <ConfirmModal
          title="Água já está limpa"
          message={`Sua água está a ${Number(tank?.maintenanceLevel ?? 0).toFixed(0)}% — um filtro agora não faria diferença na renda. Comprar mesmo assim?`}
          confirmLabel="Comprar mesmo assim"
          onConfirm={() => doBuy(filterItem)} onClose={() => setWarnFilter(false)}
        />
      )}
      {celebrate && (
        <CollectCelebration
          creature={celebrate.creature} variant="egg" eggTier={celebrate.eggTier}
          onClose={() => setCelebrate(null)}
        />
      )}
      {readMore && (
        <Modal onClose={() => setReadMore(null)} narrow>
          <strong>{readMore.name}</strong>
          <p className="muted" style={{ marginTop: 8 }}>{readMore.text}</p>
        </Modal>
      )}
    </div>
  );
}
