using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Lunex.Components
{
    public static class ScrollViewerHelper
    {
        public static readonly DependencyProperty IsSmoothScrollEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsSmoothScrollEnabled",
                typeof(bool),
                typeof(ScrollViewerHelper),
                new PropertyMetadata(false, OnIsSmoothScrollEnabledChanged));

        public static bool GetIsSmoothScrollEnabled(DependencyObject obj) => (bool)obj.GetValue(IsSmoothScrollEnabledProperty);
        public static void SetIsSmoothScrollEnabled(DependencyObject obj, bool value) => obj.SetValue(IsSmoothScrollEnabledProperty, value);

        // Custom dependency property to animate the vertical offset
        public static readonly DependencyProperty CurrentVerticalOffsetProperty =
            DependencyProperty.RegisterAttached(
                "CurrentVerticalOffset",
                typeof(double),
                typeof(ScrollViewerHelper),
                new PropertyMetadata(0.0, OnCurrentVerticalOffsetChanged));

        public static double GetCurrentVerticalOffset(DependencyObject obj) => (double)obj.GetValue(CurrentVerticalOffsetProperty);
        public static void SetCurrentVerticalOffset(DependencyObject obj, double value) => obj.SetValue(CurrentVerticalOffsetProperty, value);

        private static void OnCurrentVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToVerticalOffset((double)e.NewValue);
            }
        }

        private static void OnIsSmoothScrollEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer scrollViewer)
            {
                if ((bool)e.NewValue)
                {
                    scrollViewer.PreviewMouseWheel += ScrollViewer_PreviewMouseWheel;
                }
                else
                {
                    scrollViewer.PreviewMouseWheel -= ScrollViewer_PreviewMouseWheel;
                }
            }
        }

        private static void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                // Calculate the target offset
                double delta = e.Delta;
                double increment;

                if (scrollViewer.CanContentScroll)
                {
                    // For logical scrolling, scroll by a few items (e.g., 2 items per wheel notch)
                    increment = (delta > 0 ? 1 : -1) * 2;
                }
                else
                {
                    // For pixel scrolling, scroll by pixels
                    double scrollSpeed = 3.0;
                    increment = delta * scrollSpeed;
                }

                double targetOffset = Math.Max(0, Math.Min(scrollViewer.ScrollableHeight, GetTargetOffset(scrollViewer) - increment));

                // Animate to target offset
                AnimateScroll(scrollViewer, targetOffset);
                e.Handled = true;
            }
        }

        private static double GetTargetOffset(ScrollViewer scrollViewer)
        {
            var localVal = scrollViewer.ReadLocalValue(CurrentVerticalOffsetProperty);
            if (localVal == DependencyProperty.UnsetValue)
            {
                return scrollViewer.VerticalOffset;
            }
            
            double currentTarget = (double)localVal;
            if (Math.Abs(currentTarget - scrollViewer.VerticalOffset) > 300)
            {
                return scrollViewer.VerticalOffset;
            }
            return currentTarget;
        }

        private static void AnimateScroll(ScrollViewer scrollViewer, double targetOffset)
        {
            if (scrollViewer.ReadLocalValue(CurrentVerticalOffsetProperty) == DependencyProperty.UnsetValue)
            {
                SetCurrentVerticalOffset(scrollViewer, scrollViewer.VerticalOffset);
            }

            var animation = new DoubleAnimation
            {
                To = targetOffset,
                Duration = TimeSpan.FromMilliseconds(200),
                DecelerationRatio = 0.8
            };

            scrollViewer.BeginAnimation(CurrentVerticalOffsetProperty, animation);
        }
    }
}
