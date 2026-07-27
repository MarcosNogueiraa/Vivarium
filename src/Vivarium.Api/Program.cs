using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Vivarium.Api.Data;
using Vivarium.Core.Generation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<VivariumDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Vivarium")));

// Enums como string no JSON — mesmos nomes que o port JS do protótipo usa
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Preview do motor de geração: não toca no banco. Útil pro front validar a
// renderização e pra inspecionar qualquer seed sem criar uma criatura.
app.MapGet("/api/creatures/preview/{seed:long}", (long seed) =>
    Results.Ok(TraitGenerator.Generate(seed)));

app.Run();
