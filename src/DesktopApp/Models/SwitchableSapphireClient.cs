// -----------------------------------------------------------------------
// <copyright file="SwitchableSapphireClient.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.DesktopApp.Models
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using VerifoneCommander.PriceBookManager.Core;
    using VerifoneCommander.PriceBookManager.Core.Models;

    /// <summary>
    /// Routes every call to either the mock or the live client based on the current
    /// setting, so the app can switch modes without rebuilding its object graph.
    /// The mode is read fresh on each call; a clean re-login (which re-runs
    /// RefreshCacheAsync) is what swaps the data the user actually sees.
    /// </summary>
    public class SwitchableSapphireClient : ISapphireClient
    {
        private readonly Func<bool> useMocks;
        private readonly ISapphireClient mock;
        private readonly ISapphireClient live;

        public SwitchableSapphireClient(
            Func<bool> useMocks,
            ISapphireClient mock,
            ISapphireClient live)
        {
            this.useMocks = useMocks ?? throw new ArgumentNullException(nameof(useMocks));
            this.mock = mock ?? throw new ArgumentNullException(nameof(mock));
            this.live = live ?? throw new ArgumentNullException(nameof(live));
        }

        private ISapphireClient Active => this.useMocks() ? this.mock : this.live;

        public Task<List<Plu>> GetPriceLookUpsAsync(CancellationToken cancellationToken)
            => this.Active.GetPriceLookUpsAsync(cancellationToken);

        public Task UpdatePriceLookUpAsync(Plu plu, CancellationToken cancellationToken)
            => this.Active.UpdatePriceLookUpAsync(plu, cancellationToken);

        public Task DeletePriceLookUpAsync(long ean13, int modifier, CancellationToken cancellationToken)
            => this.Active.DeletePriceLookUpAsync(ean13, modifier, cancellationToken);

        public Task<List<Department>> GetDepartmentsAsync(CancellationToken cancellationToken)
            => this.Active.GetDepartmentsAsync(cancellationToken);

        public Task<List<TaxRate>> GetTaxRatesAsync(CancellationToken cancellationToken)
            => this.Active.GetTaxRatesAsync(cancellationToken);

        public Task<List<AgeValidation>> GetAgeValidationsAsync(CancellationToken cancellationToken)
            => this.Active.GetAgeValidationsAsync(cancellationToken);
    }
}
