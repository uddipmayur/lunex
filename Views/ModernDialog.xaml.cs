using System.Windows;
using System.Windows.Input;

namespace Lunex.Views
{
    public partial class ModernDialog : Window
    {
        public bool Result { get; set; } = false;
        private bool? _pendingDialogResult;
        private bool _isCloseAnimationCompleted = false;

        public ModernDialog(string title, string message, bool isConfirmation = false)
        {
            InitializeComponent();
            TitleText.Text = title.ToUpper();
            MessageText.Text = message;

            if (isConfirmation)
            {
                OkButton.Visibility = Visibility.Collapsed;
                ConfirmButtons.Visibility = Visibility.Visible;
            }
            else
            {
                OkButton.Visibility = Visibility.Visible;
                ConfirmButtons.Visibility = Visibility.Collapsed;
            }
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

        private void CloseDialog(object sender, RoutedEventArgs e)
        {
            CloseWithAnimation(true);
        }

        private void Yes_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            CloseWithAnimation(true);
        }

        private void No_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            CloseWithAnimation(false);
        }
    }
}
