using System.Windows;

namespace Illustra.Views
{
    public partial class EditPromptDialog : Window
    {
        public string PromptText { get; set; }

        public bool IsSaved { get; private set; }

        public EditPromptDialog(string initialPrompt)
        {
            InitializeComponent();
            PromptText = initialPrompt;
            DataContext = this;
            IsSaved = false;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            IsSaved = true;
            DialogResult = true;
            Close();
        }
    }
}
