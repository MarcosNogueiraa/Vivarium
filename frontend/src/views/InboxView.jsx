import { useState } from "react";
import { api } from "../lib/api.js";
import { FishCanvas } from "../components/FishCanvas.jsx";
import { ConfirmModal } from "../components/ConfirmModal.jsx";
import { CollectCelebration } from "../components/CollectCelebration.jsx";
import { EggIcon } from "../components/EggIcon.jsx";
import { Coin } from "../components/Coin.jsx";
import { EGG_TIER, EGG_NAMES } from "../lib/eggs.js";

const KIND_LABELS = {
  AdminMessage: "📨 Mensagem",
  MarketPurchase: "🛒 Comprado no mercado",
  DirectTransfer: "🎁 Recebido por transferência",
  MarketSale: "💰 Vendido no mercado",
  DailyRewardEgg: "🎁 Recompensa diária",
};

const isDeliveryKind = (kind) => kind === "MarketPurchase" || kind === "DirectTransfer";
const hasMessageKind = (kind) => kind === "AdminMessage" || kind === "MarketSale" || kind === "DailyRewardEgg";

function EntryCard({ entry, onClaim, busy }) {
  const claimed = Boolean(entry.claimedAt);
  const isDelivery = isDeliveryKind(entry.kind);
  const hasMessage = hasMessageKind(entry.kind);
  const eggTier = entry.rewardEggKey ? EGG_TIER[entry.rewardEggKey] : null;

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

      {hasMessage && (
        <>
          <strong>{entry.title}</strong>
          <p className="hint" style={{ padding: 0 }}>{entry.body}</p>
          {entry.rewardCurrencyAmount != null && (
            <div className="card-row">
              <span className="produces mono"><Coin /> {Number(entry.rewardCurrencyAmount).toFixed(0)} {entry.rewardCurrencyCode}</span>
            </div>
          )}
          {/* O peixe do ovo só existe depois do resgate (gerado na hora) — antes disso mostra
              só o ícone tingido pelo tier, como uma prévia do que está esperando. */}
          {eggTier && !claimed && (
            <div className="card-row">
              <EggIcon tier={eggTier} />
              <span className="produces mono">{EGG_NAMES[entry.rewardEggKey]}</span>
            </div>
          )}
        </>
      )}

      {!claimed && (
        <div className="card-row">
          <button className="btn-primary" disabled={busy} onClick={() => onClaim(entry)}>
            {isDelivery ? "Resgatar pro tanque/mochila" : eggTier ? "Chocar ovo" : "Resgatar"}
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
  const [celebrate, setCelebrate] = useState(null); // { creature, eggTier } — ovo de admin recém-chocado

  async function claim(entry) {
    setBusyId(entry.id);
    try {
      const result = await api.claimInboxEntry(entry.id);
      if (result?.creature) {
        // Ovo de admin (§7.21) — mesma celebração de abrir o ovo já usada na Loja, com a
        // cor do tier certa (a mensagem já dizia qual ovo era, antes mesmo do resgate).
        setCelebrate({ creature: result.creature, eggTier: EGG_TIER[entry.rewardEggKey] ?? "common" });
      } else {
        notify(isDeliveryKind(entry.kind) ? "Peixe entregue!" : "Resgatado!");
      }
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
        <p className="hint">Sua caixa de entrada está vazia. Peixes comprados no mercado ou recebidos por transferência aparecem aqui pra resgatar, e você recebe um aviso quando um peixe seu é vendido.</p>
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
      {celebrate && (
        <CollectCelebration
          creature={celebrate.creature} variant="egg" eggTier={celebrate.eggTier}
          onClose={() => setCelebrate(null)}
        />
      )}
    </>
  );
}
