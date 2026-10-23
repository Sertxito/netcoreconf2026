using System.Diagnostics;

namespace TechRiders.Application;

public static class Telemetry
{
    public const string SourceName = "TechRiders";
    public static readonly ActivitySource Source = new(SourceName);
}
