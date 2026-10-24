using Pharmacy.Infrastructure.Identity;

namespace Pharmacy.ArchitectureTests;

public class PasswordLetterValidatorTests
{
    [Theory]
    [InlineData("12345678a", true)]
    [InlineData("كلمة1234", true)]
    [InlineData("12345678", false)]
    [InlineData("!!!!1234", false)]
    [InlineData(null, false)]
    public async Task RequiresALetterInAnyScript(string? password, bool valid)
    {
        var result = await new PasswordLetterValidator().ValidateAsync(null!, new ApplicationUser(), password);

        result.Succeeded.ShouldBe(valid);
        if (!valid)
        {
            result.Errors.Single().Code.ShouldBe(PasswordLetterValidator.ErrorCode);
        }
    }
}
