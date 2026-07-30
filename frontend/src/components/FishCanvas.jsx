import { useEffect, useMemo, useRef } from "react";
import { generateTraits } from "../lib/generator.js";
import { drawFish, VIEW_H, VIEW_W } from "../lib/fishRenderer.js";
import { reducedMotion } from "../lib/motion.js";

/** Um peixe isolado (thumbnail/detalhe), animado a partir do seed. */
export function FishCanvas({ seed, width = 220 }) {
  const canvasRef = useRef(null);
  const height = Math.round(width * (VIEW_H / VIEW_W));
  const bigSeed = useMemo(() => BigInt(seed), [seed]);
  const traits = useMemo(() => generateTraits(bigSeed), [bigSeed]);

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
