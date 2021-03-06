using System;
using System.Threading.Tasks;
using SteamAuthenticatorCore.Mobile.Helpers;
using SteamAuthenticatorCore.Shared;
using Xamarin.Forms;

namespace SteamAuthenticatorCore.Mobile.Services;

internal class MobileImplementations : IPlatformImplementations
{
    public object CreateImage(string imageSource)
    {
        return ImageSource.FromUri(new Uri(imageSource, UriKind.Absolute));
    }

    public void InvokeMainThread(Action method)
    {
        Device.BeginInvokeOnMainThread(method);
    }

    public Task DisplayAlert(string message)
    {
        return Application.Current.MainPage.DisplayAlert("Alert", message, "Ok");
    }

    public void SetTheme(Theme theme)
    {
        Application.Current.UserAppTheme = theme switch
        {
            Theme.System => OSAppTheme.Unspecified,
            Theme.Light => OSAppTheme.Light,
            Theme.Dark => OSAppTheme.Dark,
            _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, null)
        };

        var environment = DependencyService.Get<IEnvironment>();

        if (Application.Current.RequestedTheme == OSAppTheme.Dark)
            environment.SetStatusBarColor(Color.Black, false);
        else
            environment.SetStatusBarColor(Color.White, true);
    }
}