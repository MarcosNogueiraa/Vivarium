export function TraitRow({ label, value }) {
  return (
    <div className="trait-row">
      <span className="tr-label">{label}</span>
      <span className="tr-value">{value}</span>
    </div>
  );
}
