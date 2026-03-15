using Microsoft.UI.Xaml.Controls;

namespace Pelicano;

/// <summary>
/// 설정창과 메인 창 사이에서 상태 메시지와 표시 강도를 함께 전달한다.
/// </summary>
internal readonly record struct StatusFeedback(string Message, InfoBarSeverity Severity);
