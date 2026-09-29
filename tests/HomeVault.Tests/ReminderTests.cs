using System.Text.Json;
using HomeVault.Domain.Reminders;
using NUnit.Framework;

namespace HomeVault.Tests;

[TestFixture]
public sealed class ReminderTests
{
    private static readonly DateTimeOffset Due = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static Reminder CreateReminder(DateTimeOffset due) =>
        Reminder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " Example action ", due).Reminder!;

    [Test]
    public void CreationPreservesReferencesAndActionAndStartsPending()
    {
        var id = Guid.NewGuid();
        var vault = Guid.NewGuid();
        var asset = Guid.NewGuid();
        var result = Reminder.Create(id, vault, asset, "  Review – København  ", Due);
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Error, Is.EqualTo(ReminderError.None));
        Assert.That(result.Reminder!.Id, Is.EqualTo(id));
        Assert.That(result.Reminder.VaultId, Is.EqualTo(vault));
        Assert.That(result.Reminder.AssetId, Is.EqualTo(asset));
        Assert.That(result.Reminder.ReadAction(), Is.EqualTo("  Review – København  "));
        Assert.That(result.Reminder.Status, Is.EqualTo(ReminderStatus.Pending));
    }

    [TestCase(0, ReminderError.EmptyIdentity)]
    [TestCase(1, ReminderError.EmptyVaultIdentity)]
    [TestCase(2, ReminderError.EmptyAssetIdentity)]
    public void IdentityValidationPrecedesRemainingInputs(int firstEmpty, ReminderError error)
    {
        var ids = Enumerable.Range(0, 3).Select(index => index < firstEmpty ? Guid.NewGuid() : Guid.Empty).ToArray();
        var result = Reminder.Create(ids[0], ids[1], ids[2], null, Due);
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Reminder, Is.Null);
        Assert.That(result.Error, Is.EqualTo(error));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \t\r\n")]
    [TestCase("\u2003")]
    public void InvalidActionFailsCreationAndLeavesUpdatesAtomic(string? action)
    {
        var result = Reminder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), action, Due);
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Reminder, Is.Null);
        Assert.That(result.Error, Is.EqualTo(ReminderError.BlankAction));
        var reminder = CreateReminder(Due);
        Assert.That(reminder.Update(action, Due.AddDays(2)), Is.EqualTo(ReminderError.BlankAction));
        Assert.That(reminder.ReadAction(), Is.EqualTo(" Example action "));
        Assert.That(reminder.DueAt, Is.EqualTo(Due));
        Assert.That(reminder.Status, Is.EqualTo(ReminderStatus.Pending));
    }

    [TestCase(-14)]
    [TestCase(-5)]
    [TestCase(0)]
    [TestCase(2)]
    [TestCase(14)]
    public void OffsetsNormalizeToUtcAndCompareByInstant(int hours)
    {
        var input = Due.ToOffset(TimeSpan.FromHours(hours));
        var reminder = CreateReminder(input);
        Assert.That(reminder.DueAt, Is.EqualTo(Due));
        Assert.That(reminder.DueAt.Offset, Is.EqualTo(TimeSpan.Zero));
        Assert.That(reminder.IsOverdue(input), Is.False);
        Assert.That(reminder.IsOverdue(input.AddTicks(-1)), Is.False);
        Assert.That(reminder.IsOverdue(input.AddTicks(1)), Is.True);
    }

    [Test]
    public void FullRangeAndDefaultAreValidWithoutClockDependentValidation()
    {
        foreach (var due in new[] { DateTimeOffset.MinValue, DateTimeOffset.MaxValue, default(DateTimeOffset) })
        {
            var reminder = CreateReminder(due);
            Assert.That(reminder.DueAt, Is.EqualTo(due));
            Assert.That(reminder.IsOverdue(due), Is.False);
            Assert.That(reminder.Update("Boundary update", due), Is.EqualTo(ReminderError.None));
        }

        Assert.That(CreateReminder(DateTimeOffset.MinValue).IsOverdue(DateTimeOffset.MaxValue), Is.True);
        Assert.That(CreateReminder(DateTimeOffset.MaxValue).IsOverdue(DateTimeOffset.MaxValue), Is.False);
    }

    [Test]
    public void UpdateChangesOnlyActionAndDueInstantAndAllowsPastDates()
    {
        var reminder = CreateReminder(Due);
        var id = reminder.Id;
        var vault = reminder.VaultId;
        var asset = reminder.AssetId;
        var earlier = Due.AddYears(-10).ToOffset(TimeSpan.FromHours(3));
        Assert.That(reminder.Update(" Updated action ", earlier), Is.EqualTo(ReminderError.None));
        Assert.That(reminder.ReadAction(), Is.EqualTo(" Updated action "));
        Assert.That(reminder.DueAt, Is.EqualTo(earlier));
        Assert.That(reminder.DueAt.Offset, Is.EqualTo(TimeSpan.Zero));
        Assert.That(reminder.IsOverdue(Due), Is.True);
        Assert.That(reminder.Id, Is.EqualTo(id));
        Assert.That(reminder.VaultId, Is.EqualTo(vault));
        Assert.That(reminder.AssetId, Is.EqualTo(asset));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void TerminalStatesAreIdempotentButCannotSwitchOrUpdate(bool complete)
    {
        var reminder = CreateReminder(Due);
        Assert.That(complete ? reminder.Complete() : reminder.Cancel(), Is.EqualTo(ReminderError.None));
        Assert.That(complete ? reminder.Complete() : reminder.Cancel(), Is.EqualTo(ReminderError.None));
        Assert.That(complete ? reminder.Cancel() : reminder.Complete(), Is.EqualTo(ReminderError.NotPending));
        Assert.That(reminder.Update(null, Due.AddDays(1)), Is.EqualTo(ReminderError.NotPending));
        Assert.That(reminder.Update("Changed", Due.AddDays(1)), Is.EqualTo(ReminderError.NotPending));
        Assert.That(reminder.Status, Is.EqualTo(complete ? ReminderStatus.Completed : ReminderStatus.Cancelled));
        Assert.That(reminder.IsOverdue(Due.AddDays(1)), Is.False);
        Assert.That(reminder.ReadAction(), Is.EqualTo(" Example action "));
        Assert.That(reminder.DueAt, Is.EqualTo(Due));
    }

    [Test]
    public void DiagnosticsAndDefaultJsonOmitPrivateActionText()
    {
        const string action = "fictional-private-action-sentinel";
        var result = Reminder.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), action, Due);
        Assert.That(result.ToString(), Is.EqualTo("ReminderCreationResult"));
        Assert.That(result.Reminder!.ToString(), Is.EqualTo("Reminder"));
        Assert.That(JsonSerializer.Serialize(result), Does.Not.Contain(action));
        Assert.That(JsonSerializer.Serialize(result.Reminder), Does.Not.Contain(action));
        Assert.That(result.Reminder.ReadAction(), Is.EqualTo(action));
    }
}
