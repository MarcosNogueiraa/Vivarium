import { useMemo } from "react";
import { traitsOf } from "../lib/generator.js";
import { PT } from "../lib/fishRenderer.js";

export function ShimmerLabel({ creature }) {
  const traits = useMemo(() => traitsOf(creature), [creature]);
  if (traits.shimmerTier === "None") return null;
  return (
    <span className="shimmer-label">
      ✦ {PT.tier[traits.shimmerTier]} · {PT.shimmer[traits.shimmerColor]}
    </span>
  );
}
