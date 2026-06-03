// -----------------------------------------------------------------------
// <copyright file="BoolToBorderThicknessConverter.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.DesktopApp.Converters
{
    using System;
    using System.Globalization;
    using Microsoft.UI.Xaml;
    using Microsoft.UI.Xaml.Data;

    /// <summary>
    /// Returns a uniform Thickness when the bound bool is true, and zero thickness
    /// otherwise. Optional ConverterParameter (string double) sets the "true" width;
    /// defaults to 2.
    /// </summary>
    public class BoolToBorderThicknessConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool flag = value is bool b && b;
            double thickness = 2.0;
            if (parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
            {
                thickness = p;
            }

            return new Thickness(flag ? thickness : 0.0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
