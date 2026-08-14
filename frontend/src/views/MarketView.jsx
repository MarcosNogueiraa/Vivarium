import { useCallback, useEffect, useState } from "react";
import { api } from "../lib/api.js";
import { coinsPerHourOf } from "../lib/generator.js";
import { bandOf, BANDS } from "../lib/fishRenderer.js";
import { RarityBadge } from "../components/RarityBadge.jsx";
import { Coin } from "../components/Coin.jsx";
import { FishCanvas } from "../components/FishCanvas.jsx";
import { Select } from "../components/Select.jsx";
import { AppearanceFilters } from "../components/AppearanceFilters.jsx";
import { CollapsibleSection } from "../components/CollapsibleSection.jsx";
import { Pagination } from "../components/Pagination.jsx";
import { usePartFilters } from "../hooks/usePartFilters.js";
import { FishDetail } from "./FishDetail.jsx";

const PAGE_SIZE = 24;

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
  // Padrão "raridade, maior primeiro" (14/08/2026, pedido do usuário) — o mercado abre
  // já destacando os peixes mais valiosos, em vez de ordenado por data de listagem.
  const [sortBy, setSortBy] = useState("rarity-desc");
  const [bandFilter, setBandFilter] = useState("all");
  const appearance = usePartFilters();
  const { partFilters } = appearance;

  function resetFilters() {
    setBandFilter("all");
    appearance.reset();
  }

  const refresh = useCallback(async () => {
    setData(await api.listings({
      skip: page * PAGE_SIZE, take: PAGE_SIZE, sort: sortBy, band: bandFilter,
      tailColor: partFilters.tail.colors.join(","), tailPattern: partFilters.tail.patterns.join(","),
      dorsalColor: partFilters.dorsal.colors.join(","), dorsalPattern: partFilters.dorsal.patterns.join(","),
      pectoralColor: partFilters.pectoral.colors.join(","), pectoralPattern: partFilters.pectoral.patterns.join(","),
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
    && bandFilter === "all" && appearance.activeCount === 0;

  const myListingsSection = (
    // Nasce minimizada (13/08/2026, pedido do usuário) — logo abaixo dos filtros, não mais
    // acima deles: o mercado dos OUTROS jogadores é o conteúdo principal da tela, os próprios
    // anúncios são consulta ocasional. Renderizada sempre (mesmo quando o resto do mercado
    // está totalmente vazio) — é informação própria do jogador, independente do que existe
    // pra filtrar/navegar no restante da tela.
    <CollapsibleSection
      variant="prominent"
      icon="🏷️"
      hint="Seus próprios anúncios ativos no mercado — toque pra ver, editar preço ou cancelar."
      title={
        <>
          Meus anúncios ativos{" "}
          <span className="filter-count-badge">{data.myActiveListingsCount}/{data.maxActiveListings}</span>
        </>
      }
    >
      {data.myListings.length === 0 ? (
        <p className="hint">Você não tem anúncios ativos. Vá até o Tanque ou a Mochila e venda um peixe pra listar aqui.</p>
      ) : (
        <div className="grid market-mine-grid">
          {data.myListings.map((l) => (
            <FishCard key={l.id} l={l} userId={userId} onOpen={setDetail} onBuy={buy} onCancel={cancel} />
          ))}
        </div>
      )}
    </CollapsibleSection>
  );

  return (
    <>
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
            <span className="spacer" />
            {(appearance.activeCount > 0 || bandFilter !== "all") && (
              <button type="button" className="filter-reset-btn" onClick={resetFilters}>
                ↺ Redefinir filtros
              </button>
            )}
          </div>
          <AppearanceFilters
            partFilters={appearance.partFilters}
            activeCount={appearance.activeCount}
            onToggleColor={appearance.toggleColor}
            onTogglePattern={appearance.togglePattern}
            onClearColors={appearance.clearColors}
            onClearPatterns={appearance.clearPatterns}
          />
        </>
      )}

      {/* Posição estável como irmão de nível superior, fora das ramificações condicionais
          acima e abaixo — se ficasse ANINHADA dentro de uma delas, cancelar o único anúncio
          ativo (o que costuma fazer nothingAtAll virar true, trocando o ramo renderizado)
          desmontaria e remontaria esta seção, perdendo o estado local de expandido/recolhido
          do CollapsibleSection (bug real pego no e2e). Continua "abaixo dos filtros" na
          posição visual porque é o próximo irmão logo depois do bloco de filtros/estado vazio. */}
      {myListingsSection}

      {!nothingAtAll && (
        <>
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
