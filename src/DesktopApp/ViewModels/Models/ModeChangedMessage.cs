// -----------------------------------------------------------------------
// <copyright file="ModeChangedMessage.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.DesktopApp.ViewModels.Models
{
    /// <summary>
    /// Raised when the mock/live data-source setting is toggled at runtime. The
    /// account VM responds by ending the current session so the newly selected
    /// backend is used cleanly on the next login.
    /// </summary>
    internal class ModeChangedMessage
    {
        public ModeChangedMessage(bool useMocks)
        {
            this.UseMocks = useMocks;
        }

        public bool UseMocks { get; }
    }
}
