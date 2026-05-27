// -----------------------------------------------------------------------
// <copyright file="App.xaml.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.DesktopApp
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using CommunityToolkit.Mvvm.Messaging;
    using Microsoft.Extensions.Logging;
    using Microsoft.UI.Dispatching;
    using Microsoft.UI.Xaml;
    using Newtonsoft.Json;
    using VerifoneCommander.PriceBookManager.Core;
    using VerifoneCommander.PriceBookManager.DesktopApp.Mocks;
    using VerifoneCommander.PriceBookManager.DesktopApp.Models;
    using VerifoneCommander.PriceBookManager.DesktopApp.ViewModels;

    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private static Settings activeSettings;

        private readonly Settings settings = new Settings();

        private Window window;
        private ILoggerFactory loggerFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="App"/> class.
        /// </summary>
        /// <remarks>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </remarks>
        public App()
        {
            this.InitializeComponent();

            this.LoadSettings();

#pragma warning disable CA2000 // Dispose objects before losing scope
            this.loggerFactory = new LoggerFactory()
                .AddFile(App.AppDataFolderPath + "\\" + "log-{Date}.txt");
#pragma warning restore CA2000 // Dispose objects before losing scope

            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            var uiThreadDispatcher = new UiThreadDispatcher(dispatcherQueue);
            var messenger = WeakReferenceMessenger.Default;
            var logger = this.loggerFactory.CreateLogger<App>();

            // Build BOTH the mock and the live graphs and route between them at call
            // time via a switchable wrapper, so the mock/live setting can be changed
            // without restarting. The active mode is read fresh from Settings on each
            // call; toggling the setting forces a clean re-login (see SettingsPageVm /
            // AccountPageVm) so the cache is repopulated from the selected backend.
            App.activeSettings = this.settings;

            var mockCredentialsProvider = new MockCredentialProvider();
            var mockSapphireClient = new MockSapphireClient();

#pragma warning disable CA2000 // Dispose objects before losing scope (lives for the app lifetime)
            var httpRequestSender = new HttpClientHttpRequestSender(() => this.settings.AllowUntrustedCertificates);
#pragma warning restore CA2000 // Dispose objects before losing scope

            var liveCredentialsProvider = new SapphireCredentialProvider(
                httpRequestSender,
                this.loggerFactory.CreateLogger<SapphireCredentialProvider>());

            var liveSapphireClient = new SapphireClient(
                httpRequestSender,
                liveCredentialsProvider,
                this.loggerFactory.CreateLogger<SapphireClient>());

            IModifiableSapphireCredentialsProvider credentialsProvider = new SwitchableSapphireCredentialsProvider(
                () => this.settings.UseMocks,
                mockCredentialsProvider,
                liveCredentialsProvider);

            ISapphireClient sapphireClient = new SwitchableSapphireClient(
                () => this.settings.UseMocks,
                mockSapphireClient,
                liveSapphireClient);

            var cachingSapphireClient = new CachingSapphireClient(sapphireClient);
            var fileSystem = new FileSystem(this.loggerFactory.CreateLogger<FileSystem>());

            var mainNavigationVm = new MainNavigationVm(
                uiThreadDispatcher,
                messenger,
                logger,
                this.settings,
                credentialsProvider,
                cachingSapphireClient,
                fileSystem);

            ViewModelResolver = new AppViewModelResolver(mainNavigationVm);
        }

        public static IAppViewModelResolver ViewModelResolver { get; private set; }

        public static Window MainWindow { get; private set; }

        /// <summary>Gets a value indicating whether the app is currently using mock data (reads the live setting).</summary>
        public static bool IsMockMode => activeSettings?.UseMocks ?? false;

        public static string AppDataFolderPath => Windows.Storage.ApplicationData.Current.LocalFolder.Path;

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            this.window = new MainWindow();
            this.window.Closed += this.Window_Closed;
            MainWindow = this.window;

            this.window.Activate();
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            try
            {
                this.SaveSettings();
                this.loggerFactory.Dispose();
            }
            catch (Exception)
            {
            }
        }

        private void LoadSettings()
        {
            var settingsFilePath = Path.Combine(App.AppDataFolderPath, "settings.json");

            if (!File.Exists(settingsFilePath))
            {
                return;
            }

            Settings parsed;
            try
            {
                parsed = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(settingsFilePath));
            }
            catch (Exception)
            {
                // Corrupt or unreadable settings file — fall back to defaults rather
                // than crashing at startup. (Logging isn't available this early.)
                return;
            }

            if (parsed == null)
            {
                return;
            }

            this.settings.UseMocks = parsed.UseMocks;
            this.settings.Hostname = parsed.Hostname;
            this.settings.Username = parsed.Username;
            this.settings.AllowUntrustedCertificates = parsed.AllowUntrustedCertificates;
        }

        private void SaveSettings()
        {
            var settingsFilePath = Path.Combine(App.AppDataFolderPath, "settings.json");
            var serialized = JsonConvert.SerializeObject(this.settings, Formatting.Indented);

            File.WriteAllText(settingsFilePath, serialized);
        }

        private class UiThreadDispatcher : IUiThreadDispatcher
        {
            private readonly DispatcherQueue dispatcherQueue;

            public UiThreadDispatcher(
                DispatcherQueue dispatcherQueue)
            {
                this.dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
            }

            public Task DispatchAsync(Action action)
            {
                var tcs = new TaskCompletionSource();
                if (!this.dispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        action();
                        tcs.SetResult();
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                }))
                {
                    tcs.SetException(new InvalidOperationException("Failed to enqueue the operation on the DispatcherQueue."));
                }

                return tcs.Task;
            }
        }
    }
}
