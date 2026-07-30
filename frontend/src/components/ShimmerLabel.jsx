import { useMemo } from "react";
import { traitsOf } from "../lib/generator.js";
import { PT } from "../lib/fishRenderer.js";

export function ShimmerLabel({ seed, isBred = false, parentASeed = null, parentBSeed = null }) {
  const traits = useMemo(
    () => traitsOf({ seed, isBred, parentASeed, parentBSeed }),
    [seed, isBred, parentASeed, parentBSeed],
  );
  if (traits.shimmerTier === "None") return null;
  return (
    <span className="shimmer-label">
      ✦ {PT.tier[traits.shimmerTier]} · {PT.shimmer[traits.shimmerColor]}
    </span>
  );
}
