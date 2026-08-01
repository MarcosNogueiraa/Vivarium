import { useCallback, useEffect, useState } from "react";
import { api } from "../lib/api.js";
import { Coin } from "../components/Coin.jsx";
import { ConfirmModal } from "../components/ConfirmModal.jsx";
import { FILTER_WARN_THRESHOLD } from "../lib/tankMath.js";

const DESCRIPTIONS = {
  filter_basic: "Restaura a qualidade da água para 100 na hora.",
  auto_filter: "Permanente: a água degrada na metade da velocidade.",
  tank_upgrade: "+1 de capacidade no tanque (preço sobe a cada compra).",
};

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

  return (
    <div className="grid">
      {items.map((item) => (
        <div key={item.key} className="card store-card">
          <strong>{item.name}</strong>
          <p className="muted">{DESCRIPTIONS[item.key] ?? ""}</p>
          <div className="card-row">
            <span className="price"><Coin />{Number(item.price).toFixed(0)}</span>
            {item.owned
              ? <span className="owned">Adquirido ✓</span>
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
