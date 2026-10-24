// c:\Users\ivand\Documents\Coding\Project\U-Monitor\src\ExamMonitor.Common\Enums\ServerCommandType.cs
namespace ExamMonitor.Common.Enums;

public enum ServerCommandType
{
    LockScreen = 0,
    UnlockScreen = 1,
    TerminateApp = 2,
    SendBroadcastMessage = 3,
    RequestHighResStream = 4,
    StopHighResStream = 5,
    RemoteMouseInput = 6,
    RemoteKeyboardInput = 7
}
