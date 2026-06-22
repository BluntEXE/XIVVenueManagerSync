using Dalamud.Plugin.Ipc;
using System;

namespace VenueManager;

public class LifestreamIpc : IDisposable
{
    private readonly ICallGateSubscriber<string, object?> _executeCommand;
    private readonly ICallGateSubscriber<bool> _isBusy;

    public bool IsAvailable { get; private set; }

    public LifestreamIpc()
    {
        _executeCommand = Plugin.PluginInterface.GetIpcSubscriber<string, object?>("Lifestream.ExecuteCommand");
        _isBusy = Plugin.PluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");

        IsAvailable = CheckAvailability();
    }

    private bool CheckAvailability()
    {
        foreach (var plugin in Plugin.PluginInterface.InstalledPlugins)
        {
            if (plugin.InternalName == "Lifestream" && plugin.IsLoaded)
                return true;
        }
        return false;
    }

    public void TeleportToVenue(Venue venue)
    {
        if (!IsAvailable) return;

        var args = $"{venue.WorldName} {venue.district} {venue.ward} {venue.plot}";
        try
        {
            _executeCommand.InvokeAction(args);
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, "Lifestream teleport failed");
        }
    }

    public bool IsBusy()
    {
        if (!IsAvailable) return false;
        try { return _isBusy.InvokeFunc(); }
        catch { return false; }
    }

    public void Dispose() { }
}
