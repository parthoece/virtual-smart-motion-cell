using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VirtualSmartMotionCell.Contracts;

namespace VirtualSmartMotionCell.Domain;

public sealed record Position2D(double X, double Y);
public sealed record MotionParameters(double MaximumVelocity, double MaximumAcceleration, double FollowingErrorLimit);

public sealed record MachineRecipe(
    int SchemaVersion,
    string RecipeId,
    int Revision,
    string Status,
    Position2D Pick,
    Position2D Inspect,
    Position2D Place,
    Position2D Home,
    MotionParameters Motion,
    double PickDwellSeconds,
    double InspectDwellSeconds,
    double PlaceDwellSeconds)
{
    public static MachineRecipe Default => new(
        1, "standard-widget", 1, "active",
        new Position2D(0.8, 0.3), new Position2D(0.2, 0.9), new Position2D(-0.7, 0.6), new Position2D(0.0, 0.0),
        new MotionParameters(0.55, 1.4, 0.35), 0.35, 0.45, 0.35);

    public RecipeLifecycle Lifecycle => Status.Trim().ToLowerInvariant() switch
    {
        "draft" => RecipeLifecycle.Draft,
        "approved" => RecipeLifecycle.Approved,
        "active" => RecipeLifecycle.Active,
        "retired" => RecipeLifecycle.Retired,
        _ => RecipeLifecycle.Draft
    };

    public string Checksum
    {
        get
        {
            var payload = new
            {
                SchemaVersion,
                RecipeId,
                Revision,
                Status,
                Pick,
                Inspect,
                Place,
                Home,
                Motion,
                PickDwellSeconds,
                InspectDwellSeconds,
                PlaceDwellSeconds
            };

            var json = JsonSerializer.Serialize(payload);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));

            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }

    public RecipeSnapshot ToSnapshot() => new(RecipeId, Revision, SchemaVersion, Lifecycle, Checksum);

    public IReadOnlyList<string> Validate(bool requireActivatable = true)
    {
        var errors = new List<string>();
        if (SchemaVersion < 1) errors.Add("SchemaVersion must be at least 1.");
        if (string.IsNullOrWhiteSpace(RecipeId)) errors.Add("RecipeId is required.");
        if (Revision < 1) errors.Add("Revision must be at least 1.");
        if (requireActivatable && Lifecycle is not (RecipeLifecycle.Approved or RecipeLifecycle.Active)) errors.Add("Only approved or active recipes may be activated.");
        if (Motion.MaximumVelocity <= 0 || Motion.MaximumVelocity > 2) errors.Add("MaximumVelocity must be in (0, 2].");
        if (Motion.MaximumAcceleration <= 0 || Motion.MaximumAcceleration > 5) errors.Add("MaximumAcceleration must be in (0, 5].");
        if (Motion.FollowingErrorLimit <= 0) errors.Add("FollowingErrorLimit must be positive.");
        if (PickDwellSeconds < 0 || InspectDwellSeconds < 0 || PlaceDwellSeconds < 0) errors.Add("Dwell times cannot be negative.");
        foreach (var (name, position) in new[] { ("Pick", Pick), ("Inspect", Inspect), ("Place", Place), ("Home", Home) })
        {
            if (position.X is < -1.0 or > 1.0 || position.Y is < 0.0 or > 1.2)
                errors.Add($"{name} position is outside the simulated work envelope.");
        }
        return errors;
    }

    public MachineRecipe WithStatus(RecipeLifecycle lifecycle) => this with { Status = lifecycle.ToString().ToLowerInvariant() };
}
