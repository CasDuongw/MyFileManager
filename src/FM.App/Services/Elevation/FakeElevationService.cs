using FM.Core.Elevation;

namespace FM.App.Services.Elevation;

public class FakeElevationService : IElevationService
{
    public bool CanElevate { get; set; } = true;
    public bool ShouldSucceed { get; set; } = true;
    public string? ErrorToReturn { get; set; }

    public List<ElevationPayload> ExecutedPayloads { get; } = new();

    public Task<ElevationResponse> ExecuteElevatedAsync(ElevationPayload payload, CancellationToken ct = default)
    {
        ExecutedPayloads.Add(payload);

        if (!ShouldSucceed)
        {
            var failedResults = payload.Items.Select(i => new ElevationItemResult(i.SourcePath, false, ErrorToReturn ?? "Access Denied")).ToList();
            return Task.FromResult(new ElevationResponse(payload.OperationId, false, failedResults, ErrorToReturn ?? "Failed"));
        }

        var results = payload.Items.Select(i => new ElevationItemResult(i.SourcePath, true)).ToList();
        return Task.FromResult(new ElevationResponse(payload.OperationId, true, results));
    }
}
