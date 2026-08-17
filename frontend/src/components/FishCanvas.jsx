import { useEffect, useMemo, useRef } from "react";
import { traitsOf } from "../lib/generator.js";
import { drawFish, FULL_FISH_LAYERS, VIEW_H, VIEW_W } from "../lib/fishRenderer.js";
import { reducedMotion } from "../lib/motion.js";

// Ordem de montagem da revelação suspense (CollectCelebration.jsx) — corpo
// (sempre visível, é a "base") → brilho → cauda → dorsal → peitoral.
const REVEAL_ORDER = ["shimmer", "tail", "dorsal", "pectoral"];
export const REVEAL_STEP_COUNT = REVEAL_ORDER.length;

function layersForStep(step) {
  if (step == null || step >= REVEAL_ORDER.length) return FULL_FISH_LAYERS;
  const layers = { shimmer: false, tail: false, dorsal: false, pectoral: false };
  for (let i = 0; i < step; i++) layers[REVEAL_ORDER[i]] = true;
  return layers;
}

/**
 * Um peixe isolado (thumbnail/detalhe), animado a partir da criatura completa
 * (`creature.seed` + `creature.traits`, já congelados pelo servidor no
 * nascimento — CLAUDE.md §8.19.1/13-08-2026). Sempre passe a criatura INTEIRA,
 * nunca um objeto parcial reconstruído (`{seed, isBred, ...}`) — foi
 * exatamente esse padrão que escondeu um bug real 2x antes desta simplificação
 * (10/08 e 12/08/2026): um componente reconstruindo um subconjunto de campos
 * divergia do texto "por que é raro", que sempre usou a criatura completa.
 * `revealStep` (opcional, 0–4): monta o peixe parte a parte em vez de tudo de
 * uma vez — ver `REVEAL_ORDER`/`CollectCelebration.jsx`. Omitido = peixe
 * completo (uso normal em toda lista/detalhe do jogo).
 */
export function FishCanvas({ creature, width = 220, revealStep = null }) {
  const canvasRef = useRef(null);
  const height = Math.round(width * (VIEW_H / VIEW_W));
  const bigSeed = useMemo(() => BigInt(creature.seed), [creature.seed]);
  const traits = useMemo(() => traitsOf(creature), [creature]);
  const layers = useMemo(() => layersForStep(revealStep), [revealStep]);

  useEffect(() => {
    const ctx = canvasRef.current.getContext("2d");
    let raf;
    function frame(now) {
      const time = reducedMotion ? 0 : now;
      ctx.setTransform(width / VIEW_W, 0, 0, width / VIEW_W, 0, 0);
      ctx.clearRect(0, 0, VIEW_W, VIEW_H);
      // Traits corrompidos (ex: TraitsJson nulo) fazem drawFish lançar — sem isso, o rAF
      // desse card parava pra sempre no primeiro frame ruim, deixando o canvas em branco
      // silenciosamente (achado 18/08/2026 revisando um relato de aquário "transparente").
      try { drawFish(ctx, bigSeed, traits, time, 0, layers); }
      catch (err) { console.error(`Falha ao desenhar a criatura #${creature.id ?? "?"}:`, err); }
      if (!reducedMotion) raf = requestAnimationFrame(frame);
    }
    raf = requestAnimationFrame(frame);
    return () => cancelAnimationFrame(raf);
  }, [bigSeed, traits, width, layers]);

  return <canvas ref={canvasRef} width={width} height={height} className="fish-canvas" />;
}
