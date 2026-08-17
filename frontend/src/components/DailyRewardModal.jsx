import { useEffect, useRef, useState } from "react";
import { Modal } from "./Modal.jsx";
import { Coin } from "./Coin.jsx";
import { EggIcon } from "./EggIcon.jsx";
import { EGG_TIER, EGG_NAMES } from "../lib/eggs.js";
import { api } from "../lib/api.js";

const SPIN_MS = 1800; // duração total do giro, devagar o bastante pra dar tempo de perceber
const MIN_TICK_MS = 45; // velocidade no início (rápido)
const MAX_TICK_MS = 260; // velocidade no fim (devagar, "freando" antes de cair no valor)

function easeOutCubic(t) { return 1 - (1 - t) ** 3; }

/**
 * Roleta da recompensa diária (17/08/2026, redesenho §8.10) — antes era só um botão que
 * creditava na hora, sem mostrar nada do cálculo. Mostra a faixa possível antes de resgatar
 * e, ao resgatar, "roda" por valores dentro da faixa (anel dourado girando + números trocando,
 * FREANDO gradualmente — não velocidade constante, senão não lê como "roleta caindo em algo")
 * até assentar no valor real devolvido pela API. A sequência atual e o bônus concedido por ela
 * ficam visíveis o tempo todo (pedido do usuário: "na roleta deve aparecer a sequencia atual e
 * o bonus concedido"), não só o soft.
 */
export function DailyRewardModal({ status, onClose, onClaimed, notify }) {
  const [spinning, setSpinning] = useState(false);
  const [displayAmount, setDisplayAmount] = useState(null);
  const [result, setResult] = useState(null); // { amount, streak, gotEgg }
  const timerRef = useRef(null);

  useEffect(() => () => clearTimeout(timerRef.current), []);

  function spinTicks(min, max, elapsed) {
    setDisplayAmount(Math.round(min + Math.random() * (max - min)));
    if (elapsed >= SPIN_MS) return;
    const delay = MIN_TICK_MS + (MAX_TICK_MS - MIN_TICK_MS) * easeOutCubic(elapsed / SPIN_MS);
    timerRef.current = setTimeout(() => spinTicks(min, max, elapsed + delay), delay);
  }

  async function claim() {
    setSpinning(true);
    setResult(null);
    const min = Number(status.minAmount);
    const max = Number(status.maxAmount);
    spinTicks(min, max, 0);

    try {
      const [claimed] = await Promise.all([
        api.claimDailyReward(),
        new Promise((resolve) => setTimeout(resolve, SPIN_MS)),
      ]);
      clearTimeout(timerRef.current);
      setDisplayAmount(claimed.amount);
      setResult(claimed);
      await onClaimed();
    } catch (err) {
      clearTimeout(timerRef.current);
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

      <div className="daily-reward-roulette-wrap">
        {spinning && <div className="daily-reward-roulette-ring" />}
        <div className={`daily-reward-roulette${spinning ? " is-spinning" : ""}${result ? " is-settled" : ""}`}>
          <Coin />
          <span className="daily-reward-roulette-value">
            {displayAmount === null ? `${Math.round(status.minAmount)}–${Math.round(status.maxAmount)}` : displayAmount}
          </span>
          <small>soft</small>
        </div>
      </div>

      {!result && !spinning && (
        <p className="muted">
          Faixa possível hoje: {Math.round(status.minAmount)}–{Math.round(status.maxAmount)} soft.
          {status.eggChancePercent > 0 && ` Chance de ${status.eggChancePercent}% de vir um ovo de brinde.`}
        </p>
      )}

      {result && !result.gotEgg && <p className="muted">Creditado na carteira. Volte amanhã pra manter a sequência.</p>}

      {/* Ovo é um bônus À PARTE da moeda (rolagem independente, CLAUDE.md §7.10) — pedido do
          usuário: não misturar com o valor da roleta, mostrar como uma recompensa própria. */}
      {result?.gotEgg && (
        <div className="daily-reward-egg-card">
          <EggIcon tier={EGG_TIER[result.eggItemKey] ?? "rare"} />
          <div>
            <strong>🎉 Sorte grande, bônus extra!</strong>
            <p className="muted" style={{ margin: 0 }}>
              Um {EGG_NAMES[result.eggItemKey] ?? "ovo"} caiu na sua Caixa de Entrada, além do soft de hoje.
            </p>
          </div>
        </div>
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
