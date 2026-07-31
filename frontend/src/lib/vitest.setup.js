// fishRenderer.js constrói alguns Path2D no escopo do módulo (formas do peixe) — não
// precisamos que eles desenhem de verdade pros testes de lógica pura (format.js, tankMath.js),
// só que o import não quebre em ambiente Node (sem Canvas). Stub mínimo.
if (typeof globalThis.Path2D === "undefined") {
  globalThis.Path2D = class Path2D {
    constructor() {}
  };
}
