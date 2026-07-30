import { useCallback, useEffect, useState } from "react";
import { api } from "../lib/api.js";
import { coinsPerHourOf } from "../lib/generator.js";
import { bandOf } from "../lib/fishRenderer.js";
import { RarityBadge } from "../components/RarityBadge.jsx";
import { FishCanvas } from "../components/FishCanvas.jsx";
import { PromptModal } from "../components/PromptModal.jsx";
import { FishDetail } from "./FishDetail.jsx";

export function BackpackView({ refreshTank, notify }) {
  const [data, setData] = useState(null);
  const [detail, setDetail] = useState(null);
  const [prompt, setPrompt] = useState(null); // { kind: "sell"|"transfer", creature }

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
  function sell(c) { setDetail(null); setPrompt({ kind: "sell", creature: c }); }
  function transfer(c) { setDetail(null); setPrompt({ kind: "transfer", creature: c }); }

  async function confirmSell(price) {
    await api.createListing(prompt.creature.id, Number(price));
    setPrompt(null);
    notify("Listado no mercado.");
    await refresh();
  }
  async function confirmTransfer(username) {
    await api.transferCreature(prompt.creature.id, username.trim());
    setPrompt(null);
    notify(`Transferido para ${username.trim()}.`);
    await refresh();
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
                <FishCanvas seed={c.seed} isBred={c.isBred} parentASeed={c.parentASeed} parentBSeed={c.parentBSeed} />
              </button>
              <div className="card-row">
                <RarityBadge score={Number(c.rarityScore)} />
                <span className="produces mono">~{coinsPerHourOf(Number(c.rarityScore)).toFixed(1)}/h</span>
              </div>
              {c.isBred && <span className="bred-tag">🐣 Filhote</span>}
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
      {prompt?.kind === "sell" && (
        <PromptModal
          title="Vender no mercado" label="Preço em moedas soft" type="number"
          defaultValue="50" confirmLabel="Listar peixe"
          onConfirm={confirmSell} onClose={() => setPrompt(null)}
        />
      )}
      {prompt?.kind === "transfer" && (
        <PromptModal
          title="Transferir peixe" label="Username do jogador que vai receber"
          placeholder="username" confirmLabel="Transferir"
          onConfirm={confirmTransfer} onClose={() => setPrompt(null)}
        />
      )}
    </>
  );
}
