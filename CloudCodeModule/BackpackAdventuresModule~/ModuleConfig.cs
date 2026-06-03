using System.Diagnostics;
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

    // Server execution time (ms). Present on the non-generic response too, so endpoints that
    // return no data still report runtime. Mirrors ApiResponse<T>.ServerExecutionMs.
    public long ServerExecutionMs { get; set; }

    public static ApiResponse Ok(Stopwatch sw, string message = "OK")
        => new ApiResponse { StatusCode = 200, Message = message, ServerExecutionMs = sw.ElapsedMilliseconds };
}

public class ApiResponse<T>
{
    public int StatusCode { get; set; } = 200;
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
    public long ServerExecutionMs { get; set; }

    public static ApiResponse<T> Ok(T data, string message = "OK")
    {
        return new ApiResponse<T>
        {
            StatusCode = 200,
            Message = message,
            Data = data
        };
    }

    public static ApiResponse<T> Ok(T data, Stopwatch sw, string message = "OK")
    {
        return new ApiResponse<T>
        {
            StatusCode = 200,
            Message = message,
            Data = data,
            ServerExecutionMs = sw.ElapsedMilliseconds
        };
    }
}
