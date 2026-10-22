using Sotsera.Spar.Development;

namespace Spar.Dev;

internal static class SessionPresentation
{
    internal static string? Format(DevelopmentEvent value)
    {
        var text = value.Kind switch
        {
            "session-phase" => value.Code switch
            {
                "building" => "Building backend and contracts",
                "starting-frontend" => "Starting frontend",
                "launching-application" => "Launching application",
                "waiting-debugger" => "Waiting for debugger launch",
                "awaiting-readiness" => "Waiting for native document and typed readiness",
                _ => null
            },
            "application-ready" => "Application ready",
            "application-not-ready" or "bridge-connected" => "Waiting for native document and typed readiness",
            "backend-restarting" => "Retiring backend before restart",
            "backend-updated" => "Backend update observed; readiness remains document-bound",
            "backend-stopped" or "session-failed" or "cleanup-failed" => $"{value.Code}: {value.Detail}",
            "build-diagnostic" or "debugger-diagnostic" => value.Detail,
            "debugger-state" => $"Debugger {value.Code}",
            "session-stopping" => "Stopping session; waiting for owned work",
            "session-stopped" => $"Session stopped ({value.Code})",
            _ => null
        };
        if (text is null)
        {
            return null;
        }
        // One bounded display line; structured stdout retains complete identities and log paths.
        text = string.Concat(text.Take(4096).Select(character => char.IsControl(character)
            || character is '\u2028' or '\u2029' ? ' ' : character));
        return $"Spar [{value.Generation}]: {text}";
    }
}
