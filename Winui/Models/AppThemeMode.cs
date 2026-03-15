using System.Text.Json.Serialization;

namespace Pelicano.Models;

/// <summary>
/// Pelicano UI 테마 적용 방식을 정의한다.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2
}
