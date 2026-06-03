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
    using Microsoft.UI.Xaml.Input;
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

            var target = App.IsMockMode ? "MOCK data (no controller)" : "the LIVE POS controller";
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

        private void ItemRow_Tapped(object sender, TappedRoutedEventArgs e)
        {
            // Only act when the duplicates toggle is on; otherwise let taps pass through
            // (so background-click dismissal still works for the rest of the UI).
            if (!this.ViewModel.ShowPossibleDuplicates)
            {
                return;
            }

            // ItemsRepeater doesn't propagate DataContext to realized children the way
            // ListView does, so the row identity comes from Tag (bound via x:Bind).
            if (sender is FrameworkElement el && el.Tag is ItemRowVm row)
            {
                // Selecting a row with duplicates reveals the borders on its matches and
                // opens the panel; a row with no duplicates clears any prior selection.
                var hasDuplicates = row.PossibleDuplicates != null && row.PossibleDuplicates.Count > 0;
                this.ViewModel.SelectedDuplicateRow = hasDuplicates ? row : null;
                e.Handled = true;
            }
        }

        private void DuplicatePanel_Tapped(object sender, TappedRoutedEventArgs e)
        {
            // Clicking inside the detail panel must not dismiss it.
            e.Handled = true;
        }

        private void PageBackground_Tapped(object sender, TappedRoutedEventArgs e)
        {
            // Any unhandled tap (i.e. blank space, filter bar, header, summary) clears
            // the duplicate selection and hides the side panel.
            this.ViewModel.SelectedDuplicateRow = null;
        }
    }
}
