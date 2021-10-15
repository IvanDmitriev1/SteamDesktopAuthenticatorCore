﻿using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using GoogleDrive;
using Newtonsoft.Json;
using SteamAuthCore.Models;
using GoogleFile = Google.Apis.Drive.v3.Data.File;

namespace SteamDesktopAuthenticatorCore.Services
{
    public static partial class ManifestModelService
    {
        public static GoogleDriveApi? Api { get; set; }

        public static async Task<ManifestModel> GetManifestFromGoogleDrive()
        {
            if (_manifest is not null)
                return _manifest;

            if (Api is null)
                throw new ArgumentNullException(nameof(Api));

            if (await Api.CheckForFile(ManifestFileName) is not { } manifestFile)
            {
                await CreateNewManifestInGoogleDrive();
                return _manifest!;
            }

            if (JsonConvert.DeserializeObject<ManifestModel>(await Api.DownloadFileAsString(manifestFile.Id)) is not { } manifest)
                throw new ArgumentNullException(nameof(manifest));

            _manifest = manifest;

            await GetAccountsInGoogleDrive();
            return _manifest;
        }

        public static async Task SaveManifestInGoogleFile()
        {
            if (Api is null || _manifest is null)
                throw new ArgumentNullException();

            ManifestModel newModel = new(_manifest);

            string serialized = JsonConvert.SerializeObject(newModel);
            await using MemoryStream stream = new(Encoding.UTF8.GetBytes(serialized));
            await Api.UploadFile(ManifestFileName, stream);
        }

        public static async Task AddSteamGuardAccountInGoogleDrive(string fileName, string fileData)
        {
            if (Api is null || _manifest is null)
                throw new ArgumentNullException();

            await using MemoryStream memoryStream = new(Encoding.UTF8.GetBytes(fileData));
            await Api.UploadFile(fileName, memoryStream);
        }

        public static async Task GetAccountsInGoogleDrive()
        {
            if (Api is null || _manifest is null)
                throw new ArgumentNullException();

            if (await Api.GetFiles() is not { } files)
            {
                files = Array.Empty<GoogleFile>();
            }

            _manifest.Accounts.Clear();
            foreach (var file in files)
            {
                if (!file.Name.Contains(".maFile")) continue;
                
                if (JsonConvert.DeserializeObject<SteamGuardAccount>(await Api.DownloadFileAsString(file.Id)) is not { } account)
                    throw new ArgumentNullException(nameof(account));

                _manifest.Accounts.Add(account);
            }
        }

        public static async Task DeleteSteamGuardAccountInGoogleDrive(SteamGuardAccount account)
        {
            if (_manifest is null || Api is null)
                throw new ArgumentNullException();

            _manifest.Accounts.Remove(account);

            if (await FindMaFileInGoogleDrive(account) is { } file)
            {
                await Api.DeleteFile(file.Id);
            }
        }

        public static async Task SaveAccountInGoogleDrive(SteamGuardAccount account)
        {
            string serialized = JsonConvert.SerializeObject(account);

            if (await FindMaFileInGoogleDrive(account) is { } file)
            {
                await Api!.UploadFile(file, new MemoryStream(Encoding.UTF8.GetBytes(serialized)));
            }
        }

        #region PrivateFields

        private static async Task CreateNewManifestInGoogleDrive()
        {
            _manifest = new ManifestModel(true);
            await SaveManifestInGoogleFile();
        }

        private static async Task<GoogleFile?> FindMaFileInGoogleDrive(SteamGuardAccount account)
        {
            if (await Api!.GetFiles() is not { } files) return null;

            foreach (var file in files)
            {
                if (!file.Name.Contains(".maFile")) continue;

                if (JsonConvert.DeserializeObject<SteamGuardAccount>(await Api.DownloadFileAsString(file.Id)) is not { } account2)
                    throw new ArgumentNullException(nameof(account2));

                if (account.Secret1 == account2.Secret1 && account.IdentitySecret == account2.IdentitySecret)
                    return file;
            }

            return null;
        }

        #endregion
    }
}