# Vivarium

Jogo idle de navegador (aquário virtual): geração passiva de peixes com raridade
genética derivada de seed determinístico, mercado interno entre jogadores e
diferenciação online/offline por heartbeat. Contexto completo de design e
arquitetura em [CLAUDE.md](CLAUDE.md).

## Rodar

```bash
# Testes (motor + gameplay + integração da API)
dotnet test

# Simulação estatística dos pesos de raridade (100k seeds)
dotnet run --project tools/Vivarium.Simulation

# API em dev (precisa de Postgres local ou docker compose up db)
dotnet run --project src/Vivarium.Api

# Stack completa em containers (API :8080 + Postgres :5432)
docker compose up --build
```

O protótipo visual (`prototype/fish-composer.html`) abre direto no navegador,
sem build — digite um seed ou busque por tier de brilho.

## Banco

Migrations EF Core em `src/Vivarium.Api/Data/Migrations`. Para aplicar:

```bash
dotnet dotnet-ef database update --project src/Vivarium.Api
```

Produção usa Neon.tech: definir `ConnectionStrings__Vivarium` como env var.

## Estrutura

| Caminho | O quê |
|---|---|
| `src/Vivarium.Core` | Domínio + motor seed→traits + lógica de gameplay (puro, sem web/banco) |
| `src/Vivarium.Api` | Minimal API (auth JWT, jogo, mercado) + EF Core/Npgsql |
| `tests/Vivarium.Core.Tests` | Unit tests do motor e do loop |
| `tests/Vivarium.Api.Tests` | Integração da API (SQLite in-memory) |
| `tools/Vivarium.Simulation` | Validação estatística dos pesos (`dump` verifica ports do motor) |
| `prototype/` | Compositor visual em Canvas (standalone) |
