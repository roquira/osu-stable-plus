using System;
using System.IO;
using System.Text;

namespace Osu.StablePlus.Hook;

/// <summary>Retain previous sessions and flush each timestamped entry for crash diagnosis.</summary>
internal sealed class SessionLog : TextWriter
{
    private readonly TextWriter output;
    private readonly object sync = new();
    private bool lineStart = true;
    private SessionLog(TextWriter output) { this.output = output; }
    public override Encoding Encoding => Encoding.UTF8;

    internal static SessionLog Open(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Product.LogFilename);
        if (File.Exists(path))
        {
            var archive = Path.Combine(directory, "Logs", "osu-stable-plus");
            Directory.CreateDirectory(archive);
            // Preserve the old file before truncating it, including logs from
            // versions without timestamps. Never prune crash history here.
            var name = File.GetLastWriteTimeUtc(path).ToString("yyyyMMdd-HHmmss-fffffff") + "-" + Guid.NewGuid().ToString("N") + ".log";
            File.Copy(path, Path.Combine(archive, name));
        }
        var writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false))
        { AutoFlush = true };
        var log = new SessionLog(writer);
        log.WriteLine($"[Session] {Product.Name} {Product.Version}; PID {System.Diagnostics.Process.GetCurrentProcess().Id}; timestamps UTC.");
        return log;
    }

    public override void Write(char value) => Write(value.ToString());
    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        lock (sync)
        {
            var text = new StringBuilder();
            foreach (var c in value!)
            {
                if (lineStart) { text.Append('[').Append(DateTime.UtcNow.ToString("O")).Append("] "); lineStart = false; }
                text.Append(c);
                if (c == '\n') lineStart = true;
            }
            output.Write(text.ToString());
        }
    }
    public override void WriteLine(string? value) => Write((value ?? "") + NewLine);
    public override void WriteLine() => Write(NewLine);
    public override void Flush() { lock (sync) output.Flush(); }
    protected override void Dispose(bool disposing) { if (disposing) { lock (sync) output.Dispose(); } base.Dispose(disposing); }
}
