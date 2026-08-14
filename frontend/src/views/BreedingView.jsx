import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { api } from "../lib/api.js";
import { useBreeding } from "../hooks/useBreeding.js";
import { AquariumCanvas } from "../components/AquariumCanvas.jsx";
import { RarityBadge } from "../components/RarityBadge.jsx";
import { FishCanvas } from "../components/FishCanvas.jsx";
import { Modal } from "../components/Modal.jsx";
import { ConfirmModal } from "../components/ConfirmModal.jsx";
import { Coin } from "../components/Coin.jsx";
import { Select } from "../components/Select.jsx";
import { CollectCelebration } from "../components/CollectCelebration.jsx";
import { PeekPanel } from "../components/PeekPanel.jsx";
import { BreedingHistory } from "../components/BreedingHistory.jsx";
import { CollapsibleSection } from "../components/CollapsibleSection.jsx";
import { bandOf, BANDS, PART_HEX, PT } from "../lib/fishRenderer.js";
import { breedingPreview, coinsPerHourOf, traitsOf, CONFIG } from "../lib/generator.js";
import { PART_PT, partSummary } from "../lib/format.js";

const PARTS = ["tail", "dorsal", "pectoral"];
const PATTERN_VALUES = CONFIG.patternTypes.map(([v]) => v);
const emptyPartFilter = { color: "all", pattern: "all" };

const SORTS = {
  "rarity-desc": { label: "Raridade (maior primeiro)", cmp: (a, b) => Number(b.rarityScore) - Number(a.rarityScore) },
  "rarity-asc": { label: "Raridade (menor primeiro)", cmp: (a, b) => Number(a.rarityScore) - Number(b.rarityScore) },
  "production-desc": { label: "Produção (maior primeiro)", cmp: (a, b) => coinsPerHourOf(Number(b.rarityScore)) - coinsPerHourOf(Number(a.rarityScore)) },
  "production-asc": { label: "Produção (menor primeiro)", cmp: (a, b) => coinsPerHourOf(Number(a.rarityScore)) - coinsPerHourOf(Number(b.rarityScore)) },
};

function ParentPreviewCard({ label, creature, traits }) {
  const score = Number(creature.rarityScore);
  const band = bandOf(score);
  return (
    <div className="parent-preview-card">
      <span className="eyebrow">{label}</span>
      <FishCanvas creature={creature} width={220} />
      <span className="badge" style={{ "--tier": band.color }}><span className="gem" /> {band.name} · {score.toFixed(1)}</span>
      {creature.isBred && <span className="bred-tag">🐣 Filhote</span>}
      <div className="peek-row">
        {traits.shimmerTier === "None" ? "Corpo sem brilho" : `${PT.tier[traits.shimmerTier]} · ${PT.shimmer[traits.shimmerColor]}`}
      </div>
      <div className="peek-row">{PART_PT.tail}: {partSummary(traits.tail)}</div>
      <div className="peek-row">{PART_PT.dorsal}: {partSummary(traits.dorsal)}</div>
      <div className="peek-row">{PART_PT.pectoral}: {partSummary(traits.pectoral)}</div>
    </div>
  );
}

function DistBars({ dist, labelOf }) {
  const top = dist.slice(0, 4);
  const restProb = dist.slice(4).reduce((sum, d) => sum + d.prob, 0);
  const rows = restProb > 0.004 ? [...top, { value: "__rest", prob: restProb }] : top;
  return (
    <div className="breakdown">
      {rows.map((d, i) => (
        <div className="bd-row" key={i}>
          <span className="bd-label">{d.value === "__rest" ? "Outras" : labelOf(d.value)}</span>
          <span className="bd-bar"><span style={{ width: `${d.prob * 100}%` }} /></span>
          <span className="bd-prob mono">{(d.prob * 100).toFixed(1)}%</span>
        </div>
      ))}
    </div>
  );
}

function timeLeft(readyAt) {
  const ms = new Date(readyAt).getTime() - Date.now();
  if (ms <= 0) return "pronto!";
  const totalMin = Math.ceil(ms / 60_000);
  const h = Math.floor(totalMin / 60);
  const m = totalMin % 60;
  return h > 0 ? `${h}h ${m}min` : `${m}min`;
}

