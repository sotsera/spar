using System.Text;
using Spar.Bridge;
using Spar.Hosting;

namespace Spar.Tests;

/// <summary>
/// The page's side of a close request on the wire (decision "Two-stage close"): <c>close-request</c> sent only to a page
/// that listed it, the guards' <c>close-answer</c> read as untrusted text, <c>close-ended</c>, and the pong's flag for a
/// dialog of the page's own.
/// </summary>
public sealed class PageCloseTests
{
    private const string HelloWithClose =
        "{\"type\":\"hello\",\"protocol\":1,\"types\":[\"open\",\"result\",\"failure\",\"event\",\"closing\",\"close-request\",\"close-ended\"],\"contracts\":\"0123456789abcdef\"}\t";

    [Fact]
    public async Task A_page_that_listed_close_request_is_asked_and_its_answer_reaches_the_host()
    {
        await using var bridge = await TestBridge.OpenAsync(hello: HelloWithClose);
        var answers = new List<(long Seq, string Payload)>();
        bridge.Session.CloseAnswered += (seq, payload) => answers.Add((seq, Encoding.UTF8.GetString(payload.Span)));

        Assert.True(bridge.Session.RequestClose(3, PageCloseAnswer.RequestPayload(CloseReason.SessionEnd, automated: true)));
        var request = await bridge.Sent.NextAsync();
        bridge.Session.EndClose(3);
        var ended = await bridge.Sent.NextAsync();
        bridge.Session.Receive("{\"type\":\"close-answer\",\"seq\":3}\t{\"answer\":\"keep\"}");

        Assert.Equal((BridgeMessageType.CloseRequest, 3L, "{\"reason\":\"sessionEnd\",\"automated\":true}"), (request.Type, request.Seq!.Value, Encoding.UTF8.GetString(request.Payload.Span)));
        Assert.Equal((BridgeMessageType.CloseEnded, 3L), (ended.Type, ended.Seq!.Value));
        Assert.Equal([(3L, "{\"answer\":\"keep\"}")], answers);
    }

    [Fact]
    public async Task A_page_that_did_not_list_close_request_is_never_sent_one()
    {
        await using var bridge = await TestBridge.OpenAsync();

        Assert.False(bridge.Session.RequestClose(1, PageCloseAnswer.RequestPayload(CloseReason.User, automated: false)));
        bridge.Session.EndClose(1);

        Assert.True(await bridge.Sent.StaysQuietAsync(TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public async Task A_closed_document_is_not_asked()
    {
        await using var bridge = await TestBridge.OpenAsync(hello: HelloWithClose);
        bridge.Session.Close();

        Assert.False(bridge.Session.RequestClose(1, PageCloseAnswer.RequestPayload(CloseReason.User, automated: false)));
    }

    [Theory]
    [InlineData(CloseReason.User, false, "{\"reason\":\"user\",\"automated\":false}")]
    [InlineData(CloseReason.App, true, "{\"reason\":\"app\",\"automated\":true}")]
    [InlineData(CloseReason.SessionEnd, false, "{\"reason\":\"sessionEnd\",\"automated\":false}")]
    public void The_request_names_its_reason_as_the_page_reads_it(CloseReason reason, bool automated, string expected) =>
        Assert.Equal(expected, Encoding.UTF8.GetString(PageCloseAnswer.RequestPayload(reason, automated)));

    [Theory]
    [InlineData("{\"answer\":\"close\"}", "Close", null)]
    [InlineData("{\"answer\":\"keep\",\"future\":[1,{\"a\":2}]}", "Keep", null)]
    [InlineData("{\"question\":\" Close without saving? \",\"answer\":\"ask\"}", "Ask", "Close without saving?")]
    [InlineData("{\"answer\":\"failed\"}", "Failed", null)]
    public void The_guards_answers_are_read(string payload, string kind, string? question)
    {
        var answer = PageCloseAnswer.Read(Encoding.UTF8.GetBytes(payload), out var problem);

        Assert.Equal((Enum.Parse<PageCloseKind>(kind), question), (answer.Kind, answer.Question));
        Assert.Null(problem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[\"close\"]")]
    [InlineData("{}")]
    [InlineData("{\"answer\":\"close please\"}")]
    [InlineData("{\"answer\":\"ask\"}")]
    [InlineData("{\"answer\":\"ask\",\"question\":\"   \"}")]
    [InlineData("{\"answer\":true}")]
    public void An_answer_that_cannot_be_read_counts_as_a_failed_guard(string payload)
    {
        var answer = PageCloseAnswer.Read(Encoding.UTF8.GetBytes(payload), out var problem);

        Assert.Equal(PageCloseKind.Failed, answer.Kind);
        Assert.NotNull(problem);
    }

    [Fact]
    public void A_long_question_is_cut_and_never_between_the_halves_of_a_surrogate_pair()
    {
        var question = new string('a', PageCloseAnswer.MaxQuestionLength - 2) + "😀😀😀";
        var payload = Encoding.UTF8.GetBytes($"{{\"answer\":\"ask\",\"question\":\"{question}\"}}");

        var answer = PageCloseAnswer.Read(payload, out _);

        Assert.Equal(PageCloseAnswer.MaxQuestionLength - 1, answer.Question!.Length);
        Assert.EndsWith("…", answer.Question, StringComparison.Ordinal);
        Assert.False(char.IsHighSurrogate(answer.Question[^2]));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("{\"dialog\":true}", true)]
    [InlineData("{\"other\":[1],\"dialog\":true}", true)]
    [InlineData("{\"dialog\":false}", false)]
    [InlineData("{\"dialog\":\"true\"}", false)]
    [InlineData("not json", false)]
    public void A_pong_says_whether_the_page_shows_a_dialog_of_its_own(string payload, bool dialog) =>
        Assert.Equal(dialog, BridgeSession.ShowsDialog(Encoding.UTF8.GetBytes(payload)));
}
