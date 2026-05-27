// -----------------------------------------------------------------------
// <copyright file="SwitchableSapphireCredentialsProvider.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.DesktopApp.Models
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using VerifoneCommander.PriceBookManager.Core;

    /// <summary>
    /// Routes credential operations to the mock or live provider based on the current
    /// setting, mirroring <see cref="SwitchableSapphireClient"/>.
    /// </summary>
    public class SwitchableSapphireCredentialsProvider : IModifiableSapphireCredentialsProvider
    {
        private readonly Func<bool> useMocks;
        private readonly IModifiableSapphireCredentialsProvider mock;
        private readonly IModifiableSapphireCredentialsProvider live;

        public SwitchableSapphireCredentialsProvider(
            Func<bool> useMocks,
            IModifiableSapphireCredentialsProvider mock,
            IModifiableSapphireCredentialsProvider live)
        {
            this.useMocks = useMocks ?? throw new ArgumentNullException(nameof(useMocks));
            this.mock = mock ?? throw new ArgumentNullException(nameof(mock));
            this.live = live ?? throw new ArgumentNullException(nameof(live));
        }

        private IModifiableSapphireCredentialsProvider Active => this.useMocks() ? this.mock : this.live;

        public Task<ISapphireCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
            => this.Active.GetCredentialsAsync(cancellationToken);

        public void SetLoginCredentials(string hostName, string username, string password)
            => this.Active.SetLoginCredentials(hostName, username, password);
    }
}
