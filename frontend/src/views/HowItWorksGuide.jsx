import { useState } from "react";
import { Modal } from "../components/Modal.jsx";

const SECTIONS = [
  {
    title: "Geração e coleta",
    body: (
      <>
        <p>O tanque gera peixe sozinho: a cada <b>60 minutos</b> online, surge 1 item novo na fila (até 5 acumulados).
        Se a fila enche, a geração trava até você coletar — abrir o jogo pelo menos a cada ~5h evita perder tempo de geração.</p>
        <p>O seed do peixe é sorteado <b>na coleta</b>, não na entrada da fila — então não dá pra "adivinhar" o que vai sair antes de coletar.</p>
      </>
    ),
  },
  {
    title: "Qualidade da água",
    body: (
      <>
        <p>Cada peixe no tanque suja a água continuamente (mais peixes = degrada mais rápido). Isso <b>não mata</b> ninguém nem some com nada — só reduz sua renda.</p>
        <p>De <b>80 a 100%</b> a água não tira nada da sua renda. Abaixo de 80%, a renda cai suavemente (não é um "penhasco"); com a água zerada, a renda também vai a zero.</p>
        <p>Abaixo de 40% a velocidade de geração cai pela metade; abaixo de 15%, itens novos da fila têm 10% de chance de nascer "doentes" (nascem com desvantagem no sorteio de raridade, não morrem).</p>
        <p><b>Filtro</b> (soft) restaura a água pra 100%. <b>Filtro automático</b> (item permanente) reduz a degradação pela metade — mas não substitui o filtro manual, só desacelera a sujeira.</p>
      </>
    ),
  },
  {
    title: "Renda passiva por raridade",
    body: (
      <>
        <p>Todo peixe no tanque farma moeda soft por hora, sozinho, sem clicar em nada — a renda escala <b>exponencialmente</b> com a raridade: um comum rende poucas moedas/h, um lendário rende centenas/h.</p>
        <p><b>Sinergia de cor:</b> ter várias peças com a mesma cor de cauda no tanque multiplica a renda de cada uma (até +80% com o suficiente). Vale montar um tanque temático por cor.</p>
        <p>Offline, a renda cai pra 45% da taxa online, com teto de 8h acumuladas — ficar dias fora não multiplica sem limite.</p>
      </>
    ),
  },
  {
    title: "Mochila",
    body: (
      <>
        <p>Peixe pode estar em 3 estados: <b>no tanque</b> (ocupa vaga, farma renda), <b>na mochila</b> (guardado, não farma, não ocupa vaga do tanque) ou <b>à venda</b> no mercado.</p>
        <p>A mochila (até 50 peixes) é útil pra guardar peixes que você não quer vender nem manter rendendo agora — por exemplo, peixes reservados pra breeding.</p>
      </>
    ),
  },
  {
    title: "Mercado e venda ao NPC",
    body: (
      <>
        <p><b>Mercado:</b> lista um peixe seu por um preço em soft que você escolhe; outro jogador compra. É o único lugar onde o preço reflete o valor real do peixe.</p>
        <p><b>Vender ao NPC</b> (vendor): venda instantânea por um preço baixo (poucas horas do que ele já renderia sozinho) — pensado pra duplicatas e comuns acumulados que não valem o esforço de listar, não como alternativa ao mercado.</p>
      </>
    ),
  },
  {
    title: "Ninho (breeding)",
    body: (
      <>
        <p>Cruza 2 peixes seus (tanque ou mochila) num habitat dedicado — enquanto isso, eles não rendem no tanque. O filhote herda traits dos pais (com viés pra herdar os valores mais raros, não é 50/50 puro) e tem chance pequena de "pular uma geração" e herdar de um avô.</p>
        <p><b>Custo em soft</b> escala pouco com a raridade dos pais; o verdadeiro custo pra pares raros é o <b>tempo</b> que eles ficam parados sem render (gestação pode levar até 10 dias pros pares mais raros).</p>
        <p><b>Risco:</b> cada cruzamento tem chance de o pai não sobreviver, crescendo a cada uso do mesmo peixe. Três formas de reduzir o risco, todas opcionais: <b>descansar</b> (esperar, de graça, o risco decai com o tempo), <b>estabilizador genético</b> (soft, reduz o risco à metade) ou <b>seguro de cruzamento</b> (premium, zera o risco).</p>
      </>
    ),
  },
  {
    title: "Moeda premium e rush",
    body: (
      <>
        <p>Premium não compra poder nem raridade — só <b>tempo</b>. Use pra pular instantaneamente a espera da fila de geração ou de uma gestação em andamento (botão ⚡). O custo é proporcional ao tempo restante: quanto mais falta, mais caro pular tudo de uma vez.</p>
        <p>É a única forma de acelerar esses dois relógios — por design, o jogo é deliberadamente lento de graça.</p>
      </>
    ),
  },
  {
    title: "VIP",
    body: (
      <>
        <p>VIP não muda a velocidade de nada — só troca a coleta manual da fila por <b>coleta automática</b>, enquanto a aba do jogo estiver aberta (trocar de aba ainda conta como "aberta"; fechar o navegador, não).</p>
        <p>Fechar o jogo por mais de ~3 minutos volta a exigir coleta manual ao retornar, até a próxima vez que a aba ficar aberta de novo.</p>
      </>
    ),
  },
  {
    title: "Recompensa diária",
    body: <p>Resgate 1x por dia (vira à meia-noite UTC) — sem streak, sem penalidade por pular um dia. Só não acumula: dá pra resgatar no máximo o dia corrente.</p>,
  },
];

function Section({ title, body, defaultOpen }) {
  const [open, setOpen] = useState(!!defaultOpen);
  return (
    <div className="howto-section">
      <button className="howto-head" onClick={() => setOpen((o) => !o)} aria-expanded={open}>
        <span className={`chevron${open ? " open" : ""}`}>▸</span>
        <span className="howto-title">{title}</span>
      </button>
      {open && <div className="howto-body muted">{body}</div>}
    </div>
  );
}

export function HowItWorksGuide({ onClose, onOpenRarityGuide }) {
  return (
    <Modal onClose={onClose} className="wide">
      <div className="eyebrow">Como o Vivarium funciona</div>
      <p className="muted guide-intro">
        Um resumo de cada mecânica do jogo. Quer entender a raridade dos peixes em detalhe?{" "}
        <button className="link-btn" onClick={onOpenRarityGuide}>Veja o guia de raridade</button>.
      </p>
      <div className="howto-list">
        {SECTIONS.map((s, i) => <Section key={s.title} {...s} defaultOpen={i === 0} />)}
      </div>
    </Modal>
  );
}
