using LiteDB;
using Spaste.Models;
using Spaste.Services;

namespace Spaste;

/// <summary>
/// LiteDB 기반 히스토리 저장소다.
/// 최대 200건 수준의 작은 데이터셋을 전제로 두고 전체 교체 방식으로 단순하게 유지한다.
/// </summary>
internal sealed class HistoryManager
{
    private const string CollectionName = "clipboard_history";
    private readonly string _databasePath;
    private readonly Logger _logger;

    /// <summary>
    /// DB 경로와 로거를 받아 저장소를 초기화한다.
    /// </summary>
    public HistoryManager(string databasePath, Logger logger)
    {
        _databasePath = databasePath;
        _logger = logger;
    }

    /// <summary>
    /// 저장된 히스토리를 최신 순으로 읽는다.
    /// </summary>
    public IReadOnlyList<ClipboardItem> LoadAll(int maxItems)
    {
        try
        {
            using var database = OpenDatabase();
            var collection = database.GetCollection<ClipboardItem>(CollectionName);
            collection.EnsureIndex(item => item.CapturedAt);

            return collection.Query()
                .OrderByDescending(item => item.CapturedAt)
                .Limit(maxItems)
                .ToList();
        }
        catch (Exception exception)
        {
            _logger.Error("LiteDB에서 히스토리를 읽는 중 오류가 발생했다.", exception);
            return [];
        }
    }

    /// <summary>
    /// 현재 메모리 상태를 DB에 그대로 반영한다.
    /// </summary>
    public void ReplaceAll(IReadOnlyCollection<ClipboardItem> items)
    {
        try
        {
            using var database = OpenDatabase();
            var collection = database.GetCollection<ClipboardItem>(CollectionName);
            collection.DeleteAll();
            collection.EnsureIndex(item => item.CapturedAt);

            if (items.Count > 0)
            {
                collection.InsertBulk(items.OrderByDescending(item => item.CapturedAt));
            }

            database.Checkpoint();
        }
        catch (Exception exception)
        {
            _logger.Error("LiteDB 히스토리 동기화에 실패했다.", exception);
        }
    }

    /// <summary>
    /// LiteDB 연결을 연다.
    /// </summary>
    private LiteDatabase OpenDatabase()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        return new LiteDatabase(_databasePath);
    }
}
