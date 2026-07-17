using System.Linq;
using MobiHymn4.Models;
using Newtonsoft.Json;

var json = File.ReadAllText("sample.json");
try
{
    var diff = JsonConvert.DeserializeObject<CatalogDiff>(json);
    diff.Normalize();
    Console.WriteLine("Deserialize OK");
    Console.WriteLine($"AddedCount={diff.AddedCount}, RemovedCount={diff.RemovedCount}, ModifiedCount={diff.ModifiedCount}, ChangeCount={diff.ChangeCount}");
    Console.WriteLine($"AddedOrModifiedNumbers count={diff.AddedOrModifiedNumbers.Count()}");
}
catch (Exception ex)
{
    Console.WriteLine("FAILED: " + ex);
}
