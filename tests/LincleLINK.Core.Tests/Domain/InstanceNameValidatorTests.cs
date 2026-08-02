using FluentAssertions;
using LincleLINK.Core.Domain.Validation;
using Xunit;

namespace LincleLINK.Core.Tests.Domain;

public sealed class InstanceNameValidatorTests
{
    [Theory]
    [InlineData("IIDX28")]
    [InlineData("beatmania IIDX")]
    [InlineData("a.b.c")]
    [InlineData("Mixed_Case_Name-2")]
    public void Valid_names_pass(string name)
    {
        InstanceNameValidator.IsValid(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData(@"a\b")]
    [InlineData("a:b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a\"b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a|b")]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("CON.txt")]
    [InlineData("prn")]
    [InlineData("COM1")]
    [InlineData("lpt9")]
    [InlineData("name.")]
    [InlineData("name ")]
    public void Invalid_names_are_rejected(string name)
    {
        InstanceNameValidator.IsValid(name).Should().BeFalse();
    }

    [Fact]
    public void Reserved_device_names_are_rejected_case_insensitively()
    {
        InstanceNameValidator.IsValid("NUL").Should().BeFalse();
        InstanceNameValidator.IsValid("Com3").Should().BeFalse();
    }
}
