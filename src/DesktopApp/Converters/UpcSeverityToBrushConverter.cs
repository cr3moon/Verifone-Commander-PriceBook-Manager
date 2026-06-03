// -----------------------------------------------------------------------
// <copyright file="UpcSeverityToBrushConverter.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.DesktopApp.Converters
{
    using System;
    using Microsoft.UI.Xaml;
    using Microsoft.UI.Xaml.Data;
    using Microsoft.UI.Xaml.Media;
    using VerifoneCommander.PriceBookManager.Core;

    /// <summary>
    /// Maps a <see cref="UpcUtilities.UpcSeverity"/> to a foreground brush keyed
    /// in App.xaml (UpcErrorBrush / UpcWarningBrush / UpcInfoBrush). Returns null
    /// for None so the binding falls back to the inherited Foreground.
    /// </summary>
    public class UpcSeverityToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is not UpcUtilities.UpcSeverity severity)
            {
                return null;
            }

            string key;
            switch (severity)
            {
                case UpcUtilities.UpcSeverity.Error:
                    key = "UpcErrorBrush";
                    break;
                case UpcUtilities.UpcSeverity.Warning:
                    key = "UpcWarningBrush";
                    break;
                case UpcUtilities.UpcSeverity.Info:
                    key = "UpcInfoBrush";
                    break;
                default:
                    return null;
            }

            if (Application.Current?.Resources != null &&
                Application.Current.Resources.TryGetValue(key, out var brush) &&
                brush is Brush b)
            {
                return b;
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
