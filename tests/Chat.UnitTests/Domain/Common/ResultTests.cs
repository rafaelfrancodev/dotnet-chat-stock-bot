using Chat.Domain.Common;

namespace Chat.UnitTests.Domain.Common;

public sealed class ResultTests
{
    [Fact]
    public void Success_WithValue_ExposesValue()
    {
        Result<string> result = Result.Success("aapl.us");

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Value.Should().Be("aapl.us");
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void Failure_WithError_CarriesErrorAndBlocksValueAccess()
    {
        Error error = Error.Validation("StockCode.Invalid", "Stock code is not valid.");

        Result<string> result = Result.Failure<string>(error);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
        FluentActions.Invoking(() => result.Value).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Failure_WithoutError_IsRejected()
    {
        FluentActions.Invoking(() => Result.Failure(Error.None)).Should().Throw<ArgumentException>();
    }
}
