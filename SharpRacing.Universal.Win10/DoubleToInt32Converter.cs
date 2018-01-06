using System;
using Windows.UI.Xaml.Data;

namespace SharpRacing.Universal.Win10
{
    public class DoubleToInt32Converter : IValueConverter
    {
        public DoubleToInt32Converter()
        {
        }

        public virtual object Convert(object value, Type targetType, object parameter, string language)
        {
            if (!(value is double))
                return null;

            Int32 intValue = (int)(double)value;
            return intValue;
        }

        public virtual object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }
    }
}