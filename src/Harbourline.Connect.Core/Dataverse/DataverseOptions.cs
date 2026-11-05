using System.ComponentModel.DataAnnotations;

namespace Harbourline.Connect.Core.Dataverse;

/// <summary>
/// Strongly typed settings for the Dataverse connection.
/// Bound from the "Dataverse" configuration section
/// (app setting <c>Dataverse__Url</c>).
/// </summary>
public sealed class DataverseOptions
{
    public const string SectionName = "Dataverse";

    /// <summary>
    /// Environment URL, for example https://contoso-dev.crm.dynamics.com.
    /// Not a secret: authentication uses an Azure identity.
    /// </summary>
    [Required]
    [Url]
    public string Url { get; set; } = string.Empty;
}
