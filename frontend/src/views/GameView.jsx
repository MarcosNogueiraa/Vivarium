import { useEffect, useRef, useState } from "react";
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
import { RankingView } from "./RankingView.jsx";
import { HowItWorksGuide } from "./HowItWorksGuide.jsx";
import { RarityGuide } from "./RarityGuide.jsx";
import { ConfirmModal } from "../components/ConfirmModal.jsx";
import { AccountMenu } from "../components/AccountMenu.jsx";
import { TankUpgradeCelebration } from "../components/TankUpgradeCelebration.jsx";

export function GameView({ onLogout }) {
  const { tank, userId, refreshTank, syncError, bandUpgrade, dismissBandUpgrade } = useGame();
  const { toast, notify, dismiss: dismissToast } = useToast();
  const { status: dailyReward, refresh: refreshDailyReward } = useDailyReward();
  const [tab, setTab] = useState("tank");
  const [rankingExitSignal, setRankingExitSignal] = useState(0);
  // Efeito "cinema" (escurece tudo fora do aquário, §CSS `.cinema-dim`/`.tank-layout::before`)
  // ficava sempre escuro no mobile — o :hover que clareia de volta no desktop não existe em
  // telas de toque (pedido do usuário, 12/08/2026). Toggle persistido; ligado por padrão
  // (preserva o comportamento de sempre pra quem não mexer).
  const [cinemaEnabled, setCinemaEnabled] = useState(() => localStorage.getItem("cinemaEnabled") !== "false");
  const toggleCinema = () => setCinemaEnabled((v) => { localStorage.setItem("cinemaEnabled", String(!v)); return !v; });
  const [claimingReward, setClaimingReward] = useState(false);
  const [showHowItWorks, setShowHowItWorks] = useState(false);
  const [showRarityGuide, setShowRarityGuide] = useState(false);
  const [showGiveFishConfirm, setShowGiveFishConfirm] = useState(false);
  const [showGrantPremiumConfirm, setShowGrantPremiumConfirm] = useState(false);
  const navRef = useRef(null);

  // Em telas estreitas o nav vira uma tira de rolagem horizontal (styles.css,
  // `.topbar-tabs .nav-pills`) — sem isso, trocar pra uma aba fora da área
  // visível (ex: "Ninho" ou "🏆 Ranking" com a tela ainda no fundo) deixava o
  // botão ativo escondido, sem indicação de qual aba está selecionada.
  useEffect(() => {
    navRef.current?.querySelector("button.active")
      ?.scrollIntoView({ block: "nearest", inline: "center", behavior: "smooth" });
  }, [tab]);

  async function giveStarterFishToAll() {
    const { habitatsAffected } = await api.adminGiveStarterFishAll();
    setShowGiveFishConfirm(false);
    notify(`+1 peixe pronto pra ${habitatsAffected} jogador(es)`);
  }

  async function grantPremiumToAll() {
    const { usersAffected, amount } = await api.adminGrantPremiumAll(1000);
    setShowGrantPremiumConfirm(false);
    notify(`+${amount} premium pra ${usersAffected} jogador(es)`);
    await refreshTank();
  }

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
    <div className={`app-shell${tab === "tank" && cinemaEnabled ? " cinema-mode" : ""}`}>
      <header className="topbar">
        <div className="brand"><span className="dot" />Vivarium <small>aquário vivo</small></div>
        <span className="spacer" />
        {/* `.topbar-tabs`/`.topbar-stats` só existem pra dar ao header um jeito de
            se reorganizar em linhas separadas no mobile (CSS `order` + linha
            cheia por grupo) sem duplicar nada — em telas largas eles usam
            `display:contents` e desaparecem da árvore de layout, deixando o
            topbar exatamente como sempre foi (ver styles.css). */}
        <div className="topbar-tabs">
          <nav className="nav-pills" ref={navRef}>
            <button data-tab="tank" className={tab === "tank" ? "active" : ""} onClick={() => setTab("tank")}>Tanque</button>
            <button data-tab="backpack" className={tab === "backpack" ? "active" : ""} onClick={() => setTab("backpack")}>Mochila</button>
            <button data-tab="market" className={tab === "market" ? "active" : ""} onClick={() => setTab("market")}>Mercado</button>
            <button data-tab="store" className={tab === "store" ? "active" : ""} onClick={() => setTab("store")}>Loja</button>
            <button data-tab="breeding" className={tab === "breeding" ? "active" : ""} onClick={() => setTab("breeding")}>Ninho</button>
            <button data-tab="ranking" className={tab === "ranking" ? "active" : ""} onClick={() => {
              // Já na aba Ranking (ex: visitando um aquário)? Clicar de novo volta pra lista
              // direto, sem precisar do botão "Voltar" (pedido do usuário, 12/08/2026).
              if (tab === "ranking") setRankingExitSignal((s) => s + 1);
              setTab("ranking");
            }}>🏆 Ranking</button>
          </nav>
          <button className="guide-btn" onClick={() => setShowHowItWorks(true)} title="Como o jogo funciona">?</button>
          {/* PWA "adicionar à tela de início" no iOS não tem puxar-pra-atualizar nem
              botão de reload do navegador — pedido do usuário (13/08/2026), jogando
              assim, sem jeito fácil de recarregar quando algo trava. */}
          <button className="guide-btn" onClick={() => window.location.reload()} title="Recarregar a página">🔄</button>
        </div>
        <span className="spacer" />
        <div className="topbar-stats">
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
          {tank.isVip && (
            <span className="vip-chip" title={`VIP ativo até ${new Date(tank.vipEndAt).toLocaleDateString("pt-BR")} — coleta automática enquanto online`}>
              👑 VIP
            </span>
          )}
          {import.meta.env.DEV && (
            <button className="dev-btn" onClick={devCoins} title="Só existe em dev">+1000 fichas</button>
          )}
          {import.meta.env.DEV && (
            <button className="dev-btn" onClick={devPremium} title="Só existe em dev">+100 premium</button>
          )}
          {tank.isAdmin && (
            <button className="dev-btn" onClick={() => setShowGiveFishConfirm(true)} title="Dá 1 peixe pronto pra coletar a todo jogador com espaço na fila">
              🎣 Dar peixe a todos
            </button>
          )}
          {tank.isAdmin && (
            <button className="dev-btn" onClick={() => setShowGrantPremiumConfirm(true)} title="Credita 1000 de moeda premium na carteira de todo jogador">
              💎 1000 premium a todos
            </button>
          )}
        </div>
        <AccountMenu onLogout={() => { clearToken(); onLogout(); }} />
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
        {tab === "tank" && (
          <TankView tank={tank} refresh={refreshTank} notify={notify}
            cinemaEnabled={cinemaEnabled} toggleCinema={toggleCinema} />
        )}
        {tab === "backpack" && <BackpackView refreshTank={refreshTank} notify={notify} />}
        {tab === "market" && <MarketView userId={userId} refreshTank={refreshTank} notify={notify} />}
        {tab === "store" && <StoreView tank={tank} refreshTank={refreshTank} notify={notify} />}
        {tab === "breeding" && <BreedingView tank={tank} refreshTank={refreshTank} notify={notify} />}
        {tab === "ranking" && <RankingView notify={notify} exitSpectatorSignal={rankingExitSignal} />}
      </main>

      <Toast message={toast} onDismiss={dismissToast} />

      {showHowItWorks && (
        <HowItWorksGuide
          onClose={() => setShowHowItWorks(false)}
          onOpenRarityGuide={() => { setShowHowItWorks(false); setShowRarityGuide(true); }}
        />
      )}
      {showRarityGuide && <RarityGuide onClose={() => setShowRarityGuide(false)} />}
      {showGiveFishConfirm && (
        <ConfirmModal
          title="Dar peixe a todos"
          message="Todo jogador com espaço na fila (menos de 5 pendentes) recebe +1 peixe pronto pra coletar. Confirma?"
          confirmLabel="Dar peixe a todos"
          onConfirm={giveStarterFishToAll}
          onClose={() => setShowGiveFishConfirm(false)}
        />
      )}
      {showGrantPremiumConfirm && (
        <ConfirmModal
          title="Dar 1000 premium a todos"
          message="Todo jogador recebe +1000 de moeda premium na carteira. Ação de teste em produção — não é reversível. Confirma?"
          confirmLabel="Dar 1000 premium a todos"
          onConfirm={grantPremiumToAll}
          onClose={() => setShowGrantPremiumConfirm(false)}
        />
      )}
      {bandUpgrade && <TankUpgradeCelebration bandName={bandUpgrade} onClose={dismissBandUpgrade} />}
    </div>
  );
}
