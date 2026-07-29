using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
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

// Front roda em outro domínio (Cloudflare Pages) — origens via config
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

// Rate limiting: global (folgado, pro polling do jogo) por usuário/IP + política
// "auth" apertada por IP contra brute-force de login. NB: atrás de proxy
// (Cloudflare/Oracle) o IP real exige UseForwardedHeaders — configurar no deploy.
static string ClientKey(HttpContext ctx) =>
    ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
    ?? ctx.User.FindFirst("sub")?.Value
    ?? ctx.Connection.RemoteIpAddress?.ToString()
    ?? "anon";
int globalPerMinute = builder.Configuration.GetValue("RateLimiting:GlobalPerMinute", 300);
int authPerMinute = builder.Configuration.GetValue("RateLimiting:AuthPerMinute", 10);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ClientKey(ctx),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = globalPerMinute, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy("auth", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = authPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

app.UseRateLimiter();
app.UseCors();
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
app.MapItemEndpoints();

if (app.Environment.IsDevelopment())
    app.MapDevEndpoints();

app.Run();

// Exposto pros testes de integração (WebApplicationFactory)
public partial class Program;
