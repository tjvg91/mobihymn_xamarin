using System;

namespace MobiHymn4.Utils
{
	public enum InputType
	{
		Grid = 0,
		Numpad = 1,
		Voice = 2
	}

	public enum DownloadStatus
	{
		None = 0,
		Started = 1,
		Ongoing = 2,
		Success = 3,
		Error = 4
	}

	public enum CRUD
	{
		Create = 0,
		Update = 1,
		Delete = 2,
	}

	public enum ResyncType
	{
		Lyrics = 0,
		Audio = 1
	}

	public enum UserRole
	{
		Pastor = 0,
		WorshipLeader = 1,
		Projector = 2,
		Accompaniment = 3,
		Congregant = 4,
	}

	public enum SectionApplyFrequency
	{
		Daily = 0,
		Weekly = 1,
		Monthly = 2,
		Yearly = 3,
	}

	public enum SectionSortMode
	{
		AddedOrder = 0,
		DateNewest = 1,
		DateOldest = 2,
		NameAsc = 3,
		NameDesc = 4,
	}
}

