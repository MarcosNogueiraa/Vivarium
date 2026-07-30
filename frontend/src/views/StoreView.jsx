import { useCallback, useEffect, useState } from "react";
import { api } from "../lib/api.js";
import { Coin } from "../components/Coin.jsx";

const DESCRIPTIONS = {
  filter_basic: "Restaura a qualidade da água para 100 na hora.",
  auto_filter: "Permanente: a água degrada na metade da velocidade.",
  tank_upgrade: "+1 de capacidade no tanque (preço sobe a cada compra).",
};

export function StoreView({ refreshTank, notify }) {
  const [items, setItems] = useState(null);

  const refresh = useCallback(async () => { setItems(await api.items()); }, []);
  useEffect(() => { refresh().catch((err) => notify(err.message)); }, [refresh, notify]);

  async function buy(item) {
    try {
      await api.buyItem(item.key);
      notify(`${item.name} comprado!`);
      await Promise.all([refresh(), refreshTank()]);
    } catch (err) { notify(err.message); }
  }

  if (items === null) return <p className="hint">Carregando loja…</p>;

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
    </div>
  );
}
