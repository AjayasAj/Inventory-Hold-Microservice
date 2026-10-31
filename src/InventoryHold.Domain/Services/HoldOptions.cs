namespace InventoryHold.Domain.Services;

public sealed class HoldOptions
{
    public const string SectionName = "Holds";

    /// <summary>How long a hold reserves stock before it expires. Fractional values are allowed for demos.</summary>
    public double DurationMinutes { get; set; } = 15;

    public TimeSpan Duration => TimeSpan.FromMinutes(DurationMinutes);
}
