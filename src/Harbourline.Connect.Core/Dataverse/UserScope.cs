using Microsoft.PowerPlatform.Dataverse.Client;

namespace Harbourline.Connect.Core.Dataverse;

/// <summary>
/// Runs Dataverse calls as a signed-in user, so Dataverse applies that
/// user's security roles and row access (Chapter 22).
/// </summary>
public interface IDataverseUserScope
{
    Task<T> RunAsUserAsync<T>(
        Guid entraObjectId,
        Func<IOrganizationServiceAsync2, Task<T>> work);
}

// <listing:user-scope>
public sealed class ServiceClientUserScope(ServiceClient root)
    : IDataverseUserScope
{
    public async Task<T> RunAsUserAsync<T>(
        Guid entraObjectId,
        Func<IOrganizationServiceAsync2, Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        using var client = root.Clone();                   // <1>
        client.CallerAADObjectId = entraObjectId;          // <2>
        return await work(client).ConfigureAwait(false);
    }
}
// </listing:user-scope>
