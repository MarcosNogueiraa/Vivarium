import { useEffect, useRef, useState } from "react";
import { api, clearToken } from "../lib/api.js";
import { useGame } from "../hooks/useGame.js";
import { useToast } from "../hooks/useToast.js";
import { useDailyReward } from "../hooks/useDailyReward.js";
import { useInbox } from "../hooks/useInbox.js";
import { Coin } from "../components/Coin.jsx";
import { Toast } from "../components/Toast.jsx";
import { TankView } from "./TankView.jsx";
import { BackpackView } from "./BackpackView.jsx";
import { MarketView } from "./MarketView.jsx";
import { StoreView } from "./StoreView.jsx";
import { BreedingView } from "./BreedingView.jsx";
import { InboxView } from "./InboxView.jsx";
import { RankingView } from "./RankingView.jsx";
import { HowItWorksGuide } from "./HowItWorksGuide.jsx";
import { RarityGuide } from "./RarityGuide.jsx";
import { AdminPanel } from "../components/AdminPanel.jsx";
import { AccountMenu } from "../components/AccountMenu.jsx";
import { TankUpgradeCelebration } from "../components/TankUpgradeCelebration.jsx";
import { Modal } from "../components/Modal.jsx";
import { DailyRewardModal } from "../components/DailyRewardModal.jsx";

export function GameView({ onLogout }) {
  const { tank, userId, refreshTank, syncError, bandUpgrade, dismissBandUpgrade } = useGame();
  const { toast, notify, dismiss: dismissToast } = useToast();
  const { status: dailyReward, refresh: refreshDailyReward } = useDailyReward();
  // Compartilhado entre o badge do ícone e a InboxView (recebe entries/refresh via props, não
  // chama useInbox() de novo) — evita duplicar o polling batendo no mesmo endpoint.
  const { entries: inboxEntries, unclaimedCount: inboxUnclaimedCount, refresh: refreshInbox } = useInbox();
  const [tab, setTab] = useState("tank");
  const [rankingExitSignal, setRankingExitSignal] = useState(0);
  // Efeito "cinema" (escurece tudo fora do aquário, §CSS `.cinema-dim`/`.tank-layout::before`)
  // ficava sempre escuro no mobile — o :hover que clareia de volta no desktop não existe em
  // telas de toque (pedido do usuário, 12/08/2026). Desligado por padrão (14/08/2026, pedido do
  // usuário: não gostava de a tela escurecer e precisar passar o mouse em cima pra clarear de
  // volta) — toggle continua disponível (🎬 no tank-tools) pra quem preferir o efeito.
  const [cinemaEnabled, setCinemaEnabled] = useState(() => localStorage.getItem("cinemaEnabled") === "true");
  const toggleCinema = () => setCinemaEnabled((v) => { localStorage.setItem("cinemaEnabled", String(!v)); return !v; });
  const [showDailyReward, setShowDailyReward] = useState(false);
  const [showHowItWorks, setShowHowItWorks] = useState(false);
  const [showRarityGuide, setShowRarityGuide] = useState(false);
  const [showAdminPanel, setShowAdminPanel] = useState(false);
  const [showInbox, setShowInbox] = useState(false);
  const navRef = useRef(null);

  // Em telas estreitas o nav vira uma tira de rolagem horizontal (styles.css,
  // `.topbar-tabs .nav-pills`) — sem isso, trocar pra uma aba fora da área
  // visível (ex: "Ninho" ou "🏆 Ranking" com a tela ainda no fundo) deixava o
  // botão ativo escondido, sem indicação de qual aba está selecionada.
  useEffect(() => {
    navRef.current?.querySelector("button.active")
      ?.scrollIntoView({ block: "nearest", inline: "center", behavior: "smooth" });
  }, [tab]);

  async function devCoins() {
    try { await api.devCoins(1000); notify("+1000 fichas"); await refreshTank(); }
    catch (err) { notify(err.message); }
  }
  async function devPremium() {
    try { await api.devCoins(100, "PREMIUM"); notify("+100 premium"); await refreshTank(); }
    catch (err) { notify(err.message); }
  }

  if (tank === null) return <div className="loading">Enchendo o aquário…</div>;

  const soft = Number(tank.wallet?.SOFT ?? 0);
  const premium = Number(tank.wallet?.PREMIUM ?? 0);
  // 14/08/2026, pedido do usuário: não é mais uma aba — só um ícone com uma bolinha
  // vermelha (número branco), travada em "9+" a partir de 10 pra nunca estourar o layout.
  const inboxBadgeText = inboxUnclaimedCount > 9 ? "9+" : String(inboxUnclaimedCount);

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
          <button className="guide-btn inbox-btn" onClick={() => setShowInbox(true)} title="Caixa de entrada">
            📬
            {inboxUnclaimedCount > 0 && <span className="inbox-badge">{inboxBadgeText}</span>}
          </button>
          <button className="guide-btn" onClick={() => setShowHowItWorks(true)} title="Como o jogo funciona">?</button>
          {/* PWA "adicionar à tela de início" no iOS não tem puxar-pra-atualizar nem
              botão de reload do navegador — pedido do usuário (13/08/2026), jogando
              assim, sem jeito fácil de recarregar quando algo trava. */}
          <button className="guide-btn" onClick={() => window.location.reload()} title="Recarregar a página">🔄</button>
        </div>
        <span className="spacer" />
        <div className="topbar-stats">
          {dailyReward?.canClaim && (
            <button className="daily-reward-btn" onClick={() => setShowDailyReward(true)}
              title={`Faixa de hoje: ${Math.round(dailyReward.minAmount)}–${Math.round(dailyReward.maxAmount)} soft`}>
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
            <button className="dev-btn" onClick={() => setShowAdminPanel(true)} title="Ferramentas administrativas">
              🛠️ Admin
            </button>
          )}
        </div>
        <AccountMenu notify={notify} onLogout={() => { clearToken(); onLogout(); }} />
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

      {showInbox && (
        <Modal onClose={() => setShowInbox(false)}>
          <InboxView entries={inboxEntries} refresh={refreshInbox} refreshTank={refreshTank} notify={notify} />
        </Modal>
      )}
      {showHowItWorks && (
        <HowItWorksGuide
          onClose={() => setShowHowItWorks(false)}
          onOpenRarityGuide={() => { setShowHowItWorks(false); setShowRarityGuide(true); }}
        />
      )}
      {showRarityGuide && <RarityGuide onClose={() => setShowRarityGuide(false)} />}
      {showAdminPanel && (
        <AdminPanel
          notify={notify}
          onClose={async () => { setShowAdminPanel(false); await Promise.all([refreshTank(), refreshInbox()]); }}
        />
      )}
      {bandUpgrade && <TankUpgradeCelebration bandName={bandUpgrade} onClose={dismissBandUpgrade} />}
      {showDailyReward && dailyReward && (
        <DailyRewardModal
          status={dailyReward}
          notify={notify}
          onClose={() => setShowDailyReward(false)}
          onClaimed={async () => { await Promise.all([refreshDailyReward(), refreshTank(), refreshInbox()]); }}
        />
      )}
    </div>
  );
}
