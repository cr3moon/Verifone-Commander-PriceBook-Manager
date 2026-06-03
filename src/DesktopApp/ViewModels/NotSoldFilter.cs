// -----------------------------------------------------------------------
// <copyright file="NotSoldFilter.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.DesktopApp.ViewModels
{
    /// <summary>
    /// Items-page filter selector for Not Sold (flag sysid 2) handling.
    /// </summary>
    public enum NotSoldFilter
    {
        /// <summary>Show only items that are sold (default).</summary>
        Sold,

        /// <summary>Show only items marked Not Sold.</summary>
        NotSold,

        /// <summary>Show every item, regardless of Not Sold status.</summary>
        All,
    }
}
