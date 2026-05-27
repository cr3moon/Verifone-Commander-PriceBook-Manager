// -----------------------------------------------------------------------
// <copyright file="AccountPageVm.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.DesktopApp.ViewModels
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows.Input;
    using CommunityToolkit.Mvvm.ComponentModel;
    using CommunityToolkit.Mvvm.Input;
    using CommunityToolkit.Mvvm.Messaging;
    using Microsoft.Extensions.Logging;
    using VerifoneCommander.PriceBookManager.Core;
    using VerifoneCommander.PriceBookManager.DesktopApp.Models;
    using VerifoneCommander.PriceBookManager.DesktopApp.ViewModels.Models;

    public partial class AccountPageVm : PageVm, IRecipient<ModeChangedMessage>
    {
        private readonly Settings settings;
        private readonly IModifiableSapphireCredentialsProvider credentialProvider;
        private readonly ICachingSapphireClient sapphireClient;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsLoggingIn))]
        [NotifyPropertyChangedFor(nameof(IsLoggedIn))]
        [NotifyPropertyChangedFor(nameof(IsLoggedOut))]
        private LoginState loginState = LoginState.LoggedOut;

        [ObservableProperty]
        private ValidatedTextVm username = new ValidatedTextVm(
            v =>
            {
                if (string.IsNullOrWhiteSpace(v))
                {
                    return "Empty username is not allowed";
                }

                return string.Empty;
            });

        [ObservableProperty]
        private ValidatedTextVm password = new ValidatedTextVm(
            v =>
            {
                if (string.IsNullOrWhiteSpace(v))
                {
                    return "Empty password is not allowed";
                }

                return string.Empty;
            });

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasLoginError))]
        private string loginError;

        public AccountPageVm(
            IUiThreadDispatcher uiThreadDispatcher,
            IMessenger messenger,
            ILogger logger,
            Settings settings,
            IModifiableSapphireCredentialsProvider credentialProvider,
            ICachingSapphireClient sapphireClient)
            : base(uiThreadDispatcher, messenger, logger)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.credentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
            this.sapphireClient = sapphireClient ?? throw new ArgumentNullException(nameof(sapphireClient));

            this.Username.Text = this.settings.Username;

            this.LoginCommand = new AsyncRelayCommand(this.LoginAsync);
            this.LogoutCommand = new RelayCommand(this.Logout);

            this.Messenger.Register<ModeChangedMessage>(this);
        }

        public override string Name => "Account";

        // Contact
        public override int SymbolCode => 0xE77B;

        public ICommand LoginCommand { get; }

        public ICommand LogoutCommand { get; }

        public bool IsLoggingIn => this.LoginState == LoginState.LoggingIn;

        public bool IsLoggedIn => this.LoginState == LoginState.LoggedIn;

        public bool IsLoggedOut => this.LoginState == LoginState.LoggedOut;

        public bool HasLoginError => !string.IsNullOrEmpty(this.LoginError);

        /// <summary>
        /// In mock mode the login screen is just a gate over canned data, so log in
        /// automatically (credentials are ignored). Called when the Account page is
        /// shown — at startup and after a mock/live switch.
        /// </summary>
        public Task EnsureMockSessionAsync()
        {
            if (!this.settings.UseMocks || this.LoginState != LoginState.LoggedOut)
            {
                return Task.CompletedTask;
            }

            return this.PerformLoginAsync("mock", "mock", saveUsername: false, default);
        }

        void IRecipient<ModeChangedMessage>.Receive(ModeChangedMessage message)
        {
            // The mock/live setting changed: end the session so the newly selected
            // backend is used cleanly. Logout resets all pages; the user logs back in
            // (mock auto-logs in via EnsureMockSessionAsync when the page reappears).
            this.Logout();
        }

        private async Task LoginAsync(
            CancellationToken cancellationToken)
        {
            if (this.LoginState != LoginState.LoggedOut)
            {
                // Do nothing
                return;
            }

            if (this.Username.HasError_Revalidate() ||
                this.Password.HasError_Revalidate())
            {
                return;
            }

            await this.PerformLoginAsync(
                this.Username.Text,
                this.Password.Text,
                saveUsername: true,
                cancellationToken).ConfigureAwait(false);
        }

        private async Task PerformLoginAsync(
            string username,
            string password,
            bool saveUsername,
            CancellationToken cancellationToken)
        {
            if (this.LoginState != LoginState.LoggedOut)
            {
                return;
            }

            this.credentialProvider.SetLoginCredentials(
                this.settings.Hostname,
                username,
                password);

            await this.DispatchOnUiThreadAsync(() =>
            {
                this.LoginError = string.Empty;
                this.LoginState = LoginState.LoggingIn;
            }).ConfigureAwait(false);

            try
            {
                // Execute an operation that would indicate if login was successful
                await this.sapphireClient.RefreshCacheAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await this.DispatchOnUiThreadAsync(() =>
                {
                    this.LoginError = ex.Message;
                    this.LoginState = LoginState.LoggedOut;
                }).ConfigureAwait(false);

                return;
            }

            await this.DispatchOnUiThreadAsync(() =>
            {
                this.LoginError = string.Empty;
                this.LoginState = LoginState.LoggedIn;

                if (saveUsername)
                {
                    // Save the current username since it was valid for login
                    this.settings.Username = username;
                }
            }).ConfigureAwait(false);
        }

        private void Logout()
        {
            this.LoginState = LoginState.LoggedOut;
        }

        partial void OnLoginStateChanged(LoginState value)
        {
            if (value == LoginState.LoggedIn || value == LoginState.LoggedOut)
            {
                this.Messenger.Send(new LoginStateChangedMessage(value));
            }
        }
    }
}
