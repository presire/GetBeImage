using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace GetBeImage
{
    public class FontSizeConverter : IValueConverter
    {
        public static readonly FontSizeConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not double width) return 14.0;  // デフォルト値
            return width switch
            {
                // 画面の幅に基づいてフォントサイズを決定
                >= 2500 => 16.0,
                >= 2000 => 15.0,
                >= 1500 => 14.0,
                _ => 12.0
            };
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class WidthConverter : IValueConverter
    {
        public static readonly WidthConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double width)
            {
                // ウインドウの幅の80%を返す
                return width * 0.8;
            }
            
            return 850.0; // デフォルト値
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class HeightConverter : IValueConverter
    {
        public static readonly HeightConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double height)
            {
                // ウインドウの高さの40%を返す
                return height * 0.4;
            }
            
            return 300.0; // デフォルト値
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}