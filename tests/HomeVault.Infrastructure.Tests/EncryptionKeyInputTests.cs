using System.Security.Cryptography;
using HomeVault.Playground;
using NUnit.Framework;

namespace HomeVault.Infrastructure.Tests;

[NonParallelizable]
public sealed class EncryptionKeyInputTests
{
    [TestCase(64, true), TestCase(63, false), TestCase(65, false)]
    public void HiddenInputEnforcesExactHexLengthWithoutEcho(int count, bool valid)
    {
        var previous = Console.Out;
        using var output = new StringWriter(); Console.SetOut(output);
        try
        {
            var calls = 0;
            ConsoleKeyInfo Read(bool intercept)
            {
                Assert.That(intercept, Is.True);
                return calls++ < count ? new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false) : new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false);
            }
            if (valid)
            {
                var secret = EncryptionKeyJourney.ReadSecret(Read);
                Assert.That(secret, Is.EqualTo(Enumerable.Repeat((byte)0xaa, 32)));
                CryptographicOperations.ZeroMemory(secret);
            }
            else Assert.Throws<InvalidOperationException>(() => EncryptionKeyJourney.ReadSecret(Read));
            Assert.That(output.ToString(), Does.Not.Contain(new string('a', count)));
        }
        finally { Console.SetOut(previous); }
    }

    [Test]
    public void InvalidCharactersAndCancellationFailWithoutReturningSecret()
    {
        Assert.Throws<InvalidOperationException>(() => EncryptionKeyJourney.ReadSecret(_ => new ConsoleKeyInfo('z', ConsoleKey.Z, false, false, false)));
        Assert.Throws<InvalidOperationException>(() => EncryptionKeyJourney.ReadSecret(_ => new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false)));
    }

    [Test]
    public void ExtraSecretArgumentIsRejectedWithoutPrintingIt()
    {
        var previous = Console.Error; using var output = new StringWriter(); Console.SetError(output);
        try
        {
            Assert.That(EncryptionKeyJourney.Run(["encryption-keys", "initialize", "ring", "exports", "fictional-secret-argument"]), Is.EqualTo(2));
            Assert.That(output.ToString(), Does.Not.Contain("fictional-secret-argument"));
        }
        finally { Console.SetError(previous); }
    }
}
