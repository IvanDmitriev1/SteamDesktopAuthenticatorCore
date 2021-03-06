using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SteamAuthCore;

namespace SteamAuthenticatorCore.Shared;

public sealed partial class TokenService : ObservableObject, IDisposable
{
    public TokenService(IPlatformTimer timer)
    {
        _timer = timer;
        _timer.ChangeInterval(TimeSpan.FromSeconds(2));
        _timer.SetFuncOnTick(SteamGuardTimerOnTick);
        _timer.Start();
    }

    private Int64 _currentSteamChunk;
    private Int64 _steamTime;

    [ObservableProperty]
    private string _token = string.Empty;

    [ObservableProperty]
    private double _tokenProgressBar;

    private readonly IPlatformTimer _timer;

    public bool IsMobile { get; set; }

    public SteamGuardAccount? SelectedAccount { get; set; }

    public void Dispose()
    {
        _timer.Stop();
    }

    private void SteamGuardTimerOnTick()
    {
        if (SelectedAccount is null) return;

        _steamTime = TimeAligner.GetSteamTime();
        _currentSteamChunk = _steamTime / 30L;
        int secondsUntilChange = (int)(_steamTime - (_currentSteamChunk * 30L));

        if (_steamTime != 0)
        {
            if (SelectedAccount.GenerateSteamGuardCode(_steamTime) is { } token)
                Token = token;
        }

        TokenProgressBar = 30 - secondsUntilChange;

        if (IsMobile)
            TokenProgressBar /= 30;
    }
}