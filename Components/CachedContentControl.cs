using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Lunex.Components
{
    // Cache views for ViewModels so we don't recreate them on every navigation. Essential for keeping WebView2 alive.
    public class CachedContentControl : ContentControl
    {
        private readonly Dictionary<object, UIElement> _viewCache = new();
        private Border? _container;

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _container = GetTemplateChild("PART_Container") as Border;
            UpdateView();
        }

        protected override void OnContentChanged(object oldContent, object newContent)
        {
            UpdateView();
        }

        private void UpdateView()
        {
            if (_container == null) return;

            var content = Content;
            if (content == null)
            {
                _container.Child = null;
                return;
            }

            if (!_viewCache.TryGetValue(content, out var view))
            {
                view = CreateViewForContent(content);
                if (view != null)
                {
                    _viewCache[content] = view;
                }
            }

            _container.Child = view;
        }

        private UIElement? CreateViewForContent(object content)
        {
            var templateKey = new DataTemplateKey(content.GetType());
            var template = TryFindResource(templateKey) as DataTemplate;

            if (template == null)
            {
                template = Application.Current.TryFindResource(templateKey) as DataTemplate;
            }

            if (template != null)
            {
                var element = template.LoadContent() as FrameworkElement;
                if (element != null)
                {
                    element.DataContext = content;
                    return element;
                }
            }

            // fallback if devs forgot to define a DataTemplate in App.xaml
            return new TextBlock
            {
                Text = $"No DataTemplate found for {content.GetType().Name}",
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(20)
            };
        }
    }
}
