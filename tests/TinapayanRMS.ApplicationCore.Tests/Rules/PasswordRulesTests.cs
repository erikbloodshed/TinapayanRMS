using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

public class PasswordRulesTests
{
    [Test]
    public void A_hashed_password_verifies_and_only_that_password_does()
    {
        var (hash, salt, iterations) = PasswordRules.Hash("pandesal 6am");

        PasswordRules.Verify("pandesal 6am", hash, salt, iterations).Should().BeTrue();
        PasswordRules.Verify("Pandesal 6am", hash, salt, iterations).Should().BeFalse("a password is kept exactly as typed, case and all");
        PasswordRules.Verify("pandesal 6am ", hash, salt, iterations).Should().BeFalse("a space counts wherever it is");
        PasswordRules.Verify("", hash, salt, iterations).Should().BeFalse();
        PasswordRules.Verify(null, hash, salt, iterations).Should().BeFalse();
    }

    [Test]
    public void A_hash_is_pbkdf2_at_the_iterations_it_says_with_a_salt_of_its_own()
    {
        var first = PasswordRules.Hash("pandesal 6am");
        var second = PasswordRules.Hash("pandesal 6am");

        first.Iterations.Should().Be(PasswordRules.Iterations);
        first.Hash.Should().HaveCount(PasswordRules.HashBytes);
        first.Salt.Should().HaveCount(PasswordRules.SaltBytes);
        second.Salt.Should().NotEqual(first.Salt);
        second.Hash.Should().NotEqual(first.Hash, "the same password with another salt hashes to something else");
    }

    [Test]
    public void A_hash_checked_with_another_salt_or_iteration_count_fails()
    {
        var (hash, salt, iterations) = PasswordRules.Hash("pandesal 6am");
        var otherSalt = (byte[])salt.Clone();
        otherSalt[0] ^= 1;

        PasswordRules.Verify("pandesal 6am", hash, otherSalt, iterations).Should().BeFalse();
        PasswordRules.Verify("pandesal 6am", hash, salt, iterations - 1).Should().BeFalse();
        PasswordRules.Verify("pandesal 6am", hash, salt, 0).Should().BeFalse();
    }

    [Test]
    public void A_hash_made_with_fewer_iterations_still_verifies_at_its_own_count()
    {
        // A password set before Iterations is raised is checked with the count it was made with.
        var salt = new byte[PasswordRules.SaltBytes];
        var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            "pandesal 6am"u8.ToArray(), salt, 1_000, System.Security.Cryptography.HashAlgorithmName.SHA256, PasswordRules.HashBytes);

        PasswordRules.Verify("pandesal 6am", hash, salt, 1_000).Should().BeTrue();
    }

    [Test]
    [Arguments("")]
    [Arguments("12345")]
    public void A_password_shorter_than_six_characters_is_refused(string password)
    {
        var act = () => PasswordRules.EnsureValid(password);

        act.Should().Throw<BusinessRuleException>().WithMessage("The password must have at least 6 characters.");
    }

    [Test]
    public void A_password_of_six_to_128_characters_is_allowed_and_a_longer_one_refused()
    {
        PasswordRules.EnsureValid("123456");
        PasswordRules.EnsureValid(new string('x', PasswordRules.MaxLength));

        var act = () => PasswordRules.EnsureValid(new string('x', PasswordRules.MaxLength + 1));

        act.Should().Throw<BusinessRuleException>().WithMessage("The password can have at most 128 characters.");
    }

    [Test]
    public void Hashing_refuses_a_password_the_rules_refuse()
    {
        var act = () => PasswordRules.Hash("123");

        act.Should().Throw<BusinessRuleException>();
    }

    [Test]
    public void A_new_password_typed_differently_the_second_time_is_refused()
    {
        PasswordRules.EnsureMatches("pandesal", "pandesal");

        var act = () => PasswordRules.EnsureMatches("pandesal", "Pandesal");

        act.Should().Throw<BusinessRuleException>().WithMessage("The two passwords aren't the same*");
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(240)]
    public void An_auto_lock_of_0_to_240_minutes_is_allowed(int minutes) => PasswordRules.EnsureValidAutoLock(minutes);

    [Test]
    [Arguments(-1)]
    [Arguments(241)]
    public void An_auto_lock_outside_0_to_240_minutes_is_refused(int minutes)
    {
        var act = () => PasswordRules.EnsureValidAutoLock(minutes);

        act.Should().Throw<BusinessRuleException>().WithMessage("The auto-lock must be from 0 (never) to 240 minutes.");
    }
}
