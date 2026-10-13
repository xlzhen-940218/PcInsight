using System;
using Microsoft.UI.Xaml.Markup;
using PcInsight.Core;

namespace PcInsight.Views;

/// <summary>
/// Markup extension that returns the <see cref="Babel"/> singleton, so XAML can bind
/// <c>{Binding L[key]}</c> and every label refreshes when the language changes.
/// </summary>
public sealed class LocExtension : MarkupExtension
{
    private readonly Babel _babel = Babel.L;

    protected override object ProvideValue() => _babel;
}
