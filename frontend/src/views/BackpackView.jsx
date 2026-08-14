import { useCallback, useEffect, useMemo, useState } from "react";
import { api } from "../lib/api.js";
import { CONFIG, coinsPerHourOf, traitsOf, vendorPriceOf } from "../lib/generator.js";
import { bandOf, BANDS, PART_HEX, PT } from "../lib/fishRenderer.js";
import { PART_PT } from "../lib/format.js";
import { RarityBadge } from "../components/RarityBadge.jsx";
import { FishCanvas } from "../components/FishCanvas.jsx";
import { Select } from "../components/Select.jsx";
import { PromptModal } from "../components/PromptModal.jsx";
import { ConfirmModal } from "../components/ConfirmModal.jsx";
import { CollapsibleSection } from "../components/CollapsibleSection.jsx";
import { CollectCelebration } from "../components/CollectCelebration.jsx";
import { FishDetail } from "./FishDetail.jsx";

const PARTS = ["tail", "dorsal", "pectoral"];
const PATTERN_VALUES = CONFIG.patternTypes.map(([v]) => v);
const emptyPartFilter = { color: "all", pattern: "all" };

const SORTS = {
  "rarity-desc": { label: "Raridade (maior primeiro)", cmp: (a, b) => Number(b.rarityScore) - Number(a.rarityScore) },
  "rarity-asc": { label: "Raridade (menor primeiro)", cmp: (a, b) => Number(a.rarityScore) - Number(b.rarityScore) },
  "production-desc": { label: "Produção (maior primeiro)", cmp: (a, b) => coinsPerHourOf(Number(b.rarityScore)) - coinsPerHourOf(Number(a.rarityScore)) },
  "production-asc": { label: "Produção (menor primeiro)", cmp: (a, b) => coinsPerHourOf(Number(a.rarityScore)) - coinsPerHourOf(Number(b.rarityScore)) },
  "newest": { label: "Mais recentes primeiro", cmp: (a, b) => new Date(b.createdAt) - new Date(a.createdAt) },
  "oldest": { label: "Mais antigos primeiro", cmp: (a, b) => new Date(a.createdAt) - new Date(b.createdAt) },
};

