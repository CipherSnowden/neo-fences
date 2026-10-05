using System.IO;
using NeoFences.Core.Lifecycle;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace NeoFences.App;

/// <summary>
/// The log line as before (the file sink's default template), with the user profile path written as %USERPROFILE% (M33):
/// a log attached to an issue does not show the Windows user name.
/// </summary>
public sealed class PrivateLogFormatter : ITextFormatter
{
    private readonly MessageTemplateTextFormatter _inner = new("{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
    private readonly string _profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public void Format(LogEvent logEvent, TextWriter output)
    {
        using var line = new StringWriter();
        _inner.Format(logEvent, line);
        output.Write(LogPrivacy.Mask(line.ToString(), _profile));
    }
}
