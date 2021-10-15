﻿using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SteamAuthCore.Models;
using SteamDesktopAuthenticatorCore.Services;
using SteamDesktopAuthenticatorCore.Views;
using WpfHelper;
using WpfHelper.Commands;
using WpfHelper.Custom;

namespace SteamDesktopAuthenticatorCore.ViewModels
{
    class WelcomeWindowViewModel : BaseViewModel
    {
        private Window _thisWindow = null!;

        #region Commands

        public ICommand WindowOnLoadedCommand => new RelayCommand( o =>
        {
            if (o is not RoutedEventArgs {Source: Window window}) return;

            _thisWindow = window;
        });

        public ICommand JustRunButtonOnClick => new AsyncRelayCommand(async o =>
        {
            // Mark as not first run anymore
            ManifestModel manifest = await ManifestModelService.GetManifest();
            manifest.FirstRun = false;

            await ShowMainWindow();
        });

        public ICommand ImportConfigButtonOnClickCommand => new AsyncRelayCommand(async o =>
        {
            OpenFileDialog folderBrowser = new OpenFileDialog();
            folderBrowser.ValidateNames = false;
            folderBrowser.CheckFileExists = false;
            folderBrowser.CheckPathExists = true;
            folderBrowser.FileName = "Folder Selection.";
            folderBrowser.ShowReadOnly = true;
            folderBrowser.Title = "Select maFiles directory";

            if (folderBrowser.ShowDialog() != true) return;

            if (Path.GetDirectoryName(folderBrowser.FileName) is not { } directoryPath)
                return;

            foreach (var file in Directory.GetFiles(directoryPath))
            {
                await CopySteamGuardAccounts(file);
                await CopyManifest(file);
            }

            await ShowMainWindow();
        });

        #endregion

        private static async Task CopySteamGuardAccounts(string filePath)
        {
            string fileName = Path.GetFileName(filePath);

            if (!filePath.Contains(".maFile"))
                return;

            try
            {
                await using FileStream fileStream = new(filePath, FileMode.Open);
                using StreamReader reader = new(fileStream);
                await ManifestModelService.AddSteamGuardAccountInDrive(fileName, await reader.ReadToEndAsync());
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
                MessageBox.Show($"Your {fileName} is corrupted ");
            }
        }

        private static async Task CopyManifest(string file)
        {
            try
            {
                await ManifestModelService.CopyManifest(file);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
                CustomMessageBox.Show($"Your manifest file is corrupted");
            }
        }

        private async Task ShowMainWindow()
        {
            await ManifestModelService.SaveManifest();
            await ManifestModelService.GetAccounts();

            MainWindowView mainWindow = new();
            mainWindow.Show();

            _thisWindow.Close();
        }
    }
}