function hoursLabel(h) {
  if (h < 24) return `${h.toFixed(1)}h`;
  return `${(h / 24).toFixed(1)} dias`;
}

export function BreedingView({ tank, refreshTank, notify }) {
  const { status, refresh } = useBreeding();
  const [backpack, setBackpack] = useState(null);
  const [pickA, setPickA] = useState(null);
  const [pickB, setPickB] = useState(null);
  const [quote, setQuote] = useState(null); // null = fechado, "loading" = carregando, objeto = pronto
  const [safety, setSafety] = useState("none"); // "none" | "stabilizer" | "insurance" — proteção do casal
  const [celebrate, setCelebrate] = useState(null); // { child, parentLosses }
  const [peekId, setPeekId] = useState(null); // id do peixe com o painel de detalhes aberto (pairar 1s)
  const [confirmRush, setConfirmRush] = useState(false); // pede confirmação antes de gastar premium
  const [showHistory, setShowHistory] = useState(false);
  const peekTimer = useRef(null);
  const [, forceTick] = useState(0);
  const [sortBy, setSortBy] = useState("rarity-desc");
  const [bandFilter, setBandFilter] = useState("all");
  const [partFilters, setPartFilters] = useState({
    tail: { ...emptyPartFilter }, dorsal: { ...emptyPartFilter }, pectoral: { ...emptyPartFilter },
  });
  const [onlyBred, setOnlyBred] = useState(false);

  const softBalance = Number(tank.wallet?.SOFT ?? 0);
  const premiumBalance = Number(tank.wallet?.PREMIUM ?? 0);

  const loadBackpack = useCallback(async () => { setBackpack(await api.backpack()); }, []);
  useEffect(() => { loadBackpack().catch((e) => notify(e.message)); }, [loadBackpack, notify]);

  // Sem timer de 1s no servidor: só re-renderiza periodicamente pra a contagem
  // regressiva local acompanhar o relógio.
  useEffect(() => {
    const t = setInterval(() => forceTick((n) => n + 1), 30_000);
    return () => clearInterval(t);
  }, []);

  // Hooks precisam rodar em toda renderização, mesmo antes do backpack
  // carregar — por isso o useMemo vem ANTES do `return` de loading abaixo
  // (colocar depois violava a regra dos hooks: nº de hooks mudava entre a
  // renderização de "carregando" e a normal, e o React quebrava com
  // "Rendered more hooks than during the previous render").
  const candidates = backpack ? [...tank.creatures, ...backpack.creatures] : [];
  // Peixe já selecionado (pickA/pickB) nunca some da lista por causa de um filtro alterado
  // depois da escolha — fica visível pra comparação até ser desselecionado, mesmo que não
  // corresponda mais ao filtro atual (pedido do usuário).
  const visibleCandidates = useMemo(() => candidates
    .filter((c) => c.id === pickA?.id || c.id === pickB?.id
      || (bandFilter === "all" || bandOf(Number(c.rarityScore)).name === bandFilter))
    .filter((c) => {
      if (c.id === pickA?.id || c.id === pickB?.id) return true;
      const t = traitsOf(c);
      return PARTS.every((part) => {
        const f = partFilters[part];
        return (f.color === "all" || t[part].color === f.color)
          && (f.pattern === "all" || t[part].pattern === f.pattern);
      });
    })
    .filter((c) => c.id === pickA?.id || c.id === pickB?.id || !onlyBred || c.isBred)
    .sort(SORTS[sortBy].cmp),
  [candidates, bandFilter, partFilters, onlyBred, sortBy, pickA, pickB]);

  const activeAppearanceFilters = PARTS.reduce(
    (n, part) => n + (partFilters[part].color !== "all" ? 1 : 0) + (partFilters[part].pattern !== "all" ? 1 : 0),
    0
  );
  function setPartColor(part, color) {
    setPartFilters((prev) => ({ ...prev, [part]: { ...prev[part], color } }));
  }
  function setPartPattern(part, pattern) {
    setPartFilters((prev) => ({ ...prev, [part]: { ...prev[part], pattern } }));
  }
  function resetFilters() {
    setBandFilter("all");
    setPartFilters({ tail: { ...emptyPartFilter }, dorsal: { ...emptyPartFilter }, pectoral: { ...emptyPartFilter } });
    setOnlyBred(false);
  }
  const filtersActive = activeAppearanceFilters > 0 || bandFilter !== "all" || onlyBred;

  if (status === null || backpack === null) return <p className="hint">Carregando o ninho…</p>;

  async function start() {
    try {
      await api.startBreeding(pickA.id, pickB.id, {
        useStabilizer: safety === "stabilizer",
        useInsurance: safety === "insurance",
      });
      notify("Casal levado pro ninho!");
      setPickA(null);
      setPickB(null);
      setQuote(null);
      setSafety("none");
      await Promise.all([refresh(), refreshTank(), loadBackpack()]);
    } catch (e) { notify(e.message); }
  }

  async function collect() {
    // Snapshot dos pais ANTES de coletar: depois de coletar a gestação vira inativa
    // e `status.slot` some — sem isso não teríamos o retrato de quem não sobreviveu.
    const parentA = status.slot?.parentA;
    const parentB = status.slot?.parentB;
    try {
      const result = await api.collectBreeding();
      const deadParents = [
        result.parentADied ? parentA : null,
        result.parentBDied ? parentB : null,
      ].filter(Boolean);
      setCelebrate({ child: result.child, parentA, parentB, deadParents });
      await Promise.all([refresh(), refreshTank(), loadBackpack()]);
    } catch (e) { notify(e.message); }
  }

  async function devFinish() {
    try {
      await api.devFinishBreeding();
      notify("Gestação zerada (dev).");
      await refresh();
    } catch (e) { notify(e.message); }
  }

  // Sem try/catch aqui de propósito — o ConfirmModal que chama isso já captura o erro
  // e mostra inline, sem fechar sozinho (mesmo padrão de outras ações destrutivas/pagas).
  async function confirmRushAction() {
    await api.rushBreeding();
    setConfirmRush(false);
    notify("Gestação acelerada!");
    await Promise.all([refresh(), refreshTank()]);
  }

  function togglePick(c) {
    if (pickA?.id === c.id) { setPickA(null); return; }
    if (pickB?.id === c.id) { setPickB(null); return; }
    if (!pickA) setPickA(c);
    else if (!pickB) setPickB(c);
  }

  async function openQuote() {
    setSafety("none");
    setQuote("loading");
    try {
      setQuote(await api.breedingQuote(pickA.id, pickB.id));
    } catch (e) {
      notify(e.message);
      setQuote(null);
    }
  }

  async function confirmStart() {
    await start();
  }

  function schedulePeek(c) {
    clearTimeout(peekTimer.current);
    peekTimer.current = setTimeout(() => setPeekId(c.id), 1000);
  }
  function cancelPeek() {
    clearTimeout(peekTimer.current);
    setPeekId(null);
  }

  const bothPicked = pickA && pickB;
  const preview = bothPicked ? breedingPreview(pickA, pickB) : null;
  // Custo total em soft da gestação (só existe com a prévia carregada) — usado tanto na linha
  // "Custo" quanto direto no botão de confirmar, pra ficar explícito antes de gastar (pedido do
  // usuário: o custo não estava visível no momento da decisão, só rolando a prévia pra cima).
  const totalCostSoft = quote && quote !== "loading"
    ? quote.costSoft + (safety === "stabilizer" ? quote.stabilizerCostSoft : 0)
    : 0;

  const content = status.active ? (
    <>
      <div className="section-head">
        <span className="eyebrow">Ninho</span>
        <span className="count">{status.slot.isReady ? "pronto!" : timeLeft(status.slot.readyAt)}</span>
        <span className="spacer" />
        <button className="guide-btn" onClick={() => setShowHistory(true)} title="Registro de cruzamentos">📜</button>
      </div>
      <AquariumCanvas
        creatures={[status.slot.parentA, status.slot.parentB]}
        selectedId={null} onSelect={() => {}} interactive={false} ambient theme="breeding"
      />
      <div className="card-row" style={{ justifyContent: "center", gap: 16, marginTop: 12 }}>
        <RarityBadge score={Number(status.slot.parentA.rarityScore)} />
        <span>+</span>
        <RarityBadge score={Number(status.slot.parentB.rarityScore)} />
      </div>
      <p className="hint" style={{ textAlign: "center" }}>Custo pago: <Coin /> {Number(status.slot.costPaid).toFixed(0)} soft</p>
      {status.slot.insuranceUsed ? (
        <p className="hint" style={{ textAlign: "center" }}>🛡️ Seguro ativo — nenhum dos pais pode morrer nesta gestação.</p>
      ) : (
        <p className="hint" style={{ textAlign: "center" }}>
          Risco travado: Peixe A {(Number(status.slot.parentADeathChance) * 100).toFixed(0)}%
          &nbsp;·&nbsp; Peixe B {(Number(status.slot.parentBDeathChance) * 100).toFixed(0)}%
        </p>
      )}
      <div className="card-row" style={{ justifyContent: "center", gap: 8, marginTop: 12 }}>
        <button className="btn-primary" disabled={!status.slot.isReady} onClick={collect}>
          {status.slot.isReady ? "Coletar filhote" : "Aguardando…"}
        </button>
        {!status.slot.isReady && (
          <button
            className="rush-btn" onClick={() => setConfirmRush(true)}
            title="Gasta moeda premium pra pular o tempo restante de gestação"
          >
            ⚡ Acelerar com <span className="rush-btn-premium">💎 {Number(status.slot.rushCostPremium).toFixed(0)}</span>
          </button>
        )}
        {import.meta.env.DEV && !status.slot.isReady && (
          <button className="dev-btn" onClick={devFinish} title="Só existe em dev">
            Terminar gestação
          </button>
        )}
      </div>
    </>
  ) : (
    <>
      <div className="section-head">
        <span className="eyebrow">Ninho</span>
        <span className="spacer" />
        <button className="guide-btn" onClick={() => setShowHistory(true)} title="Registro de cruzamentos">📜</button>
      </div>
      {candidates.length < 2 ? (
        <p className="hint">Você precisa de pelo menos 2 peixes (no tanque ou na mochila) pra tentar cruzar.</p>
      ) : (
        <>
          <p className="hint">
            Escolha 2 peixes pra levar pro ninho. Quanto mais raro o casal, mais demorada (e cara) a gestação.
          </p>
          <div className="backpack-toolbar">
            <Select
              value={sortBy} onChange={setSortBy}
              options={Object.entries(SORTS).map(([key, { label }]) => ({ value: key, label }))}
            />
            <div className="filter-chips">
              <button className={`filter-chip${bandFilter === "all" ? " active" : ""}`} onClick={() => setBandFilter("all")}>
                Todos
              </button>
              {BANDS.map((b) => (
                <button
                  key={b.name}
                  className={`filter-chip${bandFilter === b.name ? " active" : ""}`}
                  style={{ "--tier": b.color }}
                  onClick={() => setBandFilter(b.name)}
                >
                  {b.name}
                </button>
              ))}
            </div>
            <label className="filter-toggle">
              <input type="checkbox" checked={onlyBred} onChange={(e) => setOnlyBred(e.target.checked)} />
              Só filhotes 🐣
            </label>
            <span className="spacer" />
            {filtersActive && (
              <button type="button" className="filter-reset-btn" onClick={resetFilters}>
                ↺ Redefinir filtros
              </button>
            )}
          </div>
          <CollapsibleSection
            variant="prominent"
            hint="Filtre por cor e padrão de cada parte — cauda, dorsal e peitoral, de forma independente. Útil pra escolher pais que combinem traços específicos."
            title={
              <>
                Filtros avançados{" "}
                {activeAppearanceFilters > 0 && <span className="filter-count-badge">({activeAppearanceFilters})</span>}
              </>
            }
          >
            <div className="appearance-filter-group">
              {PARTS.map((part) => (
                <div className="appearance-filter-part" key={part}>
                  <strong>{PART_PT[part]}</strong>
                  <div className="filter-chips">
                    <button
                      className={`filter-chip${partFilters[part].color === "all" ? " active" : ""}`}
                      onClick={() => setPartColor(part, "all")}
                    >
                      Toda cor
                    </button>
                    {Object.keys(PART_HEX).map((color) => (
                      <button
                        key={color}
                        className={`filter-chip color-chip${partFilters[part].color === color ? " active" : ""}`}
                        style={{ "--tier": PART_HEX[color] }}
                        title={PT.color[color]}
                        onClick={() => setPartColor(part, color)}
                      >
                        <span className="dot-color" style={{ background: PART_HEX[color] }} />
                      </button>
                    ))}
                  </div>
                  <div className="filter-chips">
                    <button
                      className={`filter-chip${partFilters[part].pattern === "all" ? " active" : ""}`}
                      onClick={() => setPartPattern(part, "all")}
                    >
                      Todo padrão
                    </button>
                    {PATTERN_VALUES.map((pattern) => (
                      <button
                        key={pattern}
                        className={`filter-chip${partFilters[part].pattern === pattern ? " active" : ""}`}
                        onClick={() => setPartPattern(part, pattern)}
                      >
                        {PT.pattern[pattern]}
                      </button>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </CollapsibleSection>
          {visibleCandidates.length === 0 ? (
            <p className="hint">Nenhum peixe corresponde a esse filtro.</p>
          ) : (
          <div className="grid" style={{ paddingBottom: bothPicked ? 70 : 0 }}>
            {visibleCandidates.map((c) => {
              const picked = pickA?.id === c.id || pickB?.id === c.id;
              return (
                <div
                  key={c.id}
                  className="card"
                  style={{
                    "--tier": bandOf(Number(c.rarityScore)).color,
                    ...(picked ? { borderColor: "var(--tier)", boxShadow: "0 0 0 2px var(--tier)" } : {}),
                  }}
                  onMouseEnter={() => schedulePeek(c)}
                  onMouseLeave={cancelPeek}
                >
                  <button className="fish-stage as-button" onClick={() => togglePick(c)} title="Selecionar">
                    <FishCanvas creature={c} />
                  </button>
                  <RarityBadge score={Number(c.rarityScore)} />
                  {c.isBred && <span className="bred-tag">🐣 Filhote</span>}
                  {peekId === c.id && (
                    <div className="peek-overlay">
                      <PeekPanel creature={c} />
                    </div>
                  )}
                </div>
              );
            })}
          </div>
          )}
        </>
      )}

      {bothPicked && (
        <div className="sticky-bar">
          <span className="hint" style={{ padding: 0 }}>2 peixes selecionados</span>
          <button onClick={() => { setPickA(null); setPickB(null); }}>Desselecionar</button>
          <button className="btn-primary" onClick={openQuote}>Ver chances e cruzar</button>
        </div>
      )}
    </>
  );

  return (
    <>
      {content}

      {quote && preview && (
        <Modal onClose={() => setQuote(null)} className="wide">
          <div className="eyebrow">Prévia do cruzamento</div>

          <div className="breed-parents">
            <ParentPreviewCard label="Peixe A" creature={pickA} traits={preview.parentA} />
            <span className="breed-plus">+</span>
            <ParentPreviewCard label="Peixe B" creature={pickB} traits={preview.parentB} />
          </div>

          {quote === "loading" ? (
            <p className="hint">Calculando…</p>
          ) : (
            <>
              <div className="card-row" style={{ marginTop: 16 }}>
                <span>Custo</span>
                <span className="mono">
                  <Coin /> {totalCostSoft.toFixed(0)} soft
                  {safety === "insurance" && <> + 💎 {quote.insuranceCostPremium.toFixed(0)} premium</>}
                </span>
              </div>
              <div className="card-row">
                <span>Gestação</span>
                <span className="mono">{hoursLabel(quote.gestationHours)}</span>
              </div>

              <div className="detail-section">
                <div className="eyebrow">Chance do brilho do filhote</div>
                <div className="breakdown">
                  {Object.entries(quote.childTierProbabilities)
                    .sort((a, b) => b[1] - a[1])
                    .map(([tier, p]) => (
                      <div className="bd-row" key={tier}>
                        <span className="bd-label">{PT.tier[tier] ?? tier}</span>
                        <span className="bd-bar"><span style={{ width: `${p * 100}%` }} /></span>
                        <span className="bd-prob mono">{(p * 100).toFixed(1)}%</span>
                      </div>
                    ))}
                </div>
              </div>

              {["tail", "dorsal", "pectoral"].map((part) => (
                <div className="detail-section" key={part}>
                  <div className="eyebrow">{PART_PT[part]} do filhote</div>
                  <p className="bd-help" style={{ marginBottom: 4 }}>Cor</p>
                  <DistBars dist={preview[part].color} labelOf={(v) => PT.color[v]} />
                  <p className="bd-help" style={{ margin: "10px 0 4px" }}>Padrão</p>
                  <DistBars dist={preview[part].pattern} labelOf={(v) => PT.pattern[v]} />
                </div>
              ))}

              <div className="detail-section">
                <div className="eyebrow">Risco de morte dos pais</div>
                <p className="bd-help">
                  Cada cruzamento completado aumenta o risco do próximo — nunca garantido, mas cresce com o uso.
                  Descansar o peixe (não cruzar por uns dias) já reduz esse risco sozinho, de graça.
                </p>
                {(() => {
                  const factor = safety === "insurance" ? 0 : safety === "stabilizer" ? quote.stabilizerReductionFactor : 1;
                  const effA = quote.parentADeathChance * factor;
                  const effB = quote.parentBDeathChance * factor;
                  return (
                    <>
                      <div className="card-row">
                        <span>Peixe A ({quote.parentABreedCount}× cruzado)</span>
                        <span className="mono">{(effA * 100).toFixed(0)}%</span>
                      </div>
                      <div className="card-row">
                        <span>Peixe B ({quote.parentBBreedCount}× cruzado)</span>
                        <span className="mono">{(effB * 100).toFixed(0)}%</span>
                      </div>
                    </>
                  );
                })()}

                <div className="safety-options">
                  {(() => {
                    const canAffordNone = softBalance >= quote.costSoft;
                    const canAffordStabilizer = softBalance >= quote.costSoft + quote.stabilizerCostSoft;
                    const canAffordInsurance = softBalance >= quote.costSoft && premiumBalance >= quote.insuranceCostPremium;
                    return (
                      <>
                        <label className="safety-option" title={canAffordNone ? undefined : "Saldo de soft insuficiente"}>
                          <input type="radio" name="safety" checked={safety === "none"} disabled={!canAffordNone} onChange={() => setSafety("none")} />
                          <span>Sem proteção</span>
                        </label>
                        <label className="safety-option" title={canAffordStabilizer ? undefined : "Saldo de soft insuficiente"}>
                          <input type="radio" name="safety" checked={safety === "stabilizer"} disabled={!canAffordStabilizer} onChange={() => setSafety("stabilizer")} />
                          <span>🧪 Estabilizador genético — reduz o risco pela metade (<Coin /> {quote.stabilizerCostSoft.toFixed(0)} soft)</span>
                        </label>
                        <label className="safety-option" title={canAffordInsurance ? undefined : "Saldo de premium insuficiente"}>
                          <input type="radio" name="safety" checked={safety === "insurance"} disabled={!canAffordInsurance} onChange={() => setSafety("insurance")} />
                          <span>🛡️ Seguro de cruzamento — garante que nenhum pai morre (💎 {quote.insuranceCostPremium.toFixed(0)} premium)</span>
                        </label>
                      </>
                    );
                  })()}
                </div>
              </div>

              <div className="detail-actions">
                <button className="btn-primary" onClick={confirmStart}>
                  Confirmar cruzamento · <Coin /> {totalCostSoft.toFixed(0)} soft
                  {safety === "insurance" && <> + 💎 {quote.insuranceCostPremium.toFixed(0)}</>}
                </button>
                <button onClick={() => setQuote(null)}>Cancelar</button>
              </div>
            </>
          )}
        </Modal>
      )}

      {celebrate && (
        <CollectCelebration
          creature={celebrate.child}
          variant="breeding"
          parentA={celebrate.parentA}
          parentB={celebrate.parentB}
          deadParents={celebrate.deadParents}
          onClose={() => setCelebrate(null)}
        />
      )}

      {showHistory && <BreedingHistory onClose={() => setShowHistory(false)} />}

      {confirmRush && status.active && (
        <ConfirmModal
          title="Acelerar cruzamento"
          message={`Isso vai gastar 💎 ${Number(status.slot.rushCostPremium).toFixed(0)} premium pra pular o tempo restante (${timeLeft(status.slot.readyAt)}) e liberar o filhote agora. Essa moeda não volta — confirma?`}
          confirmLabel="Gastar premium e acelerar"
          danger
          onConfirm={confirmRushAction}
          onClose={() => setConfirmRush(false)}
        />
      )}
    </>
  );
}
