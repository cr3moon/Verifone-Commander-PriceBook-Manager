// -----------------------------------------------------------------------
// <copyright file="ItemsPageVm.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.DesktopApp.ViewModels
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows.Input;
    using CommunityToolkit.Mvvm.ComponentModel;
    using CommunityToolkit.Mvvm.Input;
    using CommunityToolkit.Mvvm.Messaging;
    using Microsoft.Extensions.Logging;
    using VerifoneCommander.PriceBookManager.Core.Models;
    using VerifoneCommander.PriceBookManager.DesktopApp.Models;
    using VerifoneCommander.PriceBookManager.DesktopApp.ViewModels.Models;

    public partial class ItemsPageVm : PageVm, IRecipient<LoginStateChangedMessage>
    {
        private const string AllDepartmentsOption = "All Departments";

        private readonly ICachingSapphireClient sapphireClient;

        private List<ItemRowVm> allItems = new List<ItemRowVm>();
        private bool suppressFilter;
        private bool suppressSelectionRecount;

        // Bumped whenever a load starts or the view is cleared (logout). A load only
        // applies its results if its captured generation is still the latest, so an
        // out-of-order load can't overwrite newer state (e.g. resurrect rows after a
        // delete, or repopulate after logout).
        private int loadGeneration;

        [ObservableProperty]
        private ObservableCollection<ItemRowVm> items = new ObservableCollection<ItemRowVm>();

        [ObservableProperty]
        private ObservableCollection<string> departmentNames = new ObservableCollection<string>();

        [ObservableProperty]
        private string selectedDepartmentName = AllDepartmentsOption;

        [ObservableProperty]
        private string filterText;

        [ObservableProperty]
        private bool foodStampsOnly;

        [ObservableProperty]
        private bool ageRestrictedOnly;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
        [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
        private bool isBusy;

        [ObservableProperty]
        private int totalCount;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
        private int selectedCount;

        public ItemsPageVm(
            IUiThreadDispatcher uiThreadDispatcher,
            IMessenger messenger,
            ILogger logger,
            ICachingSapphireClient sapphireClient)
            : base(uiThreadDispatcher, messenger, logger)
        {
            this.sapphireClient = sapphireClient ?? throw new ArgumentNullException(nameof(sapphireClient));

            this.RefreshCommand = new AsyncRelayCommand(this.RefreshAsync, () => !this.IsBusy);
            this.SelectAllCommand = new RelayCommand(this.SelectAllFiltered);
            this.ClearSelectionCommand = new RelayCommand(this.ClearSelection);
            this.DeleteSelectedCommand = new AsyncRelayCommand(
                this.DeleteSelectedAsync,
                () => this.SelectedCount > 0 && !this.IsBusy);

            this.Messenger.Register<LoginStateChangedMessage>(this);
        }

        public override string Name => "Items";

        // ViewAll
        public override int SymbolCode => 0xE8A9;

        public IRelayCommand RefreshCommand { get; }

        public IRelayCommand SelectAllCommand { get; }

        public IRelayCommand ClearSelectionCommand { get; }

        public IRelayCommand DeleteSelectedCommand { get; }

        public bool CanDelete => this.SelectedCount > 0 && !this.IsBusy;

        public string DeleteButtonText => this.SelectedCount > 0
            ? $"Delete selected ({this.SelectedCount})"
            : "Delete selected";

        public string Summary => this.IsBusy
            ? "Loading…"
            : $"Showing {this.Items.Count} of {this.TotalCount} items";

        void IRecipient<LoginStateChangedMessage>.Receive(LoginStateChangedMessage message)
        {
            if (message.State == LoginState.LoggedIn)
            {
                // The cache was already refreshed during login, so read straight from it.
                Task.Run(async () => await this.LoadFromCacheAsync(default).ConfigureAwait(false));
            }
            else if (message.State == LoginState.LoggedOut)
            {
                Task.Run(async () => await this.DispatchOnUiThreadAsync(this.Clear).ConfigureAwait(false));
            }
        }

        partial void OnFilterTextChanged(string value) => this.ApplyFilter();

        partial void OnSelectedDepartmentNameChanged(string value) => this.ApplyFilter();

        partial void OnFoodStampsOnlyChanged(bool value) => this.ApplyFilter();

        partial void OnAgeRestrictedOnlyChanged(bool value) => this.ApplyFilter();

        partial void OnIsBusyChanged(bool value)
        {
            this.OnPropertyChanged(nameof(this.Summary));
            this.OnPropertyChanged(nameof(this.CanDelete));
        }

        partial void OnTotalCountChanged(int value) => this.OnPropertyChanged(nameof(this.Summary));

        partial void OnSelectedCountChanged(int value)
        {
            this.OnPropertyChanged(nameof(this.CanDelete));
            this.OnPropertyChanged(nameof(this.DeleteButtonText));
        }

        private async Task RefreshAsync(CancellationToken cancellationToken)
        {
            // Re-pull the live catalog from the controller, then rebuild from cache.
            try
            {
                await this.DispatchOnUiThreadAsync(() => this.IsBusy = true).ConfigureAwait(false);
                await this.sapphireClient.RefreshCacheAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex, "Failed to refresh the catalog");

                await this.DispatchOnUiThreadAsync(() =>
                {
                    this.IsBusy = false;
                    this.SetInfoBar(InfoBarSeverity.Error, $"Failed to refresh the catalog: {ex.Message}");
                }).ConfigureAwait(false);

                return;
            }

            await this.LoadFromCacheAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task LoadFromCacheAsync(CancellationToken cancellationToken)
        {
            var generation = Interlocked.Increment(ref this.loadGeneration);

            await this.DispatchOnUiThreadAsync(() => this.IsBusy = true).ConfigureAwait(false);

            //// For thread safety: No access to bindable object properties beyond this point.

            var plus = await this.sapphireClient.GetPriceLookUpsAsync(cancellationToken).ConfigureAwait(false);
            var departments = await this.sapphireClient.GetDepartmentsAsync(cancellationToken).ConfigureAwait(false);

            var departmentNamesById = departments
                .GroupBy(d => d.SystemId)
                .ToDictionary(g => g.Key, g => g.First().Name);

            var rows = plus
                .Select(plu => new ItemRowVm
                {
                    Ean13 = plu.Ean13,
                    Modifier = plu.Modifier,
                    Description = plu.Description,
                    Price = plu.Price,
                    DepartmentName = departmentNamesById.TryGetValue(plu.DepartmentId, out var name) ? name : string.Empty,
                    AllowsFoodStamps = plu.FlagIds.Contains(PluFlags.FoodStamps),
                    IsAgeRestricted = plu.AgeValidationIds.Count > 0,
                    EditCommand = new RelayCommand(() =>
                        this.Messenger.Send(new LoadProductForEditMessage(plu.Ean13, plu.Modifier))),
                })
                .OrderBy(x => x.Ean13)
                .ThenBy(x => x.Modifier)
                .ToList();

            foreach (var row in rows)
            {
                row.PropertyChanged += this.OnRowSelectionChanged;
            }

            var sortedDepartmentNames = departments
                .Select(d => d.Name)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            await this.DispatchOnUiThreadAsync(() =>
            {
                // A newer load or a logout started while we were fetching — discard
                // these stale results (the newer operation owns the state and IsBusy).
                if (generation != Volatile.Read(ref this.loadGeneration))
                {
                    return;
                }

                this.suppressFilter = true;

                this.allItems = rows;
                this.TotalCount = rows.Count;
                this.SelectedCount = 0;

                this.DepartmentNames = new ObservableCollection<string>(
                    new[] { AllDepartmentsOption }.Concat(sortedDepartmentNames));

                if (!this.DepartmentNames.Contains(this.SelectedDepartmentName))
                {
                    this.SelectedDepartmentName = AllDepartmentsOption;
                }

                this.suppressFilter = false;
                this.ApplyFilter();

                this.IsBusy = false;
            }).ConfigureAwait(false);
        }

        private void Clear()
        {
            // Supersede any in-flight load so it won't repopulate after logout.
            Interlocked.Increment(ref this.loadGeneration);

            this.allItems = new List<ItemRowVm>();
            this.Items = new ObservableCollection<ItemRowVm>();
            this.DepartmentNames = new ObservableCollection<string>();
            this.SelectedDepartmentName = AllDepartmentsOption;
            this.FilterText = null;
            this.FoodStampsOnly = false;
            this.AgeRestrictedOnly = false;
            this.TotalCount = 0;
            this.SelectedCount = 0;
        }

        private void ApplyFilter()
        {
            if (this.suppressFilter)
            {
                return;
            }

            IEnumerable<ItemRowVm> query = this.allItems;

            if (!string.IsNullOrEmpty(this.SelectedDepartmentName) &&
                !string.Equals(this.SelectedDepartmentName, AllDepartmentsOption, StringComparison.Ordinal))
            {
                query = query.Where(x => string.Equals(x.DepartmentName, this.SelectedDepartmentName, StringComparison.Ordinal));
            }

            if (!string.IsNullOrWhiteSpace(this.FilterText))
            {
                var text = this.FilterText.Trim();
                query = query.Where(x =>
                    (x.Description != null && x.Description.Contains(text, StringComparison.OrdinalIgnoreCase)) ||
                    x.Ean13.ToString("D14").Contains(text, StringComparison.Ordinal) ||
                    x.Modifier.ToString("D3").Contains(text, StringComparison.Ordinal));
            }

            if (this.FoodStampsOnly)
            {
                query = query.Where(x => x.AllowsFoodStamps);
            }

            if (this.AgeRestrictedOnly)
            {
                query = query.Where(x => x.IsAgeRestricted);
            }

            this.Items = new ObservableCollection<ItemRowVm>(query);
            this.OnPropertyChanged(nameof(this.Summary));
        }

        private void OnRowSelectionChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!this.suppressSelectionRecount && e.PropertyName == nameof(ItemRowVm.IsSelected))
            {
                this.SelectedCount = this.allItems.Count(x => x.IsSelected);
            }
        }

        private void SelectAllFiltered()
        {
            this.suppressSelectionRecount = true;
            foreach (var row in this.Items)
            {
                row.IsSelected = true;
            }

            this.suppressSelectionRecount = false;
            this.SelectedCount = this.allItems.Count(x => x.IsSelected);
        }

        private void ClearSelection()
        {
            this.suppressSelectionRecount = true;
            foreach (var row in this.allItems)
            {
                row.IsSelected = false;
            }

            this.suppressSelectionRecount = false;
            this.SelectedCount = 0;
        }

        private async Task DeleteSelectedAsync(CancellationToken cancellationToken)
        {
            // Selection persists across filtering, so delete every selected row,
            // including any currently hidden by the active filter.
            var selected = this.allItems.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0)
            {
                return;
            }

            await this.DispatchOnUiThreadAsync(() => this.IsBusy = true).ConfigureAwait(false);

            //// For thread safety: No access to bindable object properties beyond this point.

            var deleted = new List<ItemRowVm>();
            var failCount = 0;

            foreach (var row in selected)
            {
                try
                {
                    await this.sapphireClient.DeletePriceLookUpAsync(
                        row.Ean13,
                        row.Modifier,
                        cancellationToken).ConfigureAwait(false);
                    deleted.Add(row);
                }
                catch (Exception ex)
                {
                    this.Logger.LogError(ex, "Failed to delete UPC {Upc} modifier {Modifier}", row.Ean13, row.Modifier);
                    failCount++;
                }

                var done = deleted.Count + failCount;
                await this.DispatchOnUiThreadAsync(() =>
                    this.SetInfoBar(InfoBarSeverity.Informational, $"Deleting… {done}/{selected.Count}")).ConfigureAwait(false);
            }

            var deletedSet = new HashSet<ItemRowVm>(deleted);
            var localFailCount = failCount;

            await this.DispatchOnUiThreadAsync(() =>
            {
                foreach (var row in deleted)
                {
                    row.PropertyChanged -= this.OnRowSelectionChanged;
                }

                this.allItems = this.allItems.Where(x => !deletedSet.Contains(x)).ToList();
                this.TotalCount = this.allItems.Count;
                this.SelectedCount = this.allItems.Count(x => x.IsSelected);
                this.ApplyFilter();

                this.IsBusy = false;

                if (localFailCount == 0)
                {
                    this.SetInfoBar(InfoBarSeverity.Success, $"Deleted {deleted.Count} item(s) from the live POS.");
                }
                else
                {
                    this.SetInfoBar(
                        InfoBarSeverity.Warning,
                        $"Deleted {deleted.Count} item(s); {localFailCount} failed (see log).");
                }
            }).ConfigureAwait(false);
        }
    }

#pragma warning disable SA1402 // File may only contain a single type

    public partial class ItemRowVm : ObservableObject
    {
        [ObservableProperty]
        private bool isSelected;

        [ObservableProperty]
        private long ean13;

        [ObservableProperty]
        private int modifier;

        [ObservableProperty]
        private string description;

        [ObservableProperty]
        private double price;

        [ObservableProperty]
        private string departmentName;

        [ObservableProperty]
        private bool allowsFoodStamps;

        [ObservableProperty]
        private bool isAgeRestricted;

        [ObservableProperty]
        private ICommand editCommand;
    }
}
