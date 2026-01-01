using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Utf8Json;

namespace PluginManager_V2.JSON.Objects;

public class DataJson
{
    public string? GitHubPersonalAccessToken;

    public DateTime? EulaAccepted;

    public bool PluginManagerWarningDismissed;

    public DateTime? LastPluginAliasesRefresh;

    public Dictionary<string, PluginVersionCache> PluginVersionCache;

    //public Dictionary<string, PluginAlias> PluginAliases;
    
    public List<Plugin> AvailablePlugins;

    internal DataJson()
    {
        PluginVersionCache = new();
        //PluginAliases = new();
    }

    /*[SerializationConstructor]
    public DataJson(string? gitHubPersonalAccessToken, DateTime? eulaAccepted, bool pluginManagerWarningDismissed, DateTime? lastPluginAliasesRefresh, Dictionary<string, PluginVersionCache> pluginVersionCache, Dictionary<string, PluginAlias> pluginAliases)
    {
        GitHubPersonalAccessToken = gitHubPersonalAccessToken;
        EulaAccepted = eulaAccepted;
        PluginManagerWarningDismissed = pluginManagerWarningDismissed;
        LastPluginAliasesRefresh = lastPluginAliasesRefresh;
        PluginVersionCache = pluginVersionCache;
        PluginAliases = pluginAliases;

        PluginVersionCache ??= new();
        PluginAliases ??= new();
    }*/
}

public struct PluginVersionCache
{
    public string Version;

    public uint ReleaseId;

    public DateTime PublishmentTime;

    public DateTime LastRefreshed;

    public string DllDownloadUrl;

    public string? DependenciesDownloadUrl;

    [SerializationConstructor]
    public PluginVersionCache(string version, uint releaseId, DateTime publishmentTime, DateTime lastRefreshed, string dllDownloadUrl, string? dependenciesDownloadUrl)
    {
        Version = version;
        ReleaseId = releaseId;
        PublishmentTime = publishmentTime;
        LastRefreshed = lastRefreshed;
        DllDownloadUrl = dllDownloadUrl;
        DependenciesDownloadUrl = dependenciesDownloadUrl;
    }
}

public struct PluginResponse
{
    [JsonProperty("message")]
    public string Message { get; set; }

    [JsonProperty("data")]
    public PluginResponseData Data { get; set; }
}

public struct PluginResponseData
{
    [JsonProperty("data")]
    public List<Plugin> Data { get; set; }

    [JsonProperty("meta")]
    public PluginMeta Meta { get; set; }
}

public struct PluginMeta
{
    [JsonProperty("total")]
    public int Total { get; set; }

    [JsonProperty("page")]
    public int Page { get; set; }

    [JsonProperty("limit")]
    public int Limit { get; set; }

    [JsonProperty("totalPages")]
    public int? TotalPages { get; set; }
}

public struct Plugin
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("icon")]
    public string Icon { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("repository")]
    public string Repository { get; set; }

    [JsonProperty("repositoryId")]
    public long RepositoryId { get; set; }

    [JsonProperty("authorId")]
    public int AuthorId { get; set; }

    [JsonProperty("state")]
    public string State { get; set; }

    [JsonProperty("stars")]
    public int Stars { get; set; }

    [JsonProperty("downloads")]
    public int Downloads { get; set; }

    [JsonProperty("readme")]
    public string Readme { get; set; }

    [JsonProperty("releases")]
    public List<PluginRelease> Releases { get; set; }

    [JsonProperty("repoCreatedAt")]
    public DateTime RepoCreatedAt { get; set; }

    [JsonProperty("repoUpdatedAt")]
    public DateTime RepoUpdatedAt { get; set; }

    [JsonProperty("pinned")]
    public bool Pinned { get; set; }

    [JsonProperty("recommended")]
    public bool Recommended { get; set; }

    [JsonProperty("lastCheckedAt")]
    public DateTime LastCheckedAt { get; set; }

    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonProperty("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [JsonProperty("organizationId")]
    public string OrganizationId { get; set; }

    [JsonProperty("author")]
    public PluginAuthor Author { get; set; }

    [JsonProperty("tags")]
    public List<PluginTag> Tags { get; set; }

    [JsonProperty("upvotes")]
    public int Upvotes { get; set; }

    [JsonProperty("organization")]
    public object Organization { get; set; }

    [JsonProperty("dependencies")]
    public List<object> Dependencies { get; set; }

    [JsonProperty("dependentOn")]
    public List<object> DependentOn { get; set; }
}

public struct PluginRelease
{
    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("body")]
    public string Body { get; set; }

    [JsonProperty("tagName")]
    public string TagName { get; set; }

    [JsonProperty("htmlUrl")]
    public string HtmlUrl { get; set; }

    [JsonProperty("publishedAt")]
    public DateTime PublishedAt { get; set; }

    [JsonProperty("author")]
    public ReleaseAuthor Author { get; set; }

    [JsonProperty("assets")]
    public List<ReleaseAsset> Assets { get; set; }

    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonProperty("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

public struct ReleaseAuthor
{
    [JsonProperty("username")]
    public string Username { get; set; }

    [JsonProperty("avatarUrl")]
    public string AvatarUrl { get; set; }
}

public struct ReleaseAsset
{
    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("downloadUrl")]
    public string DownloadUrl { get; set; }

    [JsonProperty("size")]
    public int Size { get; set; }

    [JsonProperty("downloadCount")]
    public int DownloadCount { get; set; }
}

public struct PluginAuthor
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("username")]
    public string Username { get; set; }

    [JsonProperty("displayName")]
    public string DisplayName { get; set; }

    [JsonProperty("avatarUrl")]
    public string AvatarUrl { get; set; }

    [JsonProperty("_count")]
    public PluginAuthorCount Count { get; set; }
}

public struct PluginAuthorCount
{
    [JsonProperty("plugins")]
    public int Plugins { get; set; }
}

public struct PluginTag
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("slug")]
    public string Slug { get; set; }

    [JsonProperty("icon")]
    public string Icon { get; set; }

    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonProperty("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}