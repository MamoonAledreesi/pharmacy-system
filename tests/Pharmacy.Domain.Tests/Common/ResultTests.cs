using Pharmacy.Domain.Common;

namespace Pharmacy.Domain.Tests.Common;

public class ResultTests
{
    private static readonly Error _sampleError = new("Errors.Sample", 5);

    [Fact]
    public void Success_HasNoError()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void Failure_CarriesTheError()
    {
        var result = Result.Failure(_sampleError);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_sampleError);
    }

    [Fact]
    public void Failure_RejectsNullError()
    {
        Should.Throw<ArgumentNullException>(() => Result.Failure(null!));
        Should.Throw<ArgumentNullException>(() => Result.Failure<int>(null!));
    }

    [Fact]
    public void GenericSuccess_ExposesValue()
    {
        var result = Result.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void GenericFailure_ReadingValueThrows()
    {
        var result = Result.Failure<int>(_sampleError);

        result.IsFailure.ShouldBeTrue();
        var exception = Should.Throw<InvalidOperationException>(() => result.Value);
        exception.Message.ShouldContain("Errors.Sample");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Error_RequiresCode(string code)
    {
        Should.Throw<ArgumentException>(() => new Error(code));
    }

    [Fact]
    public void Error_KeepsArgumentsForLocalizedMessage()
    {
        _sampleError.Code.ShouldBe("Errors.Sample");
        _sampleError.Arguments.ShouldBe([5]);
    }
}
