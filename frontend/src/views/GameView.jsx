import { useState } from "react";
import { api, clearToken } from "../lib/api.js";
import { useGame } from "../hooks/useGame.js";
import { useToast } from "../hooks/useToast.js";
import { Coin } from "../components/Coin.jsx";
import { Toast } from "../components/Toast.jsx";
import { TankView } from "./TankView.jsx";
import { BackpackView } from "./BackpackView.jsx";
import { MarketView } from "./MarketView.jsx";
import { StoreView } from "./StoreView.jsx";
import { BreedingView } from "./BreedingView.jsx";

export function GameView({ onLogout }) {
  const { tank, userId, refreshTank } = useGame();
  const { toast, notify } = useToast();
  const [tab, setTab] = useState("tank");

  async function devCoins() {
    try { await api.devCoins(1000); notify("+1000 fichas"); await refreshTank(); }
    catch (err) { notify(err.message); }
  }

  if (tank === null) return <div className="loading">Enchendo o aquário…</div>;

  const soft = Number(tank.wallet?.SOFT ?? 0);

  return (
    <div className="app-shell">
      <header className="topbar">
        <div className="brand"><span className="dot" />Vivarium <small>aquário vivo</small></div>
        <span className="spacer" />
        <nav className="nav-pills">
          <button className={tab === "tank" ? "active" : ""} onClick={() => setTab("tank")}>Tanque</button>
          <button className={tab === "backpack" ? "active" : ""} onClick={() => setTab("backpack")}>Mochila</button>
          <button className={tab === "market" ? "active" : ""} onClick={() => setTab("market")}>Mercado</button>
          <button className={tab === "store" ? "active" : ""} onClick={() => setTab("store")}>Loja</button>
          <button className={tab === "breeding" ? "active" : ""} onClick={() => setTab("breeding")}>Ninho</button>
        </nav>
        <span className="spacer" />
        <span className="wallet-chip"><Coin />{soft.toFixed(0)} <small>soft</small></span>
        {import.meta.env.DEV && (
          <button className="dev-btn" onClick={devCoins} title="Só existe em dev">+1000 fichas</button>
        )}
        <button onClick={() => { clearToken(); onLogout(); }}>Sair</button>
      </header>

      <main className="content">
        {tab === "tank" && <TankView tank={tank} refresh={refreshTank} notify={notify} />}
        {tab === "backpack" && <BackpackView refreshTank={refreshTank} notify={notify} />}
        {tab === "market" && <MarketView userId={userId} refreshTank={refreshTank} notify={notify} />}
        {tab === "store" && <StoreView refreshTank={refreshTank} notify={notify} />}
        {tab === "breeding" && <BreedingView tank={tank} refreshTank={refreshTank} notify={notify} />}
      </main>

      <Toast message={toast} />
    </div>
  );
}
