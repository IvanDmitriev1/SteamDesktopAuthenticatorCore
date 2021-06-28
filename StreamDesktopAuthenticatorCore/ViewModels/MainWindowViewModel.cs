﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using SteamAuthCore;
using SteamAuthCore.Models;
using SteamDesktopAuthenticatorCore.Models;
using SteamDesktopAuthenticatorCore.Services;
using SteamDesktopAuthenticatorCore.Views;
using WpfHelper;
using WpfHelper.Commands;
using WpfHelper.Services;

namespace SteamDesktopAuthenticatorCore.ViewModels
{
    class MainWindowViewModel : BaseViewModel
    {
        public MainWindowViewModel()
        {
            if (!App.InDesignMode)
            {
                Manifest = ManifestModelService.GetManifest().Result;

                Task.Run(async () =>
                {
                    SettingsModel settings = (await SettingsModelService.GetSettingsModel())!;

                    SwitchText = settings.ManifestLocation switch
                    {
                        ManifestLocation.Drive => "Switch to using the files on your disk",
                        ManifestLocation.GoogleDrive => "Switch to using the files on your Google Drive",
                        _ => throw new ArgumentOutOfRangeException()
                    };
                });
            }
            else
            {
                Manifest = new ManifestModel();
            }

            CurrentVersion = Assembly.GetExecutingAssembly().GetName().Version!.ToString();

            _steamGuardTimer = new DispatcherTimer()
            {
                Interval = new TimeSpan(0, 0, 1),
            };
            _steamGuardTimer.Tick += SteamGuardTimerOnTick;
            _steamGuardTimer.Start();

            _autoTradeConfirmationTimer = new DispatcherTimer()
            {
                Interval = TimeSpan.FromSeconds(Manifest.PeriodicCheckingInterval)
            };
            _autoTradeConfirmationTimer.Tick += async (sender, args) => await AutoTradeConfirmationTimerOnTick();

            if (Manifest.AutoConfirmMarketTransactions)
                _autoTradeConfirmationTimer.Start();
        }

        #region Variables

        private Window _thisWindow = null!;
        private Int64 _steamTime;
        private Int64 _currentSteamChunk;
        private readonly DispatcherTimer _steamGuardTimer;
        private readonly DispatcherTimer _autoTradeConfirmationTimer;
        private static readonly Regex Regex = new("[^0-9]+");

        private SteamGuardAccount? _selectedAccount;
        private string _loginTokenText = string.Empty;
        private string _statusText = string.Empty;
        private int _progressBar;
        private string _switchText = string.Empty;

        #endregion

        #region Fields

        public ManifestModel Manifest { get; set; }
        public SteamGuardAccount? SelectedAccount
        {
            get => _selectedAccount;
            set => Set(ref _selectedAccount, value);
        }
        public string StatusText
        {
            get => _statusText;
            set => Set(ref _statusText, value);
        }
        public string LoginTokenText
        {
            get => _loginTokenText;
            set => Set(ref _loginTokenText, value);
        }
        public int ProgressBar
        {
            get => _progressBar;
            set => Set(ref _progressBar, value);
        }

        public string SwitchText
        {
            get => _switchText;
            set => Set(ref _switchText, value);
        }

        public string CurrentVersion { get; private set; }

        #endregion

        #region Commands

        public ICommand WindowOnLoadedCommand => new RelayCommand(o =>
        {
            if (o is not RoutedEventArgs { Source: Window window }) return;

            _thisWindow = window;
        });

        public ICommand WindowCloseCommand => new RelayCommand(o => _thisWindow.Close());

        public ICommand TokenOnInputCommand => new RelayCommand(o =>
        {
            if (o is not TextCompositionEventArgs args) return;

            args.Handled = true;
        });

        public ICommand SwitchCommand => new AsyncRelayCommand(async o =>
        {
            SettingsModel settings = (await SettingsModelService.GetSettingsModel())!;
            settings.ManifestLocation = settings.ManifestLocation switch
            {
                ManifestLocation.Drive => ManifestLocation.GoogleDrive,
                ManifestLocation.GoogleDrive => ManifestLocation.Drive,
                _ => throw new ArgumentOutOfRangeException()
            };

            await SettingsModelService.SaveSettings();

            MessageBox.Show("Restart application");
            Application.Current.Shutdown(0);
        });

        public ICommand ShowWindowCommand => new RelayCommand(o =>
        {
            _thisWindow.Show();

            if (_thisWindow.WindowState == WindowState.Minimized)
                _thisWindow.WindowState = WindowState.Normal;
        });

