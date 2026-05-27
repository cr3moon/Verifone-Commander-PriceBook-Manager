// -----------------------------------------------------------------------
// <copyright file="ImportPage.xaml.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.DesktopApp
{
    using System;
    using Microsoft.UI.Xaml;
    using Microsoft.UI.Xaml.Controls;
    using VerifoneCommander.PriceBookManager.DesktopApp.ViewModels;
    using Windows.Storage;
    using Windows.Storage.Pickers;

    public sealed partial class ImportPage : Page
    {
        public ImportPage()
        {
            this.InitializeComponent();
        }

        public ImportPageVm ViewModel { get; } = App.ViewModelResolver.Resolve<ImportPageVm>();

        private async void ChooseFileButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();

            // A picker in a packaged desktop app must be associated with the window's HWND.
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".csv");
            picker.FileTypeFilter.Add(".txt");

            var file = await picker.PickSingleFileAsync();
            if (file == null)
            {
                return;
            }

            var content = await FileIO.ReadTextAsync(file);
            await this.ViewModel.LoadCsvAsync(file.Name, content).ConfigureAwait(false);
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var count = this.ViewModel.ImportableCount;
            if (count == 0)
            {
                return;
            }

            var target = App.StartupUseMocks ? "MOCK data (no controller)" : "the LIVE POS controller";
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "Import these items?",
                Content = $"This writes {count} item(s) to {target}, " +
                          "creating or overwriting each PLU. Continue?",
                PrimaryButtonText = "Import",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                this.ViewModel.ImportCommand.Execute(null);
            }
        }
    }
}
