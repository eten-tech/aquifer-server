using Aquifer.Common.Tiptap;
using Aquifer.Common.Utilities;
using Aquifer.Data;
using Aquifer.Data.Entities;
using Aquifer.Data.Schemas;
using Microsoft.EntityFrameworkCore;

namespace Aquifer.Public.API.Endpoints.Resources.Get;

public static class ResourceHelper
{
    public static async Task<Response> GetResourceContentAsync(
        AquiferDbReadOnlyContext dbContext,
        CommonResourceRequest req,
        Action<string, int?> throwError,
        CancellationToken ct)
    {
        var responseChoices = await dbContext.ResourceContentVersions
            .Where(x => ((req.LanguageCode == null && x.ResourceContentId == req.ContentId) ||
                    (x.ResourceContent.Resource.ResourceContents.Any(rc => rc.Id == req.ContentId) &&
                        x.ResourceContent.Language.ISO6393Code == req.LanguageCode)) &&
                x.IsPublished &&
                x.ResourceContent.Resource.ParentResource.Enabled)
            .Select(x => new Response
            {
                Id = x.ResourceContentId,
                ReferenceId = x.ResourceContent.ResourceId,
                Name = x.ResourceContent.Resource.EnglishLabel,
                LocalizedName = x.DisplayName,
                ContentValue = x.Content,
                Language = new ResourceContentLanguage
                {
                    Id = x.ResourceContent.Language.Id,
                    DisplayName = x.ResourceContent.Language.EnglishDisplay,
                    Code = x.ResourceContent.Language.ISO6393Code,
                    ScriptDirection = x.ResourceContent.Language.ScriptDirection,
                },
                Grouping = new ResourceTypeMetadata
                {
                    Name = x.ResourceContent.Resource.ParentResource.DisplayName,
                    Type = x.ResourceContent.Resource.ParentResource.ResourceType,
                    MediaTypeValue = x.ResourceContent.MediaType,
                    LicenseInfo = JsonUtilities.DefaultDeserialize<ParentResourceLicenseInfoSchema>(
                        x.ResourceContent.Resource.ParentResource.LicenseInfo),
                },
            })
            .ToListAsync(ct);

        Response? response = null;
        if (responseChoices.Count == 1)
        {
            response = responseChoices[0];
        }
        else if (responseChoices.Count > 1)
        {
            var originalMediaType =
                await dbContext.ResourceContents.Where(x => x.Id == req.ContentId).Select(x => x.MediaType).SingleAsync(ct);
            response = responseChoices.SingleOrDefault(x => x.Grouping.MediaTypeValue == originalMediaType);
        }

        if (response is null)
        {
            throwError($"No record found for {req.ContentId}", 404);
        }

        var contentTextType = response!.Grouping.MediaTypeValue == ResourceContentMediaType.Text
            ? req.ContentTextType == TiptapContentType.None ? TiptapContentType.Json : req.ContentTextType
            : TiptapContentType.None;

        response.Content = TiptapConverter.ConvertJsonToType(response.ContentValue, contentTextType);
        response.Grouping.MediaType = response.Grouping.MediaTypeValue.ToString();

        return response;
    }
}

public record CommonResourceRequest(int ContentId, TiptapContentType ContentTextType, string? LanguageCode = null);