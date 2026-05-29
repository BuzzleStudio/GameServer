using System.Threading.Tasks;

namespace BackpackAdventures.CloudCode.Client
{
    public interface ICloudCodeBackend
    {
        Task<T> CallEndpointAsync<T>(string endpoint, object request);
    }
}
