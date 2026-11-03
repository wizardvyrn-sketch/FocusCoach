using System.Text.Json;
using FocusCoach.Core;

namespace FocusCoach.Storage;

/// <summary>JSONL の1行 = 1イベント(docs/spec/04 §4.3)。キー名は仕様どおり seq / ts / type / data。</summary>
internal sealed record JsonlLine(long seq, DateTimeOffset ts, string type, Dictionary<string, string> data);

public static class JsonlFormat
{
    private static readonly JsonSerializerOptions Opt = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string ToLine(CoreEvent e) =>
        JsonSerializer.Serialize(new JsonlLine(e.Seq, e.Ts, e.Type, new Dictionary<string, string>(e.Data)), Opt);

    public static CoreEvent FromLine(string line)
    {
        var l = JsonSerializer.Deserialize<JsonlLine>(line, Opt) ?? throw new FormatException("empty line");
        return new CoreEvent(l.seq, l.ts, l.type, l.data ?? []);
    }
}

/// <summary>
/// セッションイベントログのライター。1イベントごとに追記して Flush する(08章 §8.3: クラッシュしても直前まで残す)。
/// </summary>
public sealed class JsonlEventWriter : IEventSink, IDisposable
{
    private readonly StreamWriter _w;
    public string Path { get; }

    public JsonlEventWriter(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        // 既存ファイルへの追記(異常終了・持ち越しからの再開)では、途中で切れた末尾行を先に取り除く(R-09)
        RepairTail(path);
        var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        _w = new StreamWriter(fs, new System.Text.UTF8Encoding(false)) { AutoFlush = false };
    }

    /// <summary>
    /// ファイルが改行で終わっていなければ、最後の改行の後ろ(書きかけの行)を切り詰める。切り詰めたバイト数を返す。
    /// そのまま追記すると、書きかけの行と新しい行が1行につながり、ファイルの途中が壊れるため。
    /// </summary>
    public static long RepairTail(string path)
    {
        if (!File.Exists(path)) return 0;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        if (fs.Length == 0) return 0;
        var pos = fs.Length - 1;
        fs.Position = pos;
        if (fs.ReadByte() == '\n') return 0;
        while (pos > 0)
        {
            fs.Position = pos - 1;
            if (fs.ReadByte() == '\n') break;
            pos--;
        }
        var cut = fs.Length - pos;
        fs.SetLength(pos);
        return cut;
    }

    public static string PathFor(string dataRoot, string runId) => System.IO.Path.Combine(dataRoot, "sessions", runId + ".jsonl");

    public void Append(CoreEvent e)
    {
        _w.WriteLine(JsonlFormat.ToLine(e));
        _w.Flush();
        if (_w.BaseStream is FileStream fs) fs.Flush(flushToDisk: true);
    }

    public void Dispose() => _w.Dispose();
}

public static class JsonlEventReader
{
    /// <summary>
    /// 読み込む。クラッシュで最後の行が欠けている場合は、その行だけ捨てる([仮案-13])。
    /// </summary>
    public static List<CoreEvent> Read(string path)
    {
        var res = new List<CoreEvent>();
        // ライターが開いたままでも読めるように ReadWrite 共有で開く。File.ReadAllLines は共有が Read だけなので、
        // Windows では書き込み中のファイルを開けない(Linux は排他しないので、クラウドのテストでは出なかった)。
        var lines = new List<string>();
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var sr = new StreamReader(fs, System.Text.Encoding.UTF8))
            while (sr.ReadLine() is { } l) lines.Add(l);
        for (var i = 0; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            try { res.Add(JsonlFormat.FromLine(lines[i])); }
            catch (JsonException) when (i == lines.Count - 1) { /* 途中で切れた末尾行 */ }
        }
        return res;
    }
}
