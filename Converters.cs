using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Lunex
{
    /// <summary>Converts bool → Visibility (true = Visible, false = Collapsed).</summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility v && v == Visibility.Visible;
    }

    /// <summary>Collapses the element when the string value is null or empty.</summary>
    public class NullOrEmptyToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Converts bool → Visibility (true = Collapsed, false = Visible).</summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility v && v == Visibility.Collapsed;
    }

    /// <summary>Converts window width to compact boolean state (true if width &lt; 640).</summary>
    public class WidthToIsCompactConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double width)
            {
                return width < 640.0;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Helper class to simulate CharacterSpacing in WPF TextBlock using TextEffects.</summary>
    public static class TypographyHelper
    {
        public static readonly DependencyProperty CharacterSpacingProperty =
            DependencyProperty.RegisterAttached(
                "CharacterSpacing",
                typeof(int),
                typeof(TypographyHelper),
                new PropertyMetadata(0, OnCharacterSpacingChanged));

        public static int GetCharacterSpacing(DependencyObject obj) => (int)obj.GetValue(CharacterSpacingProperty);
        public static void SetCharacterSpacing(DependencyObject obj, int value) => obj.SetValue(CharacterSpacingProperty, value);

        private static void OnCharacterSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBlock textBlock)
            {
                textBlock.Loaded -= OnTextBlockLoaded;
                textBlock.Loaded += OnTextBlockLoaded;

                var dpd = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
                if (dpd != null)
                {
                    dpd.RemoveValueChanged(textBlock, OnTextChanged);
                    dpd.AddValueChanged(textBlock, OnTextChanged);
                }

                ApplySpacing(textBlock);
            }
        }

        private static void OnTextBlockLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBlock textBlock)
            {
                ApplySpacing(textBlock);
            }
        }

        private static void OnTextChanged(object? sender, EventArgs e)
        {
            if (sender is TextBlock textBlock)
            {
                ApplySpacing(textBlock);
            }
        }

        private static void ApplySpacing(TextBlock textBlock)
        {
            int spacing = GetCharacterSpacing(textBlock);
            if (spacing == 0 || string.IsNullOrEmpty(textBlock.Text))
            {
                textBlock.TextEffects = null;
                return;
            }

            // Spacing is in 1/1000 of an em. spacingPixels = (spacing / 1000.0) * fontSize
            double spacingPixels = (spacing / 1000.0) * textBlock.FontSize;

            var effects = new TextEffectCollection();
            string text = textBlock.Text;
            for (int i = 1; i < text.Length; i++)
            {
                var effect = new TextEffect
                {
                    PositionStart = i,
                    PositionCount = 1,
                    Transform = new TranslateTransform(i * spacingPixels, 0)
                };
                effects.Add(effect);
            }
            textBlock.TextEffects = effects;
        }
    }

    /// <summary>Converts Game → ImageSource (Icon) for the game list sidebar.</summary>
    public class GameToIconConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Models.Game game)
            {
                return Components.GameCard.GetGameIcon(game.IconPath, game.ExePath);
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
