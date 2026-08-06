import { useState } from "react";
import { api, clearToken } from "../lib/api.js";
import { useGame } from "../hooks/useGame.js";
import { useToast } from "../hooks/useToast.js";
import { useDailyReward } from "../hooks/useDailyReward.js";
import { Coin } from "../components/Coin.jsx";
import { Toast } from "../components/Toast.jsx";
import { TankView } from "./TankView.jsx";
import { BackpackView } from "./BackpackView.jsx";
import { MarketView } from "./MarketView.jsx";
import { StoreView } from "./StoreView.jsx";
import { BreedingView } from "./BreedingView.jsx";
import { HowItWorksGuide } from "./HowItWorksGuide.jsx";
import { RarityGuide } from "./RarityGuide.jsx";

export function GameView({ onLogout }) {
  const { tank, userId, refreshTank, syncError } = useGame();
  const { toast, notify } = useToast();
  const { status: dailyReward, refresh: refreshDailyReward } = useDailyReward();
  const [tab, setTab] = useState("tank");
  const [claimingReward, setClaimingReward] = useState(false);
  const [showHowItWorks, setShowHowItWorks] = useState(false);
  const [showRarityGuide, setShowRarityGuide] = useState(false);

  async function devCoins() {
    try { await api.devCoins(1000); notify("+1000 fichas"); await refreshTank(); }
    catch (err) { notify(err.message); }
  }
  async function devPremium() {
    try { await api.devCoins(100, "PREMIUM"); notify("+100 premium"); await refreshTank(); }
    catch (err) { notify(err.message); }
  }

  async function claimDailyReward() {
    setClaimingReward(true);
    try {
      const { amount } = await api.claimDailyReward();
      notify(`Recompensa diária: +${Number(amount).toFixed(0)} soft!`);
      await Promise.all([refreshDailyReward(), refreshTank()]);
    } catch (err) { notify(err.message); }
    finally { setClaimingReward(false); }
  }

  if (tank === null) return <div className="loading">Enchendo o aquário…</div>;

  const soft = Number(tank.wallet?.SOFT ?? 0);
  const premium = Number(tank.wallet?.PREMIUM ?? 0);

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
        <button className="guide-btn" onClick={() => setShowHowItWorks(true)} title="Como o jogo funciona">?</button>
        <span className="spacer" />
        {dailyReward?.canClaim && (
          <button className="daily-reward-btn" onClick={claimDailyReward} disabled={claimingReward}
            title={`Resgatar recompensa diária: +${Number(dailyReward.amount).toFixed(0)} soft`}>
            🎁 Recompensa diária
          </button>
        )}
        <span className="wallet-chip"><Coin />{soft.toFixed(0)} <small>soft</small></span>
        <span className="premium-chip" title="Moeda premium — única forma de acelerar fila/gestação (⚡)">
          💎{premium.toFixed(0)} <small>premium</small>
        </span>
        {import.meta.env.DEV && (
          <button className="dev-btn" onClick={devCoins} title="Só existe em dev">+1000 fichas</button>
        )}
        {import.meta.env.DEV && (
          <button className="dev-btn" onClick={devPremium} title="Só existe em dev">+100 premium</button>
        )}
        <button onClick={() => { clearToken(); onLogout(); }}>Sair</button>
      </header>

      {syncError && (
        <div className="sync-banner" role="alert">
          <span className="sync-banner-icon">⚠️</span>
          <span className="sync-banner-text">
            <strong>Sincronização perdida</strong> — o jogo não está conseguindo falar com o servidor. Verifique sua conexão e recarregue a página (tecla <kbd>F5</kbd>).
          </span>
          <button className="sync-banner-reload" onClick={() => window.location.reload()}>Recarregar agora</button>
        </div>
      )}

      <main className="content">
        {tab === "tank" && <TankView tank={tank} refresh={refreshTank} notify={notify} />}
        {tab === "backpack" && <BackpackView refreshTank={refreshTank} notify={notify} />}
        {tab === "market" && <MarketView userId={userId} refreshTank={refreshTank} notify={notify} />}
        {tab === "store" && <StoreView tank={tank} refreshTank={refreshTank} notify={notify} />}
        {tab === "breeding" && <BreedingView tank={tank} refreshTank={refreshTank} notify={notify} />}
      </main>

      <Toast message={toast} />

      {showHowItWorks && (
        <HowItWorksGuide
          onClose={() => setShowHowItWorks(false)}
          onOpenRarityGuide={() => { setShowHowItWorks(false); setShowRarityGuide(true); }}
        />
      )}
      {showRarityGuide && <RarityGuide onClose={() => setShowRarityGuide(false)} />}
    </div>
  );
}
