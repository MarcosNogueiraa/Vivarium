using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
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
builder.Services.AddScoped<MarketService>();
builder.Services.AddScoped<ItemService>();
builder.Services.AddScoped<BreedingService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<LeaderboardService>();
builder.Services.AddScoped<VipService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddScoped<InboxService>();

// Sem Resend:ApiKey configurada, NullEmailSender só loga (app continua funcionando sem
// email — mesmo espírito do gap documentado pro processador de pagamento, CLAUDE.md §8.11).
if (!string.IsNullOrEmpty(builder.Configuration["Resend:ApiKey"]))
    builder.Services.AddHttpClient<IEmailSender, ResendEmailSender>();
else
    builder.Services.AddSingleton<IEmailSender, NullEmailSender>();

// Front roda em outro domínio (Cloudflare Pages) — origens via config
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

// Atrás de proxy/CDN (Cloudflare → Oracle) o IP real do cliente só chega via
// X-Forwarded-For/-Proto; sem isso o rate limit por IP abaixo vira global (todo
// mundo atrás do mesmo proxy cai no mesmo balde). Desligado por padrão (dev/testes
// não têm proxy); ligar com ForwardedHeaders__Enabled=true no deploy. KnownNetworks/
// KnownProxies limpos porque o proxy (Cloudflare) não tem IP fixo conhecido — seguro
// desde que a origem só seja alcançável através dele (bloquear acesso direto no host).
bool forwardedHeadersEnabled = builder.Configuration.GetValue("ForwardedHeaders:Enabled", false);
if (forwardedHeadersEnabled)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

// Rate limiting: global (folgado, pro polling do jogo) por usuário/IP + política
// "auth" apertada por IP contra brute-force de login.
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

if (forwardedHeadersEnabled) app.UseForwardedHeaders();
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
app.MapAccountEndpoints();
app.MapInboxEndpoints();
app.MapGameEndpoints();
app.MapMarketEndpoints();
app.MapItemEndpoints();
app.MapBreedingEndpoints();
app.MapAdminEndpoints();
app.MapLeaderboardEndpoints();
app.MapVipEndpoints();

if (app.Environment.IsDevelopment())
    app.MapDevEndpoints();

app.Run();

// Exposto pros testes de integração (WebApplicationFactory)
public partial class Program;
