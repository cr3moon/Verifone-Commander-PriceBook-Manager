// -----------------------------------------------------------------------
// <copyright file="AccountPage.xaml.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.DesktopApp
{
    using Microsoft.UI.Xaml;
    using Microsoft.UI.Xaml.Controls;
    using VerifoneCommander.PriceBookManager.DesktopApp.ViewModels;

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
    }
}
