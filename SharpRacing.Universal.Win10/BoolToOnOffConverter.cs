using System;
using Windows.UI.Xaml.Data;

namespace SharpRacing.Universal.Win10
{
    public class BoolToOnOffConverter : IValueConverter
    {
        public BoolToOnOffConverter()
        {
        }

        public virtual object Convert(object value, Type targetType, object parameter, string language)
        {
            if (!(value is bool))
                return null;

            bool boolValue = (bool)value;
            return boolValue ? "on" : "off";
        }

        public virtual object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }
    }
}