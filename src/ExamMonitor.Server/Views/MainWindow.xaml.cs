// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Server\Views\MainWindow.xaml.cs
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ExamMonitor.Server.ViewModels;

namespace ExamMonitor.Server.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow() : this(new MainViewModel())
    {
    }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.StartServer();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        await _viewModel.DisposeAsync();
        base.OnClosing(e);
    }

    private void OnStudentCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StudentSessionViewModel student })
        {
            _viewModel.SelectedStudent = student;
            if (e.ClickCount == 2)
            {
                OpenStudentDetailDialog(student);
            }
        }
    }

    private void OnInspectStudentClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: StudentSessionViewModel student })
        {
            _viewModel.SelectedStudent = student;
            OpenStudentDetailDialog(student);
        }
    }

    private void OpenStudentDetailDialog(StudentSessionViewModel student)
    {
        var detailVm = new StudentDetailViewModel(student, _viewModel.SessionManager);
        var dialog = new StudentDetailDialog(detailVm)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void OnOpenSettingsClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(_viewModel.Settings)
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            _viewModel.GridColumns = _viewModel.Settings.GridColumns;
        }
    }
}
