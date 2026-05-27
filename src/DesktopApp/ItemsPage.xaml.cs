// -----------------------------------------------------------------------
// <copyright file="ItemsPage.xaml.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.DesktopApp
{
    using System;
    using Microsoft.UI.Xaml;
    using Microsoft.UI.Xaml.Controls;
    using VerifoneCommander.PriceBookManager.DesktopApp.ViewModels;

    public sealed partial class ItemsPage : Page
    {
        public ItemsPage()
        {
            this.InitializeComponent();
        }

        public ItemsPageVm ViewModel { get; } = App.ViewModelResolver.Resolve<ItemsPageVm>();

        private async void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            var count = this.ViewModel.SelectedCount;
            if (count == 0)
            {
                return;
            }

            var target = App.StartupUseMocks ? "MOCK data (no controller)" : "the LIVE POS controller";
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "Delete selected items?",
                Content = $"This permanently deletes {count} item(s) from {target}. " +
                          "This cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };

            // IAsyncOperation has no ConfigureAwait; awaiting it resumes on the UI thread.
            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                this.ViewModel.DeleteSelectedCommand.Execute(null);
            }
        }
    }
}
