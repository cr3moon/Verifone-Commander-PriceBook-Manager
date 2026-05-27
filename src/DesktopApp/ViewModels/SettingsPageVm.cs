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

    public class SettingsPageVm : PageVm
    {
        private readonly Settings settings;
        private readonly bool startupUseMocks;

        public SettingsPageVm(
            IUiThreadDispatcher uiThreadDispatcher,
            IMessenger messenger,
            ILogger logger,
            Settings settings)
            : base(uiThreadDispatcher, messenger, logger)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.startupUseMocks = App.StartupUseMocks;

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
            set => this.settings.UseMocks = value;
        }

        // Describes the mode the running session is actually in (fixed at startup),
        // so the operator never mistakes a live session for a mock one.
        public string ActiveModeText => this.startupUseMocks
            ? "This session is running on built-in MOCK data — changes are not sent to a controller."
            : "This session is connected to the LIVE POS — edits, deletes, and imports affect the controller.";
    }
}
