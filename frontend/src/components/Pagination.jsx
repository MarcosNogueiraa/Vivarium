/** Paginação simples (anterior/próxima + "Página N de M"). `page` é 0-based. */
export function Pagination({ page, totalCount, pageSize, onPageChange }) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  if (totalPages <= 1) return null;
  return (
    <div className="pagination">
      <button disabled={page <= 0} onClick={() => onPageChange(page - 1)}>‹ Anterior</button>
      <span className="mono">Página {page + 1} de {totalPages}</span>
      <button disabled={page >= totalPages - 1} onClick={() => onPageChange(page + 1)}>Próxima ›</button>
    </div>
  );
}
