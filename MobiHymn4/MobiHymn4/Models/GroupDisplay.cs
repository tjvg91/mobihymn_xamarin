
namespace MobiHymn4.Models
{
	public class GroupDisplay
	{
		public string Name { get; set; }
		public int Count { get; set; }
		public string CountString { get => $"{Count} hymn{(Count != 1  ? "s" : "")}"; }

		public GroupDisplay()
		{
			Name = "General";
		}
	}
}

