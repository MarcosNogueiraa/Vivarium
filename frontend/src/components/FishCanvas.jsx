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
 * Um peixe isolado (thumbnail/detalhe), animado a partir do seed. Se for um
 * filhote (`isBred`), passe `parentASeed`/`parentBSeed` — os traits reais vêm
 * da herança (BreedTraits), não do seed do filhote sozinho. Se o PAI (A ou B)
 * também for filhote, passe também `parentAGrandparentASeed`/`BSeed` e
 * `parentBGrandparentASeed`/`BSeed` — sem eles, o cálculo de herança trata
 * silenciosamente esse pai como se não tivesse ancestralidade própria (cai pra
 * `generateTraits(seed)` puro em vez de `breedTraits`), o que pode produzir um
 * peixe visualmente diferente do que realmente é (bug real 10/08/2026: nenhum
 * chamador passava esses 4 campos, então todo FishCanvas de filhote com avós
 * renderizava errado — só o texto do "por que é raro", que usa `traitsOf`
 * direto com a criatura completa, mostrava os traits certos). `revealStep`
 * (opcional, 0–4): monta o peixe parte a parte em vez de tudo de uma vez —
 * ver `REVEAL_ORDER`/`CollectCelebration.jsx`. Omitido = peixe completo (uso
 * normal em toda lista/detalhe do jogo).
 */
export function FishCanvas({
  seed, width = 220, isBred = false, parentASeed = null, parentBSeed = null,
  parentAGrandparentASeed = null, parentAGrandparentBSeed = null,
  parentBGrandparentASeed = null, parentBGrandparentBSeed = null,
  revealStep = null,
}) {
  const canvasRef = useRef(null);
  const height = Math.round(width * (VIEW_H / VIEW_W));
  const bigSeed = useMemo(() => BigInt(seed), [seed]);
  const traits = useMemo(
    () => traitsOf({
      seed, isBred, parentASeed, parentBSeed,
      parentAGrandparentASeed, parentAGrandparentBSeed,
      parentBGrandparentASeed, parentBGrandparentBSeed,
    }),
    [seed, isBred, parentASeed, parentBSeed,
      parentAGrandparentASeed, parentAGrandparentBSeed, parentBGrandparentASeed, parentBGrandparentBSeed],
  );
  const layers = useMemo(() => layersForStep(revealStep), [revealStep]);

  useEffect(() => {
    const ctx = canvasRef.current.getContext("2d");
    let raf;
    function frame(now) {
      const time = reducedMotion ? 0 : now;
      ctx.setTransform(width / VIEW_W, 0, 0, width / VIEW_W, 0, 0);
      ctx.clearRect(0, 0, VIEW_W, VIEW_H);
      drawFish(ctx, bigSeed, traits, time, 0, layers);
      if (!reducedMotion) raf = requestAnimationFrame(frame);
    }
    raf = requestAnimationFrame(frame);
    return () => cancelAnimationFrame(raf);
  }, [bigSeed, traits, width, layers]);

  return <canvas ref={canvasRef} width={width} height={height} className="fish-canvas" />;
}
