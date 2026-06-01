using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Lunex.Components
{
    // Cache views for ViewModels so we don't recreate them on every navigation.
    // Uses a ContentPresenter per ViewModel instance so WPF resolves DataTemplates and
    // StaticResources correctly (they need to be inside the logical/visual tree).
    [TemplatePart(Name = "PART_Container", Type = typeof(Border))]
    public class CachedContentControl : ContentControl
    {
        private readonly Dictionary<object, ContentPresenter> _viewCache = new();
        private Border? _container;

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _container = GetTemplateChild("PART_Container") as Border;
            // Show whatever is already bound when the template first applies
            if (Content != null)
                SwapView(Content);
        }

        protected override void OnContentChanged(object oldContent, object newContent)
        {
            base.OnContentChanged(oldContent, newContent);
            SwapView(newContent);
        }

        private void SwapView(object? content)
        {
            if (_container == null) return;

            if (content == null)
            {
                _container.Child = null;
                return;
            }

            // Reuse the cached ContentPresenter for this ViewModel instance, or create one.
            // ContentPresenter lives inside the visual tree, so StaticResources resolve correctly.
            if (!_viewCache.TryGetValue(content, out var presenter))
            {
                presenter = new ContentPresenter { Content = content };
                _viewCache[content] = presenter;
            }

            _container.Child = presenter;
        }
    }
}
