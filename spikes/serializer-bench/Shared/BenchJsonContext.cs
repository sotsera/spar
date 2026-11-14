using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bench;

/// <summary>(B) The D1 spike's System.Text.Json baseline options, for the benchmark DTOs.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Note))]
[JsonSerializable(typeof(Note[]))]
internal sealed partial class BenchJsonContext : JsonSerializerContext;
