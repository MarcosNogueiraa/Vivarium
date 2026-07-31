import { useEffect, useRef, useState } from "react";
import { api } from "../lib/api.js";
import { bandOf, PART_HEX, PT } from "../lib/fishRenderer.js";
import { nextFishEta, tankFishSorted, tankPotential, tankSynergy } from "../lib/tankMath.js";
import { AquariumCanvas, PIP_SUPPORTED } from "../components/AquariumCanvas.jsx";
import { FishCanvas } from "../components/FishCanvas.jsx";
import { Coin } from "../components/Coin.jsx";
import { PromptModal } from "../components/PromptModal.jsx";
import { CollectCelebration } from "../components/CollectCelebration.jsx";
import { FishDetail } from "./FishDetail.jsx";
import { RarityGuide } from "./RarityGuide.jsx";

export function TankView({ tank, refresh, notify }) {
  const [selectedId, setSelectedId] = useState(null);
  const [showGuide, setShowGuide] = useState(false);
  const [prompt, setPrompt] = useState(null);       // { kind: "sell"|"transfer", creature }
  const [celebrate, setCelebrate] = useState(null); // criatura raro+ recém-coletada
  const [onboardDone, setOnboardDone] = useState(() => localStorage.getItem("onboardDone") === "true");
  const dismissOnboard = () => { localStorage.setItem("onboardDone", "true"); setOnboardDone(true); };
  const [listOpen, setListOpen] = useState(() => localStorage.getItem("tankListOpen") !== "false");
  const toggleList = () => setListOpen((v) => { localStorage.setItem("tankListOpen", String(!v)); return !v; });
  const selected = tank.creatures.find((c) => c.id === selectedId) ?? null;
  const lowWater = Number(tank.maintenanceLevel) < 40;

  // ---- Modo "enfeite de monitor": clique, tela cheia, modo aquário e pop-up ----
  const [clickEnabled, setClickEnabled] = useState(() => localStorage.getItem("tankClicks") !== "false");
  const toggleClicks = () => setClickEnabled((v) => { localStorage.setItem("tankClicks", String(!v)); return !v; });
  const [aquariumMode, setAquariumMode] = useState(false);
  const stageRef = useRef(null);
  const [isFullscreen, setIsFullscreen] = useState(false);
  useEffect(() => {
    const onFsChange = () => setIsFullscreen(document.fullscreenElement === stageRef.current);
    document.addEventListener("fullscreenchange", onFsChange);
    return () => document.removeEventListener("fullscreenchange", onFsChange);
  }, []);
  async function toggleFullscreen() {
    try {
      if (document.fullscreenElement) await document.exitFullscreen();
      else await stageRef.current?.requestFullscreen();
    } catch { notify("Tela cheia não suportada neste navegador."); }
  }
  const aquariumRef = useRef(null);
  const [pipActive, setPipActive] = useState(false);

  const current = Number(tank.coinsPerHour ?? 0);
  const synergy = tankSynergy(tank.creatures);
  const potential = tankPotential(tank.creatures);
  const nextFish = nextFishEta(tank);
  const listFish = tankFishSorted(tank.creatures);

  async function collect(itemId) {
    try {
      const c = await api.collect(itemId);
      await refresh();
      // Raro+ ganha um momento de celebração; comum/incomum só um toast discreto.
      if (c && Number(c.rarityScore) >= 7.5) setCelebrate(c);
      else if (c) notify(`Peixe coletado! ${bandOf(Number(c.rarityScore)).name}`);
    } catch (err) { notify(err.message); }
  }
  async function rushQueue(itemId) {
    try { await api.rushQueueItem(itemId); notify("Acelerado!"); await refresh(); }
    catch (err) { notify(err.message); }
  }
  async function devSpawn() {
    try { await api.devSpawn(); await refresh(); }
    catch (err) { notify(err.message); }
  }
  async function devClear() {
    try {
      const { removed } = await api.devClear();
      setSelectedId(null);
      notify(`${removed} peixe(s) removido(s).`);
      await refresh();
    } catch (err) { notify(err.message); }
  }
  async function buyFilter() {
    try { await api.buyItem("filter_basic"); notify("Água restaurada!"); await refresh(); }
    catch (err) { notify(err.message); }
  }
  function sell(creature) { setSelectedId(null); setPrompt({ kind: "sell", creature }); }
  function transfer(creature) { setSelectedId(null); setPrompt({ kind: "transfer", creature }); }

  async function confirmSell(price) {
    await api.createListing(prompt.creature.id, Number(price));
    setPrompt(null);
    notify("Peixe listado no mercado.");
    await refresh();
  }
  async function confirmTransfer(username) {
    await api.transferCreature(prompt.creature.id, username.trim());
    setPrompt(null);
    notify(`Transferido para ${username.trim()}.`);
    await refresh();
  }
  async function store(creature) {
    try {
      await api.storeCreature(creature.id);
      notify("Guardado na mochila.");
      setSelectedId(null);
      await refresh();
    } catch (err) { notify(err.message); }
  }

  return (
    <div className="tank-layout">
      {!onboardDone && !aquariumMode && (
        <div className="onboard glass">
          <div className="onboard-body">
            <span className="eyebrow">Bem-vindo ao seu aquário</span>
            <p><b>1.</b> Colete os peixes da fila &nbsp;·&nbsp; <b>2.</b> Cuide da água (filtro) &nbsp;·&nbsp;
               <b>3.</b> Peixes raros farmam mais moedas &nbsp;·&nbsp; <b>4.</b> Negocie no Mercado</p>
          </div>
          <button className="btn-primary" onClick={dismissOnboard}>Entendi</button>
        </div>
      )}
      <div className={`tank-stage${isFullscreen ? " is-fullscreen" : ""}${aquariumMode ? " aquarium-mode" : ""}`} ref={stageRef}>
        {!aquariumMode && (
          <div className="tank-hud">
            <span className={`status-pill ${tank.online ? "on" : "off"}`}>
              <span className="led" />{tank.online ? "Online" : "Offline"}
            </span>
            <span className="capacity-chip" title="Peixes ativos no tanque / capacidade">
              🐟 {tank.creatures.length}/{tank.capacity}
            </span>
            <button className="guide-btn" onClick={() => setShowGuide(true)} title="Como funciona a raridade">?</button>
            <span className="spacer" style={{ flex: 1 }} />
            <span className="income-chip" title={
              potential > current + 0.05
                ? `Produção atual (com a água). Potencial a água cheia: ${potential.toFixed(1)}/h`
                : "Moedas por hora que seu tanque farma"
            }>
              <Coin />+{current.toFixed(1)}<small>/h</small>
              {potential > current + 0.05 && <em className="of-potential"> de {potential.toFixed(0)}</em>}
            </span>
            <span className="water-gauge">
              <span className="label">Água</span>
              <span className="water-track">
                <span className={`water-fill${lowWater ? " low" : ""}`} style={{ width: `${tank.maintenanceLevel}%` }} />
              </span>
              <span className="val">{Number(tank.maintenanceLevel).toFixed(0)}</span>
            </span>
            <button onClick={buyFilter} title="Restaura a qualidade da água pra 100">Filtro · 20</button>
          </div>
        )}

        <AquariumCanvas
          ref={aquariumRef}
          creatures={tank.creatures} selectedId={selectedId}
          onSelect={setSelectedId} quality={Number(tank.maintenanceLevel)}
          interactive={clickEnabled} onPipChange={setPipActive}
        />

        <div className="tank-tools">
          <button className={`tool-btn${clickEnabled ? " active" : ""}`} onClick={toggleClicks}
            title={clickEnabled ? "Cliques no peixe: ativados (clique pra desativar)" : "Cliques no peixe: desativados (clique pra ativar)"}>
            {clickEnabled ? "🖱️" : "🚫"}
          </button>
          <button className={`tool-btn${aquariumMode ? " active" : ""}`} onClick={() => setAquariumMode((v) => !v)}
            title={aquariumMode ? "Sair do modo aquário" : "Modo aquário — só o tanque, decorativo"}>
            🐠
          </button>
          <button className={`tool-btn${isFullscreen ? " active" : ""}`} onClick={toggleFullscreen}
            title={isFullscreen ? "Sair da tela cheia" : "Tela cheia"}>
            {isFullscreen ? "⤢" : "⛶"}
          </button>
          {PIP_SUPPORTED && (
            <button className={`tool-btn${pipActive ? " active" : ""}`} onClick={() => aquariumRef.current?.togglePip()}
              title={pipActive ? "Fechar pop-up flutuante" : "Abrir aquário em pop-up flutuante"}>
              📺
            </button>
          )}
        </div>

        {tank.creatures.length === 0 && !aquariumMode && (
          <div className="tank-empty">
            <strong>Seu aquário está esperando</strong>
            <span className="muted">Colete um peixe da fila para começar.</span>
          </div>
        )}
      </div>

      {!aquariumMode && synergy.length > 0 && (
        <div className="synergy-bar glass">
          <span className="eyebrow">Sinergia de cor</span>
          {synergy.map((g) => (
            <span key={g.color} className="synergy-chip" style={{ "--tier": PART_HEX[g.color] }}>
              <span className="dot-color" /> {PT.color[g.color]} ×{g.n} <em>+{(g.bonus * 100).toFixed(0)}%</em>
            </span>
          ))}
        </div>
      )}
      {!aquariumMode && tank.creatures.length > 0 && !selected && (
        <p className="hint">Clique num peixe para ver os detalhes, guardar, vender ou transferir.</p>
      )}
      {selected && (
        <FishDetail creature={selected} onClose={() => setSelectedId(null)}>
          <button onClick={() => store(selected)} title="Guardar na mochila (não farma)">Guardar</button>
          <button onClick={() => sell(selected)}>Vender</button>
          <button onClick={() => transfer(selected)}>Transferir</button>
        </FishDetail>
      )}
      {showGuide && <RarityGuide onClose={() => setShowGuide(false)} />}
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
      {celebrate && <CollectCelebration creature={celebrate} onClose={() => setCelebrate(null)} />}

      {!aquariumMode && tank.creatures.length > 0 && (
        <section>
          <div className="section-head">
            <button className="collapse-btn" onClick={toggleList} aria-expanded={listOpen}
              title={listOpen ? "Recolher lista" : "Expandir lista"}>
              <span className={`chevron${listOpen ? " open" : ""}`}>▾</span>
            </button>
            <span className="eyebrow">Peixes no tanque</span>
            <span className="count">{tank.creatures.length}/{tank.capacity}</span>
            <span className="spacer" />
            {listOpen && <span className="faint" style={{ fontSize: "0.82rem" }}>clique para detalhes</span>}
          </div>
          {listOpen && (
          <div className="fish-list">
            {listFish.map(({ c, traits, col, colorLabel, prod }) => {
              const band = bandOf(Number(c.rarityScore));
              return (
                <button key={c.id} className="fish-row" onClick={() => setSelectedId(c.id)} style={{ "--tier": band.color }}>
                  <span className="fr-thumb">
                    <FishCanvas seed={c.seed} width={72} isBred={c.isBred} parentASeed={c.parentASeed} parentBSeed={c.parentBSeed} />
                  </span>
                  <span className="fr-body">
                    <span className="fr-line1">
                      <span className="badge" style={{ "--tier": band.color }}><span className="gem" /> {band.name}</span>
                      <span className="fr-score mono">{Number(c.rarityScore).toFixed(1)}</span>
                    </span>
                    <span className="fr-line2">
                      <span className="fr-color"><span className="dot-color" style={{ background: PART_HEX[col] }} /> {colorLabel}</span>
                      {traits.shimmerTier !== "None" && <span className="shimmer-label">✦ {PT.shimmer[traits.shimmerColor]}</span>}
                      {c.isBred && <span className="bred-tag">🐣 Filhote</span>}
                    </span>
                  </span>
                  <span className="fr-prod mono"><Coin /> ~{prod.toFixed(1)}<small>/h</small></span>
                </button>
              );
            })}
          </div>
          )}
        </section>
      )}

      {!aquariumMode && (
      <section>
        <div className="section-head">
          <span className="eyebrow">Fila de criação</span>
          <span className="count">{tank.queue.length}/{tank.queueCap}</span>
          <span className="spacer" />
          {import.meta.env.DEV && (
            <button className="dev-btn" onClick={devSpawn} title="Só existe em dev">Gerar peixe (dev)</button>
          )}
          {import.meta.env.DEV && tank.creatures.length > 0 && (
            <button className="dev-btn" onClick={devClear} title="Só existe em dev">Limpar (dev)</button>
          )}
        </div>
        {nextFish.full ? (
          <p className="hint">Fila cheia — colete peixes para liberar espaço.</p>
        ) : (
          <div className="next-fish">
            <span className="nf-label">
              Próximo peixe {nextFish.mins < 1 ? "em instantes" : `em ~${Math.ceil(nextFish.mins)} min`}
              {!tank.online && " (offline, mais devagar)"}
            </span>
            <span className="nf-track"><span style={{ width: `${(nextFish.fraction * 100).toFixed(0)}%` }} /></span>
          </div>
        )}
        <div className="queue">
          {tank.queue.map((item) => (
            <div key={item.id} className={`queue-item glass ${item.isSick ? "sick" : ""}`}>
              <span className="q-label">
                <span>{item.isSick ? "Doente" : "Pronto"}</span>
                <small>{item.isSick ? "raridade reduzida" : "aguardando coleta"}</small>
              </span>
              {!item.isReady && (
                <button className="rush-btn" onClick={() => rushQueue(item.id)}
                  title="Pula a espera pagando moeda premium — a única forma de acelerar">
                  ⚡ {Number(item.rushCostPremium).toFixed(0)}
                </button>
              )}
              <button className="btn-primary" disabled={!item.isReady} onClick={() => collect(item.id)}>
                Coletar
              </button>
            </div>
          ))}
        </div>
      </section>
      )}
    </div>
  );
}
