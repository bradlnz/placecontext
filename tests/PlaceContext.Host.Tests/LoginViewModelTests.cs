using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class LoginViewModelTests
{
    [Fact]
    public void SetParameters_exposes_query_values_to_the_view()
    {
        var viewModel = new LoginViewModel();

        viewModel.SetParameters("Invalid credentials", "/overview", "member@example.com");

        Assert.Equal("Invalid credentials", viewModel.Error);
        Assert.Equal("/overview", viewModel.ReturnUrl);
        Assert.Equal("member@example.com", viewModel.Email);
    }
}
