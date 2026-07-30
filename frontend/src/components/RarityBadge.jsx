import { bandOf } from "../lib/fishRenderer.js";

export function RarityBadge({ score }) {
  const band = bandOf(score);
  return (
    <span className="badge" style={{ "--tier": band.color }}>
      <span className="gem" /> {band.name} · {score.toFixed(1)}
    </span>
  );
}
