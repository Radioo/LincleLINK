using FluentAssertions;
using LincleLINK.Core.Infrastructure.Instances;
using Xunit;

namespace LincleLINK.Core.Tests.Instances;

public sealed class InstanceStorageExceptionTests
{
    [Fact]
    public void Message_only_constructor_sets_message()
    {
        var ex = new InstanceStorageException("boom");

        ex.Message.Should().Be("boom");
        ex.InnerException.Should().BeNull();
    }

    [Fact]
    public void Inner_exception_constructor_preserves_cause()
    {
        var inner = new IOException("root cause");
        var ex = new InstanceStorageException("boom", inner);

        ex.Message.Should().Be("boom");
        ex.InnerException.Should().BeSameAs(inner);
    }
}
