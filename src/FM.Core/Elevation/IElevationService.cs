namespace FM.Core.Elevation;

public interface IElevationService
{
    bool CanElevate { get; }

    Task<ElevationResponse> ExecuteElevatedAsync(ElevationPayload payload, CancellationToken ct = default);
}
