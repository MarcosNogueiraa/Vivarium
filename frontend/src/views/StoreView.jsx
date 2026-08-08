import { useCallback, useEffect, useState } from "react";
import { api } from "../lib/api.js";
import { Coin } from "../components/Coin.jsx";
import { ConfirmModal } from "../components/ConfirmModal.jsx";
import { FILTER_WARN_THRESHOLD, tankFishWeight } from "../lib/tankMath.js";

const DESCRIPTIONS = {
  filter_basic: "Restaura a qualidade da água para 100 na hora.",
  auto_filter: "Permanente: cobre até 5 de peso de peixes com metade da degradação (acima disso, o benefício cai aos poucos).",
  auto_filter_2: "Permanente: cobre até 10 de peso de peixes — upgrade do Filtro Automático (não acumula com ele).",
  auto_filter_3: "Permanente: cobre até 18 de peso de peixes — o nível mais forte, pro tanque Master cheio.",
  tank_upgrade: "+1 de capacidade no tanque (preço sobe a cada compra, muda de faixa em 5 e 10).",
};

const FILTER_KEYS = ["auto_filter", "auto_filter_2", "auto_filter_3"];

export function StoreView({ tank, refreshTank, notify }) {
  const [items, setItems] = useState(null);
  const [warnFilter, setWarnFilter] = useState(false);

  const refresh = useCallback(async () => { setItems(await api.items()); }, []);
  useEffect(() => { refresh().catch((err) => notify(err.message)); }, [refresh, notify]);

  async function doBuy(item) {
    await api.buyItem(item.key);
    setWarnFilter(false);
    notify(`${item.name} comprado!`);
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

  return (
    <div className="grid">
      <div className="card store-card filter-status-card">
        <strong>Filtro automático</strong>
        {activeFilterKey
          ? (
            <p className="muted">
              Nível ativo: <b>{items.find((i) => i.key === activeFilterKey)?.name}</b> — cobre até{" "}
              <b>{filterCapacity}</b> de peso. Seu tanque pesa <b>{fishWeight.toFixed(1)}</b> agora
              {fishWeight > filterCapacity ? " (acima da cobertura — o benefício cai aos poucos)." : "."}
            </p>
          )
          : <p className="muted">Você ainda não tem filtro automático — a água degrada na velocidade cheia.</p>}
      </div>
      {items.map((item) => (
        <div key={item.key} className="card store-card">
          <strong>{item.name}</strong>
          <p className="muted">{DESCRIPTIONS[item.key] ?? ""}</p>
          <div className="card-row">
            <span className="price"><Coin />{Number(item.price).toFixed(0)}</span>
            {item.owned
              ? (item.key === activeFilterKey
                ? <span className="owned owned-active">Ativo ✓</span>
                : <span className="owned">Possuído (nível anterior)</span>)
              : <button className="btn-primary" onClick={() => buy(item)}>Comprar</button>}
          </div>
        </div>
      ))}
      {warnFilter && filterItem && (
        <ConfirmModal
          title="Água já está limpa"
          message={`Sua água está a ${Number(tank?.maintenanceLevel ?? 0).toFixed(0)}% — um filtro agora não faria diferença na renda. Comprar mesmo assim?`}
          confirmLabel="Comprar mesmo assim"
          onConfirm={() => doBuy(filterItem)} onClose={() => setWarnFilter(false)}
        />
      )}
    </div>
  );
}
