using System.Text.Json.Serialization;

namespace Spar;

internal sealed record EvaluateParameters(string Expression, bool AwaitPromise = true, bool ReturnByValue = true);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EvaluateParameters))]
internal sealed partial class MiniJson : JsonSerializerContext;
