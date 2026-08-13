import { useState } from "react";

/** Seção recolhível (nasce fechada) — reaproveita o visual do .eyebrow, só some o corpo até o clique. */
export function CollapsibleSection({ title, children }) {
  const [open, setOpen] = useState(false);
  return (
    <div className="detail-section">
      <button className="detail-section-head" onClick={() => setOpen((o) => !o)} aria-expanded={open}>
        <span className="eyebrow">{title}</span>
        <span className={`chevron${open ? " open" : ""}`}>▾</span>
      </button>
      {open && children}
    </div>
  );
}
