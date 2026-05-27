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
        private bool isBusy;

        [ObservableProperty]
        private int totalCount;

        public ItemsPageVm(
            IUiThreadDispatcher uiThreadDispatcher,
            IMessenger messenger,
            ILogger logger,
            ICachingSapphireClient sapphireClient)
            : base(uiThreadDispatcher, messenger, logger)
        {
            this.sapphireClient = sapphireClient ?? throw new ArgumentNullException(nameof(sapphireClient));

            this.RefreshCommand = new AsyncRelayCommand(this.RefreshAsync, () => !this.IsBusy);

            this.Messenger.Register<LoginStateChangedMessage>(this);
        }

        public override string Name => "Items";

        // ViewAll
        public override int SymbolCode => 0xE8A9;

        public IRelayCommand RefreshCommand { get; }

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

        partial void OnIsBusyChanged(bool value) => this.OnPropertyChanged(nameof(this.Summary));

        partial void OnTotalCountChanged(int value) => this.OnPropertyChanged(nameof(this.Summary));

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

            var sortedDepartmentNames = departments
                .Select(d => d.Name)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            await this.DispatchOnUiThreadAsync(() =>
            {
                this.suppressFilter = true;

                this.allItems = rows;
                this.TotalCount = rows.Count;

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
            this.allItems = new List<ItemRowVm>();
            this.Items = new ObservableCollection<ItemRowVm>();
            this.DepartmentNames = new ObservableCollection<string>();
            this.SelectedDepartmentName = AllDepartmentsOption;
            this.FilterText = null;
            this.FoodStampsOnly = false;
            this.AgeRestrictedOnly = false;
            this.TotalCount = 0;
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
