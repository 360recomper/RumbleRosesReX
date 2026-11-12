using System.Runtime.InteropServices;

namespace RRXXLauncher;

/// <summary>The graphics cards Windows lists, by the exact names both renderers match against
/// (DXGI adapter descriptions, as the native renderer matches them; ported from UFC-native launcher/Gpu.cs). Read through DXGI's COM vtable directly, so no extra package is needed.</summary>
public static class Gpu
{
    /// <summary>The RumbleRosesReX.toml key the choice is written to (native renderer only; emulated keeps its own
    /// pick). Empty = automatic (the high-performance GPU first).</summary>
    public const string TomlKey = "rrxx_native_adapter";

    [DllImport("dxgi.dll")]
    static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int EnumAdapters1Fn(IntPtr self, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int GetDesc1Fn(IntPtr self, out DXGI_ADAPTER_DESC1 desc);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate uint ReleaseFn(IntPtr self);

    const uint FlagRemote = 1, FlagSoftware = 2;
    // Vtable slots: IUnknown 0-2, IDXGIObject 3-6, IDXGIFactory 7-11, IDXGIFactory1::EnumAdapters1 = 12;
    // IDXGIAdapter 7-9, IDXGIAdapter1::GetDesc1 = 10.
    const int SlotRelease = 2, SlotEnumAdapters1 = 12, SlotGetDesc1 = 10;

    static T Method<T>(IntPtr obj, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size));

    /// <summary>Hardware adapters (no software or remote ones), in Windows' order, without duplicates.
    /// Empty if DXGI can't be read.</summary>
    public static List<string> AdapterNames()
    {
        var names = new List<string>();
        try
        {
            var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IID_IDXGIFactory1
            if (CreateDXGIFactory1(ref iid, out var factory) < 0 || factory == IntPtr.Zero) return names;
            try
            {
                var enumAdapters = Method<EnumAdapters1Fn>(factory, SlotEnumAdapters1);
                for (uint i = 0; enumAdapters(factory, i, out var adapter) >= 0 && adapter != IntPtr.Zero; i++)
                {
                    try
                    {
                        if (Method<GetDesc1Fn>(adapter, SlotGetDesc1)(adapter, out var d) >= 0 &&
                            (d.Flags & (FlagRemote | FlagSoftware)) == 0 &&
                            !string.IsNullOrWhiteSpace(d.Description) && !names.Contains(d.Description))
                            names.Add(d.Description);
                    }
                    finally { Method<ReleaseFn>(adapter, SlotRelease)(adapter); }
                }
            }
            finally { Method<ReleaseFn>(factory, SlotRelease)(factory); }
        }
        catch { }
        return names;
    }
}
