using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MobiHymn4.Models
{
    public class CatalogMeta
    {
        [JsonProperty("catalogHash")]
        public string CatalogHash { get; set; }

        [JsonProperty("total")]
        public int Total { get; set; }
    }

    public class CatalogDiff
    {
        [JsonProperty("changed")]
        public bool Changed { get; set; }

        [JsonProperty("catalogHash")]
        public string CatalogHash { get; set; }

        [JsonProperty("added")]
        [JsonConverter(typeof(CatalogNumberGroupConverter))]
        public CatalogNumberGroup Added { get; set; } = new();

        [JsonProperty("removed")]
        [JsonConverter(typeof(CatalogNumberGroupConverter))]
        public CatalogNumberGroup Removed { get; set; } = new();

        [JsonProperty("modifiedGroups")]
        public List<CatalogModifiedGroup> ModifiedGroups { get; set; } = new();

        /// <summary>Legacy flat per-hymn list; used when server has not yet switched to modifiedGroups.</summary>
        [JsonProperty("modified")]
        public List<CatalogModifiedHymn> Modified { get; set; } = new();

        [JsonIgnore]
        public int AddedCount => Math.Max(Added?.Count ?? 0, Added?.Numbers?.Count ?? 0);

        [JsonIgnore]
        public int RemovedCount => Math.Max(Removed?.Count ?? 0, Removed?.Numbers?.Count ?? 0);

        [JsonIgnore]
        public int ModifiedCount
        {
            get
            {
                if (ModifiedGroups != null && ModifiedGroups.Count > 0)
                    return ModifiedGroups.Sum(g => Math.Max(g.Count, g.Numbers?.Count ?? 0));
                return Modified?.Count ?? 0;
            }
        }

        [JsonIgnore]
        public int ChangeCount => AddedCount + RemovedCount + ModifiedCount;

        [JsonIgnore]
        public bool NumbersIncomplete =>
            IsIncomplete(Added)
            || IsIncomplete(Removed)
            || (ModifiedGroups?.Any(IsIncomplete) == true);

        static bool IsIncomplete(CatalogNumberGroup group)
        {
            if (group == null)
                return false;
            // Prefer explicit flag when present; otherwise infer from count vs numbers.
            if (!group.NumbersComplete && group.Count > (group.Numbers?.Count ?? 0))
                return true;
            return false;
        }

        static bool IsIncomplete(CatalogModifiedGroup group)
        {
            if (group == null)
                return false;
            if (!group.NumbersComplete && group.Count > (group.Numbers?.Count ?? 0))
                return true;
            return false;
        }

        [JsonIgnore]
        public IEnumerable<string> AddedOrModifiedNumbers
        {
            get
            {
                var numbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var number in Added?.Numbers ?? Enumerable.Empty<string>())
                    if (!string.IsNullOrWhiteSpace(number))
                        numbers.Add(number);

                if (ModifiedGroups != null && ModifiedGroups.Count > 0)
                {
                    foreach (var group in ModifiedGroups)
                        foreach (var number in group.Numbers ?? Enumerable.Empty<string>())
                            if (!string.IsNullOrWhiteSpace(number))
                                numbers.Add(number);
                }
                else if (Modified != null)
                {
                    foreach (var item in Modified)
                        if (!string.IsNullOrWhiteSpace(item?.Number))
                            numbers.Add(item.Number);
                }

                return numbers;
            }
        }

        public IReadOnlyList<string> GetRemovedNumbers() =>
            Removed?.Numbers ?? (IReadOnlyList<string>)Array.Empty<string>();

        /// <summary>Normalize after deserialize so missing optional fields are safe.</summary>
        public void Normalize()
        {
            Added = CatalogNumberGroupConverter.Normalize(Added);
            Removed = CatalogNumberGroupConverter.Normalize(Removed);
            ModifiedGroups ??= new List<CatalogModifiedGroup>();
            Modified ??= new List<CatalogModifiedHymn>();

            foreach (var group in ModifiedGroups)
            {
                if (group == null)
                    continue;
                group.Numbers ??= new List<string>();
                group.ChangedProps ??= new List<string>();
                group.Examples ??= new List<CatalogModifiedExample>();
                if (group.Count <= 0)
                    group.Count = group.Numbers.Count;
                // Server may omit numbersComplete; treat as complete when we have all ids.
                if (group.Numbers.Count >= group.Count)
                    group.NumbersComplete = true;

                foreach (var example in group.Examples)
                {
                    if (example == null)
                        continue;
                    example.Props ??= new Dictionary<string, CatalogPropertyChange>();
                }
            }
        }
    }

    public class CatalogNumberGroup
    {
        [JsonProperty("count")]
        public int Count { get; set; }

        [JsonProperty("numbers")]
        public List<string> Numbers { get; set; } = new();

        [JsonProperty("numbersComplete")]
        public bool NumbersComplete { get; set; } = true;
    }

    public class CatalogModifiedGroup
    {
        [JsonProperty("count")]
        public int Count { get; set; }

        [JsonProperty("numbers")]
        public List<string> Numbers { get; set; } = new();

        [JsonProperty("numbersComplete")]
        public bool NumbersComplete { get; set; } = true;

        [JsonProperty("props")]
        public Dictionary<string, CatalogPropertyChange> Props { get; set; }

        [JsonProperty("changedProps")]
        public List<string> ChangedProps { get; set; } = new();

        [JsonProperty("valueVaries")]
        public bool ValueVaries { get; set; }

        [JsonProperty("examples")]
        public List<CatalogModifiedExample> Examples { get; set; } = new();
    }

    public class CatalogModifiedExample
    {
        [JsonProperty("number")]
        public string Number { get; set; }

        [JsonProperty("props")]
        public Dictionary<string, CatalogPropertyChange> Props { get; set; } = new();
    }

    public class CatalogModifiedHymn
    {
        [JsonProperty("number")]
        public string Number { get; set; }

        [JsonProperty("props")]
        public Dictionary<string, CatalogPropertyChange> Props { get; set; } = new();
    }

    public class CatalogPropertyChange
    {
        [JsonProperty("old")]
        public JToken Old { get; set; }

        [JsonProperty("new")]
        public JToken New { get; set; }
    }

    /// <summary>
    /// Accepts either a plain string array (legacy) or
    /// { count, numbers, numbersComplete? } (current API).
    /// Parses manually — never ToObject&lt;CatalogNumberGroup&gt; (avoids converter recursion).
    /// </summary>
    public sealed class CatalogNumberGroupConverter : JsonConverter<CatalogNumberGroup>
    {
        public override CatalogNumberGroup ReadJson(
            JsonReader reader,
            Type objectType,
            CatalogNumberGroup existingValue,
            bool hasExistingValue,
            JsonSerializer serializer)
        {
            var token = JToken.Load(reader);
            return Parse(token);
        }

        public override void WriteJson(JsonWriter writer, CatalogNumberGroup value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteStartObject();
            writer.WritePropertyName("count");
            writer.WriteValue(value.Count);
            writer.WritePropertyName("numbers");
            serializer.Serialize(writer, value.Numbers ?? new List<string>());
            writer.WritePropertyName("numbersComplete");
            writer.WriteValue(value.NumbersComplete);
            writer.WriteEndObject();
        }

        public static CatalogNumberGroup Parse(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return new CatalogNumberGroup();

            if (token.Type == JTokenType.Array)
            {
                var list = token.ToObject<List<string>>() ?? new List<string>();
                return Normalize(new CatalogNumberGroup
                {
                    Count = list.Count,
                    Numbers = list,
                    NumbersComplete = true
                });
            }

            if (token.Type != JTokenType.Object)
                return new CatalogNumberGroup();

            var obj = (JObject)token;
            var numbers = obj["numbers"]?.ToObject<List<string>>() ?? new List<string>();
            var countToken = obj["count"];
            var count = countToken != null && countToken.Type != JTokenType.Null
                ? countToken.Value<int>()
                : numbers.Count;
            var completeToken = obj["numbersComplete"];
            var numbersComplete = completeToken == null || completeToken.Type == JTokenType.Null
                ? numbers.Count >= count
                : completeToken.Value<bool>();

            return Normalize(new CatalogNumberGroup
            {
                Count = count,
                Numbers = numbers,
                NumbersComplete = numbersComplete
            });
        }

        public static CatalogNumberGroup Normalize(CatalogNumberGroup group)
        {
            group ??= new CatalogNumberGroup();
            group.Numbers ??= new List<string>();
            if (group.Count <= 0)
                group.Count = group.Numbers.Count;
            if (group.Numbers.Count >= group.Count)
                group.NumbersComplete = true;
            return group;
        }
    }
}
