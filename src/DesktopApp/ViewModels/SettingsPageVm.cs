// -----------------------------------------------------------------------
// <copyright file="SettingsPageVm.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.DesktopApp.ViewModels
{
    using System;
    using CommunityToolkit.Mvvm.Messaging;
    using Microsoft.Extensions.Logging;
    using VerifoneCommander.PriceBookManager.DesktopApp.Models;
    using VerifoneCommander.PriceBookManager.DesktopApp.ViewModels.Models;

    public class SettingsPageVm : PageVm
    {
        private readonly Settings settings;

        public SettingsPageVm(
            IUiThreadDispatcher uiThreadDispatcher,
            IMessenger messenger,
            ILogger logger,
            Settings settings)
            : base(uiThreadDispatcher, messenger, logger)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));

            this.Hostname = new ValidatedTextVm(
                v =>
                {
                    if (string.IsNullOrWhiteSpace(v))
                    {
                        return "Empty hostname not allowed";
                    }

                    // Valid. Update in settings.
                    this.settings.Hostname = v;
                    return string.Empty;
                });

            this.Hostname.Text = this.settings.Hostname;
        }

        public override string Name => "Settings";

        // Settings
        public override int SymbolCode => 0xE115;

        public ValidatedTextVm Hostname { get; }

        public bool AllowUntrustedCertificates
        {
            get => this.settings.AllowUntrustedCertificates;
            set => this.settings.AllowUntrustedCertificates = value;
        }

        public bool UseMocks
        {
            get => this.settings.UseMocks;
            set
            {
                if (this.settings.UseMocks == value)
                {
                    return;
                }

                this.settings.UseMocks = value;
                this.OnPropertyChanged(nameof(this.ActiveModeText));

                // Switching takes effect immediately: end the session so the newly
                // selected backend is used cleanly on the next login.
                this.Messenger.Send(new ModeChangedMessage(value));
            }
        }

        // Reflects the data source the app is currently using (updates as soon as the
        // toggle flips), so the operator never mistakes a live session for a mock one.
        public string ActiveModeText => this.settings.UseMocks
            ? "This session is running on built-in MOCK data — changes are not sent to a controller."
            : "This session is connected to the LIVE POS — edits, deletes, and imports affect the controller.";
    }
}
