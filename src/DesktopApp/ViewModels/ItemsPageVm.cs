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
    using VerifoneCommander.PriceBookManager.Core;
    using VerifoneCommander.PriceBookManager.Core.Models;
    using VerifoneCommander.PriceBookManager.DesktopApp.Models;
    using VerifoneCommander.PriceBookManager.DesktopApp.ViewModels.Models;

    public partial class ItemsPageVm : PageVm, IRecipient<LoginStateChangedMessage>
    {
        private const string AllDepartmentsOption = "All Departments";

        private static readonly char[] DescriptionDelimiters = new[] { ' ', '-', '_', '.', ',', '/', '\\', '(', ')', '\t' };

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
        private ObservableCollection<NotSoldFilterOptionVm> notSoldFilterOptions = new ObservableCollection<NotSoldFilterOptionVm>();

        [ObservableProperty]
        private NotSoldFilterOptionVm selectedNotSoldFilter;

        [ObservableProperty]
        private ObservableCollection<UpcStatusFilterOptionVm> upcStatusFilterOptions = new ObservableCollection<UpcStatusFilterOptionVm>();

        [ObservableProperty]
        private UpcStatusFilterOptionVm selectedUpcStatusFilter;

        [ObservableProperty]
        private bool showPossibleDuplicates;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowDuplicateDetailsPanel))]
        private ItemRowVm selectedDuplicateRow;

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

            // Always default to Sold-only on app open (per the operator's workflow).
            this.NotSoldFilterOptions.Add(new NotSoldFilterOptionVm(NotSoldFilter.Sold));
            this.NotSoldFilterOptions.Add(new NotSoldFilterOptionVm(NotSoldFilter.NotSold));
            this.NotSoldFilterOptions.Add(new NotSoldFilterOptionVm(NotSoldFilter.All));
            this.SelectedNotSoldFilter = this.NotSoldFilterOptions[0];

            // UPC quality dropdown — default "All".
            this.UpcStatusFilterOptions.Add(new UpcStatusFilterOptionVm(UpcStatusFilter.All));
            this.UpcStatusFilterOptions.Add(new UpcStatusFilterOptionVm(UpcStatusFilter.Valid));
            this.UpcStatusFilterOptions.Add(new UpcStatusFilterOptionVm(UpcStatusFilter.AnyIssue));
            this.UpcStatusFilterOptions.Add(new UpcStatusFilterOptionVm(UpcStatusFilter.BadCheckDigit));
            this.UpcStatusFilterOptions.Add(new UpcStatusFilterOptionVm(UpcStatusFilter.RandomWeight));
            this.UpcStatusFilterOptions.Add(new UpcStatusFilterOptionVm(UpcStatusFilter.CouponNs5));
            this.UpcStatusFilterOptions.Add(new UpcStatusFilterOptionVm(UpcStatusFilter.CouponNs9));
            this.UpcStatusFilterOptions.Add(new UpcStatusFilterOptionVm(UpcStatusFilter.Ndc));
            this.UpcStatusFilterOptions.Add(new UpcStatusFilterOptionVm(UpcStatusFilter.InStore));
            this.SelectedUpcStatusFilter = this.UpcStatusFilterOptions[0];

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

        public bool ShowDuplicateDetailsPanel =>
            this.ShowPossibleDuplicates &&
            this.SelectedDuplicateRow != null &&
            this.SelectedDuplicateRow.PossibleDuplicates != null &&
            this.SelectedDuplicateRow.PossibleDuplicates.Count > 0;

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

        partial void OnSelectedNotSoldFilterChanged(NotSoldFilterOptionVm value) => this.ApplyFilter();

        partial void OnSelectedUpcStatusFilterChanged(UpcStatusFilterOptionVm value) => this.ApplyFilter();

        partial void OnShowPossibleDuplicatesChanged(bool value)
        {
            // Rebuild each row's duplicate list (or wipe it when toggling off). Borders
            // and the detail panel stay hidden until the operator actually taps a row —
            // turning the toggle on never highlights anything on its own.
            this.RecomputeDuplicates();
            this.SelectedDuplicateRow = null;
            this.OnPropertyChanged(nameof(this.ShowDuplicateDetailsPanel));
        }

        partial void OnSelectedDuplicateRowChanged(ItemRowVm value)
        {
            // Reveal the blue border only on the OTHER rows that are possible duplicates
            // of the tapped row. Clearing the selection (tap-away, or a row with no
            // duplicates) clears every border.
            foreach (var row in this.allItems)
            {
                row.IsPossibleDuplicate = false;
            }

            if (value?.PossibleDuplicates != null)
            {
                foreach (var duplicate in value.PossibleDuplicates)
                {
                    duplicate.IsPossibleDuplicate = true;
                }
            }
        }

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
                .Select(plu =>
                {
                    // Read-only UPC quality audit. We never normalize, pad, or recompute a
                    // check digit — the upc field is the literal scan code (see
                    // docs/reference/upc-handling.md). A code with ≤5 significant digits is a
                    // legitimate short internal PLU and is treated as fine, not flagged.
                    var ean14 = plu.Ean13.ToString("D14");
                    var significantDigits = ean14.TrimStart('0').Length;
                    var hasUpcIssue = false;
                    string upcIssueLabel = null;
                    string upcIssueMessage = null;
                    var severity = UpcUtilities.UpcSeverity.None;
                    var filterKey = UpcStatusFilter.Valid;
                    if (significantDigits > 5)
                    {
                        var classification = UpcUtilities.ClassifyUpc(ean14);
                        upcIssueLabel = UpcUtilities.GetIssueLabel(classification);
                        severity = UpcUtilities.GetSeverity(classification);

                        // Primary filter bucket: risky NS wins over bad-check-digit
                        // so "Random-weight + bad check" still appears under RandomWeight.
                        switch (classification.Risk)
                        {
                            case UpcUtilities.UpcRisk.RandomWeight:
                                filterKey = UpcStatusFilter.RandomWeight;
                                break;
                            case UpcUtilities.UpcRisk.Coupon:
                                filterKey = classification.NumberSystemDigit == "9"
                                    ? UpcStatusFilter.CouponNs9
                                    : UpcStatusFilter.CouponNs5;
                                break;
                            case UpcUtilities.UpcRisk.Ndc:
                                filterKey = UpcStatusFilter.Ndc;
                                break;
                            case UpcUtilities.UpcRisk.InStore:
                                filterKey = UpcStatusFilter.InStore;
                                break;
                            default:
                                if (classification.Class == UpcUtilities.UpcClass.AmbiguousInvalid)
                                {
                                    filterKey = UpcStatusFilter.BadCheckDigit;
                                }

                                break;
                        }

                        if (upcIssueLabel != null)
                        {
                            hasUpcIssue = true;
                            var riskNote = UpcUtilities.RiskNote(classification.Risk);
                            if (classification.Class == UpcUtilities.UpcClass.AmbiguousInvalid)
                            {
                                upcIssueMessage = (riskNote != null ? riskNote + "  " : string.Empty) +
                                    "UPC check digit does not validate — verify the item scans.";
                            }
                            else
                            {
                                upcIssueMessage = riskNote;
                            }
                        }
                    }

                    var isNotSold = plu.IsNotSold;
                    string statusText;
                    string statusTooltip;
                    if (isNotSold && hasUpcIssue)
                    {
                        statusText = "Not sold · " + upcIssueLabel;
                        statusTooltip = "Marked Not Sold (won't ring up at the register). " + upcIssueMessage;
                    }
                    else if (isNotSold)
                    {
                        statusText = "Not sold";
                        statusTooltip = "Marked Not Sold (won't ring up at the register).";
                    }
                    else if (hasUpcIssue)
                    {
                        statusText = upcIssueLabel;
                        statusTooltip = upcIssueMessage;
                    }
                    else
                    {
                        statusText = string.Empty;
                        statusTooltip = string.Empty;
                    }

                    return new ItemRowVm
                    {
                        Ean13 = plu.Ean13,
                        Modifier = plu.Modifier,
                        Description = plu.Description,
                        Price = plu.Price,
                        DepartmentName = departmentNamesById.TryGetValue(plu.DepartmentId, out var name) ? name : string.Empty,
                        AllowsFoodStamps = plu.FlagIds.Contains(PluFlags.FoodStamps),
                        IsAgeRestricted = plu.AgeValidationIds.Count > 0,
                        IsNotSold = isNotSold,
                        HasUpcIssue = hasUpcIssue,
                        UpcIssueLabel = upcIssueLabel,
                        Severity = severity,
                        UpcFilterKey = filterKey,
                        StatusText = statusText,
                        StatusTooltip = statusTooltip,
                        EditCommand = new RelayCommand(() =>
                            this.Messenger.Send(new LoadProductForEditMessage(plu.Ean13, plu.Modifier))),
                    };
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

                // Update the Sold / Not Sold / All counts on the dropdown options.
                var notSoldCount = rows.Count(x => x.IsNotSold);
                var soldCount = rows.Count - notSoldCount;
                foreach (var opt in this.NotSoldFilterOptions)
                {
                    switch (opt.Mode)
                    {
                        case NotSoldFilter.Sold:
                            opt.Count = soldCount;
                            break;
                        case NotSoldFilter.NotSold:
                            opt.Count = notSoldCount;
                            break;
                        case NotSoldFilter.All:
                            opt.Count = rows.Count;
                            break;
                    }
                }

                // Update the UPC-status dropdown counts.
                foreach (var opt in this.UpcStatusFilterOptions)
                {
                    switch (opt.Mode)
                    {
                        case UpcStatusFilter.All:
                            opt.Count = rows.Count;
                            break;
                        case UpcStatusFilter.AnyIssue:
                            opt.Count = rows.Count(x => x.UpcFilterKey != UpcStatusFilter.Valid);
                            break;
                        default:
                            opt.Count = rows.Count(x => x.UpcFilterKey == opt.Mode);
                            break;
                    }
                }

                this.DepartmentNames = new ObservableCollection<string>(
                    new[] { AllDepartmentsOption }.Concat(sortedDepartmentNames));

                if (!this.DepartmentNames.Contains(this.SelectedDepartmentName))
                {
                    this.SelectedDepartmentName = AllDepartmentsOption;
                }

                this.suppressFilter = false;
                this.ApplyFilter();

                // Rebuild the duplicate match lists against the new catalog (matching the
                // current toggle state). A no-op when the toggle is off. Any prior tapped
                // selection is stale now, so drop it (also clears leftover borders).
                this.RecomputeDuplicates();
                this.SelectedDuplicateRow = null;

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
            this.SelectedDuplicateRow = null;
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

            // Sold-only by default; Not-Sold-only or All on operator request.
            var notSoldMode = this.SelectedNotSoldFilter?.Mode ?? NotSoldFilter.Sold;
            switch (notSoldMode)
            {
                case NotSoldFilter.Sold:
                    query = query.Where(x => !x.IsNotSold);
                    break;
                case NotSoldFilter.NotSold:
                    query = query.Where(x => x.IsNotSold);
                    break;
                case NotSoldFilter.All:
                    // No filter.
                    break;
            }

            // UPC quality dropdown filter.
            var upcMode = this.SelectedUpcStatusFilter?.Mode ?? UpcStatusFilter.All;
            switch (upcMode)
            {
                case UpcStatusFilter.All:
                    break;
                case UpcStatusFilter.Valid:
                    query = query.Where(x => x.UpcFilterKey == UpcStatusFilter.Valid);
                    break;
                case UpcStatusFilter.AnyIssue:
                    query = query.Where(x => x.UpcFilterKey != UpcStatusFilter.Valid);
                    break;
                default:
                    query = query.Where(x => x.UpcFilterKey == upcMode);
                    break;
            }

            this.Items = new ObservableCollection<ItemRowVm>(query);
            this.OnPropertyChanged(nameof(this.Summary));
        }

        private void RecomputeDuplicates()
        {
            static HashSet<string> Tokenize(string description)
            {
                var set = new HashSet<string>(StringComparer.Ordinal);
                if (string.IsNullOrWhiteSpace(description))
                {
                    return set;
                }

                foreach (var token in description.Split(DescriptionDelimiters, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (token.Length >= 4)
                    {
                        set.Add(token.ToUpperInvariant());
                    }
                }

                return set;
            }

            // Always clear the previous state — toggling off (or a fresh catalog)
            // wipes the highlights.
            foreach (var row in this.allItems)
            {
                row.IsPossibleDuplicate = false;
                row.PossibleDuplicates = null;
            }

            if (!this.ShowPossibleDuplicates)
            {
                return;
            }

            // Per department, build a token→rows inverted index and find every row
            // that shares at least one ≥4-char word with another row.
            foreach (var deptGroup in this.allItems.GroupBy(x => x.DepartmentName ?? string.Empty))
            {
                var tokensByRow = new Dictionary<ItemRowVm, HashSet<string>>();
                var wordIndex = new Dictionary<string, List<ItemRowVm>>(StringComparer.Ordinal);

                foreach (var row in deptGroup)
                {
                    var tokens = Tokenize(row.Description);
                    tokensByRow[row] = tokens;
                    foreach (var t in tokens)
                    {
                        if (!wordIndex.TryGetValue(t, out var bucket))
                        {
                            bucket = new List<ItemRowVm>();
                            wordIndex[t] = bucket;
                        }

                        bucket.Add(row);
                    }
                }

                foreach (var pair in tokensByRow)
                {
                    var row = pair.Key;
                    var related = new HashSet<ItemRowVm>();
                    foreach (var t in pair.Value)
                    {
                        if (wordIndex.TryGetValue(t, out var bucket))
                        {
                            foreach (var other in bucket)
                            {
                                if (!ReferenceEquals(other, row))
                                {
                                    related.Add(other);
                                }
                            }
                        }
                    }

                    if (related.Count > 0)
                    {
                        // Store the match list now; the borders are only painted later,
                        // for the specific row the operator taps (OnSelectedDuplicateRowChanged).
                        row.PossibleDuplicates = related.OrderBy(x => x.Ean13).ThenBy(x => x.Modifier).ToList();
                    }
                }
            }
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
                    var target = App.IsMockMode ? "mock data" : "the live POS";
                    this.SetInfoBar(InfoBarSeverity.Success, $"Deleted {deleted.Count} item(s) from {target}.");
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

    public partial class NotSoldFilterOptionVm : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Label))]
        private int count;

        public NotSoldFilterOptionVm(NotSoldFilter mode)
        {
            this.Mode = mode;
        }

        public NotSoldFilter Mode { get; }

        public string Label
        {
            get
            {
                string baseLabel;
                switch (this.Mode)
                {
                    case NotSoldFilter.Sold:
                        baseLabel = "Sold";
                        break;
                    case NotSoldFilter.NotSold:
                        baseLabel = "Not Sold";
                        break;
                    case NotSoldFilter.All:
                        baseLabel = "All";
                        break;
                    default:
                        baseLabel = "?";
                        break;
                }

                return baseLabel + " (" + this.Count + ")";
            }
        }
    }

    public partial class UpcStatusFilterOptionVm : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Label))]
        private int count;

        public UpcStatusFilterOptionVm(UpcStatusFilter mode)
        {
            this.Mode = mode;
        }

        public UpcStatusFilter Mode { get; }

        public string Label
        {
            get
            {
                string baseLabel;
                switch (this.Mode)
                {
                    case UpcStatusFilter.All:
                        baseLabel = "All UPC status";
                        break;
                    case UpcStatusFilter.Valid:
                        baseLabel = "Valid";
                        break;
                    case UpcStatusFilter.AnyIssue:
                        baseLabel = "Any UPC issue";
                        break;
                    case UpcStatusFilter.BadCheckDigit:
                        baseLabel = "Bad check digit";
                        break;
                    case UpcStatusFilter.RandomWeight:
                        baseLabel = "Random-weight";
                        break;
                    case UpcStatusFilter.CouponNs5:
                        baseLabel = "Coupon (NS=5)";
                        break;
                    case UpcStatusFilter.CouponNs9:
                        baseLabel = "Coupon (NS=9)";
                        break;
                    case UpcStatusFilter.Ndc:
                        baseLabel = "NDC";
                        break;
                    case UpcStatusFilter.InStore:
                        baseLabel = "In-store";
                        break;
                    default:
                        baseLabel = "?";
                        break;
                }

                return baseLabel + " (" + this.Count + ")";
            }
        }
    }

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
        private bool isNotSold;

        [ObservableProperty]
        private bool hasUpcIssue;

        [ObservableProperty]
        private string upcIssueLabel;

        [ObservableProperty]
        private UpcUtilities.UpcSeverity severity;

        [ObservableProperty]
        private UpcStatusFilter upcFilterKey;

        [ObservableProperty]
        private string statusText;

        [ObservableProperty]
        private string statusTooltip;

        [ObservableProperty]
        private bool isPossibleDuplicate;

        [ObservableProperty]
        private ICommand editCommand;

        // Other rows in the same department whose description overlaps this one.
        // Populated when the "Possible duplicates" toggle is on.
        public System.Collections.Generic.List<ItemRowVm> PossibleDuplicates { get; set; }
    }
}