        public ICommand SettingsShowCommand => new RelayCommand(o =>
        {
            _steamGuardTimer.Stop();
            _autoTradeConfirmationTimer.Stop();

            SettingWindowView window = new();
            window.ShowDialog();

            _steamGuardTimer.Start();

            _autoTradeConfirmationTimer.Interval = TimeSpan.FromSeconds(Manifest.PeriodicCheckingInterval);
            if (Manifest.AutoConfirmMarketTransactions)
            {
                _autoTradeConfirmationTimer.Start();
            }
        });

        public ICommand ImportAccountsCommand => new AsyncRelayCommand(async o =>
        {
            FileDialog fileDialog = new OpenFileDialog()
            {
                Multiselect = true
            };

            if (fileDialog.ShowDialog() == false) return;

            for (var i = 0; i < fileDialog.FileNames.Length; i++)
            {
                string filePath = fileDialog.FileNames[i];
                string fileNam = fileDialog.SafeFileNames[i];

                if (!fileNam.Contains(".maFile"))
                {
                    MessageBox.Show("This is not .maFile");
                    continue;
                }

                await ManifestModelService.AddSteamGuardAccount(fileNam, filePath);
            }

            await ManifestModelService.GetAccounts();
        });

        public ICommand RefreshAccountCommand => new AsyncRelayCommand(async o =>
        {
            await ManifestModelService.GetAccounts();
        });

        public ICommand DeleteAccountCommand => new AsyncRelayCommand(async o =>
        {
            if (SelectedAccount is null) return;

            if (MessageBox.Show("are you sure you want to delete a account from disk ?", "Delete account", MessageBoxButton.YesNo, MessageBoxImage.Asterisk) == MessageBoxResult.Yes)
            {
                await ManifestModelService.DeleteSteamGuardAccount(SelectedAccount);
            }
        });

        public ICommand LoginWindowShowCommand => new RelayCommand(o =>
        {
            if (SelectedAccount is null) return;

            ShowLoginWindow(LoginType.Refresh);
        });

        public ICommand ForceRefreshSession => new AsyncRelayCommand(async o =>
        {
            if (await RefreshAccountSession())
            {
                MessageBox.Show("Your session has been refreshed.", "Session refresh", MessageBoxButton.OK, MessageBoxImage.Information);
                await ManifestModelService.SaveManifest();

                return;
            }

            MessageBox.Show("Failed to refresh your session.\nTry using the \"Login again\" option.", "Session refresh", MessageBoxButton.OK, MessageBoxImage.Error);
        });

