// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Server\ViewModels\StudentSessionViewModel.cs
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using ExamMonitor.Common.Enums;
using ExamMonitor.Common.Models;

namespace ExamMonitor.Server.ViewModels;

public sealed class StudentSessionViewModel : ObservableObject
{
    private string _clientId = string.Empty;
    private string _studentId = string.Empty;
    private string _studentName = string.Empty;
    private string _machineName = string.Empty;
    private string _ipAddress = string.Empty;
    private string _osVersion = string.Empty;
    private ClientStatus _status = ClientStatus.Normal;
    private int _currentQuestionIndex = 1;
    private string _activeWindowTitle = "ExamBrowser";
    private DateTimeOffset _lastHeartbeatUtc = DateTimeOffset.UtcNow;
    private bool _isActive = true;
    private bool _isLocked;
    private BitmapSource? _thumbnailFrame;
    private BitmapSource? _highResFrame;
    private int _frameWidth;
    private int _frameHeight;
    private int _focusLostCount;

    public string ClientId
    {
        get => _clientId;
        set => SetProperty(ref _clientId, value);
    }

    public string StudentId
    {
        get => _studentId;
        set => SetProperty(ref _studentId, value);
    }

    public string StudentName
    {
        get => _studentName;
        set => SetProperty(ref _studentName, value);
    }

    public string MachineName
    {
        get => _machineName;
        set => SetProperty(ref _machineName, value);
    }

    public string IpAddress
    {
        get => _ipAddress;
        set => SetProperty(ref _ipAddress, value);
    }

    public string OsVersion
    {
        get => _osVersion;
        set => SetProperty(ref _osVersion, value);
    }

    public ClientStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusBadgeText));
                OnPropertyChanged(nameof(HasAlert));
            }
        }
    }

    public int CurrentQuestionIndex
    {
        get => _currentQuestionIndex;
        set => SetProperty(ref _currentQuestionIndex, value);
    }

    public string ActiveWindowTitle
    {
        get => _activeWindowTitle;
        set => SetProperty(ref _activeWindowTitle, value);
    }

    public DateTimeOffset LastHeartbeatUtc
    {
        get => _lastHeartbeatUtc;
        set => SetProperty(ref _lastHeartbeatUtc, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
            {
                OnPropertyChanged(nameof(StatusBadgeText));
                OnPropertyChanged(nameof(HasAlert));
            }
        }
    }

    public bool IsLocked
    {
        get => _isLocked;
        set => SetProperty(ref _isLocked, value);
    }

    public BitmapSource? ThumbnailFrame
    {
        get => _thumbnailFrame;
        set => SetProperty(ref _thumbnailFrame, value);
    }

    public BitmapSource? HighResFrame
    {
        get => _highResFrame;
        set => SetProperty(ref _highResFrame, value);
    }

    public int FrameWidth
    {
        get => _frameWidth;
        set
        {
            if (SetProperty(ref _frameWidth, value))
            {
                OnPropertyChanged(nameof(ResolutionText));
            }
        }
    }

    public int FrameHeight
    {
        get => _frameHeight;
        set
        {
            if (SetProperty(ref _frameHeight, value))
            {
                OnPropertyChanged(nameof(ResolutionText));
            }
        }
    }

    public int FocusLostCount
    {
        get => _focusLostCount;
        set => SetProperty(ref _focusLostCount, value);
    }

    public string ResolutionText => FrameWidth > 0 && FrameHeight > 0 ? $"{FrameWidth}x{FrameHeight}" : "N/A";

    public string StatusBadgeText => !IsActive ? "INACTIVE" : Status.ToString().ToUpperInvariant();

    public bool HasAlert => !IsActive || Status == ClientStatus.FocusLost || Status == ClientStatus.AppTerminated;

    public void ApplyHandshake(HandshakePacket packet)
    {
        ClientId = packet.ClientId;
        StudentId = packet.StudentId;
        StudentName = string.IsNullOrWhiteSpace(packet.StudentName) ? packet.StudentId : packet.StudentName;
        MachineName = packet.MachineName;
        IpAddress = packet.IpAddress;
        OsVersion = packet.OsVersion;
        LastHeartbeatUtc = packet.ConnectedAtUtc;
        IsActive = true;
        if (Status == ClientStatus.Disconnected)
        {
            Status = ClientStatus.Normal;
        }
    }

    public void ApplyHeartbeat(HeartbeatPacket packet)
    {
        if (packet.Status == ClientStatus.FocusLost && Status != ClientStatus.FocusLost)
        {
            FocusLostCount++;
        }

        Status = packet.Status;
        CurrentQuestionIndex = packet.CurrentQuestionIndex;
        if (!string.IsNullOrWhiteSpace(packet.ActiveWindowTitle))
        {
            ActiveWindowTitle = packet.ActiveWindowTitle;
        }
        LastHeartbeatUtc = packet.Timestamp;
        IsActive = packet.Status != ClientStatus.AppTerminated && packet.Status != ClientStatus.Disconnected;
        IsLocked = packet.Status == ClientStatus.Locked;
    }
}
