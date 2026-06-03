using System;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;

namespace BackpackAdventures.CloudCode.Tests;

/// <summary>
/// Swaps the private static readonly CloudSaveHelper._http so tests can intercept REST traffic.
/// FieldInfo.SetValue throws FieldAccessException on initonly statics in .NET 9, so we emit a
/// stsfld via DynamicMethod(skipVisibility:true) — the JIT does not enforce initonly for
/// unverifiable IL. Returns the previous client so the caller can restore it on Dispose.
/// </summary>
internal static class HttpSeam
{
    public static HttpClient Inject(HttpClient client)
    {
        var field = typeof(CloudSaveHelper).GetField("_http", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("CloudSaveHelper._http field not found");

        var previous = (HttpClient)field.GetValue(null)!;

        var dm = new DynamicMethod(
            "__seam_set_CloudSaveHelper_http",
            null,
            new[] { typeof(HttpClient) },
            typeof(CloudSaveHelper).Module,
            skipVisibility: true);
        var il = dm.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stsfld, field);
        il.Emit(OpCodes.Ret);
        ((Action<HttpClient>)dm.CreateDelegate(typeof(Action<HttpClient>)))(client);

        return previous;
    }
}
