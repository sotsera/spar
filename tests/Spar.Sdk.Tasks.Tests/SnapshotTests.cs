using Spar.Sdk.Tasks.Client;

namespace Spar.Sdk.Tasks.Tests;

/// <summary>The contract snapshot's text: what a reviewer reads in a diff.</summary>
public sealed class SnapshotTests
{
    [Fact]
    public void Every_command_record_and_error_case_has_one_line_in_a_stable_order()
    {
        var layout = ClientHarness.Plan(Fixtures.Shop);

        var text = ContractSnapshot.Text("Shop", layout.Contracts);

        var body = text[text.IndexOf("\ncontract ", StringComparison.Ordinal)..];
        var expected = """

            contract orders (Shop.OrdersService)
              cancel(string) -> void | Shop.OrderError
              clear() -> void
              count() -> long
              place(Shop.Basket) -> Shop.Receipt | Shop.OrderError

            contract stock (Shop.IStockService)
              find(string) -> Shop.Item
              list() -> IReadOnlyList<Shop.Item>

            record Shop.Basket
              items: Shop.Item[]
              coupon: int?
              notes: IReadOnlyList<string?>

            record Shop.Item
              sku: string
              stock: long
              price: double
              tags: IReadOnlyList<string>
              replacement: Shop.Item?

            record Shop.Receipt
              id: Guid
              at: DateTimeOffset
              paid: bool

            error Shop.OrderError
              outOfStock(sku: string, left: long)
              closed

            """.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(expected, body);
        Assert.StartsWith("# The contracts of Shop as pages see them.", text, StringComparison.Ordinal);
        Assert.Matches("\nfingerprint [0-9a-f]{16}\n", text);
    }

    [Fact]
    public void A_new_field_changes_one_line_and_the_fingerprint()
    {
        var before = ContractSnapshot.Text("Shop", ClientHarness.Plan(Fixtures.Shop).Contracts).Split('\n');
        var changed = Fixtures.Shop.Replace("public sealed record Receipt(Guid Id, DateTimeOffset At, bool Paid);", "public sealed record Receipt(Guid Id, DateTimeOffset At, bool Paid, string? Note);", StringComparison.Ordinal);

        var after = ContractSnapshot.Text("Shop", ClientHarness.Plan(changed).Contracts).Split('\n');

        Assert.Equal(["  note: string?"], after.Except(before).Where(line => !line.StartsWith("fingerprint", StringComparison.Ordinal)));
        Assert.NotEqual(before.Single(l => l.StartsWith("fingerprint", StringComparison.Ordinal)), after.Single(l => l.StartsWith("fingerprint", StringComparison.Ordinal)));
    }
}
