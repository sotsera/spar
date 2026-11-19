using System.Text;
using System.Text.Json;

namespace Spar.Hosting;

/// <summary>What the page's close guards answered (<c>windows.onCloseRequested</c>, decision "Two-stage close").</summary>
internal enum PageCloseKind
{
    /// <summary>Every guard agreed, or the page has none.</summary>
    Close,

    /// <summary>A guard keeps the window open: the request ends, and the backend's guard is not asked.</summary>
    Keep,

    /// <summary>A guard asks the user; <see cref="PageCloseAnswer.Question"/> is the question.</summary>
    Ask,

    /// <summary>A guard threw, or the answer could not be read: the host asks as for a failed backend guard.</summary>
    Failed,
}

/// <summary>
/// The page's side of a close request on the wire (plan section 2c, <c>close-request</c> and <c>close-answer</c>): the
/// request's payload the host writes, and the answer's payload, the page's untrusted text, which the host reads here.
/// A plain struct, not a record: a record's generated members cost the NativeAOT image (pitfalls).
/// </summary>
internal readonly struct PageCloseAnswer(PageCloseKind kind, string? question = null)
{
    /// <summary>The longest question shown; a longer one is cut, so a page cannot fill the screen with the dialog.</summary>
    public const int MaxQuestionLength = 500;

    /// <summary>The question for a page guard that failed: the same choice the backend's failed guard gives.</summary>
    public const string FailedQuestion = "The page could not check whether it is safe to close. Close anyway?";

    public static PageCloseAnswer Close { get; } = new(PageCloseKind.Close);

    public PageCloseKind Kind { get; } = kind;

    /// <summary>For <see cref="PageCloseKind.Ask"/>, the question; null otherwise.</summary>
    public string? Question { get; } = question;

    /// <summary>The reason as the page reads it: <c>user</c>, <c>app</c> or <c>sessionEnd</c>.</summary>
    public static string ReasonName(CloseReason reason) => reason switch
    {
        CloseReason.App => "app",
        CloseReason.SessionEnd => "sessionEnd",
        _ => "user",
    };

    /// <summary><c>close-request</c>'s payload: <c>{"reason":"user","automated":false}</c>.</summary>
    public static byte[] RequestPayload(CloseReason reason, bool automated) =>
        Encoding.UTF8.GetBytes($$"""{"reason":"{{ReasonName(reason)}}","automated":{{(automated ? "true" : "false")}}}""");

    /// <summary>
    /// Reads <c>close-answer</c>'s payload: <c>{"answer":"close"|"keep"|"failed"}</c> or
    /// <c>{"answer":"ask","question":"…"}</c>. Anything else is <see cref="PageCloseKind.Failed"/>, with why in
    /// <paramref name="problem"/>; unknown fields are ignored.
    /// </summary>
    public static PageCloseAnswer Read(ReadOnlySpan<byte> payload, out string? problem)
    {
        problem = null;
        string? answer = null;
        string? question = null;
        try
        {
            var reader = new Utf8JsonReader(payload, new JsonReaderOptions { MaxDepth = 4 });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                problem = "the answer is not a JSON object";
                return new(PageCloseKind.Failed);
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                reader.Read();
                switch (name)
                {
                    case "answer" when reader.TokenType == JsonTokenType.String:
                        answer = reader.GetString();
                        break;
                    case "question" when reader.TokenType == JsonTokenType.String:
                        question = reader.GetString();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }
        }
        catch (JsonException exception)
        {
            problem = "the answer is not valid JSON: " + exception.Message;
            return new(PageCloseKind.Failed);
        }

        switch (answer)
        {
            case "close":
                return Close;
            case "keep":
                return new(PageCloseKind.Keep);
            case "failed":
                return new(PageCloseKind.Failed);
            case "ask" when !string.IsNullOrWhiteSpace(question):
                var text = question.Trim();
                return new(PageCloseKind.Ask, Shorten(text));
            case "ask":
                problem = "an ask without a question";
                return new(PageCloseKind.Failed);
            default:
                problem = answer is null ? "the answer has no 'answer'" : $"'{(answer.Length > 20 ? answer[..20] + "…" : answer)}' is not an answer";
                return new(PageCloseKind.Failed);
        }
    }

    /// <summary>The question cut to <see cref="MaxQuestionLength"/>, never between the halves of a surrogate pair.</summary>
    private static string Shorten(string text)
    {
        if (text.Length <= MaxQuestionLength)
        {
            return text;
        }

        var cut = MaxQuestionLength - 1;
        if (char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }

        return string.Concat(text.AsSpan(0, cut), "…");
    }
}
