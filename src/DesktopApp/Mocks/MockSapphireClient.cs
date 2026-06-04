// -----------------------------------------------------------------------
// <copyright file="MockSapphireClient.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.DesktopApp.Mocks
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using VerifoneCommander.PriceBookManager.Core;
    using VerifoneCommander.PriceBookManager.Core.Models;

    public class MockSapphireClient : ISapphireClient
    {
        private List<Plu> plus;
        private List<Department> departments = new List<Department>()
        {
            new Department
            {
                SystemId = 1,
                AllowFoodStamps = true,
                Name = "GROCERY",
            },
            new Department
            {
                SystemId = 2,
                Name = "NON GROCERY",
                TaxRateIds = new HashSet<int>() { 1, 2 },
            },
            new Department
            {
                SystemId = 3,
                Name = "ALCOHOL",
                TaxRateIds = new HashSet<int>() { 1, 2 },
                AgeValidationIds = new HashSet<int>() { 1 },
            },
            new Department
            {
                SystemId = 4,
                Name = "TOBACCO",
                TaxRateIds = new HashSet<int>() { 1, 2 },
                AgeValidationIds = new HashSet<int>() { 2 },
            },
        };

        private List<TaxRate> taxRates = new List<TaxRate>()
        {
            new TaxRate
            {
                SystemId = 1,
                Name = "TAX LOCAL",
            },
            new TaxRate
            {
                SystemId = 2,
                Name = "TAX FEDERAL",
            },
        };

        private List<AgeValidation> ageValidations = new List<AgeValidation>()
        {
            new AgeValidation
            {
                SystemId = 1,
                Name = "ALCOHOL ID CHECK",
            },
            new AgeValidation
            {
                SystemId = 2,
                Name = "TOBACCO ID CHECK",
            },
        };

        public MockSapphireClient()
        {
            this.plus = GenerateSamplePlus();
        }

        public async Task<List<Plu>> GetPriceLookUpsAsync(
            CancellationToken cancellationToken)
        {
            await DelayAsync().ConfigureAwait(false);
            return this.plus.Select(x => x.Clone()).ToList();
        }

        public async Task<List<Department>> GetDepartmentsAsync(
            CancellationToken cancellationToken)
        {
            await DelayAsync().ConfigureAwait(false);
            return this.departments.Select(x => x.Clone()).ToList();
        }

        public async Task<List<TaxRate>> GetTaxRatesAsync(
            CancellationToken cancellationToken)
        {
            await DelayAsync().ConfigureAwait(false);
            return this.taxRates.Select(x => x.Clone()).ToList();
        }

        public async Task<List<AgeValidation>> GetAgeValidationsAsync(
            CancellationToken cancellationToken)
        {
            await DelayAsync().ConfigureAwait(false);
            return this.ageValidations.Select(x => x.Clone()).ToList();
        }

        public async Task UpdatePriceLookUpAsync(
            Plu plu,
            CancellationToken cancellationToken)
        {
            _ = plu ?? throw new ArgumentNullException(nameof(plu));

            await DelayAsync().ConfigureAwait(false);

            var index = this.plus.FindIndex(x => x.Ean13 == plu.Ean13 && x.Modifier == plu.Modifier);

            if (index >= 0)
            {
                this.plus[index] = plu.Clone();
            }
            else
            {
                this.plus.Add(plu.Clone());
            }
        }

        public async Task DeletePriceLookUpAsync(
            long ean13,
            int modifier,
            CancellationToken cancellationToken)
        {
            await DelayAsync().ConfigureAwait(false);

            var index = this.plus.FindIndex(x => x.Ean13 == ean13 && x.Modifier == modifier);
            if (index >= 0)
            {
                this.plus.RemoveAt(index);
            }
        }

        private static Task DelayAsync()
        {
            // Simulate some async network operation by using an async delay
            return Task.Delay(500);
        }

        // Deterministic demo catalog exercising every Items-page feature:
        //  - Possible-duplicate clusters (same department, shared ≥4-char words):
        //    the three BREADs, the two MOUNTAIN DEWs, the two BOUNTY/PAPER/TOWELS
        //    (multi-word match), the two COUPONs, the two BUDWEISERs, the three
        //    MARLBOROs. BREAD BOX PLASTIC shares "BREAD" but sits in another
        //    department, so it must NOT match the grocery breads.
        //  - UPC severities: bad check digit (MILK), random-weight NS=2 (BANANAS;
        //    GROUND BEEF also has a bad check), coupons NS=5/NS=9, NDC NS=3
        //    (TYLENOL; ADVIL also has a bad check → escalates), in-store NS=4
        //    (BAKERY CAKE), short internal PLU (DELI SANDWICH — never flagged).
        //  - Not Sold flag (OLD WIDGET, BROKEN BARCODE LABEL) for the Sold/Not
        //    Sold/All dropdown.
        private static List<Plu> GenerateSamplePlus()
        {
            static Plu Make(
                long upc,
                string description,
                int departmentId,
                double price,
                bool foodStamps = false,
                bool notSold = false,
                int? ageValidationId = null,
                bool taxed = false)
            {
                var flagIds = Plu.GenerateDefaultFlagIds();
                if (foodStamps)
                {
                    flagIds.Add(PluFlags.FoodStamps);
                }

                if (notSold)
                {
                    flagIds.Add(PluFlags.NotSold);
                }

                return new Plu
                {
                    Ean13 = upc,
                    Modifier = 0,
                    Description = description,
                    DepartmentId = departmentId,
                    ProductCodeId = 400,
                    Price = price,
                    FlagIds = flagIds,
                    TaxRateIds = taxed ? new HashSet<int> { 1, 2 } : new HashSet<int>(),
                    AgeValidationIds = ageValidationId.HasValue
                        ? new HashSet<int> { ageValidationId.Value }
                        : new HashSet<int>(),
                };
            }

            return new List<Plu>
            {
                // GROCERY (1) — duplicate clusters + severity cases.
                Make(72948000015, "WONDER BREAD WHITE 20OZ", 1, 2.49, foodStamps: true),
                Make(72948000022, "WONDER BREAD WHEAT 20OZ", 1, 2.49, foodStamps: true),
                Make(74235000012, "SOURDOUGH BREAD LOAF", 1, 3.99, foodStamps: true),
                Make(12000001239, "MOUNTAIN DEW 12PK", 1, 6.99, foodStamps: true),
                Make(12000004568, "MOUNTAIN DEW 2L", 1, 2.79, foodStamps: true),
                Make(28400023450, "DORITOS NACHO CHEESE", 1, 4.29, foodStamps: true),
                Make(200123456788, "BANANAS RANDOM WEIGHT", 1, 1.99, foodStamps: true),
                Make(200987654321, "GROUND BEEF VALUE PACK", 1, 8.49, foodStamps: true),
                Make(70038123454, "MILK WHOLE GALLON", 1, 3.59, foodStamps: true),
                Make(501234567890, "STORE COUPON 50C OFF", 1, 0.50),
                Make(901234567898, "MFR COUPON FREE ITEM", 1, 0.00),
                Make(4501, "DELI SANDWICH SPECIAL", 1, 5.99, foodStamps: true),
                Make(49000123456, "OLD WIDGET DISCONTINUED", 1, 1.00, notSold: true),
                Make(75678123451, "BROKEN BARCODE LABEL", 1, 2.00, notSold: true),

                // NON GROCERY (2) — taxed; NDC / in-store cases; cross-department
                // "BREAD" that must not match the grocery breads.
                Make(37000456780, "BOUNTY PAPER TOWELS 6 ROLL", 2, 9.99, taxed: true),
                Make(37000678908, "PAPER TOWELS BOUNTY SELECT SIZE", 2, 12.49, taxed: true),
                Make(300450678904, "TYLENOL EXTRA STRENGTH 100CT", 2, 11.99, taxed: true),
                Make(305730123458, "ADVIL LIQUID GELS 40CT", 2, 9.49, taxed: true),
                Make(400001234563, "IN STORE BAKERY CAKE", 2, 15.99, taxed: true),
                Make(37000912347, "DAWN DISH SOAP ORIGINAL", 2, 3.79, taxed: true),
                Make(98765432105, "BREAD BOX PLASTIC", 2, 14.99, taxed: true),

                // ALCOHOL (3) — age-restricted.
                Make(18200001239, "BUDWEISER 6PK BOTTLES", 3, 8.99, ageValidationId: 1, taxed: true),
                Make(18200004568, "BUDWEISER 12PK CANS", 3, 14.99, ageValidationId: 1, taxed: true),
                Make(75990123459, "CORONA EXTRA 6PK", 3, 9.99, ageValidationId: 1, taxed: true),
                Make(80660951232, "JACK DANIELS 750ML", 3, 24.99, ageValidationId: 1, taxed: true),

                // TOBACCO (4) — age-restricted.
                Make(28200111111, "MARLBORO RED BOX", 4, 9.79, ageValidationId: 2, taxed: true),
                Make(28200222220, "MARLBORO LIGHT BOX", 4, 9.79, ageValidationId: 2, taxed: true),
                Make(28200333339, "MARLBORO MENTHOL BOX", 4, 9.79, ageValidationId: 2, taxed: true),
                Make(12300045674, "CAMEL CRUSH KING", 4, 8.99, ageValidationId: 2, taxed: true),
            };
        }
    }
}
