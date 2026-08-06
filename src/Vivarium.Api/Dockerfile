# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/Vivarium.Core/ src/Vivarium.Core/
COPY src/Vivarium.Api/ src/Vivarium.Api/
RUN dotnet publish src/Vivarium.Api -c Release -o /app

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --create-home --shell /usr/sbin/nologin appuser
COPY --from=build /app .
RUN chown -R appuser:appuser /app
USER appuser
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1
# Configurar via env vars no host:
#   ConnectionStrings__Vivarium (string do Neon)
#   Jwt__Key (chave forte, 32+ chars)
#   Cors__AllowedOrigins__0 (url do front no Cloudflare Pages)
#   ForwardedHeaders__Enabled=true (atrás de proxy/CDN, pra rate limit ver o IP real do cliente)
ENTRYPOINT ["dotnet", "Vivarium.Api.dll"]
