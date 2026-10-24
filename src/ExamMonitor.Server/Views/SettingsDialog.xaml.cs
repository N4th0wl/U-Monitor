// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Server\Views\SettingsDialog.xaml.cs
using System.Windows;
using ExamMonitor.Server.ViewModels;

namespace ExamMonitor.Server.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
