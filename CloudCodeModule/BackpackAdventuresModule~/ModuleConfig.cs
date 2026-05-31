using Unity.Services.CloudCode.Core;

namespace BackpackAdventures.CloudCode;

public class ModuleConfig : ICloudCodeSetup
{
    public void Setup(ICloudCodeConfig config)
    {
        // Keep DI empty. The Cloud Code runtime injects IExecutionContext,
        // IGameApiClient, and ILogger<TModule> into module classes directly.
        // Registering GameApiClient here previously caused module construction
        // failures in this project.
    }
}

public class ApiResponse
{
    public int StatusCode { get; set; } = 200;
    public string Message { get; set; } = string.Empty;
    public object? Data { get; set; }
}

public class ApiResponse<T>
{
    public int StatusCode { get; set; } = 200;
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }

    public static ApiResponse<T> Ok(T data, string message = "OK")
    {
        return new ApiResponse<T>
        {
            StatusCode = 200,
            Message = message,
            Data = data
        };
    }
}
