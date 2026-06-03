// -----------------------------------------------------------------------
// <copyright file="UpcStatusFilter.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.DesktopApp.ViewModels
{
    /// <summary>
    /// Items-page filter selector for the UPC quality of stored PLUs. Each row is
    /// classified into one of these buckets at load time (risky number-system takes
    /// priority over a bad check digit; everything else is Valid).
    /// </summary>
    public enum UpcStatusFilter
    {
        /// <summary>No UPC filter (default).</summary>
        All,

        /// <summary>Only items whose stored upc passes all the GS1 checks.</summary>
        Valid,

        /// <summary>Any item flagged with a UPC issue.</summary>
        AnyIssue,

        /// <summary>GTIN-14 check digit does not validate.</summary>
        BadCheckDigit,

        /// <summary>Number system 2 — random / variable weight.</summary>
        RandomWeight,

        /// <summary>Number system 5 — legacy UCC manufacturer coupon.</summary>
        CouponNs5,

        /// <summary>Number system 9 — GS1 DataBar Expanded coupon (NCC).</summary>
        CouponNs9,

        /// <summary>Number system 3 — NDC / pharmacy code.</summary>
        Ndc,

        /// <summary>Number system 4 — in-store / private-label code.</summary>
        InStore,
    }
}
