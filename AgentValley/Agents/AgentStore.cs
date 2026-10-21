using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StardewModdingAPI;

namespace AgentValley;

/// <summary>
/// The agents of one save. Written to Mods/AgentValley/data/&lt;save&gt;.json on every change:
/// nothing is ever added to the game's save file, so removing the mod leaves no trace.
/// </summary>
public sealed class AgentStore
{
    private readonly IModHelper helper;
    private readonly string relativePath;
    private bool dirty;
    private DateTime lastWrite = DateTime.MinValue;

    public string SaveName { get; }
    public StoreData Data { get; }
    public List<AgentRecord> Agents => Data.Agents;

    private AgentStore(IModHelper helper, string saveName, StoreData data)
    {
        this.helper = helper;
        SaveName = saveName;
        relativePath = Path.Combine("data", saveName + ".json");
        Data = data;
    }

    public static AgentStore Load(IModHelper helper, string saveName)
    {
        var data = helper.Data.ReadJsonFile<StoreData>(Path.Combine("data", saveName + ".json")) ?? new StoreData();
        foreach (var agent in data.Agents)
        {
            // The game was closed while it was working: the process is gone.
            if (agent.Status == AgentStatus.Working)
            {
                agent.Status = AgentStatus.Interrupted;
                agent.Add(EntryRole.System, I18n.Get("chat.interrupted-by-exit"));
            }
            agent.PendingMessages.Clear();
        }
        return new AgentStore(helper, saveName, data);
    }

    public AgentRecord? Get(string id) => Agents.FirstOrDefault(a => a.Id == id);

    public void MarkDirty() => dirty = true;

    public void RememberFolder(string folder)
    {
        Data.RecentFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        Data.RecentFolders.Insert(0, folder);
        if (Data.RecentFolders.Count > 6)
            Data.RecentFolders.RemoveRange(6, Data.RecentFolders.Count - 6);
        MarkDirty();
    }

    /// <summary>Writes to disk at most every 2 seconds (or right away if <paramref name="force"/>).</summary>
    public void Flush(bool force = false)
    {
        if (!dirty || (!force && DateTime.Now - lastWrite < TimeSpan.FromSeconds(2)))
            return;
        helper.Data.WriteJsonFile(relativePath, Data);
        dirty = false;
        lastWrite = DateTime.Now;
    }
}