export function BackpackView({ refreshTank, notify }) {
  const [data, setData] = useState(null);
  const [detail, setDetail] = useState(null);
  const [revealing, setRevealing] = useState(null); // peixe isNew sendo revelado (CollectCelebration)
  const [prompt, setPrompt] = useState(null); // { kind: "sell"|"transfer"|"vendor", creature }
  const [sortBy, setSortBy] = useState("rarity-desc");
  const [bandFilter, setBandFilter] = useState("all");
  const [partFilters, setPartFilters] = useState({
    tail: { ...emptyPartFilter }, dorsal: { ...emptyPartFilter }, pectoral: { ...emptyPartFilter },
  });
  const [onlyBred, setOnlyBred] = useState(false);
  const [selectMode, setSelectMode] = useState(false);
  const [selected, setSelected] = useState(() => new Set());
  const [bulkConfirm, setBulkConfirm] = useState(false);
  const [bulkProgress, setBulkProgress] = useState(null); // { done, total } | null

  const refresh = useCallback(async () => { setData(await api.backpack()); }, []);
  useEffect(() => { refresh().catch((e) => notify(e.message)); }, [refresh, notify]);

  function toggleSelectMode() {
    setSelectMode((v) => !v);
    setSelected(new Set());
  }
  function toggleSelected(c) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(c.id)) next.delete(c.id); else next.add(c.id);
      return next;
    });
  }

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
  function sellVendor(c) { setDetail(null); setPrompt({ kind: "vendor", creature: c }); }

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
  async function confirmSellVendor() {
    const { price } = await api.sellToVendor(prompt.creature.id);
    setPrompt(null);
    notify(`Vendido ao NPC por ${price} moedas.`);
    await refresh();
  }

  // Peixe coletado pela coleta AUTOMÁTICA de VIP (creature.isNew): score/produção ficam
  // escondidos até o jogador abrir a revelação (CollectCelebration — mesmo componente do
  // momento de coleta manual, Raro+ com clique-a-clique, mesmo abaixo disso instantâneo).
  // Só marca IsNew=false (mark-seen) quando a revelação de fato termina — `onRevealed` do
  // CollectCelebration, nunca ao só abrir o modal. Atualização otimista local (sem esperar
  // refresh completo), com fallback pro estado normal (refresh) se o servidor rejeitar.
  async function revealNew(c) {
    try {
      await api.markSeen(c.id);
      setData((prev) => prev && {
        ...prev,
        creatures: prev.creatures.map((x) => (x.id === c.id ? { ...x, isNew: false } : x)),
      });
    } catch (e) {
      notify(e.message);
      await refresh();
    }
  }

  const activeAppearanceFilters = PARTS.reduce(
    (n, part) => n + (partFilters[part].color !== "all" ? 1 : 0) + (partFilters[part].pattern !== "all" ? 1 : 0),
    0
  );

  const visible = useMemo(() => {
    if (!data) return [];
    return data.creatures
      .filter((c) => bandFilter === "all" || bandOf(Number(c.rarityScore)).name === bandFilter)
      .filter((c) => {
        const t = traitsOf(c);
        return PARTS.every((part) => {
          const f = partFilters[part];
          return (f.color === "all" || t[part].color === f.color)
            && (f.pattern === "all" || t[part].pattern === f.pattern);
        });
      })
      .filter((c) => !onlyBred || c.isBred)
      // Peixe novo (ainda não revelado) sempre primeiro, não importa a ordenação escolhida —
      // senão ele podia ficar perdido no meio da lista, escondido atrás de "???" sem chamar
      // atenção. Dentro de cada grupo (novo/já visto), mantém a ordenação normal.
      .sort((a, b) => (b.isNew - a.isNew) || SORTS[sortBy].cmp(a, b));
  }, [data, sortBy, bandFilter, partFilters, onlyBred]);

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

  const selectedCreatures = visible.filter((c) => selected.has(c.id));
  const selectedTotal = selectedCreatures.reduce((sum, c) => sum + vendorPriceOf(Number(c.rarityScore)), 0);

  function selectAllVisible() { setSelected(new Set(visible.map((c) => c.id))); }
  function clearSelection() { setSelected(new Set()); }

  async function confirmBulkSellVendor() {
    const ids = [...selected];
    setBulkProgress({ done: 0, total: ids.length });
    // Sequencial, não Promise.all: o Habitat usa concorrência otimista (xmin,
    // CLAUDE.md 12.1) — vários pedidos em paralelo tentando atualizar a mesma
    // linha (o tick roda antes de cada venda) colidem e voltam 409 na maioria.
    // Um de cada vez evita a corrida.
    let okCount = 0;
    let total = 0;
    for (const id of ids) {
      try {
        const { price } = await api.sellToVendor(id);
        okCount++;
        total += Number(price);
      } catch { /* segue pro próximo; contabilizado como falha abaixo */ }
      setBulkProgress((p) => (p ? { ...p, done: p.done + 1 } : p));
    }
    const failCount = ids.length - okCount;
    setBulkConfirm(false);
    setBulkProgress(null);
    setSelectMode(false);
    setSelected(new Set());
    notify(
      failCount === 0
        ? `${okCount} peixe(s) vendido(s) por ${total.toFixed(0)} moedas.`
        : `${okCount} vendido(s) por ${total.toFixed(0)} moedas — ${failCount} falharam (tente de novo).`
    );
    await Promise.all([refresh(), refreshTank()]);
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
        <>
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
            {filtersActive && (
              <button type="button" className="filter-reset-btn" onClick={resetFilters}>
                ↺ Redefinir filtros
              </button>
            )}
            <span className="spacer" />
            <button className="select-toggle" onClick={toggleSelectMode} aria-pressed={selectMode}
              title={selectMode ? "Sair da seleção" : "Selecionar vários peixes (ex: vender ao NPC de uma vez)"}>
              Selecionar peixes
              <span className={`switch-track${selectMode ? " on" : ""}`}>
                <span className="switch-thumb" />
              </span>
            </button>
          </div>
          <CollapsibleSection
            variant="prominent"
            hint="Filtre por cor e padrão de cada parte — cauda, dorsal e peitoral, de forma independente."
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
          {selectMode && (
            <div className="select-toolbar">
              <span className="muted">{selected.size} selecionado(s)</span>
              <button onClick={selectAllVisible}>Selecionar todos ({visible.length})</button>
              {selected.size > 0 && <button onClick={clearSelection}>Limpar</button>}
            </div>
          )}
          {visible.length === 0 ? (
            <p className="hint">Nenhum peixe corresponde a esse filtro.</p>
          ) : (
            <div className="grid" style={{ paddingBottom: selectMode && selected.size > 0 ? 70 : 0 }}>
              {visible.map((c) => {
                const isSelected = selected.has(c.id);
                // Cor da faixa de raridade some enquanto não revelado — a própria borda do
                // card já entregaria a raridade de relance, senão (var(--muted), neutro).
                const tierColor = c.isNew ? "var(--muted)" : bandOf(Number(c.rarityScore)).color;
                return (
                <div key={c.id} className="card" style={{
                  "--tier": tierColor,
                  ...(selectMode && isSelected ? { borderColor: "var(--tier)", boxShadow: "0 0 0 2px var(--tier)" } : {}),
                }}>
                  <button
                    className="fish-stage as-button"
                    onClick={() => {
                      if (selectMode && !c.isNew) toggleSelected(c);
                      else if (c.isNew) setRevealing(c);
                      else if (!selectMode) setDetail(c);
                    }}
                    title={c.isNew ? "Peixe novo — clique pra revelar" : selectMode ? "Selecionar" : "Ver detalhes"}
                  >
                    <div className={c.isNew ? "fish-silhouette" : undefined}>
                      <FishCanvas creature={c} />
                    </div>
                    {selectMode && <span className={`select-check${isSelected ? " checked" : ""}`}>{isSelected ? "✓" : ""}</span>}
                  </button>
                  {c.isNew ? (
                    <div className="card-row">
                      <span className="new-tag">🆕 ??? — clique pra revelar</span>
                    </div>
                  ) : (
                    <div className="card-row">
                      <RarityBadge score={Number(c.rarityScore)} />
                      <span className="produces mono">~{coinsPerHourOf(Number(c.rarityScore)).toFixed(1)}/h</span>
                    </div>
                  )}
                  {c.isBred && <span className="bred-tag">🐣 Filhote</span>}
                  {!selectMode && !c.isNew && (
                    <div className="card-row">
                      <button className="btn-primary" onClick={() => deploy(c)}>Pro tanque</button>
                      <button onClick={() => sell(c)}>Vender</button>
                      <button onClick={() => transfer(c)}>Transferir</button>
                      <button onClick={() => sellVendor(c)} title={`Venda instantânea ao NPC por ${vendorPriceOf(Number(c.rarityScore))} soft`}>
                        NPC · {vendorPriceOf(Number(c.rarityScore))}
                      </button>
                    </div>
                  )}
                </div>
                );
              })}
            </div>
          )}
          {selectMode && selected.size > 0 && (
            <div className="sticky-bar">
              <span className="hint" style={{ padding: 0 }}>
                {selected.size} peixe(s) selecionado(s) — NPC paga <b>{selectedTotal.toFixed(0)}</b> no total
              </span>
              <button onClick={clearSelection}>Desselecionar</button>
              <button className="btn-primary" onClick={() => setBulkConfirm(true)}>Vender ao NPC</button>
            </div>
          )}
        </>
      )}
      {revealing && (
        <CollectCelebration
          creature={revealing}
          onClose={() => setRevealing(null)}
          onRevealed={() => revealNew(revealing)}
        />
      )}
      {detail && (
        <FishDetail creature={detail} onClose={() => setDetail(null)}>
          <button className="btn-primary" onClick={() => deploy(detail)}>Pro tanque</button>
          <button onClick={() => sell(detail)}>Vender</button>
          <button onClick={() => transfer(detail)}>Transferir</button>
          <button onClick={() => sellVendor(detail)}>Vender ao NPC · {vendorPriceOf(Number(detail.rarityScore))}</button>
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
      {prompt?.kind === "vendor" && (
        <ConfirmModal
          title="Vender ao NPC"
          message={`Venda instantânea por ${vendorPriceOf(Number(prompt.creature.rarityScore))} moedas soft — bem abaixo do mercado, mas na hora. Essa ação não pode ser desfeita.`}
          confirmLabel="Vender agora" danger
          onConfirm={confirmSellVendor} onClose={() => setPrompt(null)}
        />
      )}
      {bulkConfirm && (
        <ConfirmModal
          title="Vender ao NPC"
          message={bulkProgress
            ? `Vendendo ${bulkProgress.done} de ${bulkProgress.total}...`
            : `Venda instantânea de ${selected.size} peixe(s) por ${selectedTotal.toFixed(0)} moedas soft no total — bem abaixo do mercado, mas na hora. Essa ação não pode ser desfeita.`}
          confirmLabel="Vender agora" danger
          onConfirm={confirmBulkSellVendor} onClose={() => setBulkConfirm(false)}
        />
      )}
    </>
  );
}
