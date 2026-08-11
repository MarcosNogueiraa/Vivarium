export function Toast({ message, onDismiss }) {
  if (!message) return null;
  return (
    <div className="toast" role="status" onClick={onDismiss} title="Clique pra fechar">
      {message}
    </div>
  );
}
