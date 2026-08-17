import { useMemo, useState } from "react";
import { Modal } from "./Modal.jsx";
import { EggIcon } from "./EggIcon.jsx";
import { EGG_TIER, EGG_NAMES } from "../lib/eggs.js";
import { PrizeWheel, buildWheelSlices, pickWheelTargetIndex, SPIN_DURATION_MS } from "./PrizeWheel.jsx";
import { api } from "../lib/api.js";

/**
 * Roleta clássica da recompensa diária (17/08/2026, redesenho §8.10, revisada a pedido do
 * usuário: "quero uma roleta classica, com 8 opcoes, mas a chance de cair cada um nao vai ser
 * igual"). O resultado (moeda + streak + chance de ovo, independente) já vem calculado do
 * servidor ANTES de girar — a roda é só a camada visual: as 8 fatias mapeiam esse resultado real
 * (a mais próxima em valor, ou a fatia de ovo certa) e a seta fixa no topo aponta pra ela quando
 * a roda para de girar.
 */
export function DailyRewardModal({ status, onClose, onClaimed, notify }) {
  const slices = useMemo(
    () => buildWheelSlices(Number(status.minAmount), Number(status.maxAmount)),
    [status.minAmount, status.maxAmount],
  );
  const [phase, setPhase] = useState("idle"); // idle | loading | spinning | done
  const [spinToken, setSpinToken] = useState(0);
  const [targetIndex, setTargetIndex] = useState(null);
  const [result, setResult] = useState(null); // { amount, streak, gotEgg, eggItemKey }

  async function claim() {
    setPhase("loading");
    try {
      const claimed = await api.claimDailyReward();
      setTargetIndex(pickWheelTargetIndex(slices, claimed));
      setSpinToken((t) => t + 1);
      setPhase("spinning");
      setTimeout(async () => {
        setResult(claimed);
        setPhase("done");
        await onClaimed();
      }, SPIN_DURATION_MS + 150);
    } catch (err) {
      setPhase("idle");
      notify(err.message);
      onClose();
    }
  }

  const streak = result?.streak ?? status.currentStreak;
  const bonusPct = status.streakBonusPercent;
  const busy = phase === "loading" || phase === "spinning";

  return (
    <Modal onClose={onClose} narrow>
      <h3 className="eyebrow">🎁 Recompensa diária</h3>

      <div className="daily-reward-streak">
        <span className="daily-reward-streak-count">🔥 {streak} {streak === 1 ? "dia" : "dias"} seguidos</span>
        {bonusPct > 0 && <span className="daily-reward-bonus-tag">+{bonusPct.toFixed(0)}% de bônus</span>}
      </div>

      <PrizeWheel slices={slices} targetIndex={targetIndex} spinToken={spinToken} />

      {phase === "idle" && (
        <p className="muted">
          Faixa possível hoje: {Math.round(status.minAmount)}–{Math.round(status.maxAmount)} soft.
          {status.eggChancePercent > 0 && ` Chance de ${status.eggChancePercent}% de vir um ovo de brinde.`}
        </p>
      )}
      {phase === "loading" && <p className="muted">Sorteando…</p>}
      {phase === "spinning" && <p className="muted">Girando…</p>}

      {phase === "done" && !result.gotEgg && (
        <p className="daily-reward-roulette-result">
          +{Number(result.amount).toFixed(0)} soft creditado na carteira. Volte amanhã pra manter a sequência.
        </p>
      )}

      {/* Ovo é um bônus À PARTE da moeda (rolagem independente) — pedido do usuário: não
          misturar com o valor da roleta, mostrar como uma recompensa própria. */}
      {phase === "done" && result.gotEgg && (
        <div className="daily-reward-egg-card">
          <EggIcon tier={EGG_TIER[result.eggItemKey] ?? "rare"} />
          <div>
            <strong>🎉 +{Number(result.amount).toFixed(0)} soft, e sorte grande!</strong>
            <p className="muted" style={{ margin: 0 }}>
              Um {EGG_NAMES[result.eggItemKey] ?? "ovo"} caiu na sua Caixa de Entrada, além do soft de hoje.
            </p>
          </div>
        </div>
      )}

      {phase !== "done" ? (
        <button className="daily-reward-btn" onClick={claim} disabled={busy}>
          {phase === "loading" ? "Sorteando…" : phase === "spinning" ? "Girando…" : "Girar a roleta"}
        </button>
      ) : (
        <button type="button" onClick={onClose}>Fechar</button>
      )}
    </Modal>
  );
}
