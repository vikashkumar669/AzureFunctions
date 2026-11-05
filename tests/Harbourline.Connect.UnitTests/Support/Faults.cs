using System.ServiceModel;
using Microsoft.Xrm.Sdk;

namespace Harbourline.Connect.UnitTests.Support;

/// <summary>Builds the exceptions Dataverse throws, for tests.</summary>
public static class Faults
{
    // <listing:test-fault>
    public static FaultException<OrganizationServiceFault> Of(
        int errorCode, string message = "Simulated Dataverse error")
    {
        var fault = new OrganizationServiceFault
        {
            ErrorCode = errorCode,
            Message = message,
        };
        return new FaultException<OrganizationServiceFault>(
            fault, new FaultReason(message));
    }
    // </listing:test-fault>
}
