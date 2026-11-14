// Microsoft.Build.Framework 18.12.2 does not contain MSBuildMultiThreadableTaskAnalyzedAttribute, which the analyzer
// looks up by metadata name. This probe declares it in source to see whether the analyzer honours a local copy.
namespace Microsoft.Build.Framework;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
internal sealed class MSBuildMultiThreadableTaskAnalyzedAttribute : Attribute
{
}
