using System.Windows;
using System.Windows.Controls;

namespace Lunex.Components
{
    public partial class GlassPanel : UserControl
    {
        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(GlassPanel), new PropertyMetadata(new CornerRadius(12)));

        public CornerRadius CornerRadius
        {
            get => (CornerRadius)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        public GlassPanel()
        {
            InitializeComponent();
        }
    }
}
