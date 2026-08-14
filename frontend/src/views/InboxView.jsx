import { useState } from "react";
import { api } from "../lib/api.js";
import { FishCanvas } from "../components/FishCanvas.jsx";
import { ConfirmModal } from "../components/ConfirmModal.jsx";
import { Coin } from "../components/Coin.jsx";

const KIND_LABELS = {
  AdminMessage: "📨 Mensagem",
  MarketPurchase: "🛒 Comprado no mercado",
  DirectTransfer: "🎁 Recebido por transferência",
};

function EntryCard({ entry, onClaim, busy }) {
  const claimed = Boolean(entry.claimedAt);
  const isDelivery = entry.kind === "MarketPurchase" || entry.kind === "DirectTransfer";

  return (
    <div className={`card${claimed ? " inbox-claimed" : ""}`}>
      <div className="card-row">
        <span className="hint" style={{ padding: 0 }}>{KIND_LABELS[entry.kind] ?? entry.kind}</span>
        {claimed && <span className="badge" style={{ "--tier": "var(--muted)" }}>✓ Resgatado</span>}
      </div>

      {isDelivery && entry.creature && (
        <>
          <div className="fish-stage">
            <FishCanvas creature={entry.creature} />
          </div>
          {entry.senderUsername && <p className="hint" style={{ padding: 0 }}>de {entry.senderUsername}</p>}
        </>
      )}

      {entry.kind === "AdminMessage" && (
        <>
          <strong>{entry.title}</strong>
          <p className="hint" style={{ padding: 0 }}>{entry.body}</p>
          {entry.rewardCurrencyAmount != null && (
            <div className="card-row">
              <span className="produces mono"><Coin /> {Number(entry.rewardCurrencyAmount).toFixed(0)} {entry.rewardCurrencyCode}</span>
            </div>
          )}
        </>
      )}

      {!claimed && (
        <div className="card-row">
          <button className="btn-primary" disabled={busy} onClick={() => onClaim(entry)}>
            {isDelivery ? "Resgatar pro tanque/mochila" : "Resgatar"}
          </button>
        </div>
      )}
    </div>
  );
}

/** Caixa de Entrada (CLAUDE.md §8.23/§8.24) — mensagens administrativas + entrega pendente de
 * peixe (compra no mercado/transferência), unificadas. Item resgatado fica marcado, não some
 * sozinho — "Apagar mensagens lidas" remove só o que já foi resgatado. */
export function InboxView({ entries, refresh, refreshTank, notify }) {
  const [busyId, setBusyId] = useState(null);
  const [busyBulk, setBusyBulk] = useState(false);
  const [confirmClear, setConfirmClear] = useState(false);

  async function claim(entry) {
    setBusyId(entry.id);
    try {
      await api.claimInboxEntry(entry.id);
      notify(entry.kind === "AdminMessage" ? "Resgatado!" : "Peixe entregue!");
      await Promise.all([refresh(), refreshTank()]);
    } catch (e) {
      notify(e.message);
    } finally {
      setBusyId(null);
    }
  }

  async function claimAll() {
    setBusyBulk(true);
    try {
      const { claimedCount, failedCount } = await api.claimAllInboxEntries();
      notify(
        failedCount === 0
          ? `${claimedCount} item(ns) resgatado(s).`
          : `${claimedCount} resgatado(s), ${failedCount} falharam (sem espaço) — tente de novo depois de liberar espaço.`,
      );
      await Promise.all([refresh(), refreshTank()]);
    } catch (e) {
      notify(e.message);
    } finally {
      setBusyBulk(false);
    }
  }

  async function markAllRead() {
    try {
      await api.markAllInboxRead();
      await refresh();
    } catch (e) {
      notify(e.message);
    }
  }

  async function clearClaimed() {
    await api.clearClaimedInboxEntries();
    setConfirmClear(false);
    notify("Mensagens resgatadas apagadas.");
    await refresh();
  }

  if (entries === null) return <p className="hint">Carregando caixa de entrada…</p>;

  const unclaimedCount = entries.filter((e) => !e.claimedAt).length;
  const hasClaimed = entries.some((e) => e.claimedAt);

  return (
    <>
      <div className="section-head">
        <span className="eyebrow">📬 Caixa de Entrada</span>
        <span className="count">{unclaimedCount} pendente(s)</span>
      </div>

      {entries.length === 0 ? (
        <p className="hint">Sua caixa de entrada está vazia. Peixes comprados no mercado ou recebidos por transferência aparecem aqui pra resgatar.</p>
      ) : (
        <>
          <div className="backpack-toolbar">
            {unclaimedCount > 0 && (
              <button className="btn-primary" disabled={busyBulk} onClick={claimAll}>Resgatar tudo</button>
            )}
            <button onClick={markAllRead}>Ler tudo</button>
            {hasClaimed && (
              <button onClick={() => setConfirmClear(true)}>Apagar mensagens lidas</button>
            )}
          </div>
          <div className="grid">
            {entries.map((entry) => (
              <EntryCard key={entry.id} entry={entry} onClaim={claim} busy={busyId === entry.id} />
            ))}
          </div>
        </>
      )}

      {confirmClear && (
        <ConfirmModal
          title="Apagar mensagens lidas"
          message="Remove da lista as mensagens/entregas já resgatadas. Nada com recompensa ainda pendente é apagado."
          confirmLabel="Apagar"
          danger
          onConfirm={clearClaimed}
          onClose={() => setConfirmClear(false)}
        />
      )}
    </>
  );
}
