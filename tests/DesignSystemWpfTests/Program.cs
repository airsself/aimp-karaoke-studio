using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace KaraokeStudio.DesignTests;

/// <summary>Real WPF/BAML smoke for shared control templates; mock-free.</summary>
internal static class Program
{
    [STAThread]
    public static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Window? host = null;
        try
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary {
                Source = new Uri("pack://application:,,,/Styles/Tokens.xaml", UriKind.Absolute)
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary {
                Source = new Uri("pack://application:,,,/Styles/Components.xaml", UriKind.Absolute)
            });

            var field = new TextBox { Text = "LibreTranslate server",
                Style = (Style)app.FindResource("StudioTextBox") };
            var choices = new ComboBox {
                Style = (Style)app.FindResource("StudioComboBox")
            };
            choices.Items.Add(new ComboBoxItem { Content = "MyMemory" });
            choices.Items.Add(new ComboBoxItem { Content = "LibreTranslate" });
            choices.Items.Add(new ComboBoxItem { Content = "DeepL" });
            choices.SelectedIndex = 1;
            var check = new CheckBox { Content = "Автопоиск", IsChecked = true,
                Style = (Style)app.FindResource("SettingCheck") };
            var button = new Button { Content = "Применить",
                Style = (Style)app.FindResource("PrimaryButton") };
            var results = new ListBox {
                ItemContainerStyle = (Style)app.FindResource("DiscoveryResultItem")
            };
            results.Items.Add("LRCLIB • synced");
            results.Items.Add("lyrics.ovh • plain");
            results.SelectedIndex = 0;

            var panel = new StackPanel { Margin = new Thickness(18) };
            foreach (var child in new UIElement[] { field, choices, check, button, results })
            {
                panel.Children.Add(child);
                if (child is FrameworkElement f) f.Margin = new Thickness(0, 0, 0, 12);
            }
            host = new Window { Title = "Design system WPF smoke", Width = 480,
                Height = 480, Content = panel, ShowInTaskbar = false };
            host.Show();
            host.UpdateLayout();
            field.ApplyTemplate();
            choices.ApplyTemplate();
            check.ApplyTemplate();
            button.ApplyTemplate();
            results.UpdateLayout();

            Check(field.Template.FindName("PART_ContentHost", field) is ScrollViewer,
                "TextBox content host");
            Check(choices.Template.FindName("PART_Popup", choices) is Popup,
                "ComboBox dropdown popup");
            Check(choices.SelectedIndex == 1 &&
                choices.SelectionBoxItem?.ToString()?.Contains("LibreTranslate") == true,
                "ComboBox selected text");
            Check(check.Template.FindName("CheckMark", check) is TextBlock mark &&
                mark.Visibility == Visibility.Visible, "Checked mark");
            Check(button.MinHeight >= 32, "Button touch geometry");
            Check(results.SelectedIndex == 0 && results.ItemContainerStyle is not null,
                "Search selection styling");

            // Test appearance state without relying on an interactive CI desktop.
            var border = choices.Template.FindName("ComboFrame", choices) as Border;
            Check(border is not null && border.CornerRadius.TopLeft >= 4,
                "Combo surface geometry");
            Console.WriteLine("WPF design components loaded and selection/templates verified");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL design system WPF smoke: " + error);
            return 1;
        }
        finally
        {
            host?.Close();
            app.Shutdown();
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
    }
}
