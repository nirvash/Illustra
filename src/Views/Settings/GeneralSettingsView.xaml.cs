using System.Windows;
using System.Windows.Controls;

namespace Illustra.Views.Settings
{
    public partial class GeneralSettingsView : UserControl
    {
        public GeneralSettingsView()
        {
            InitializeComponent();

            Loaded += GeneralSettingsView_Loaded;
        }

        private void GeneralSettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            CheckBoxStyleInfo(CheckBox);
        }

        private void CheckBoxStyleInfo(CheckBox checkBox)
        {
            var style = checkBox.Style;
            if (style != null)
            {
                Console.WriteLine("Style Key: " + style.TargetType);
                foreach (var setter in style.Setters)
                {
                    if (setter is Setter s)
                    {
                        Console.WriteLine($"Property: {s.Property}, Value: {s.Value}");
                    }
                }
            }
            else
            {
                Console.WriteLine("No style applied.");
            }
        }

    }
}
