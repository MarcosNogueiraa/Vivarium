using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vivarium.Core.Generation;

/// <summary>
/// Serialização compartilhada de <see cref="CreatureTraits"/>/<see cref="TraitGenerator.TraitSourceEntry"/>
/// pra <c>CreatureInstance.TraitsJson</c>/<c>BreedingSourceJson</c> (13/08/2026, CLAUDE.md §8.19.1).
/// Enums como string (legível direto no banco pra diagnóstico, mesmo padrão já usado nas respostas
/// HTTP da API — <c>JsonStringEnumConverter</c> em <c>Program.cs</c>) — usa as MESMAS opções aqui
/// pra ler e escrever, independente de onde a serialização acontece.
/// </summary>
public static class TraitsSerialization
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(CreatureTraits traits) => JsonSerializer.Serialize(traits, Options);

    public static CreatureTraits DeserializeTraits(string json) =>
        JsonSerializer.Deserialize<CreatureTraits>(json, Options)
        ?? throw new InvalidOperationException("TraitsJson inválido ou vazio.");

    public static string SerializeSource(IReadOnlyList<TraitGenerator.TraitSourceEntry> source) =>
        JsonSerializer.Serialize(source, Options);

    public static IReadOnlyList<TraitGenerator.TraitSourceEntry> DeserializeSource(string json) =>
        JsonSerializer.Deserialize<IReadOnlyList<TraitGenerator.TraitSourceEntry>>(json, Options)
        ?? throw new InvalidOperationException("BreedingSourceJson inválido.");
}
