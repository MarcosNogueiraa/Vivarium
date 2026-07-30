import { useCallback, useEffect, useState } from "react";
import { api } from "../lib/api.js";
import { coinsPerHourOf } from "../lib/generator.js";
import { bandOf } from "../lib/fishRenderer.js";
import { RarityBadge } from "../components/RarityBadge.jsx";
import { FishCanvas } from "../components/FishCanvas.jsx";
import { FishDetail } from "./FishDetail.jsx";

export function BackpackView({ refreshTank, notify }) {
  const [data, setData] = useState(null);
  const [detail, setDetail] = useState(null);

  const refresh = useCallback(async () => { setData(await api.backpack()); }, []);
  useEffect(() => { refresh().catch((e) => notify(e.message)); }, [refresh, notify]);

  async function deploy(c) {
    try {
      await api.deployCreature(c.id);
      notify("Peixe de volta ao tanque!");
      setDetail(null);
      await Promise.all([refresh(), refreshTank()]);
    } catch (e) { notify(e.message); }
  }
  async function sell(c) {
    const price = window.prompt("Preço em moeda soft:", "50");
    if (!price) return;
    try { await api.createListing(c.id, Number(price)); notify("Listado no mercado."); setDetail(null); await refresh(); }
    catch (e) { notify(e.message); }
  }
  async function transfer(c) {
    const to = window.prompt("Transferir para qual jogador (username)?");
    if (!to) return;
    try { await api.transferCreature(c.id, to.trim()); notify(`Transferido para ${to.trim()}.`); setDetail(null); await refresh(); }
    catch (e) { notify(e.message); }
  }

  if (data === null) return <p className="hint">Carregando mochila…</p>;

  return (
    <>
      <div className="section-head">
        <span className="eyebrow">Mochila</span>
        <span className="count">{data.creatures.length}/{data.capacity}</span>
      </div>
      {data.creatures.length === 0 ? (
        <p className="hint">Mochila vazia. Guarde peixes do tanque aqui — eles ficam seguros (mas não farmam).</p>
      ) : (
        <div className="grid">
          {data.creatures.map((c) => (
            <div key={c.id} className="card" style={{ "--tier": bandOf(Number(c.rarityScore)).color }}>
              <button className="fish-stage as-button" onClick={() => setDetail(c)} title="Ver detalhes">
                <FishCanvas seed={c.seed} />
              </button>
              <div className="card-row">
                <RarityBadge score={Number(c.rarityScore)} />
                <span className="produces mono">~{coinsPerHourOf(Number(c.rarityScore)).toFixed(1)}/h</span>
              </div>
              <div className="card-row">
                <button className="btn-primary" onClick={() => deploy(c)}>Pro tanque</button>
                <button onClick={() => sell(c)}>Vender</button>
                <button onClick={() => transfer(c)}>Transferir</button>
              </div>
            </div>
          ))}
        </div>
      )}
      {detail && (
        <FishDetail creature={detail} onClose={() => setDetail(null)}>
          <button className="btn-primary" onClick={() => deploy(detail)}>Pro tanque</button>
          <button onClick={() => sell(detail)}>Vender</button>
          <button onClick={() => transfer(detail)}>Transferir</button>
        </FishDetail>
      )}
    </>
  );
}
