import { useEffect, useRef, useState } from "react";
import { Modal } from "./Modal.jsx";
import { Coin } from "./Coin.jsx";
import { api } from "../lib/api.js";

const SPIN_MS = 1100;
const SPIN_TICK_MS = 60;

/**
 * Roleta da recompensa diária (17/08/2026, redesenho §8.10) — antes era só um botão que
 * creditava na hora, sem mostrar nada do cálculo. Mostra a faixa possível antes de resgatar
 * e, ao resgatar, "roda" por valores dentro da faixa até assentar no valor real devolvido pela
 * API — a sequência atual e o bônus concedido por ela ficam visíveis o tempo todo (pedido do
 * usuário: "na roleta deve aparecer a sequencia atual e o bonus concedido"), não só o soft.
 */
export function DailyRewardModal({ status, onClose, onClaimed, notify }) {
  const [spinning, setSpinning] = useState(false);
  const [displayAmount, setDisplayAmount] = useState(null);
  const [result, setResult] = useState(null); // { amount, streak, gotEgg }
  const timerRef = useRef(null);

  useEffect(() => () => clearInterval(timerRef.current), []);

  async function claim() {
    setSpinning(true);
    setResult(null);
    const min = Number(status.minAmount);
    const max = Number(status.maxAmount);

    timerRef.current = setInterval(() => {
      setDisplayAmount(Math.round(min + Math.random() * (max - min)));
    }, SPIN_TICK_MS);

    try {
      const claimed = await api.claimDailyReward();
      await new Promise((resolve) => setTimeout(resolve, SPIN_MS));
      clearInterval(timerRef.current);
      setDisplayAmount(claimed.amount);
      setResult(claimed);
      await onClaimed();
    } catch (err) {
      clearInterval(timerRef.current);
      setSpinning(false);
      notify(err.message);
      onClose();
      return;
    }
    setSpinning(false);
  }

  const streak = result?.streak ?? status.currentStreak;
  const bonusPct = status.streakBonusPercent;

  return (
    <Modal onClose={onClose} narrow>
      <h3 className="eyebrow">🎁 Recompensa diária</h3>

      <div className="daily-reward-streak">
        <span className="daily-reward-streak-count">🔥 {streak} {streak === 1 ? "dia" : "dias"} seguidos</span>
        {bonusPct > 0 && <span className="daily-reward-bonus-tag">+{bonusPct.toFixed(0)}% de bônus</span>}
      </div>

      <div className={`daily-reward-roulette${spinning ? " is-spinning" : ""}${result ? " is-settled" : ""}`}>
        <Coin />
        <span className="daily-reward-roulette-value">
          {displayAmount === null ? `${Math.round(status.minAmount)}–${Math.round(status.maxAmount)}` : displayAmount}
        </span>
        <small>soft</small>
      </div>

      {!result && !spinning && (
        <p className="muted">
          Faixa possível hoje: {Math.round(status.minAmount)}–{Math.round(status.maxAmount)} soft.
          {status.eggChancePercent > 0 && ` Chance de ${status.eggChancePercent}% de vir um ovo de brinde.`}
        </p>
      )}

      {result && (
        <p className={result.gotEgg ? "daily-reward-egg-note" : "muted"}>
          {result.gotEgg
            ? "🥚 Sorte grande! Um ovo de brinde caiu na sua Caixa de Entrada."
            : "Creditado na carteira. Volte amanhã pra manter a sequência."}
        </p>
      )}

      {!result ? (
        <button className="daily-reward-btn" onClick={claim} disabled={spinning}>
          {spinning ? "Girando…" : "Resgatar"}
        </button>
      ) : (
        <button type="button" onClick={onClose}>Fechar</button>
      )}
    </Modal>
  );
}
