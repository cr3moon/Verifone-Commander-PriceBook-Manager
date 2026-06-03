// -----------------------------------------------------------------------
// <copyright file="AccountPage.xaml.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.DesktopApp
{
    using Microsoft.UI.Xaml;
    using Microsoft.UI.Xaml.Controls;
    using Microsoft.UI.Xaml.Input;
    using VerifoneCommander.PriceBookManager.DesktopApp.ViewModels;
    using Windows.System;

    public sealed partial class AccountPage : Page
    {
        public AccountPage()
        {
            this.InitializeComponent();
            this.Loaded += this.OnLoaded;
        }

        public AccountPageVm ViewModel { get; } = App.ViewModelResolver.Resolve<AccountPageVm>();

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // In mock mode this auto-logs-in so the connection screen isn't a dead end.
            await this.ViewModel.EnsureMockSessionAsync().ConfigureAwait(false);
        }

        private void LoginInput_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            // Enter in either credential field submits the login, same as clicking the
            // button. Only while logged out (the button is hidden otherwise) and when the
            // command can run.
            if (e.Key == VirtualKey.Enter &&
                this.ViewModel.IsLoggedOut &&
                this.ViewModel.LoginCommand.CanExecute(null))
            {
                this.ViewModel.LoginCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
