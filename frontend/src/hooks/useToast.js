import { useCallback, useState } from "react";

/** Toast simples: mensagem efêmera (4s). Retorna { toast, notify }. */
export function useToast(durationMs = 4000) {
  const [toast, setToast] = useState(null);
  const notify = useCallback((message) => {
    setToast(message);
    setTimeout(() => setToast(null), durationMs);
  }, [durationMs]);
  return { toast, notify };
}
