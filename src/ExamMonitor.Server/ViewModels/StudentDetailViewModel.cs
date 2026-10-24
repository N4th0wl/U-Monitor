// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Server\ViewModels\StudentDetailViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExamMonitor.Common.Enums;
using ExamMonitor.Common.Models;
using ExamMonitor.Server.Services;

namespace ExamMonitor.Server.ViewModels;

public sealed class StudentDetailViewModel : ObservableObject
{
    private readonly SessionManagerService _sessionManager;
    private string _directMessage = string.Empty;
    private bool _isHighResStreaming;
    private bool _isRemoteControlEnabled;

    public StudentDetailViewModel(StudentSessionViewModel student, SessionManagerService sessionManager)
    {
        Student = student ?? throw new ArgumentNullException(nameof(student));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));

        LockStudentCommand = new AsyncRelayCommand(LockStudentAsync);
        UnlockStudentCommand = new AsyncRelayCommand(UnlockStudentAsync);
        TerminateStudentAppCommand = new AsyncRelayCommand(TerminateStudentAppAsync);
        SendDirectMessageCommand = new AsyncRelayCommand(SendDirectMessageAsync);
    }

    public StudentSessionViewModel Student { get; }

    public string DirectMessage
    {
        get => _directMessage;
        set => SetProperty(ref _directMessage, value);
    }

    public bool IsHighResStreaming
    {
        get => _isHighResStreaming;
        private set => SetProperty(ref _isHighResStreaming, value);
    }

    public bool IsRemoteControlEnabled
    {
        get => _isRemoteControlEnabled;
        set => SetProperty(ref _isRemoteControlEnabled, value);
    }

    public IRelayCommand LockStudentCommand { get; }
    public IRelayCommand UnlockStudentCommand { get; }
    public IRelayCommand TerminateStudentAppCommand { get; }
    public IRelayCommand SendDirectMessageCommand { get; }

    public async Task StartHighResInspectionAsync()
    {
        IsHighResStreaming = true;
        await _sessionManager.SendCommandAsync(Student.ClientId, ServerCommandType.RequestHighResStream);
    }

    public async Task StopHighResInspectionAsync()
    {
        IsHighResStreaming = false;
        IsRemoteControlEnabled = false;
        await _sessionManager.SendCommandAsync(Student.ClientId, ServerCommandType.StopHighResStream);
    }

    public async Task SendRemoteMouseAsync(double normalizedX, double normalizedY, int mouseButton)
    {
        if (!IsRemoteControlEnabled)
        {
            return;
        }

        var packet = new CommandPacket
        {
            TargetClientId = Student.ClientId,
            CommandType = ServerCommandType.RemoteMouseInput,
            NormalizedX = Math.Clamp(normalizedX, 0.0, 1.0),
            NormalizedY = Math.Clamp(normalizedY, 0.0, 1.0),
            MouseButton = mouseButton
        };

        await _sessionManager.SendCommandPacketAsync(Student.ClientId, packet);
    }

    public async Task SendRemoteKeyAsync(int virtualKey, bool isKeyUp)
    {
        if (!IsRemoteControlEnabled || virtualKey <= 0)
        {
            return;
        }

        var packet = new CommandPacket
        {
            TargetClientId = Student.ClientId,
            CommandType = ServerCommandType.RemoteKeyboardInput,
            VirtualKey = virtualKey,
            IsKeyUp = isKeyUp
        };

        await _sessionManager.SendCommandPacketAsync(Student.ClientId, packet);
    }

    private async Task LockStudentAsync()
    {
        await _sessionManager.SendCommandAsync(Student.ClientId, ServerCommandType.LockScreen, "Layar dikunci oleh Pengawas Ujian");
        Student.IsLocked = true;
        Student.Status = ClientStatus.Locked;
    }

    private async Task UnlockStudentAsync()
    {
        await _sessionManager.SendCommandAsync(Student.ClientId, ServerCommandType.UnlockScreen, "Layar dibuka kembali oleh Pengawas");
        Student.IsLocked = false;
        Student.Status = ClientStatus.Normal;
    }

    private async Task TerminateStudentAppAsync()
    {
        await _sessionManager.SendCommandAsync(Student.ClientId, ServerCommandType.TerminateApp, "Dihentikan oleh Pengawas");
        Student.Status = ClientStatus.AppTerminated;
        Student.IsActive = false;
    }

    private async Task SendDirectMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(DirectMessage))
        {
            return;
        }

        string msg = DirectMessage.Trim();
        await _sessionManager.SendCommandAsync(Student.ClientId, ServerCommandType.SendBroadcastMessage, msg);
        DirectMessage = string.Empty;
    }
}
