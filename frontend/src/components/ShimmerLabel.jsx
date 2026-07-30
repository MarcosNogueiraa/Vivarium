import { useMemo } from "react";
import { generateTraits } from "../lib/generator.js";
import { PT } from "../lib/fishRenderer.js";

export function ShimmerLabel({ seed }) {
  const traits = useMemo(() => generateTraits(BigInt(seed)), [seed]);
  if (traits.shimmerTier === "None") return null;
  return (
    <span className="shimmer-label">
      ✦ {PT.tier[traits.shimmerTier]} · {PT.shimmer[traits.shimmerColor]}
    </span>
  );
}
