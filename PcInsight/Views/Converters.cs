using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using PcInsight.Core;

namespace PcInsight.Views;

/// <summary>Maps a <see cref="Tone"/> to the matching severity brush declared in App.xaml.</summary>
public sealed class ToneBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var tone = value is Tone t ? t : Tone.Normal;
        var key = tone switch
        {
            Tone.Good => "ToneGoodBrush",
            Tone.Warn => "ToneWarnBrush",
            Tone.Bad => "ToneBadBrush",
            Tone.Muted => "ToneMutedBrush",
            _ => "ToneNormalBrush",
        };

        return Lookup(key);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();

    internal static Brush Lookup(string key)
    {
        try
        {
            if (Application.Current?.Resources is { } res)
            {
                if (res.TryGetValue(key, out var brush) && brush is Brush b)
                {
                    return b;
                }

                if (Application.Current.Resources.ThemeDictionaries.TryGetValue("Default", out var dict)
                    && dict is ResourceDictionary def
                    && def.TryGetValue(key, out var brush2)
                    && brush2 is Brush b2)
                {
                    return b2;
                }
            }
        }
        catch
        {
            // fall through to the neutral brush
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }
}

/// <summary>Row index → alternating background, used for long tables.</summary>
public sealed class RowBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>Formats a string-table key found in a data structure at runtime.</summary>
public sealed class LocalizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is string key ? Str.Resolve(key, Babel.L.Current) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value switch
        {
            bool b => b,
            string s => !string.IsNullOrWhiteSpace(s),
            int i => i > 0,
            _ => value is not null,
        };

        if (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>Hides an element when its bound collection is null or empty.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var count = value switch
        {
            int i => i,
            System.Collections.ICollection c => c.Count,
            null => 0,
            _ => 1,
        };

        var invert = parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase);
        var visible = count > 0;
        if (invert)
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
