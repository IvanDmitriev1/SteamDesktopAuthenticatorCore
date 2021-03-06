using System.Threading.Tasks;
using System.Windows.Input;
using SteamAuthCore;
using SteamAuthenticatorCore.Desktop.Views.Pages;
using SteamAuthenticatorCore.Shared;
using WPFUI.DIControls.Interfaces;
using AsyncRelayCommand = SteamAuthenticatorCore.Shared.Helpers.AsyncRelayCommand;

namespace SteamAuthenticatorCore.Desktop.ViewModels;

public class LoginViewModel : INavigable
{
    public LoginViewModel(LoginService loginService, App.ManifestServiceResolver manifestServiceResolver, INavigation navigation)
    {
        LoginService = loginService;
        _manifestServiceResolver = manifestServiceResolver;
        _navigation = navigation;

        LoginCommand = new AsyncRelayCommand(Login);
    }

    #region Variables

    private readonly App.ManifestServiceResolver _manifestServiceResolver;
    private readonly INavigation _navigation;

    #endregion

    public LoginService LoginService { get; }

    public ICommand LoginCommand { get; }

    #region Public methods

    public Task OnNavigationRequest(INavigation navigation, string previousPageTag, object[]? ars)
    {
        if (ars is null)
        {
            LoginService.Account = null;
            return Task.CompletedTask;
        }

        LoginService.Account = (SteamGuardAccount?) ars[0];

        return Task.CompletedTask;
    }

    #endregion

    #region PrivateMethods

    private async Task Login()
    {
        if (LoginService.Account is null)
        {
            await LoginService.InitLogin(_manifestServiceResolver.Invoke());
        }
        else
        {
            await LoginService.RefreshLogin(_manifestServiceResolver.Invoke());
        }

        await _navigation.NavigateTo($"{nameof(TokenPage)}");
    }

    #endregion
}