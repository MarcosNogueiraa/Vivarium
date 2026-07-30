import { useCallback, useEffect, useState } from "react";
import { api } from "../lib/api.js";
import { coinsPerHourOf } from "../lib/generator.js";
import { bandOf } from "../lib/fishRenderer.js";
import { RarityBadge } from "../components/RarityBadge.jsx";
import { Coin } from "../components/Coin.jsx";
import { FishCanvas } from "../components/FishCanvas.jsx";
import { FishDetail } from "./FishDetail.jsx";

export function MarketView({ userId, refreshTank, notify }) {
  const [listings, setListings] = useState(null);
  const [detail, setDetail] = useState(null);

  const refresh = useCallback(async () => {
    setListings(await api.listings());
  }, []);

  useEffect(() => { refresh().catch((err) => notify(err.message)); }, [refresh, notify]);

  async function buy(listing) {
    try {
      await api.buyListing(listing.id);
      notify(`Comprado por ${listing.priceSoft} soft!`);
      setDetail(null);
      await Promise.all([refresh(), refreshTank()]);
    } catch (err) { notify(err.message); }
  }
  async function cancel(listing) {
    try {
      await api.cancelListing(listing.id);
      setDetail(null);
      await Promise.all([refresh(), refreshTank()]);
    } catch (err) { notify(err.message); }
  }

  if (listings === null) return <p className="hint">Carregando mercado…</p>;
  if (listings.length === 0) return <p className="hint">Nenhum peixe à venda no momento — seja o primeiro a listar.</p>;

  return (
    <>
      <div className="grid">
        {listings.map((l) => (
          <div key={l.id} className="card" style={{ "--tier": bandOf(Number(l.rarityScore)).color }}>
            <button className="fish-stage as-button" onClick={() => setDetail(l)} title="Ver detalhes">
              <FishCanvas seed={l.seed} isBred={l.isBred} parentASeed={l.parentASeed} parentBSeed={l.parentBSeed} />
            </button>
            <div className="card-row">
              <RarityBadge score={Number(l.rarityScore)} />
              <span className="price"><Coin />{Number(l.priceSoft).toFixed(0)}</span>
            </div>
            <div className="card-row">
              <span className="produces mono">~{coinsPerHourOf(Number(l.rarityScore)).toFixed(1)}/h</span>
              <span className="seller">de {l.sellerName}</span>
            </div>
            {l.isBred && <span className="bred-tag">🐣 Filhote</span>}
            <div className="card-row">
              <button onClick={() => setDetail(l)}>Detalhes</button>
              {l.sellerId === userId
                ? <button onClick={() => cancel(l)}>Cancelar</button>
                : <button className="btn-primary" onClick={() => buy(l)}>Comprar</button>}
            </div>
          </div>
        ))}
      </div>
      {detail && (
        <FishDetail creature={detail} onClose={() => setDetail(null)}>
          {detail.sellerId === userId
            ? <button onClick={() => cancel(detail)}>Cancelar listagem</button>
            : <button className="btn-primary" onClick={() => buy(detail)}>Comprar por {Number(detail.priceSoft).toFixed(0)} soft</button>}
        </FishDetail>
      )}
    </>
  );
}