        public ICommand DeactivateAuthenticator => new RelayCommand(o =>
        {
            if (SelectedAccount is null) return;

            switch (MessageBox.Show("Would you like to remove Steam Guard completely?\nYes - Remove Steam Guard completely.\nNo - Switch back to Email authentication", "Remove Steam Guard"))
            {
                case MessageBoxResult.None:
                    break;
                case MessageBoxResult.OK:
                    break;
                case MessageBoxResult.Cancel:
                    break;
                case MessageBoxResult.Yes:
                    break;
                case MessageBoxResult.No:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            /*int scheme = 0;
            if (res == DialogResult.Yes)
            {
                scheme = 2;
            }
            else if (res == DialogResult.No)
            {
                scheme = 1;
            }
            else if (res == DialogResult.Cancel)
            {
                scheme = 0;
            }

            if (scheme != 0)
            {
                string confCode = currentAccount.GenerateSteamGuardCode();
                InputForm confirmationDialog = new InputForm(String.Format("Removing Steam Guard from {0}. Enter this confirmation code: {1}", currentAccount.AccountName, confCode));
                confirmationDialog.ShowDialog();

                if (confirmationDialog.Canceled)
                {
                    return;
                }

                string enteredCode = confirmationDialog.txtBox.Text.ToUpper();
                if (enteredCode != confCode)
                {
                    MessageBox.Show("Confirmation codes do not match. Steam Guard not removed.");
                    return;
                }

                bool success = currentAccount.DeactivateAuthenticator(scheme);
                if (success)
                {
                    MessageBox.Show(String.Format("Steam Guard {0}. maFile will be deleted after hitting okay. If you need to make a backup, now's the time.", (scheme == 2 ? "removed completely" : "switched to emails")));
                    this.manifest.RemoveAccount(currentAccount);
                    this.loadAccountsList();
                }
                else
                {
                    MessageBox.Show("Steam Guard failed to deactivate.");
                }
            }
            else
            {
                MessageBox.Show("Steam Guard was not removed. No action was taken.");
            }*/
        });

        public ICommand CheckNewVersionCommand => new AsyncRelayCommand(async o =>
        {
            string downloadUrl = string.Empty;
            Version newVersion = new();

            try
            {
                bool result = await Task.Run(() => UpdateService.CheckForUpdate(out downloadUrl, out newVersion));
                if (!result)
                {
                    MessageBox.Show("You are using the latest version");
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
                MessageBox.Show("Failed to check update");
                return;
            }

            if (MessageBox.Show($"Would you like to download new version {newVersion} ?", "Update service", MessageBoxButton.YesNo, MessageBoxImage.Asterisk) != MessageBoxResult.Yes)
                return;

            try
            {
                if (await UpdateService.DownloadAndInstall(downloadUrl, newVersion, "SteamDesktopAuthenticatorCore") is not { } newFile)
                    throw new ArgumentNullException();

                Application.Current.Shutdown(0);
                Process.Start(newFile);
            }
            catch
            {
                MessageBox.Show("Failed to download update");
            }
        });

        #endregion

        #region PrivateMethods

        private void LoadAccountInfo()
        {
            if (SelectedAccount is null || _steamTime == 0) return;

            if (SelectedAccount.GenerateSteamGuardCodeForTime(_steamTime) is not { } token)
                return;

            LoginTokenText = token;
        }

        private async void SteamGuardTimerOnTick(object? sender, EventArgs e)
        {
            StatusText = "Aligning time with Steam...";
            _steamTime = await TimeAligner.GetSteamTimeAsync();
            StatusText = string.Empty;

            _currentSteamChunk = _steamTime / 30L;
            int secondsUntilChange = (int)(_steamTime - (_currentSteamChunk * 30L));

            LoadAccountInfo();
            if (SelectedAccount is not null)
            {
                ProgressBar = 30 - secondsUntilChange;
            }
        }

        private async Task AutoTradeConfirmationTimerOnTick()
        {
            List<ConfirmationModel> confs = new();
            Dictionary<SteamGuardAccount, List<ConfirmationModel>> autoAcceptConfirmations = new();
            SteamGuardAccount[] accs = Manifest.Accounts.ToArray();

            StatusText = "Checking confirmations...";

            foreach (var acc in accs)
            {
                try
                {
                    ConfirmationModel[] tmp = await acc.FetchConfirmationsAsync();
                    foreach (var conf in tmp)
                    {
                        if ((conf.ConfType == ConfirmationModel.ConfirmationType.MarketSellTransaction && Manifest.AutoConfirmMarketTransactions) ||
                            (conf.ConfType == ConfirmationModel.ConfirmationType.Trade && Manifest.AutoConfirmTrades))
                        {
                            if (!autoAcceptConfirmations.ContainsKey(acc))
                                autoAcceptConfirmations[acc] = new List<ConfirmationModel>();
                            autoAcceptConfirmations[acc].Add(conf);
                        }
                        else
                            confs.Add(conf);
                    }
                }
                catch (SteamGuardAccount.WgTokenInvalidException)
                {
                    StatusText = "Refreshing session";
                    await acc.RefreshSessionAsync(); //Don't save it to the HDD, of course. We'd need their encryption passkey again.
                    StatusText = "";
                }
                catch (SteamGuardAccount.WgTokenExpiredException)
                {
                    //Prompt to relogin
                    ShowLoginWindow(LoginType.Refresh);
                    break; //Don't bombard a user with login refresh requests if they have multiple accounts. Give them a few seconds to disable the autocheck option if they want.

                }
                catch (WebException)
                {

                }
            }

            foreach (var acc in autoAcceptConfirmations.Keys)
            {
                var confirmations = autoAcceptConfirmations[acc].ToArray();
                acc.AcceptMultipleConfirmations(confirmations);
            }
        }

        private async Task<bool> RefreshAccountSession(bool attemptRefreshLogin = true)
        {
            if (SelectedAccount is null) return false;

            try
            {
                bool refreshed = await SelectedAccount.RefreshSessionAsync();
                return refreshed; //No exception thrown means that we either successfully refreshed the session or there was a different issue preventing us from doing so.
            }
            catch (SteamGuardAccount.WgTokenExpiredException)
            {
                if (!attemptRefreshLogin) return false;

                ShowLoginWindow(LoginType.Refresh);

                return await RefreshAccountSession(false);
            }
        }
        private void ShowLoginWindow(LoginType type)
        {
            LoginWindowView window = new();
            var dataContext = (window.DataContext as LoginWindowViewModel)!;
            dataContext.Account = SelectedAccount; //-V3149
            dataContext.LoginType = type;

            window.ShowDialog();
        }

        #endregion
    }
}