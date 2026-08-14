import { useCallback, useEffect, useState } from "react";
import { api } from "../lib/api.js";
import { CONFIG, coinsPerHourOf } from "../lib/generator.js";
import { bandOf, BANDS, PART_HEX, PT } from "../lib/fishRenderer.js";
import { PART_PT } from "../lib/format.js";
import { RarityBadge } from "../components/RarityBadge.jsx";
import { Coin } from "../components/Coin.jsx";
import { FishCanvas } from "../components/FishCanvas.jsx";
import { Select } from "../components/Select.jsx";
import { CollapsibleSection } from "../components/CollapsibleSection.jsx";
import { Pagination } from "../components/Pagination.jsx";
import { FishDetail } from "./FishDetail.jsx";

const PAGE_SIZE = 24;

// Mesmo padrão de filtro por parte já usado 2x (BackpackView, BreedingView) — 3º lugar,
// ainda não justifica extrair hook compartilhado (mantém o escopo desta tarefa enxuto).
const PARTS = ["tail", "dorsal", "pectoral"];
const PATTERN_VALUES = CONFIG.patternTypes.map(([v]) => v);
const emptyPartFilter = { color: "all", pattern: "all" };

const SORTS = {
  "newest": { label: "Mais recentes primeiro" },
  "oldest": { label: "Mais antigos primeiro" },
  "price-asc": { label: "Preço (menor primeiro)" },
  "price-desc": { label: "Preço (maior primeiro)" },
  "rarity-desc": { label: "Raridade (maior primeiro)" },
  "rarity-asc": { label: "Raridade (menor primeiro)" },
};

function FishCard({ l, userId, onOpen, onBuy, onCancel }) {
  return (
    <div className="card" style={{ "--tier": bandOf(Number(l.rarityScore)).color }}>
      <button className="fish-stage as-button" onClick={() => onOpen(l)} title="Ver detalhes">
        <FishCanvas creature={l} />
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
        <button onClick={() => onOpen(l)}>Detalhes</button>
        {l.sellerId === userId
          ? <button onClick={() => onCancel(l)}>Cancelar</button>
          : <button className="btn-primary" onClick={() => onBuy(l)}>Comprar</button>}
      </div>
    </div>
  );
}

export function MarketView({ userId, refreshTank, notify }) {
  const [data, setData] = useState(null);
  const [detail, setDetail] = useState(null);
  const [page, setPage] = useState(0);
  const [sortBy, setSortBy] = useState("newest");
  const [bandFilter, setBandFilter] = useState("all");
  const [partFilters, setPartFilters] = useState({
    tail: { ...emptyPartFilter }, dorsal: { ...emptyPartFilter }, pectoral: { ...emptyPartFilter },
  });

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

  const refresh = useCallback(async () => {
    setData(await api.listings({
      skip: page * PAGE_SIZE, take: PAGE_SIZE, sort: sortBy, band: bandFilter,
      tailColor: partFilters.tail.color, tailPattern: partFilters.tail.pattern,
      dorsalColor: partFilters.dorsal.color, dorsalPattern: partFilters.dorsal.pattern,
      pectoralColor: partFilters.pectoral.color, pectoralPattern: partFilters.pectoral.pattern,
    }));
  }, [page, sortBy, bandFilter, partFilters]);

  useEffect(() => { refresh().catch((err) => notify(err.message)); }, [refresh, notify]);
  // Filtro/ordenação mudou: volta pra página 0 (uma página que não existe mais no novo
  // resultado filtrado ficaria "presa" mostrando nada, sem forma óbvia de voltar).
  useEffect(() => { setPage(0); }, [sortBy, bandFilter, partFilters]);

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

  if (data === null) return <p className="hint">Carregando mercado…</p>;

  const nothingAtAll = data.listings.length === 0 && data.myListings.length === 0 && page === 0
    && bandFilter === "all" && activeAppearanceFilters === 0;

  return (
    <>
      <div className="market-mine">
        <div className="section-head">
          <span className="eyebrow">Meus anúncios ativos</span>
          <span className="count">{data.myActiveListingsCount}/{data.maxActiveListings}</span>
        </div>
        {data.myListings.length === 0 ? (
          <p className="hint">Você não tem anúncios ativos. Vá até o Tanque ou a Mochila e venda um peixe pra listar aqui.</p>
        ) : (
          <div className="grid market-mine-grid">
            {data.myListings.map((l) => (
              <FishCard key={l.id} l={l} userId={userId} onOpen={setDetail} onBuy={buy} onCancel={cancel} />
            ))}
          </div>
        )}
      </div>

      {nothingAtAll ? (
        <div className="empty-state glass">
          <span className="empty-state-icon">🐠</span>
          <strong>O mercado está vazio</strong>
          <p className="muted">
            Ninguém está vendendo peixe agora — seja o primeiro. Vá até o <b>Tanque</b> ou a{" "}
            <b>Mochila</b>, escolha um peixe e toque em "Vender".
          </p>
        </div>
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

          {data.listings.length === 0 ? (
            <p className="hint">Nenhum peixe corresponde a esse filtro.</p>
          ) : (
            <div className="grid">
              {data.listings.map((l) => (
                <FishCard key={l.id} l={l} userId={userId} onOpen={setDetail} onBuy={buy} onCancel={cancel} />
              ))}
            </div>
          )}
          <Pagination page={page} totalCount={data.totalCount} pageSize={PAGE_SIZE} onPageChange={setPage} />
        </>
      )}

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
