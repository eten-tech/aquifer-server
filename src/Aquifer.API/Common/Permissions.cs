namespace Aquifer.API.Common;

public static class Permissions
{
    public const string PermissionsClaim = "permissions";
    public const string RolesClaim = "bnRoles";
}

public static class PermissionName
{
    public const string AiTranslate = "ai:translate",
        AssignContent = "assign:content",
        AssignOutsideCompany = "assign:outside-company",
        AssignOverride = "assign:override",
        CreateApiKey = "create:apikey",
        CreateCommunityContent = "create:community-content",
        CreateContent = "create:content",
        CreateProject = "create:project",
        CreateUser = "create:user",
        CreateUsersInCompany = "create:users-in-company",
        DisableUser = "disable:users",
        DisableUsersInCompany = "disable:users-in-company",
        EditContent = "edit:content",
        EditContentBibleReferences = "edit:content-bible-references",
        EditContentResourceReferences = "edit:content-resource-references",
        EditProject = "edit:projects",
        PublishContent = "publish:content",
        ReadAllUsers = "read:all-users",
        ReadCompanyContentAssignments = "read:company-content-assignments",
        ReadProject = "read:projects",
        ReadProjectsInCompany = "read:projects-in-company",
        ReadReports = "read:reports",
        ReadReportsInCompany = "read:reports-in-company",
        ReadResourceLists = "read:resource-lists",
        ReadResources = "read:resources",
        ReadUsers = "read:users",
        ReviewContent = "review:content",
        SendReviewContent = "send-review:content",
        SendReviewCommunityContent = "send-review:community-content",
        SetStatusTranslationNotApplicable = "set-status:translation-not-applicable",
        SetStatusCompleteNotApplicable = "set-status:complete-not-applicable",
        SetTranslationPair = "set:translation-pair",
        GetTranslationPair = "get:translation-pair",
        UpdateUser = "update:user",
        UpdateUsersInCompany = "update:users-in-company";
}

public static class KeyVaultSecretName
{
    public const string Auth0ClientSecret = "Auth0ClientSecret";

    /// <summary>
    /// This secret purposefully only exists in the "biblionexus-kv-dev" key vault and should only be used for integration testing.
    /// DO NOT add this secret to the production key vault!!!
    /// </summary>
    public const string ResourceOwnerAuth0ClientSecret = "ResourceOwnerAuth0ClientSecret";

    public const string OpenAiApiKey = "OpenAiApiKey";
}