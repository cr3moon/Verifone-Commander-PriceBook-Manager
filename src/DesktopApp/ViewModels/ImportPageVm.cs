// -----------------------------------------------------------------------
// <copyright file="ImportPageVm.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.DesktopApp.ViewModels
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using CommunityToolkit.Mvvm.ComponentModel;
    using CommunityToolkit.Mvvm.Input;
    using CommunityToolkit.Mvvm.Messaging;
    using Microsoft.Extensions.Logging;
    using VerifoneCommander.PriceBookManager.Core.Import;
    using VerifoneCommander.PriceBookManager.Core.Models;
    using VerifoneCommander.PriceBookManager.DesktopApp.Models;

    public partial class ImportPageVm : PageVm
    {
        private readonly ICachingSapphireClient sapphireClient;

        private ImportResult lastResult;

        [ObservableProperty]
        private ObservableCollection<ImportRowVm> rows = new ObservableCollection<ImportRowVm>();

        [ObservableProperty]
        private string fileName;

        [ObservableProperty]
        private string fileError;

        [ObservableProperty]
        private bool hasResult;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
        private bool isBusy;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
        private int importableCount;

        [ObservableProperty]
        private int warningCount;

        [ObservableProperty]
        private int errorCount;

        public ImportPageVm(
            IUiThreadDispatcher uiThreadDispatcher,
            IMessenger messenger,
            ILogger logger,
            ICachingSapphireClient sapphireClient)
            : base(uiThreadDispatcher, messenger, logger)
        {
            this.sapphireClient = sapphireClient ?? throw new ArgumentNullException(nameof(sapphireClient));

            this.ImportCommand = new AsyncRelayCommand(
                this.ImportAsync,
                () => this.ImportableCount > 0 && !this.IsBusy);
        }

        public override string Name => "Import";

        // Import
        public override int SymbolCode => 0xE8B5;

        public IRelayCommand ImportCommand { get; }

        public bool CanImport => this.ImportableCount > 0 && !this.IsBusy;

        public string ImportButtonText => $"Import {this.ImportableCount} valid row(s)";

        public async Task LoadCsvAsync(string sourceName, string content)
        {
            await this.DispatchOnUiThreadAsync(() =>
            {
                this.IsBusy = true;
                this.FileName = sourceName;
                this.FileError = null;
            }).ConfigureAwait(false);

            //// For thread safety: No access to bindable object properties beyond this point.

            ImportResult result;
            try
            {
                var departments = await this.sapphireClient.GetDepartmentsAsync(default).ConfigureAwait(false);

                // Case-insensitive: a human-authored CSV may not match the controller's
                // department-name casing exactly.
                var departmentsByName = departments
                    .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                var csvRows = CsvReader.Parse(content);
                result = PluImportParser.Parse(csvRows, departmentsByName);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Failed to parse the import file");

                await this.DispatchOnUiThreadAsync(() =>
                {
                    this.IsBusy = false;
                    this.HasResult = false;
                    this.SetInfoBar(InfoBarSeverity.Error, $"Could not read the file: {ex.Message}");
                }).ConfigureAwait(false);

                return;
            }

            var rowVms = result.Rows
                .Select(r => new ImportRowVm
                {
                    LineNumber = r.LineNumber,
                    Upc = r.Upc,
                    Modifier = r.Modifier,
                    Description = r.Description,
                    DepartmentName = r.DepartmentName,
                    Price = r.Price,
                    Status = r.Status.ToString(),
                    Message = r.Message,
                })
                .ToList();

            await this.DispatchOnUiThreadAsync(() =>
            {
                this.lastResult = result;
                this.Rows = new ObservableCollection<ImportRowVm>(rowVms);
                this.ImportableCount = result.ImportableCount;
                this.WarningCount = result.WarningCount;
                this.ErrorCount = result.ErrorCount;
                this.HasResult = true;
                this.IsBusy = false;

                if (result.HasFileErrors)
                {
                    this.FileError = string.Join(" ", result.FileErrors);
                    this.SetInfoBar(InfoBarSeverity.Error, this.FileError);
                }
                else
                {
                    this.SetInfoBar(
                        InfoBarSeverity.Informational,
                        $"{result.Rows.Count} row(s): {result.ImportableCount} importable, {result.WarningCount} with warnings, {result.ErrorCount} error(s).");
                }
            }).ConfigureAwait(false);
        }

        private async Task ImportAsync(CancellationToken cancellationToken)
        {
            var importable = this.lastResult?.Rows
                .Where(r => r.Status != ImportRowStatus.Error && r.Plu != null)
                .ToList() ?? new List<ImportRow>();

            if (importable.Count == 0)
            {
                return;
            }

            await this.DispatchOnUiThreadAsync(() => this.IsBusy = true).ConfigureAwait(false);

            //// For thread safety: No access to bindable object properties beyond this point.

            var successCount = 0;
            var failCount = 0;

            foreach (var row in importable)
            {
                try
                {
                    await this.sapphireClient.UpdatePriceLookUpAsync(row.Plu, cancellationToken).ConfigureAwait(false);
                    successCount++;
                }
                catch (Exception ex)
                {
                    this.Logger.LogError(ex, "Failed to import UPC {Upc} modifier {Modifier}", row.Plu.Ean13, row.Plu.Modifier);
                    failCount++;
                }

                var done = successCount + failCount;
                await this.DispatchOnUiThreadAsync(() =>
                    this.SetInfoBar(InfoBarSeverity.Informational, $"Importing… {done}/{importable.Count}")).ConfigureAwait(false);
            }

            var localSuccess = successCount;
            var localFail = failCount;

            await this.DispatchOnUiThreadAsync(() =>
            {
                this.IsBusy = false;

                // Clear the preview so the same file cannot be imported twice by accident.
                this.lastResult = null;
                this.Rows = new ObservableCollection<ImportRowVm>();
                this.ImportableCount = 0;
                this.WarningCount = 0;
                this.ErrorCount = 0;
                this.HasResult = false;

                if (localFail == 0)
                {
                    var target = App.StartupUseMocks ? "mock data" : "the live POS";
                    this.SetInfoBar(InfoBarSeverity.Success, $"Imported {localSuccess} item(s) to {target}.");
                }
                else
                {
                    this.SetInfoBar(
                        InfoBarSeverity.Warning,
                        $"Imported {localSuccess} item(s); {localFail} failed (see log).");
                }
            }).ConfigureAwait(false);
        }

        partial void OnIsBusyChanged(bool value) => this.OnPropertyChanged(nameof(this.CanImport));

        partial void OnImportableCountChanged(int value)
        {
            this.OnPropertyChanged(nameof(this.CanImport));
            this.OnPropertyChanged(nameof(this.ImportButtonText));
        }
    }

#pragma warning disable SA1402 // File may only contain a single type

    public sealed class ImportRowVm
    {
        public int LineNumber { get; set; }

        public string Upc { get; set; }

        public string Modifier { get; set; }

        public string Description { get; set; }

        public string DepartmentName { get; set; }

        public string Price { get; set; }

        public string Status { get; set; }

        public string Message { get; set; }
    }
}
