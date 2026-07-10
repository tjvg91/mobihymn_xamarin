using System;
using System.Collections.Generic;

namespace MobiHymn4.Models;

public class WorshipGroup
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string JoinCode { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public static WorshipGroup FromFirestore(string id, IDictionary<string, object> data)
    {
        var group = new WorshipGroup { Id = id };
        if (data == null)
            return group;

        if (data.TryGetValue("name", out var name))
            group.Name = name?.ToString() ?? string.Empty;
        if (data.TryGetValue("joinCode", out var joinCode))
            group.JoinCode = joinCode?.ToString() ?? string.Empty;
        if (data.TryGetValue("createdBy", out var createdBy))
            group.CreatedBy = createdBy?.ToString() ?? string.Empty;
        if (data.TryGetValue("createdAt", out var createdAt) && createdAt is DateTime dt)
            group.CreatedAt = dt;

        return group;
    }
}
