using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spike.App;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Note))]
[JsonSerializable(typeof(Note[]))]
[JsonSerializable(typeof(int))]
internal sealed partial class SpikeJsonContext : JsonSerializerContext;
