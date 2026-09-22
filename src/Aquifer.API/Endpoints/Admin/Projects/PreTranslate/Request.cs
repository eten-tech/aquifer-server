namespace Aquifer.API.Endpoints.Admin.Projects.PreTranslate;

public record Request
{
    public int Id { get; set; }

    /// <summary>
    /// Re-translates Resource Contents that already have an AI draft, reading from their original snapshot and overwriting current
    /// content. This can overwrite in-progress editor work.
    /// </summary>
    public bool ShouldForceRetranslation { get; set; }

    public bool ShouldSkipCompanyLeadAssignment { get; set; }

    public bool ShouldSkipProjectStartedNotification { get; set; }

    /// <summary>
    /// When null or empty the whole Project is pre-translated; otherwise only these Resource Contents are.
    /// </summary>
    public IReadOnlyList<int>? ResourceContentIds { get; set; }
}
