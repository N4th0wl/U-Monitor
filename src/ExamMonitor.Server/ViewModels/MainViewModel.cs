// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Server\ViewModels\MainViewModel.cs
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExamMonitor.Common;
using ExamMonitor.Common.Enums;
using ExamMonitor.Common.Models;
using ExamMonitor.Server.Services;

namespace ExamMonitor.Server.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly DiscoveryBroadcasterService _discoveryBroadcaster;
    private readonly SessionManagerService _sessionManager;
    private readonly FrameDispatcherService _frameDispatcher;
    private readonly Dispatcher? _uiDispatcher;

    private StudentSessionViewModel? _selectedStudent;
    private string _broadcastMessageText = string.Empty;
    private string _searchQuery = string.Empty;
    private string _serverStatusText = "Starting...";
    private int _gridColumns = 4;

    public MainViewModel(
        DiscoveryBroadcasterService? discoveryBroadcaster = null,
        SessionManagerService? sessionManager = null,
        FrameDispatcherService? frameDispatcher = null,
        Dispatcher? uiDispatcher = null)
    {
        _uiDispatcher = uiDispatcher ?? Application.Current?.Dispatcher;
        _discoveryBroadcaster = discoveryBroadcaster ?? new DiscoveryBroadcasterService();
        _sessionManager = sessionManager ?? new SessionManagerService();
        _frameDispatcher = frameDispatcher ?? new FrameDispatcherService(_uiDispatcher);

        Settings = new SettingsViewModel();
        Students = new ObservableCollection<StudentSessionViewModel>();

        _sessionManager.HandshakeReceived += OnHandshakeReceived;
        _sessionManager.HeartbeatReceived += OnHeartbeatReceived;
        _sessionManager.FrameReceived += OnFrameReceived;
        _sessionManager.SessionMarkedInactive += OnSessionMarkedInactive;
        _sessionManager.ClientDisconnected += OnClientDisconnected;
        _frameDispatcher.FrameDecoded += OnFrameDecoded;

        LockAllCommand = new AsyncRelayCommand(LockAllAsync);
        UnlockAllCommand = new AsyncRelayCommand(UnlockAllAsync);
        BroadcastMessageCommand = new AsyncRelayCommand(BroadcastMessageAsync);
        LockSelectedCommand = new AsyncRelayCommand(LockSelectedAsync);
        UnlockSelectedCommand = new AsyncRelayCommand(UnlockSelectedAsync);
        TerminateSelectedCommand = new AsyncRelayCommand(TerminateSelectedAsync);
    }

    public ObservableCollection<StudentSessionViewModel> Students { get; }

    public SettingsViewModel Settings { get; }

    public SessionManagerService SessionManager => _sessionManager;

    public StudentSessionViewModel? SelectedStudent
    {
        get => _selectedStudent;
        set => SetProperty(ref _selectedStudent, value);
    }

    public string BroadcastMessageText
    {
        get => _broadcastMessageText;
        set => SetProperty(ref _broadcastMessageText, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set => SetProperty(ref _searchQuery, value);
    }

    public string ServerStatusText
    {
        get => _serverStatusText;
        set => SetProperty(ref _serverStatusText, value);
    }

    public int GridColumns
    {
        get => _gridColumns;
        set => SetProperty(ref _gridColumns, Math.Clamp(value, 1, 8));
    }

    public int TotalConnectedCount => Students.Count(s => s.IsActive);

    public int AlertCount => Students.Count(s => s.HasAlert);

    public IRelayCommand LockAllCommand { get; }
    public IRelayCommand UnlockAllCommand { get; }
    public IRelayCommand BroadcastMessageCommand { get; }
    public IRelayCommand LockSelectedCommand { get; }
    public IRelayCommand UnlockSelectedCommand { get; }
    public IRelayCommand TerminateSelectedCommand { get; }

    public void StartServer(int tcpPort = ProtocolConstants.DefaultServerTcpPort)
    {
        _sessionManager.Start(tcpPort);
        _discoveryBroadcaster.Start();
        string lanIp = DiscoveryBroadcasterService.ResolvePrimaryLanIp();
        ServerStatusText = $"ONLINE — IP {lanIp}:{_sessionManager.ListeningPort} | UDP Discovery :{ProtocolConstants.DiscoveryUdpPort}";
    }

    public async Task RequestHighResStreamAsync(StudentSessionViewModel student)
    {
        ArgumentNullException.ThrowIfNull(student);
        await _sessionManager.SendCommandAsync(student.ClientId, ServerCommandType.RequestHighResStream);
    }

    public async Task StopHighResStreamAsync(StudentSessionViewModel student)
    {
        ArgumentNullException.ThrowIfNull(student);
        await _sessionManager.SendCommandAsync(student.ClientId, ServerCommandType.StopHighResStream);
    }

    private async Task LockAllAsync()
    {
        await _sessionManager.BroadcastCommandAsync(ServerCommandType.LockScreen, "Exam Paused — Screen Locked by Proctor");
        RunOnUi(() =>
        {
            foreach (StudentSessionViewModel s in Students)
            {
                s.IsLocked = true;
                s.Status = ClientStatus.Locked;
            }
            NotifySummaryCounts();
        });
    }

    private async Task UnlockAllAsync()
    {
        await _sessionManager.BroadcastCommandAsync(ServerCommandType.UnlockScreen, "Exam Resumed");
        RunOnUi(() =>
        {
            foreach (StudentSessionViewModel s in Students)
            {
                s.IsLocked = false;
                if (s.IsActive)
                {
                    s.Status = ClientStatus.Normal;
                }
            }
            NotifySummaryCounts();
        });
    }

    private async Task BroadcastMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(BroadcastMessageText))
        {
            return;
        }

        string message = BroadcastMessageText.Trim();
        await _sessionManager.BroadcastCommandAsync(ServerCommandType.SendBroadcastMessage, message);
        BroadcastMessageText = string.Empty;
    }

    private async Task LockSelectedAsync()
    {
        if (SelectedStudent is null)
        {
            return;
        }

        await _sessionManager.SendCommandAsync(SelectedStudent.ClientId, ServerCommandType.LockScreen, "Screen Locked by Proctor");
        SelectedStudent.IsLocked = true;
        SelectedStudent.Status = ClientStatus.Locked;
        NotifySummaryCounts();
    }

    private async Task UnlockSelectedAsync()
    {
        if (SelectedStudent is null)
        {
            return;
        }

        await _sessionManager.SendCommandAsync(SelectedStudent.ClientId, ServerCommandType.UnlockScreen, "Screen Unlocked");
        SelectedStudent.IsLocked = false;
        SelectedStudent.Status = ClientStatus.Normal;
        NotifySummaryCounts();
    }

    private async Task TerminateSelectedAsync()
    {
        if (SelectedStudent is null)
        {
            return;
        }

        await _sessionManager.SendCommandAsync(SelectedStudent.ClientId, ServerCommandType.TerminateApp, "Terminated by Proctor");
        SelectedStudent.Status = ClientStatus.AppTerminated;
        SelectedStudent.IsActive = false;
        NotifySummaryCounts();
    }

    private void OnHandshakeReceived(HandshakePacket packet)
    {
        RunOnUi(() =>
        {
            StudentSessionViewModel? vm = Students.FirstOrDefault(s => string.Equals(s.ClientId, packet.ClientId, StringComparison.OrdinalIgnoreCase));
            if (vm is null)
            {
                vm = new StudentSessionViewModel();
                vm.ApplyHandshake(packet);
                Students.Add(vm);
            }
            else
            {
                vm.ApplyHandshake(packet);
            }
            NotifySummaryCounts();
        });
    }

    private void OnHeartbeatReceived(HeartbeatPacket packet)
    {
        RunOnUi(() =>
        {
            StudentSessionViewModel? vm = Students.FirstOrDefault(s => string.Equals(s.ClientId, packet.ClientId, StringComparison.OrdinalIgnoreCase));
            if (vm is null)
            {
                vm = new StudentSessionViewModel
                {
                    ClientId = packet.ClientId,
                    StudentId = packet.ClientId,
                    StudentName = packet.ClientId
                };
                vm.ApplyHeartbeat(packet);
                Students.Add(vm);
            }
            else
            {
                vm.ApplyHeartbeat(packet);
            }
            NotifySummaryCounts();
        });
    }

    private void OnFrameReceived(FramePacket packet)
    {
        _frameDispatcher.EnqueueFrame(packet);
    }

    private void OnFrameDecoded(object? sender, DecodedFrameEventArgs e)
    {
        RunOnUi(() =>
        {
            StudentSessionViewModel? vm = Students.FirstOrDefault(s => string.Equals(s.ClientId, e.ClientId, StringComparison.OrdinalIgnoreCase));
            if (vm is null)
            {
                return;
            }

            vm.FrameWidth = e.Width;
            vm.FrameHeight = e.Height;
            vm.ThumbnailFrame = e.Image;
            if (e.IsHighResolution)
            {
                vm.HighResFrame = e.Image;
            }
        });
    }

    private void OnSessionMarkedInactive(string clientId)
    {
        RunOnUi(() =>
        {
            StudentSessionViewModel? vm = Students.FirstOrDefault(s => string.Equals(s.ClientId, clientId, StringComparison.OrdinalIgnoreCase));
            if (vm is not null)
            {
                vm.IsActive = false;
                vm.Status = ClientStatus.Disconnected;
                NotifySummaryCounts();
            }
        });
    }

    private void OnClientDisconnected(string clientId)
    {
        OnSessionMarkedInactive(clientId);
    }

    private void NotifySummaryCounts()
    {
        OnPropertyChanged(nameof(TotalConnectedCount));
        OnPropertyChanged(nameof(AlertCount));
    }

    private void RunOnUi(Action action)
    {
        if (_uiDispatcher is not null && !_uiDispatcher.CheckAccess())
        {
            _uiDispatcher.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _discoveryBroadcaster.DisposeAsync().ConfigureAwait(false);
        await _sessionManager.DisposeAsync().ConfigureAwait(false);
    }
}
