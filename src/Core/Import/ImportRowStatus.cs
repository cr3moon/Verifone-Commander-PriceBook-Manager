// -----------------------------------------------------------------------
// <copyright file="ImportRowStatus.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.Core.Import
{
    public enum ImportRowStatus
    {
        /// <summary>Row parsed cleanly and can be imported.</summary>
        Valid,

        /// <summary>Row can be imported but carries an advisory (e.g. a risky barcode).</summary>
        Warning,

        /// <summary>Row cannot be imported (a required field is missing or invalid).</summary>
        Error,
    }
}
