// -----------------------------------------------------------------------
// <copyright file="PluFlags.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.Core.Models
{
    public static class PluFlags
    {
        /// <summary>
        /// Not Sold — prevents the item from ringing up at the register. Verified on
        /// this customer's Commander via HAR trace + operator confirmation (2026-05-24);
        /// see <c>docs/reference/upc-handling.md</c> for the wire format.
        /// </summary>
        public const short NotSold = 2;

        /// <summary>
        /// Allow EBT / food stamps
        /// </summary>
        public const short FoodStamps = 4;
    }
}
