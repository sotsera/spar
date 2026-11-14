; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SPAR050 | Spar.Results | Error | DefaultResultAnalyzer, an expression that creates a default Result
SPAR051 | Spar.Results | Warning | DefaultResultAnalyzer, an API that may return a default Result
SPAR052 | Spar.Results | Warning | DefaultResultAnalyzer, storage whose Result elements start as default
SPAR053 | Spar.Results | Warning | DefaultResultAnalyzer, a field or auto-property that can be read before it is assigned
SPAR054 | Spar.Results | Warning | DefaultResultAnalyzer, a generic method in source that creates default(T) for a Result
