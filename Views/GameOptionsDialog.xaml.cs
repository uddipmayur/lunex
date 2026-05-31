using System.Windows;
using System.Windows.Input;

namespace Lunex.Views
{
    public partial class GameOptionsDialog : Window
    {
        public bool PlaySelected { get; private set; }
        public bool CustomizeSelected { get; private set; }
        public bool RemoveSelected { get; private set; }
        private bool? _pendingDialogResult;
        private bool _isCloseAnimationCompleted = false;

        public GameOptionsDialog(string gameTitle)
        {
            InitializeComponent();
            TitleText.Text = $"OPTIONS: {gameTitle.ToUpper()}";
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void CloseWithAnimation(bool? result)
        {
            _pendingDialogResult = result;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isCloseAnimationCompleted)
            {
                e.Cancel = true;
                var sb = (System.Windows.Media.Animation.Storyboard)Resources["OnClosingStoryboard"];
                if (sb != null)
                {
                    sb.Completed += (s, ev) =>
                    {
                        _isCloseAnimationCompleted = true;
                        if (_pendingDialogResult.HasValue)
                        {
                            DialogResult = _pendingDialogResult.Value;
                        }
                        else
                        {
                            Close();
                        }
                    };
                    sb.Begin(this);
                }
                else
                {
                    _isCloseAnimationCompleted = true;
                    if (_pendingDialogResult.HasValue)
                    {
                        DialogResult = _pendingDialogResult.Value;
                    }
                    else
                    {
                        Close();
                    }
                }
            }
            base.OnClosing(e);
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            PlaySelected = true;
            CloseWithAnimation(true);
        }

        private void Customize_Click(object sender, RoutedEventArgs e)
        {
            CustomizeSelected = true;
            CloseWithAnimation(true);
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            RemoveSelected = true;
            CloseWithAnimation(true);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            CloseWithAnimation(false);
        }
    }
}
