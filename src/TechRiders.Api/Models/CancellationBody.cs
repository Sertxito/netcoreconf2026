namespace TechRiders.Api.Models;

public sealed record CancellationBody(string ReviewToken, bool Confirmed = false);
