# Deploy do backend — Oracle Cloud + DuckDNS + Caddy (grátis)

Passo a passo pra publicar a API em produção. O banco continua no Neon (nenhuma mudança
lá); isto só cobre onde/como a API roda e como ela ganha HTTPS.

## 1. VM no Oracle Cloud (Always Free)

1. Criar uma instância Compute Always Free (Ampere A1 ou VM.Standard.E2.1.Micro), Ubuntu.
2. **Reservar um IP público fixo** (Networking → IP Management → Reserved Public IPs) e
   atribuí-lo à VM — sem isso o IP pode mudar num reboot e quebra o DNS.
3. Na Security List da VCN, abrir entrada (Ingress) pras portas **80** e **443** (TCP,
   origem `0.0.0.0/0`) — são as únicas que precisam ficar públicas; a 8080 da API fica
   só interna ao Docker.
4. Instalar Docker + Docker Compose na VM:
   ```bash
   sudo apt update && sudo apt install -y docker.io docker-compose-plugin
   sudo usermod -aG docker $USER   # relogar depois disso
   ```

## 2. DuckDNS (subdomínio grátis)

1. Criar conta em https://www.duckdns.org (login social, sem cartão).
2. Criar um subdomínio (ex: `vivarium`) e apontá-lo pro IP reservado do passo 1.
3. Guardar o domínio completo (`vivarium.duckdns.org`) — vai no `.env` como `API_DOMAIN`.

## 3. Clonar o repo e configurar

```bash
git clone <url-do-repo> vivarium && cd vivarium
cp deploy/.env.example deploy/.env
nano deploy/.env   # preencher CONNECTIONSTRINGS__VIVARIUM, JWT__KEY, API_DOMAIN, FRONTEND_DOMAIN
```

Gerar uma `JWT__KEY` forte nova (não reusar a de dev):
```bash
openssl rand -base64 48
```

## 4. Migrations (antes de subir uma versão que mude o schema)

Rodar do seu próprio PC (mesmo fluxo que já usa contra o Neon), não dentro do container:
```bash
dotnet ef database update --project src/Vivarium.Api
```

## 5. Subir os containers

```bash
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d --build
```

O Caddy emite o certificado Let's Encrypt automaticamente na primeira request pro
domínio configurado — sem passo manual de certbot.

## 6. Verificar

```bash
curl https://<API_DOMAIN>/health
# esperado: {"status":"ok"}, com certificado válido (sem -k)

curl -i https://<API_DOMAIN>/api/dev/creatures
# esperado: 404 — endpoints de dev não devem existir em produção
```

## 7. Frontend (Cloudflare Pages)

No painel do Cloudflare Pages, conectar o repo e configurar:
- Build command: `npm run build`
- Build output directory: `frontend/dist`
- Root directory: `frontend`
- Env var: `VITE_API_URL=https://<API_DOMAIN>`

## Atualizações futuras

```bash
git pull
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d --build
```
Rodar a migration (passo 4) antes disso sempre que o deploy incluir mudança de schema.
