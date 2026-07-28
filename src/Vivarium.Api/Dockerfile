# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/Vivarium.Core/ src/Vivarium.Core/
COPY src/Vivarium.Api/ src/Vivarium.Api/
RUN dotnet publish src/Vivarium.Api -c Release -o /app

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
# Configurar via env vars no host:
#   ConnectionStrings__Vivarium (string do Neon)
#   Jwt__Key (chave forte, 32+ chars)
#   Cors__AllowedOrigins__0 (url do front no Cloudflare Pages)
ENTRYPOINT ["dotnet", "Vivarium.Api.dll"]
