using System.Windows.Controls;
using Lunex.Models;
using Lunex.ViewModels;

namespace Lunex.Components
{
    public partial class LunexSidebar : UserControl
    {
        public LunexSidebar()
        {
            InitializeComponent();
        }

        private void OnGameSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox listBox && listBox.SelectedItem is Game game)
            {
                if (DataContext is LibraryViewModel vm)
                {
                    // Reset immediately so the same game can be re-clicked
                    listBox.SelectedIndex = -1;
                    vm.SelectGameCommand.Execute(game);
                }
            }
        }
    }
}
