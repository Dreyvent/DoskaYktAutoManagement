using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows;

namespace DoskaYkt_AutoManagement.Converters
{
    public class NullOrEmptyToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = value?.ToString();
            return !string.IsNullOrWhiteSpace(s);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class TimeRemainingConverter : IMultiValueConverter, IValueConverter
    {
        // Single binding fallback
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;
            DateTime when;
            if (value is DateTime dt) when = dt;
            else if (value is DateTimeOffset dto) when = dto.DateTime;
            else return string.Empty;

            var remaining = when - DateTime.Now;
            if (remaining.TotalSeconds <= 0) return "0 мин";
            var mins = Math.Max(0, (int)Math.Round(remaining.TotalMinutes));
            return mins + " мин";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return DependencyProperty.UnsetValue;
        }

        // MultiBinding: values[0] = DateTime, values[1] = NowTick (int)
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length == 0 || values[0] == null) return string.Empty;
            return Convert(values[0], targetType, parameter, culture);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            return null;
        }
    }

    public class DateTimeDueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return false;
            DateTime when;
            if (value is DateTime dt) when = dt;
            else if (value is DateTimeOffset dto) when = dto.DateTime;
            else return false;
            return when <= DateTime.Now;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return DependencyProperty.UnsetValue;
        }
    }
}
