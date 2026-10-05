using System.Text.Json;
using HomeVault.Application.Reminders;
using HomeVault.Domain.Reminders;
using NUnit.Framework;

namespace HomeVault.Tests;

public sealed class ReminderRestorationTests
{
    [TestCase(ReminderStatus.Pending)]
    [TestCase(ReminderStatus.Completed)]
    [TestCase(ReminderStatus.Cancelled)]
    public void RestoresExactStateWithoutLeakingAction(ReminderStatus status)
    {
        var id = Guid.NewGuid(); var vault = Guid.NewGuid(); var asset = Guid.NewGuid();
        var instant = new DateTimeOffset(2030, 2, 3, 12, 30, 0, TimeSpan.FromHours(3)).AddTicks(1);
        var root = Reminder.Restore(id, vault, asset, " fictional action ", instant, status);
        Assert.That((root.Id, root.VaultId, root.AssetId, root.Status), Is.EqualTo((id, vault, asset, status)));
        Assert.That(root.DueAt.UtcTicks, Is.EqualTo(instant.UtcTicks));
        Assert.That(root.DueAt.Offset, Is.EqualTo(TimeSpan.Zero));
        Assert.That(root.ReadAction(), Is.EqualTo(" fictional action "));
        Assert.That(JsonSerializer.Serialize(root), Does.Not.Contain("fictional action"));
        var snapshot = new ReminderSnapshot(id, vault, asset, root.DueAt, status, root.ReadAction());
        Assert.That(JsonSerializer.Serialize(snapshot), Does.Not.Contain("fictional action"));
        Assert.That(snapshot.ToString(), Is.EqualTo("ReminderSnapshot"));
        if (status != ReminderStatus.Pending) Assert.That(root.Update("new", default), Is.EqualTo(ReminderError.NotPending));
    }
    [TestCase("id")]
    [TestCase("vault")]
    [TestCase("asset")]
    [TestCase("action")]
    [TestCase("status")]
    public void InvalidStoredStateFailsSafely(string invalid)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => Reminder.Restore(
            invalid == "id" ? Guid.Empty : Guid.NewGuid(), invalid == "vault" ? Guid.Empty : Guid.NewGuid(),
            invalid == "asset" ? Guid.Empty : Guid.NewGuid(), invalid == "action" ? "\u2000" : "private fictional action",
            default, invalid == "status" ? (ReminderStatus)99 : ReminderStatus.Pending));
        Assert.That(failure!.Message, Is.EqualTo("Invalid stored Reminder state."));
    }
}
