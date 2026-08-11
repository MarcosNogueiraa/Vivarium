import { useCallback, useRef, useState } from "react";

/**
 * Toast simples: mensagem efêmera (4s), dispensável por clique (pedido do usuário —
 * às vezes o toast fica em cima de um botão e é frustrante esperar sumir sozinho).
 * Guarda o timer num ref e sempre limpa o anterior antes de agendar um novo: sem isso,
 * um `notify` novo dentro da janela de 4s do anterior corria risco de ser apagado cedo
 * demais pelo timeout do toast antigo. Retorna { toast, notify, dismiss }.
 */
export function useToast(durationMs = 4000) {
  const [toast, setToast] = useState(null);
  const timerRef = useRef(null);

  const dismiss = useCallback(() => {
    clearTimeout(timerRef.current);
    setToast(null);
  }, []);

  const notify = useCallback((message) => {
    clearTimeout(timerRef.current);
    setToast(message);
    timerRef.current = setTimeout(() => setToast(null), durationMs);
  }, [durationMs]);

  return { toast, notify, dismiss };
}
