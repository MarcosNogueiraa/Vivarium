import { useEffect, useMemo, useRef } from "react";
import { traitsOf } from "../lib/generator.js";
import { drawFish, VIEW_H, VIEW_W } from "../lib/fishRenderer.js";
import { reducedMotion } from "../lib/motion.js";

/**
 * Um peixe isolado (thumbnail/detalhe), animado a partir do seed. Se for um
 * filhote (`isBred`), passe `parentASeed`/`parentBSeed` — os traits reais vêm
 * da herança (BreedTraits), não do seed do filhote sozinho.
 */
export function FishCanvas({ seed, width = 220, isBred = false, parentASeed = null, parentBSeed = null }) {
  const canvasRef = useRef(null);
  const height = Math.round(width * (VIEW_H / VIEW_W));
  const bigSeed = useMemo(() => BigInt(seed), [seed]);
  const traits = useMemo(
    () => traitsOf({ seed, isBred, parentASeed, parentBSeed }),
    [seed, isBred, parentASeed, parentBSeed],
  );

  useEffect(() => {
    const ctx = canvasRef.current.getContext("2d");
    let raf;
    function frame(now) {
      const time = reducedMotion ? 0 : now;
      ctx.setTransform(width / VIEW_W, 0, 0, width / VIEW_W, 0, 0);
      ctx.clearRect(0, 0, VIEW_W, VIEW_H);
      drawFish(ctx, bigSeed, traits, time);
      if (!reducedMotion) raf = requestAnimationFrame(frame);
    }
    raf = requestAnimationFrame(frame);
    return () => cancelAnimationFrame(raf);
  }, [bigSeed, traits, width]);

  return <canvas ref={canvasRef} width={width} height={height} className="fish-canvas" />;
}
