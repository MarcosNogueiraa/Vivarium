import { useEffect, useState } from "react";
import { api } from "../lib/api.js";
import { Modal } from "./Modal.jsx";
import { FishCanvas } from "./FishCanvas.jsx";
import { Coin } from "./Coin.jsx";
import { bandOf } from "../lib/fishRenderer.js";

function MiniFish({ creature, label, died }) {
  const score = Number(creature.rarityScore);
  const band = bandOf(score);
  return (
    <div className="hist-fish">
      <div className={`hist-fish-thumb${died ? " hist-fish-dead" : ""}`} style={{ "--tier": band.color }}>
        <FishCanvas
          seed={creature.seed} width={56} isBred={creature.isBred}
          parentASeed={creature.parentASeed} parentBSeed={creature.parentBSeed}
          parentAGrandparentASeed={creature.parentAGrandparentASeed} parentAGrandparentBSeed={creature.parentAGrandparentBSeed}
          parentBGrandparentASeed={creature.parentBGrandparentASeed} parentBGrandparentBSeed={creature.parentBGrandparentBSeed}
        />
        {died && <span className="hist-fish-rip" title="Não sobreviveu à gestação">🕊️</span>}
      </div>
      <span className="hist-fish-label">{label}</span>
      <span className="mono hist-fish-score" style={{ color: band.color }}>{score.toFixed(1)}</span>
    </div>
  );
}

function HistoryEntry({ entry }) {
  const date = new Date(entry.startedAt).toLocaleDateString("pt-BR", { day: "2-digit", month: "2-digit", year: "2-digit" });
  return (
    <div className="hist-entry">
      <div className="hist-entry-fish">
        <MiniFish creature={entry.parentA} label="Pai A" died={entry.parentADied} />
        <span className="hist-op">+</span>
        <MiniFish creature={entry.parentB} label="Pai B" died={entry.parentBDied} />
        <span className="hist-op">→</span>
        {entry.child
          ? <MiniFish creature={entry.child} label="Filhote" />
          : <span className="hist-fish hist-fish-missing">— filhote removido —</span>}
      </div>
      <div className="hist-entry-meta">
        <span className="hist-date">{date}</span>
        <span className="mono"><Coin /> {Number(entry.costPaid).toFixed(0)}</span>
        {entry.insuranceUsed && <span title="Seguro de cruzamento usado">🛡️</span>}
      </div>
    </div>
  );
}

/** Registro de cruzamentos (§8.19) — histórico de gestações já coletadas, pra consulta e troubleshooting. */
export function BreedingHistory({ onClose }) {
  const [entries, setEntries] = useState(null); // null = carregando

  useEffect(() => {
    api.breedingHistory().then(setEntries).catch(() => setEntries([]));
  }, []);

  return (
    <Modal onClose={onClose} className="wide">
      <div className="eyebrow">📜 Registro de cruzamentos</div>
      {entries === null && <p className="hint">Carregando…</p>}
      {entries !== null && entries.length === 0 && (
        <p className="hint">Nenhum cruzamento coletado ainda.</p>
      )}
      {entries !== null && entries.length > 0 && (
        <div className="hist-list">
          {entries.map((e) => <HistoryEntry key={e.id} entry={e} />)}
        </div>
      )}
    </Modal>
  );
}
