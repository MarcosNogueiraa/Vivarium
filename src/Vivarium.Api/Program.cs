using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Vivarium.Api.Data;
using Vivarium.Api.Endpoints;
using Vivarium.Api.Services;
using Vivarium.Core.Generation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<VivariumDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Vivarium")));

// Enums como string no JSON — mesmos nomes que o port JS do protótipo usa
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "Jwt:Key não configurada (appsettings.Development.json em dev; env var Jwt__Key em produção)");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<GameService>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Preview do motor de geração: não toca no banco. Útil pro front validar a
// renderização e pra inspecionar qualquer seed sem criar uma criatura.
app.MapGet("/api/creatures/preview/{seed:long}", (long seed) =>
    Results.Ok(TraitGenerator.Generate(seed)));

app.MapAuthEndpoints();
app.MapGameEndpoints();
app.MapMarketEndpoints();

app.Run();

// Exposto pros testes de integração (WebApplicationFactory)
public partial class Program;
